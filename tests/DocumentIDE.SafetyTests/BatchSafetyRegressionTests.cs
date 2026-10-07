using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.History;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;

namespace DocumentIDE.SafetyTests;

/// <summary>
/// QA-04 — Batch Safety Regression Suite.
/// Regression matrix over the merged destructive-batch machinery (BAT-04 apply engine,
/// HIST-02 conflict-aware undo, BAT-10 strict rename, BAT-05 search &amp; replace): every
/// injected mutation/validation/lock/capacity failure must leave the reviewed source
/// byte-identical and produce a typed, inspectable outcome, and strict operations must
/// never touch a non-target occurrence, file, or content byte.
/// Real temp filesystem, real SQLite persistence stack, real snapshot service and real
/// atomic replacer; fault-injection seams only.
/// </summary>
public sealed class BatchSafetyRegressionTests : IDisposable
{
    private const string WorkspaceId = "ws-qa04";
    private const string FolderSourceId = "src-qa04";

    private readonly string _root;
    private readonly string _sourceDir;
    private readonly AppDb _db;
    private readonly WorkspaceRepository _workspaces;
    private readonly FolderSourceRepository _folderSources;
    private readonly DocumentRepository _documents;
    private readonly BatchPlanRepository _plans;
    private readonly FileHistoryStorage _history;
    private readonly RecoverySnapshotService _snapshots;

    public BatchSafetyRegressionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qa04-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(_sourceDir);

        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();

        _workspaces = new WorkspaceRepository(_db);
        _folderSources = new FolderSourceRepository(_db);
        _documents = new DocumentRepository(_db);
        _plans = new BatchPlanRepository(_db);

        _workspaces.CreateAsync(new Workspace { Id = WorkspaceId, Name = "QA-04" }).GetAwaiter().GetResult();
        _folderSources.CreateAsync(new FolderSource
        {
            Id = FolderSourceId,
            WorkspaceId = WorkspaceId,
            CanonicalPath = _sourceDir,
            DisplayName = "qa-04 source"
        }).GetAwaiter().GetResult();

        _history = new FileHistoryStorage(Path.Combine(_root, "history"));
        _snapshots = new RecoverySnapshotService(_history);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>Every injected failure mode a destructive apply can hit between approval and commit.</summary>
    public enum InjectedApplyFailure
    {
        MutationThrowsAfterTouchingTemp,
        ValidationThrowsAfterMutation,
        DestinationOpenByExternalEditorAtCheckTime,
        DestinationLockedAfterChecksPass,
        RecoveryCapacityInsufficient
    }

