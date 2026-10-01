using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.History;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.IntegrationTests.Docx;
using Xunit;

namespace DocumentIDE.IntegrationTests.Batch;

public sealed class SafeApplyTests : IDisposable
{
    private readonly string _root;
    private readonly AppDb _db;
    private readonly BatchPlanRepository _plans;
    private readonly DocumentRepository _documents;
    private readonly FakeHistoryStorage _historyStorage;
    private readonly RecoverySnapshotService _snapshots;
    private readonly FakeHistoryRecorder _recorder;
    private readonly FakeReindexTrigger _reindex;
    private readonly BatchApplyEngine _engine;

    public SafeApplyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "safe-apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _plans = new BatchPlanRepository(_db);
        _documents = new DocumentRepository(_db);
        new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "apply" }).GetAwaiter().GetResult();
        _historyStorage = new FakeHistoryStorage(_root);
        _snapshots = new RecoverySnapshotService(_historyStorage);
        _recorder = new FakeHistoryRecorder();
        _reindex = new FakeReindexTrigger();
        _engine = new BatchApplyEngine(_plans, _documents, _snapshots, new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Apply_MutationFailureLeavesOriginalUnchanged()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        async Task FailAfterTouchingTemp(string tempPath, CancellationToken token)
        {
            await File.AppendAllTextAsync(tempPath, "partial mutation", token);
            throw new InvalidOperationException("injected mutation failure");
        }

        var result = await _engine.ApplyAsync(planId, FailAfterTouchingTemp);

        var failure = Assert.Single(result.Files);
        Assert.Equal("doc", failure.DocumentId);
        Assert.Equal(BatchApplyFileOutcome.Failure, failure.Outcome);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.DoesNotContain(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), f => Path.GetFileName(f).StartsWith("~apply-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_StaleFileIsRejected()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        // File changes after preview: current fingerprint no longer equals the preview fingerprint.
        var stale = Encoding.UTF8.GetBytes("target content changed after preview");
        await File.WriteAllBytesAsync(path, stale);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Stale", skipped.ReasonCode);
        Assert.Equal(stale, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_OpenWordDocIsSkipped()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        // Word lock-file convention: the document is open in an external editor.
        await File.WriteAllTextAsync(Path.Combine(_root, "~$notes.md"), "word lock");

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("OpenInWord", skipped.ReasonCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_SuccessfulFileGetsHistoryRecord()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);
        Assert.Equal("replaced content", await File.ReadAllTextAsync(path));

        var record = Assert.Single(_recorder.Records);
        Assert.Equal("doc", record.DocumentId);
        Assert.Equal(BatchApplyFileOutcome.Success, record.Outcome);
        Assert.Null(record.ReasonCode);
        Assert.Equal(planId, record.PlanId);

        Assert.Equal(new[] { "doc" }, _reindex.DocumentIds);
    }

    [Fact]
    public async Task Apply_RecoveryCapacityBlockedFailsClosed()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        // Recovery cannot be created (zero capacity) → mutation must be blocked.
        var blockedEngine = new BatchApplyEngine(
            _plans, _documents, new RecoverySnapshotService(new FakeHistoryStorage(_root, capacityBytes: 0)),
            new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await blockedEngine.ApplyAsync(planId, ReplaceTarget);

        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        var failure = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, failure.Outcome);
        Assert.Equal("RecoveryBlocked", failure.ReasonCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    [Fact]
    public async Task Apply_CancellationLeavesNoHalfWrittenOriginal()
    {
        var first = Encoding.UTF8.GetBytes("target one");
        var second = Encoding.UTF8.GetBytes("target two");
        var (planId, firstPath) = await SeedAsync("doc1", "one.md", first);
        var (_, secondPath) = await SeedAsync("doc2", "two.md", second, planId);

        async Task CancelAfterTempMutation(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
            throw new OperationCanceledException("injected cancellation");
        }

        var result = await _engine.ApplyAsync(planId, CancelAfterTempMutation);

        Assert.Equal(BatchApplyResultStatus.Canceled, result.Status);
        Assert.Equal(2, result.Files.Count);
        var firstResult = result.Files.Single(f => f.DocumentId == "doc1");
        var secondResult = result.Files.Single(f => f.DocumentId == "doc2");
        Assert.Equal(BatchApplyFileOutcome.Failure, firstResult.Outcome);
        Assert.Equal("Cancelled", firstResult.ReasonCode);
        Assert.Equal(BatchApplyFileOutcome.Skipped, secondResult.Outcome);
        Assert.Equal("Cancelled", secondResult.ReasonCode);

        // No file is half-written: both remain byte-identical to the reviewed originals.
        Assert.Equal(first, await File.ReadAllBytesAsync(firstPath));
        Assert.Equal(second, await File.ReadAllBytesAsync(secondPath));
        Assert.DoesNotContain(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), f => Path.GetFileName(f).StartsWith("~apply-", StringComparison.Ordinal));
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    [Fact]
    public async Task Apply_MutationFailureGetsHistoryRecord()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        async Task FailAfterTouchingTemp(string tempPath, CancellationToken token)
        {
            await File.AppendAllTextAsync(tempPath, "partial mutation", token);
            throw new InvalidOperationException("injected mutation failure");
        }

        var result = await _engine.ApplyAsync(planId, FailAfterTouchingTemp);

        var failure = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, failure.Outcome);
        Assert.Equal("MutationFailed", failure.ReasonCode);

        // Step 6 requires a persistent record for failure outcomes too.
        var record = Assert.Single(_recorder.Records);
        Assert.Equal("doc", record.DocumentId);
        Assert.Equal(BatchApplyFileOutcome.Failure, record.Outcome);
        Assert.Equal("MutationFailed", record.ReasonCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_RecorderFailureOnMutationFailureStillCompletes()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);
        var failingRecorder = new FakeFailingHistoryRecorder();
        var engine = new BatchApplyEngine(_plans, _documents, _snapshots, new WordEditStateProbe(), new AtomicFileReplacer(), failingRecorder, _reindex);

        async Task FailAfterTouchingTemp(string tempPath, CancellationToken token)
        {
            await File.AppendAllTextAsync(tempPath, "partial mutation", token);
            throw new InvalidOperationException("injected mutation failure");
        }

        // A recorder failure must not escape or strand the plan; the typed outcome stands.
        var result = await engine.ApplyAsync(planId, FailAfterTouchingTemp);

        var failure = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, failure.Outcome);
        Assert.Equal("MutationFailed", failure.ReasonCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(BatchPlanStatus.Completed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    [Fact]
    public async Task Apply_GenuineTokenCancellationReturnsTypedCanceledResult()
    {
        var first = Encoding.UTF8.GetBytes("target one");
        var second = Encoding.UTF8.GetBytes("target two");
        var (planId, firstPath) = await SeedAsync("doc1", "one.md", first);
        var (_, secondPath) = await SeedAsync("doc2", "two.md", second, planId);

        using var cts = new CancellationTokenSource();
        var tokenHonoringRecorder = new FakeTokenHonoringHistoryRecorder();
        var engine = new BatchApplyEngine(_plans, _documents, _snapshots, new WordEditStateProbe(), new AtomicFileReplacer(), tokenHonoringRecorder, _reindex);

        async Task MutateThenCancel(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
            cts.Cancel();
            throw new OperationCanceledException("injected cancellation", cts.Token);
        }

        // The real token is canceled mid-apply and the recorder honors it: the typed
        // Canceled result and plan→Failed transition must still complete.
        var result = await engine.ApplyAsync(planId, MutateThenCancel, null, cts.Token);

        Assert.Equal(BatchApplyResultStatus.Canceled, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(BatchApplyFileOutcome.Failure, result.Files.Single(f => f.DocumentId == "doc1").Outcome);
        Assert.Equal("Cancelled", result.Files.Single(f => f.DocumentId == "doc1").ReasonCode);
        Assert.Equal(BatchApplyFileOutcome.Skipped, result.Files.Single(f => f.DocumentId == "doc2").Outcome);
        Assert.Equal("Cancelled", result.Files.Single(f => f.DocumentId == "doc2").ReasonCode);
        Assert.Equal(first, await File.ReadAllBytesAsync(firstPath));
        Assert.Equal(second, await File.ReadAllBytesAsync(secondPath));
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    [Fact]
    public async Task Apply_CancellationDuringCheckPhaseEvictsFileLocks()
    {
        var first = Encoding.UTF8.GetBytes("target one");
        var second = Encoding.UTF8.GetBytes("target two");
        var (planId, firstPath) = await SeedAsync("doc1", "one.md", first);
        var (_, secondPath) = await SeedAsync("doc2", "two.md", second, planId);

        using var cts = new CancellationTokenSource();
        // Cancel while doc1 is being checked: doc1 passes its checks and holds its
        // lock, then the loop's cancellation gate fires for doc2 — so doc1's lock is
        // still held when ApplyAsync unwinds. It must be released AND evicted from
        // the static dictionary, or entries leak for every canceled apply.
        var cancelDuringCheckProbe = new FakeCallbackProbe(() => cts.Cancel());
        var engine = new BatchApplyEngine(_plans, _documents, _snapshots, cancelDuringCheckProbe, new AtomicFileReplacer(), _recorder, _reindex);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await engine.ApplyAsync(planId, ReplaceTarget, null, cts.Token);

        Assert.Equal(BatchApplyResultStatus.Canceled, result.Status);
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);

        var fileLocks = (IDictionary<string, SemaphoreSlim>)typeof(BatchApplyEngine)
            .GetField("_fileLocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        Assert.False(fileLocks.ContainsKey(firstPath), "doc1's lock entry must be evicted after release.");
        Assert.False(fileLocks.ContainsKey(secondPath), "doc2's lock entry must be evicted after release.");
    }

    [Fact]
    public async Task Apply_SelectedFileWithoutFingerprintGetsTypedSkip()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);
        var plan = (await _plans.GetByIdAsync(planId))!;
        plan.Files.Single().ExpectedFingerprint = string.Empty;
        await _plans.SavePlanAsync(plan);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Unavailable", skipped.ReasonCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        var record = Assert.Single(_recorder.Records);
        Assert.Equal(BatchApplyFileOutcome.Skipped, record.Outcome);
        Assert.Equal("Unavailable", record.ReasonCode);
    }

    [Fact]
    public async Task Apply_PartialFailureReportsPerFileOutcomes()
    {
        var stale = Encoding.UTF8.GetBytes("target stale");
        var good = Encoding.UTF8.GetBytes("target good");
        var (planId, stalePath) = await SeedAsync("doc1", "stale.md", stale);
        var (_, goodPath) = await SeedAsync("doc2", "good.md", good, planId);

        await File.WriteAllBytesAsync(stalePath, Encoding.UTF8.GetBytes("changed after preview"));

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(BatchApplyFileOutcome.Skipped, result.Files.Single(f => f.DocumentId == "doc1").Outcome);
        Assert.Equal("Stale", result.Files.Single(f => f.DocumentId == "doc1").ReasonCode);
        Assert.Equal(BatchApplyFileOutcome.Success, result.Files.Single(f => f.DocumentId == "doc2").Outcome);
        Assert.Equal("replaced good", await File.ReadAllTextAsync(goodPath));
        Assert.Equal(BatchPlanStatus.Completed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    [Fact]
    public async Task Apply_UnknownExternalEditStateSkipsSafely()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        var unknownProbeEngine = new BatchApplyEngine(
            _plans, _documents, _snapshots, new FakeProbe(ExternalEditState.Unknown),
            new AtomicFileReplacer(), _recorder, _reindex);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await unknownProbeEngine.ApplyAsync(planId, ReplaceTarget);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("LockUnknown", skipped.ReasonCode);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_UnselectedFilesAreNotMutated()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);
        var plan = (await _plans.GetByIdAsync(planId))!;
        plan.Files.Single().Selected = false;
        await _plans.SavePlanAsync(plan);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        Assert.Empty(result.Files);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_PreCanceledTokenThrowsAndDoesNotMutate()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _engine.ApplyAsync(planId, ReplaceTarget, null, cts.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(BatchPlanStatus.Previewed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    [Fact]
    public async Task Apply_ExpiredPlanIsRejected()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, _) = await SeedAsync("doc", "notes.md", original);
        var plan = (await _plans.GetByIdAsync(planId))!;
        plan.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _plans.SavePlanAsync(plan);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ApplyAsync(planId, ReplaceTarget));
        Assert.Contains("expired", rejected.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_CompletedPlanCannotBeAppliedTwice()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var first = await _engine.ApplyAsync(planId, ReplaceTarget);
        Assert.Equal(BatchApplyFileOutcome.Success, Assert.Single(first.Files).Outcome);

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => _engine.ApplyAsync(planId, ReplaceTarget));
        Assert.Contains("completed", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("replaced content", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Apply_SuccessCreatesRecoverySnapshotWithOriginalBytes()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedAsync("doc", "notes.md", original);

        async Task ReplaceTarget(string tempPath, CancellationToken token)
        {
            var text = await File.ReadAllTextAsync(tempPath, token);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
        }

        var result = await _engine.ApplyAsync(planId, ReplaceTarget);

        Assert.NotNull(result.SnapshotId);
        var snapshotRoot = _historyStorage.GetSnapshotRootPath(result.SnapshotId!);
        var snapshotFile = Assert.Single(Directory.GetFiles(snapshotRoot));
        Assert.Equal("notes.md", Path.GetFileName(snapshotFile));
        Assert.Equal(original, await File.ReadAllBytesAsync(snapshotFile));
        var record = Assert.Single(_recorder.Records);
        Assert.Equal(result.SnapshotId, record.SnapshotId);
    }

    [Fact]
    public async Task Apply_DocxFileAppliesThroughStrictMutator()
    {
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>target</w:t></w:r></w:p></w:body></w:document>");
        var (planId, path) = await SeedAsync("doc", "doc.docx", package);

        Task MutateTemp(string tempPath, CancellationToken token)
        {
            new StrictTextMutator().MutateText(tempPath, "target", "replaced", new StrictMutationOptions { CreateRecoverySnapshot = false });
            return Task.CompletedTask;
        }

        var result = await _engine.ApplyAsync(planId, MutateTemp);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);

        using var replaced = ZipFile.OpenRead(path);
        using var entry = replaced.GetEntry("word/document.xml")!.Open();
        using var reader = new StreamReader(entry);
        var documentXml = await reader.ReadToEndAsync();
        Assert.Contains("replaced", documentXml);
        Assert.DoesNotContain("target", documentXml);
    }

    private async Task<(string PlanId, string Path)> SeedAsync(string docId, string fileName, byte[] content, string? planId = null)
    {
        var path = Path.Combine(_root, fileName);
        await File.WriteAllBytesAsync(path, content);
        await _documents.CreateAsync(new Document { Id = docId, CurrentPath = path, Filename = fileName, Extension = Path.GetExtension(fileName) });
        BatchOperationPlan plan;
        if (planId == null)
        {
            plan = new BatchOperationPlan { WorkspaceId = "ws", Status = BatchPlanStatus.Previewed, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30) };
            await _plans.SavePlanAsync(plan);
        }
        else
        {
            plan = (await _plans.GetByIdAsync(planId))!;
        }
        plan.Files.Add(new BatchOperationPlanFile
        {
            PlanId = plan.Id,
            DocumentId = docId,
            ExpectedFingerprint = Convert.ToHexString(SHA256.HashData(content)),
            Selected = true
        });
        await _plans.SavePlanAsync(plan);
        return (plan.Id, path);
    }

    private sealed class FakeHistoryStorage : IHistoryStorage
    {
        private readonly string _snapshotRoot;
        private readonly long _capacityBytes;
        private readonly List<SnapshotMetadata> _metadata = new();
        private long _totalSize;

        public FakeHistoryStorage(string root, long capacityBytes = 5L * 1024 * 1024 * 1024)
        {
            _snapshotRoot = Path.Combine(root, "snapshots");
            _capacityBytes = capacityBytes;
        }

        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_snapshotRoot, snapshotId);
        public Task SaveMetadataAsync(SnapshotMetadata metadata) { _metadata.Add(metadata); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() => Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(_metadata);
        public Task DeleteSnapshotAsync(string snapshotId) { _metadata.RemoveAll(m => m.SnapshotId == snapshotId); return Task.CompletedTask; }
        public Task<long> GetTotalSizeAsync() => Task.FromResult(_totalSize);
        public Task<long> GetCapacityBytesAsync() => Task.FromResult(_capacityBytes);
    }

    private sealed class FakeHistoryRecorder : IBatchApplyHistoryRecorder
    {
        public List<BatchApplyHistoryRecord> Records { get; } = new();
        public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReindexTrigger : IBatchApplyReindexTrigger
    {
        public List<string> DocumentIds { get; } = new();
        public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCallbackProbe(Action onProbe) : IExternalEditStateProbe
    {
        public Task<ExternalEditState> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
        {
            onProbe();
            return Task.FromResult(ExternalEditState.NotLocked);
        }
    }

    private sealed class FakeProbe(ExternalEditState state) : IExternalEditStateProbe
    {
        public Task<ExternalEditState> ProbeAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(state);
    }

    private sealed class FakeTokenHonoringHistoryRecorder : IBatchApplyHistoryRecorder
    {
        public List<BatchApplyHistoryRecord> Records { get; } = new();
        public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFailingHistoryRecorder : IBatchApplyHistoryRecorder
    {
        public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("recorder unavailable");
    }
}