    // ---------------------------------------------------------------------
    // Required test 1 (mandated first): Original survives every injected failure.
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(InjectedApplyFailure.MutationThrowsAfterTouchingTemp)]
    [InlineData(InjectedApplyFailure.ValidationThrowsAfterMutation)]
    [InlineData(InjectedApplyFailure.DestinationOpenByExternalEditorAtCheckTime)]
    [InlineData(InjectedApplyFailure.DestinationLockedAfterChecksPass)]
    [InlineData(InjectedApplyFailure.RecoveryCapacityInsufficient)]
    public async Task OriginalSurvivesEveryInjectedFailure(InjectedApplyFailure injected)
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedPlanFileAsync("doc-matrix", "matrix.md", original);

        // Recovery capacity is simulated at the storage seam; every other component is real.
        IHistoryStorage storage = injected == InjectedApplyFailure.RecoveryCapacityInsufficient
            ? new FileHistoryStorage(Path.Combine(_root, "history"), capacityBytes: original.Length - 1)
            : _history;

        // An external editor grabbing the destination: either before the lock probe
        // (detected and skipped) or after the checks passed (the atomic commit itself
        // must fail closed).
        FileStream? externalLock = null;
        LockingReplacer? lockingReplacer = null;
        if (injected == InjectedApplyFailure.DestinationOpenByExternalEditorAtCheckTime)
            externalLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (injected == InjectedApplyFailure.DestinationLockedAfterChecksPass)
            lockingReplacer = new LockingReplacer();

        var engine = NewEngine(storage, lockingReplacer);

        Func<string, CancellationToken, Task> mutate = injected == InjectedApplyFailure.MutationThrowsAfterTouchingTemp
            ? async (tempPath, ct) =>
            {
                await File.AppendAllTextAsync(tempPath, "partial mutation", ct);
                throw new InvalidOperationException("injected mutation failure");
            }
            : ReplaceTargetsAsync;
        Func<string, CancellationToken, Task>? validate = injected == InjectedApplyFailure.ValidationThrowsAfterMutation
            ? (_, _) => throw new InvalidDataException("injected validation failure")
            : null;

        BatchApplyResult result;
        try
        {
            result = await engine.ApplyAsync(planId, mutate, validate);
        }
        finally
        {
            externalLock?.Dispose();
            lockingReplacer?.Dispose();
        }

        // (a) The reviewed source survives every injected failure byte-identically.
        Assert.Equal(original, await File.ReadAllBytesAsync(path));

        // (b) No transactional temp artifact is stranded next to the user's file.
        AssertNoApplyTempFiles();

        switch (injected)
        {
            case InjectedApplyFailure.RecoveryCapacityInsufficient:
                // (c) Mutation never started: fail closed with a typed, inspectable block.
                Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
                var blocked = Assert.Single(result.Files);
                Assert.Equal(BatchApplyFileOutcome.Failure, blocked.Outcome);
                Assert.Equal("RecoveryBlocked", blocked.ReasonCode);
                Assert.Null(result.SnapshotId);
                Assert.Empty(await storage.GetAllMetadataAsync());
                Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
                break;

            case InjectedApplyFailure.DestinationOpenByExternalEditorAtCheckTime:
                // (c) A detected external lock skips safely before any destructive step.
                Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
                var skipped = Assert.Single(result.Files);
                Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
                Assert.Equal("OpenInWord", skipped.ReasonCode);
                Assert.Null(result.SnapshotId);
                Assert.Empty(await storage.GetAllMetadataAsync());
                Assert.Equal(BatchPlanStatus.Completed, (await _plans.GetByIdAsync(planId))!.Status);
                break;

            default:
                // (c) Mutation/validation/commit failures are typed failures, never silent.
                Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
                var failed = Assert.Single(result.Files);
                Assert.Equal(BatchApplyFileOutcome.Failure, failed.Outcome);
                Assert.Equal("MutationFailed", failed.ReasonCode);
                Assert.False(string.IsNullOrWhiteSpace(failed.Detail));
                // (d) Recovery was created before the destructive step and still holds
                // exactly the pre-apply original bytes.
                Assert.NotNull(result.SnapshotId);
                Assert.Equal(original, await ReadSnapshotCopyAsync(storage, result.SnapshotId!, "doc-matrix"));
                Assert.Equal(BatchPlanStatus.Completed, (await _plans.GetByIdAsync(planId))!.Status);
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Required test 2: Stale plan rejected.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StalePlanRejected_ExpiredPlanThrowsBeforeAnyFileIsTouched()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedPlanFileAsync("doc-expired", "expired.md", original);
        var plan = (await _plans.GetByIdAsync(planId))!;
        plan.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        await _plans.SavePlanAsync(plan);

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewEngine().ApplyAsync(planId, ReplaceTargetsAsync));

        Assert.Contains("expired", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(BatchPlanStatus.Previewed, (await _plans.GetByIdAsync(planId))!.Status);
        Assert.Empty(await _history.GetAllMetadataAsync());
        AssertNoApplyTempFiles();
    }

    [Fact]
    public async Task StalePlanRejected_FileChangedAfterPreviewIsNeverOverwritten()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedPlanFileAsync("doc-stale", "stale.md", original);

        // The file changes after preview: the reviewed fingerprint no longer matches.
        var newer = Encoding.UTF8.GetBytes("target content changed after preview");
        await File.WriteAllBytesAsync(path, newer);

        var mutationAttempts = 0;
        Task Mutate(string tempPath, CancellationToken ct)
        {
            mutationAttempts++;
            return ReplaceTargetsAsync(tempPath, ct);
        }

        var result = await NewEngine().ApplyAsync(planId, Mutate);

        // Rejected before any temp mutation, typed and inspectable: the newer file
        // is never overwritten and no recovery snapshot is created for a stale set.
        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Stale", skipped.ReasonCode);
        Assert.Equal(0, mutationAttempts);
        Assert.Null(result.SnapshotId);
        Assert.Equal(newer, await File.ReadAllBytesAsync(path));
        Assert.Empty(await _history.GetAllMetadataAsync());
        Assert.Equal(BatchPlanStatus.Completed, (await _plans.GetByIdAsync(planId))!.Status);
        AssertNoApplyTempFiles();
    }

    // ---------------------------------------------------------------------
    // Required test 3: No-snapshot capacity blocks apply.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task NoSnapshotCapacityBlocksApply_AndExistingRecoveryIsNeverDeleted()
    {
        var one = Encoding.UTF8.GetBytes(new string('A', 200));
        var two = Encoding.UTF8.GetBytes(new string('B', 150));
        var (planId, onePath) = await SeedPlanFileAsync("doc-cap-one", "cap-one.md", one);
        var (_, twoPath) = await SeedPlanFileAsync("doc-cap-two", "cap-two.md", two, planId);

        // An earlier, still-unexpired recovery snapshot occupies part of the budget.
        var earlier = Encoding.UTF8.GetBytes(new string('C', 100));
        var earlierPath = Path.Combine(_sourceDir, "earlier.md");
        await File.WriteAllBytesAsync(earlierPath, earlier);
        var earlierSnapshot = await _snapshots.CreateSnapshotAsync(
            "op-earlier", new[] { new SnapshotSourceFile("doc-earlier", earlierPath) });

        var existingSize = await _history.GetTotalSizeAsync();
        // Room exists only if the earlier snapshot were sacrificed to make room.
        var capacity = existingSize + one.Length + two.Length - 50;
        Assert.True(one.Length + two.Length <= capacity, "injected capacity must fit the new snapshot alone");
        var constrained = new FileHistoryStorage(Path.Combine(_root, "history"), capacityBytes: capacity);

        var result = await NewEngine(constrained).ApplyAsync(planId, ReplaceTargetsAsync);

        // Fail closed before the first destructive mutation: typed block for every file.
        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, f =>
        {
            Assert.Equal(BatchApplyFileOutcome.Failure, f.Outcome);
            Assert.Equal("RecoveryBlocked", f.ReasonCode);
        });
        Assert.Null(result.SnapshotId);
        Assert.Equal(one, await File.ReadAllBytesAsync(onePath));
        Assert.Equal(two, await File.ReadAllBytesAsync(twoPath));
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
        AssertNoApplyTempFiles();

        // The earlier snapshot is never sacrificed to make room: its metadata and
        // byte-exact recovery copy are still present after the blocked apply.
        var metadata = Assert.Single(await constrained.GetAllMetadataAsync());
        Assert.Equal("op-earlier", metadata.OperationId);
        Assert.Equal(earlierSnapshot.SnapshotId, metadata.SnapshotId);
        var copyPath = Path.Combine(
            constrained.GetSnapshotRootPath(metadata.SnapshotId), metadata.Files.Single().SnapshotFileName);
        Assert.Equal(earlier, await File.ReadAllBytesAsync(copyPath));
    }

    // ---------------------------------------------------------------------
    // Required test 4: Undo conflict does not overwrite newer edit.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UndoConflictDoesNotOverwriteNewerEdit()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedPlanFileAsync("doc-undo", "undo.md", original);

        var applied = await NewEngine().ApplyAsync(planId, ReplaceTargetsAsync);
        Assert.Equal(BatchApplyFileOutcome.Success, Assert.Single(applied.Files).Outcome);
        Assert.Equal("replaced content", await File.ReadAllTextAsync(path));

        // A newer edit lands after the operation, written through the product's own
        // atomic-replace discipline (real component; no mocks).
        var newer = Encoding.UTF8.GetBytes("newer external work");
        var replacer = new AtomicFileReplacer();
        var prepared = await replacer.PrepareAsync(
            path, (temp, ct) => File.WriteAllBytesAsync(temp, newer, ct), null, CancellationToken.None);
        await replacer.CommitAsync(prepared);
        Assert.Equal(newer, await File.ReadAllBytesAsync(path));

        var recorder = new RecordingUndoHistoryRecorder();
        var undo = new UndoService(
            _history, _documents, new AtomicFileReplacer(), new WordEditStateProbe(),
            new RecordingReindexTrigger(), recorder);

        var result = await undo.UndoAsync(planId);

        // Conflicted: the newer edit is byte-identical and the operation is not
        // recorded as undone; the recovery copy still holds the pre-apply original.
        Assert.Equal(UndoResultStatus.Blocked, result.Status);
        var conflict = Assert.Single(result.Files);
        Assert.Equal(UndoFileOutcome.Conflict, conflict.Outcome);
        Assert.Equal("ExternallyModified", conflict.ReasonCode);
        Assert.Equal(newer, await File.ReadAllBytesAsync(path));
        Assert.Empty(recorder.Records);
        Assert.Equal(original, await ReadSnapshotCopyAsync(_history, applied.SnapshotId!, "doc-undo"));
        AssertNoApplyTempFiles();

        // The conflict is stable: a retry refuses again and the newer edit survives.
        var second = await undo.UndoAsync(planId);
        Assert.Equal(UndoResultStatus.Blocked, second.Status);
        Assert.Equal(UndoFileOutcome.Conflict, Assert.Single(second.Files).Outcome);
        Assert.Equal(newer, await File.ReadAllBytesAsync(path));
    }

    // ---------------------------------------------------------------------
    // Step 5: strict-operation non-target invariants.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StrictSearchReplaceTouchesOnlySelectedOccurrenceAndReviewedFile()
    {
        var targetBytes = Encoding.UTF8.GetBytes("target here and target there");
        var deselectedBytes = Encoding.UTF8.GetBytes("target stays exactly");
        var outsideBytes = Encoding.UTF8.GetBytes("target outside the reviewed plan");

        var (planId, targetPath) = await SeedPlanFileAsync("doc-target", "target.md", targetBytes);
        var (_, deselectedPath) = await SeedPlanFileAsync("doc-deselected", "deselected.md", deselectedBytes, planId, selected: false);
        var outsidePath = Path.Combine(_sourceDir, "outside.md");
        await File.WriteAllBytesAsync(outsidePath, outsideBytes);

        var operation = new SearchReplaceOperation(new StrictTextMutator());
        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "Replaced" };
        var occurrences = await operation.BuildOccurrencesAsync(targetPath, parameters);
        Assert.Equal(2, occurrences.Count);
        await SelectAsync(planId, "doc-target", new[] { occurrences[1].Id });

        var mutate = operation.CreateMutate(parameters, await CreateSearchReplaceTargetsAsync(planId));
        var result = await NewEngine().ApplyAsync(planId, mutate);

        // Only the reviewed, selected occurrence of the reviewed file changed.
        var success = Assert.Single(result.Files);
        Assert.Equal("doc-target", success.DocumentId);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Equal("target here and Replaced there", await File.ReadAllTextAsync(targetPath));

        // The deselected occurrence, the unselected plan file, and the file outside
        // the plan are byte-identical — destructive scope never widens silently.
        Assert.Equal(deselectedBytes, await File.ReadAllBytesAsync(deselectedPath));
        Assert.Equal(outsideBytes, await File.ReadAllBytesAsync(outsidePath));
        AssertNoApplyTempFiles();
    }

    [Fact]
    public async Task StrictRenameTouchesOnlyReviewedTargetAndPreservesContent()
    {
        var content = Encoding.UTF8.GetBytes("rename me, but never rewrite my bytes");
        var bystander = Encoding.UTF8.GetBytes("bystander file bytes");
        var path = await SeedDocumentFileAsync("doc-ren", "ren.md", content);
        var bystanderPath = Path.Combine(_sourceDir, "bystander.md");
        await File.WriteAllBytesAsync(bystanderPath, bystander);

        var result = await NewRenameOperation().ApplyAsync(
            new[] { new BatchRenameTarget("doc-ren", path, Sha256Hex(content), "ren-renamed.md") });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        var renamed = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, renamed.Outcome);

        var newPath = Path.Combine(_sourceDir, "ren-renamed.md");
        Assert.Equal(newPath, renamed.TargetPath);
        Assert.False(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(newPath));

        // Non-target files keep their names and their bytes; no extra artifact appears.
        Assert.Equal(bystander, await File.ReadAllBytesAsync(bystanderPath));
        Assert.Equal(
            new[] { "bystander.md", "ren-renamed.md" },
            Directory.GetFiles(_sourceDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));

        // The document identity survives; only the path-carrying fields move.
        var document = await _documents.GetByIdAsync("doc-ren");
        Assert.Equal("doc-ren", document!.Id);
        Assert.Equal(newPath, document.CurrentPath);
        Assert.Equal(path, document.LastKnownPath);
    }

    [Fact]
    public async Task StrictRenameUnderExternalLockFailsClosed()
    {
        var content = Encoding.UTF8.GetBytes("locked rename content");
        var path = await SeedDocumentFileAsync("doc-locked", "locked.md", content);

        // An external editor holds the file (read allowed, delete denied): the
        // physical rename must fail typed instead of forcing anything through.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await NewRenameOperation().ApplyAsync(
                new[] { new BatchRenameTarget("doc-locked", path, Sha256Hex(content), "locked-renamed.md") });

            Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
            var failed = Assert.Single(result.Files);
            Assert.Equal(BatchApplyFileOutcome.Failure, failed.Outcome);
            Assert.Equal("RenameFailed", failed.ReasonCode);
            Assert.False(string.IsNullOrWhiteSpace(failed.Detail));
        }

        // Original name, bytes, and workspace path are all unchanged; no target created.
        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Path.Combine(_sourceDir, "locked-renamed.md")));
        Assert.Equal(path, (await _documents.GetByIdAsync("doc-locked"))!.CurrentPath);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private BatchRenameOperation NewRenameOperation()
    {
        var identityProvider = new WindowsFileIdentityProvider();
        var identityService = new DocumentIdentityService(_documents, identityProvider);
        var correlation = new MoveCorrelationService(_documents, identityProvider, identityService, _folderSources);
        var physical = new PhysicalFileOperations(correlation, recycleBin: null, documentRepository: _documents);
        return new BatchRenameOperation(physical, _documents);
    }

    private async Task SelectAsync(string planId, string documentId, IReadOnlyList<string> occurrenceIds)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var file = plan.Files.Single(f => f.DocumentId == documentId);
        file.Selected = true;
        file.SelectedChangeIds = occurrenceIds.Distinct(StringComparer.Ordinal).ToList();
        await _plans.SavePlanAsync(plan);
    }

    private async Task<IReadOnlyList<SearchReplaceTarget>> CreateSearchReplaceTargetsAsync(string planId)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var targets = new List<SearchReplaceTarget>();
        foreach (var file in plan.Files.OrderBy(f => f.DocumentId, StringComparer.Ordinal))
        {
            var document = await _documents.GetByIdAsync(file.DocumentId);
            targets.Add(new SearchReplaceTarget(document!.CurrentPath, file.ExpectedFingerprint, file.SelectedChangeIds));
        }
        return targets;
    }

    private BatchApplyEngine NewEngine(IHistoryStorage? storage = null, IAtomicFileReplacer? replacer = null) =>
        new(
            _plans,
            _documents,
            new RecoverySnapshotService(storage ?? _history),
            new WordEditStateProbe(),
            replacer ?? new AtomicFileReplacer(),
            new RecordingHistoryRecorder(),
            new RecordingReindexTrigger());

    private static async Task ReplaceTargetsAsync(string tempPath, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(tempPath, ct);
        await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), ct);
    }

    private async Task<(string PlanId, string Path)> SeedPlanFileAsync(
        string documentId,
        string fileName,
        byte[] content,
        string? planId = null,
        bool selected = true)
    {
        var path = await SeedDocumentFileAsync(documentId, fileName, content);

        BatchOperationPlan plan;
        if (planId == null)
        {
            plan = new BatchOperationPlan
            {
                WorkspaceId = WorkspaceId,
                Status = BatchPlanStatus.Previewed,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            };
            await _plans.SavePlanAsync(plan);
        }
        else
        {
            plan = (await _plans.GetByIdAsync(planId))!;
        }

        plan.Files.Add(new BatchOperationPlanFile
        {
            PlanId = plan.Id,
            DocumentId = documentId,
            ExpectedFingerprint = Sha256Hex(content),
            Selected = selected
        });
        await _plans.SavePlanAsync(plan);
        return (plan.Id, path);
    }

    private async Task<string> SeedDocumentFileAsync(string documentId, string fileName, byte[] content)
    {
        var path = Path.Combine(_sourceDir, fileName);
        await File.WriteAllBytesAsync(path, content);
        await _documents.CreateAsync(new Document
        {
            Id = documentId,
            CurrentPath = path,
            Filename = fileName,
            Extension = Path.GetExtension(fileName),
            DocumentType = "markdown"
        });
        return path;
    }

    private async Task<byte[]> ReadSnapshotCopyAsync(IHistoryStorage storage, string snapshotId, string documentId)
    {
        var metadata = (await storage.GetAllMetadataAsync()).Single(m => m.SnapshotId == snapshotId);
        var record = metadata.Files.Single(f => f.DocumentId == documentId);
        return await File.ReadAllBytesAsync(Path.Combine(storage.GetSnapshotRootPath(snapshotId), record.SnapshotFileName));
    }

    private void AssertNoApplyTempFiles() =>
        Assert.DoesNotContain(
            Directory.GetFiles(_root, "*", SearchOption.AllDirectories),
            f => Path.GetFileName(f).StartsWith("~apply-", StringComparison.Ordinal));

    /// <summary>
    /// Fault injection (Step 2): the destination is grabbed by an external process after
    /// the engine's lock probe and fingerprint checks passed, so the atomic commit must
    /// fail and the engine must roll the temp back without touching the original.
    /// </summary>
    private sealed class LockingReplacer : IAtomicFileReplacer, IDisposable
    {
        private readonly AtomicFileReplacer _inner = new();
        private FileStream? _lockHandle;

        public async Task<PreparedFileReplacement> PrepareAsync(
            string sourcePath,
            Func<string, CancellationToken, Task> mutate,
            Func<string, CancellationToken, Task>? validate,
            CancellationToken cancellationToken)
        {
            var prepared = await _inner.PrepareAsync(sourcePath, mutate, validate, cancellationToken);
            _lockHandle = new FileStream(prepared.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return prepared;
        }

        public Task CommitAsync(PreparedFileReplacement replacement, CancellationToken cancellationToken = default) =>
            _inner.CommitAsync(replacement, cancellationToken);

        public Task RollbackAsync(PreparedFileReplacement replacement, CancellationToken cancellationToken = default)
        {
            ReleaseLock();
            return _inner.RollbackAsync(replacement, cancellationToken);
        }

        private void ReleaseLock()
        {
            _lockHandle?.Dispose();
            _lockHandle = null;
        }

        public void Dispose() => ReleaseLock();
    }

    private sealed class RecordingHistoryRecorder : IBatchApplyHistoryRecorder
    {
        public List<BatchApplyHistoryRecord> Records { get; } = new();

        public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReindexTrigger : IBatchApplyReindexTrigger
    {
        public List<string> DocumentIds { get; } = new();

        public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUndoHistoryRecorder : IUndoHistoryRecorder
    {
        public List<UndoRecord> Records { get; } = new();

        public Task RecordAsync(UndoRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Test-local real-filesystem replacement for <c>Infrastructure.History.HistoryStorage</c>,
    /// which hardcodes %LOCALAPPDATA%. Snapshot copies are real files; metadata is real JSON
    /// on disk; capacity and total size mirror the production storage semantics.
    /// </summary>
    private sealed class FileHistoryStorage : IHistoryStorage
    {
        private readonly string _basePath;
        private readonly string _metadataPath;
        private readonly long _capacityBytes;

        public FileHistoryStorage(string basePath, long capacityBytes = 5L * 1024 * 1024 * 1024)
        {
            _basePath = Path.Combine(basePath, "snapshots");
            _metadataPath = Path.Combine(_basePath, "metadata");
            Directory.CreateDirectory(_metadataPath);
            _capacityBytes = capacityBytes;
        }

        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_basePath, snapshotId);

        public async Task SaveMetadataAsync(SnapshotMetadata metadata)
        {
            var totalSize = await GetTotalSizeAsync();
            if (_capacityBytes > 0 && totalSize + metadata.SizeBytes > _capacityBytes)
                throw new InvalidOperationException("Insufficient capacity for new snapshot.");

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(_metadataPath, $"{metadata.SnapshotId}.json"), json);
        }

        public async Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync()
        {
            var list = new List<SnapshotMetadata>();
            foreach (var file in Directory.GetFiles(_metadataPath, "*.json"))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<SnapshotMetadata>(await File.ReadAllTextAsync(file));
                    if (meta != null) list.Add(meta);
                }
                catch
                {
                    // Mirror HistoryStorage: malformed entries are ignored.
                }
            }
            return list.AsReadOnly();
        }

        public Task DeleteSnapshotAsync(string snapshotId)
        {
            var dir = GetSnapshotRootPath(snapshotId);
            var meta = Path.Combine(_metadataPath, $"{snapshotId}.json");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            if (File.Exists(meta)) File.Delete(meta);
            return Task.CompletedTask;
        }

        public Task<long> GetTotalSizeAsync()
        {
            long total = 0;
            if (Directory.Exists(_basePath))
            {
                foreach (var dir in Directory.GetDirectories(_basePath))
                {
                    total += new DirectoryInfo(dir)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => f.Length);
                }
            }
            return Task.FromResult(total);
        }

        public Task<long> GetCapacityBytesAsync() => Task.FromResult(_capacityBytes);
    }
}
