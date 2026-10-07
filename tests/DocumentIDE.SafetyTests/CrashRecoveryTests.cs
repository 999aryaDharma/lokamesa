using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.History;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;
using Xunit;

namespace DocumentIDE.SafetyTests;

/// <summary>
/// QA-07 — Crash &amp; Interrupted-Operation Recovery.
/// Every test constructs the exact on-disk and persisted state a process crash leaves
/// behind (leftover <c>~apply-*.tmp</c> files, plans stuck in <c>Executing</c>, recovery
/// snapshots present or absent, half-written temps) and proves that:
///  (a) the source file remains valid and byte-identical at every injected crash point,
///  (b) startup repair marks/reconciles the incomplete operation instead of inferring success,
///  (c) temp artifacts are safely cleanable.
/// Tests use a real temp filesystem, the real SQLite persistence stack, the real
/// snapshot service, and the real atomic replacer — no filesystem mocks.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private const string WorkspaceId = "ws-qa07";
    private const string FolderSourceId = "src-1";

    private readonly string _root;
    private readonly string _sourceDir;
    private readonly AppDb _db;
    private readonly WorkspaceRepository _workspaces;
    private readonly FolderSourceRepository _folderSources;
    private readonly DocumentRepository _documents;
    private readonly BatchPlanRepository _plans;
    private readonly FileHistoryStorage _history;
    private readonly RecoverySnapshotService _snapshots;

    public CrashRecoveryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qa07-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(_sourceDir);

        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();

        _workspaces = new WorkspaceRepository(_db);
        _folderSources = new FolderSourceRepository(_db);
        _documents = new DocumentRepository(_db);
        _plans = new BatchPlanRepository(_db);

        _workspaces.CreateAsync(new Workspace { Id = WorkspaceId, Name = "QA-07" }).GetAwaiter().GetResult();
        _folderSources.CreateAsync(new FolderSource
        {
            Id = FolderSourceId,
            WorkspaceId = WorkspaceId,
            CanonicalPath = _sourceDir,
            DisplayName = "qa-07 source"
        }).GetAwaiter().GetResult();

        _history = new FileHistoryStorage(Path.Combine(_root, "history"));
        _snapshots = new RecoverySnapshotService(_history);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task CrashDuringTempMutation_SourceStaysByteIdentical_AndStartupRecoveryRepairsState()
    {
        var original = Encoding.UTF8.GetBytes("original document bytes");
        var (planId, path) = await SeedPlanAsync("doc-crash-temp", "notes.md", original, BatchPlanStatus.Executing);

        // Exact state a process crash during temp mutation leaves behind: the reviewed
        // plan is stuck in Executing, the recovery snapshot was already created, and a
        // half-written application temp file sits next to the untouched source.
        await _snapshots.CreateSnapshotAsync(planId, new[] { new SnapshotSourceFile("doc-crash-temp", path) });
        var tempPath = Path.Combine(_sourceDir, $"~apply-{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(tempPath, Encoding.UTF8.GetBytes("half-written mutation"));

        var report = await NewStartupRecovery().RecoverAsync();

        // (a) source valid and byte-consistent, before and after repair.
        Assert.Equal(original, await File.ReadAllBytesAsync(path));

        // (c) temp artifact safely cleanable.
        Assert.False(File.Exists(tempPath));
        var cleanup = Assert.Single(report.OrphanTempFiles);
        Assert.Equal(tempPath, cleanup.Path);
        Assert.Equal(OrphanTempFileCleanupOutcome.Removed, cleanup.Outcome);

        // (b) incomplete operation reconciled; success never inferred.
        var repair = Assert.Single(report.InterruptedPlans);
        Assert.Equal(planId, repair.PlanId);
        Assert.Equal(WorkspaceId, repair.WorkspaceId);
        Assert.Equal(InterruptedPlanRepairOutcome.MarkedFailed, repair.Outcome);
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
        Assert.True(report.IsClean);
    }

    [Fact]
    public async Task CrashAfterSnapshotBeforeReplace_SourceStaysOriginal_AndSnapshotRemainsUndoable()
    {
        var original = Encoding.UTF8.GetBytes("reviewed original");
        var (planId, path) = await SeedPlanAsync("doc-crash-snapshot", "plan.md", original, BatchPlanStatus.Executing);
        await _snapshots.CreateSnapshotAsync(planId, new[] { new SnapshotSourceFile("doc-crash-snapshot", path) });

        var report = await NewStartupRecovery().RecoverAsync();

        // (a) the source was never touched and is still exactly the reviewed bytes.
        Assert.Equal(original, await File.ReadAllBytesAsync(path));

        // (c) no temp existed at this crash point, and nothing was fabricated.
        Assert.Empty(report.OrphanTempFiles);

        // (b) the plan is reconciled as failed, never completed.
        var repair = Assert.Single(report.InterruptedPlans);
        Assert.Equal(planId, repair.PlanId);
        Assert.Equal(InterruptedPlanRepairOutcome.MarkedFailed, repair.Outcome);
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
        Assert.True(report.IsClean);

        // Startup repair must not destroy recovery data: verification is still possible.
        var metadata = Assert.Single(await _history.GetAllMetadataAsync());
        Assert.Equal(planId, metadata.OperationId);

        var undo = await NewUndoService().UndoAsync(planId);
        Assert.Equal(UndoFileOutcome.AlreadyRestored, Assert.Single(undo.Files).Outcome);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CrashAfterAtomicReplaceBeforeHistoryFinalization_PlanReconciled_AndUndoVerifiesReplace()
    {
        var original = Encoding.UTF8.GetBytes("before replace");
        var postOperation = Encoding.UTF8.GetBytes("after replace");
        var (planId, path) = await SeedPlanAsync("doc-crash-replace", "report.md", original, BatchPlanStatus.Executing);
        var snapshot = await _snapshots.CreateSnapshotAsync(planId, new[] { new SnapshotSourceFile("doc-crash-replace", path) });

        // Reproduce the engine's write order exactly: prepare a same-volume temp copy,
        // record the post-operation fingerprint from the prepared temp, then commit
        // atomically. The crash boundary is immediately after the commit.
        var replacer = new AtomicFileReplacer();
        var prepared = await replacer.PrepareAsync(
            path,
            (tempPath, ct) => File.WriteAllBytesAsync(tempPath, postOperation, ct),
            validate: null,
            CancellationToken.None);
        await _snapshots.RecordPostOperationFingerprintAsync(
            snapshot, "doc-crash-replace", Sha256Hex(await File.ReadAllBytesAsync(prepared.TempPath)));
        await replacer.CommitAsync(prepared);

        // The atomic replace already happened; only history finalization and plan
        // completion were lost to the crash.
        Assert.Equal(postOperation, await File.ReadAllBytesAsync(path));

        var report = await NewStartupRecovery().RecoverAsync();

        // (a) the replaced source is a complete, valid document, untouched by recovery.
        Assert.Equal(postOperation, await File.ReadAllBytesAsync(path));

        // (b) interrupted plan reconciled, success never inferred from the crash itself.
        var repair = Assert.Single(report.InterruptedPlans);
        Assert.Equal(planId, repair.PlanId);
        Assert.Equal(InterruptedPlanRepairOutcome.MarkedFailed, repair.Outcome);
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);
        Assert.Empty(report.OrphanTempFiles);

        // The committed mutation is only claimed through fingerprint verification:
        // the recorded post-operation fingerprint matches, so undo restores the original.
        var undo = await NewUndoService().UndoAsync(planId);
        Assert.Equal(UndoFileOutcome.Restored, Assert.Single(undo.Files).Outcome);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task OrphanTempCleanup_RemovesOnlyExactApplyTemps_AndNeverTouchesUserFiles()
    {
        var userFile = Path.Combine(_sourceDir, "notes.md");
        await File.WriteAllTextAsync(userFile, "user content");

        var exactLower = Path.Combine(_sourceDir, $"~apply-{Guid.NewGuid():N}.tmp");
        var exactUpper = Path.Combine(_sourceDir, "~apply-" + Guid.NewGuid().ToString("N").ToUpperInvariant() + ".tmp");
        var nestedDir = Path.Combine(_sourceDir, "sub", "deep");
        Directory.CreateDirectory(nestedDir);
        var nested = Path.Combine(nestedDir, $"~apply-{Guid.NewGuid():N}.tmp");
        foreach (var temp in new[] { exactLower, exactUpper, nested })
        {
            await File.WriteAllTextAsync(temp, "half-written mutation");
        }

        var decoys = new[]
        {
            Path.Combine(_sourceDir, "~apply-not-a-guid.tmp"),
            Path.Combine(_sourceDir, "~apply-1234.tmp"),
            Path.Combine(_sourceDir, "~apply-" + new string('a', 31) + ".tmp"),
            Path.Combine(_sourceDir, "~apply-" + new string('a', 33) + ".tmp"),
            Path.Combine(_sourceDir, "apply-" + Guid.NewGuid().ToString("N") + ".tmp"),
            Path.Combine(_sourceDir, "~$notes.md")
        };
        foreach (var decoy in decoys)
        {
            await File.WriteAllTextAsync(decoy, "keep me");
        }

        // Hidden folders are not part of the document source surface (StartupReconciler
        // skips them), so their contents are never treated as application temp files.
        var hiddenDir = Path.Combine(_sourceDir, ".git");
        Directory.CreateDirectory(hiddenDir);
        var hiddenTemp = Path.Combine(hiddenDir, $"~apply-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(hiddenTemp, "not part of the document surface");

        var report = await NewStartupRecovery().RecoverAsync();

        Assert.Equal(3, report.OrphanTempFiles.Count);
        Assert.All(report.OrphanTempFiles, c => Assert.Equal(OrphanTempFileCleanupOutcome.Removed, c.Outcome));
        Assert.False(File.Exists(exactLower));
        Assert.False(File.Exists(exactUpper));
        Assert.False(File.Exists(nested));
        foreach (var decoy in decoys)
        {
            Assert.True(File.Exists(decoy), $"decoy must survive cleanup: {decoy}");
        }
        Assert.True(File.Exists(hiddenTemp));
        Assert.Equal("user content", await File.ReadAllTextAsync(userFile));
        Assert.True(report.IsClean);
    }

    [Fact]
    public async Task OrphanTempBehindJunction_IsNeverTraversed_OrDeleted()
    {
        var outsideDir = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outsideDir);
        var outsideTemp = Path.Combine(outsideDir, $"~apply-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(outsideTemp, "outside the configured source");

        var junctionPath = Path.Combine(_sourceDir, "linked");
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C mklink /J \"{junctionPath}\" \"{outsideDir}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(startInfo)!)
        {
            await process.WaitForExitAsync();
        }
        Assert.True(SourceContainmentPolicy.IsReparsePoint(junctionPath), "junction setup failed");

        var report = await NewStartupRecovery().RecoverAsync();

        // Cleanup never follows a reparse point: nothing outside the source is touched.
        Assert.True(File.Exists(outsideTemp));
        Assert.Empty(report.OrphanTempFiles);
        Assert.True(report.IsClean);
    }

    [Fact]
    public async Task LockedOrphanTemp_ReportsTypedDeleteFailure_AndIsCleanableLater()
    {
        var tempPath = Path.Combine(_sourceDir, $"~apply-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, "still held by a crashed helper");

        using (new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = await NewStartupRecovery().RecoverAsync();

            var cleanup = Assert.Single(report.OrphanTempFiles);
            Assert.Equal(tempPath, cleanup.Path);
            Assert.Equal(OrphanTempFileCleanupOutcome.DeleteFailed, cleanup.Outcome);
            Assert.False(string.IsNullOrWhiteSpace(cleanup.Detail));
            Assert.False(report.IsClean);
            Assert.True(File.Exists(tempPath));
        }

        // A delete failure is transient state, not lost state: once released, the
        // artifact is removed by the next startup pass.
        var second = await NewStartupRecovery().RecoverAsync();
        Assert.Equal(OrphanTempFileCleanupOutcome.Removed, Assert.Single(second.OrphanTempFiles).Outcome);
        Assert.False(File.Exists(tempPath));
        Assert.True(second.IsClean);
    }

    [Fact]
    public async Task OnlyExecutingPlansAreReconciled_AndRepairCoversEveryWorkspace()
    {
        var bytes = Encoding.UTF8.GetBytes("state matrix");

        var untouchedPlans = new Dictionary<string, BatchPlanStatus>();
        foreach (var (documentId, status) in new[]
        {
            ("doc-draft", BatchPlanStatus.Draft),
            ("doc-previewed", BatchPlanStatus.Previewed),
            ("doc-completed", BatchPlanStatus.Completed),
            ("doc-failed", BatchPlanStatus.Failed),
            ("doc-expired", BatchPlanStatus.Expired)
        })
        {
            var (planId, _) = await SeedPlanAsync(documentId, documentId + ".md", bytes, status);
            untouchedPlans[planId] = status;
        }

        // A second workspace proves recovery enumerates all workspaces, not just one.
        await _workspaces.CreateAsync(new Workspace { Id = "ws-other", Name = "second" });
        var (otherPlanId, otherPath) = await SeedPlanAsync("doc-other", "other.md", bytes, BatchPlanStatus.Executing, workspaceId: "ws-other");

        var report = await NewStartupRecovery().RecoverAsync();

        var repair = Assert.Single(report.InterruptedPlans);
        Assert.Equal(otherPlanId, repair.PlanId);
        Assert.Equal("ws-other", repair.WorkspaceId);
        Assert.Equal(InterruptedPlanRepairOutcome.MarkedFailed, repair.Outcome);

        foreach (var (planId, status) in untouchedPlans)
        {
            Assert.Equal(status, (await _plans.GetByIdAsync(planId))!.Status);
        }
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(otherPlanId))!.Status);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(otherPath));
    }

    [Fact]
    public async Task InterruptedPlanWithoutSnapshot_IsReconciled_AndUndoRefusesInsteadOfInferring()
    {
        var original = Encoding.UTF8.GetBytes("no snapshot was ever created");
        var (planId, path) = await SeedPlanAsync("doc-nosnap", "nosnap.md", original, BatchPlanStatus.Executing);

        var report = await NewStartupRecovery().RecoverAsync();

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(InterruptedPlanRepairOutcome.MarkedFailed, Assert.Single(report.InterruptedPlans).Outcome);
        Assert.Equal(BatchPlanStatus.Failed, (await _plans.GetByIdAsync(planId))!.Status);

        // Without a snapshot there is no verifiable state, so undo refuses typed
        // instead of guessing that the interruption succeeded or failed.
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewUndoService().UndoAsync(planId));
    }

    [Fact]
    public async Task CrashDuringIndexing_SourceStaysByteIdentical_AndIndexStateIsTypedNotReady()
    {
        var bytes = Encoding.UTF8.GetBytes("# Title\n\nSome searchable body.");
        var path = Path.Combine(_sourceDir, "indexed.md");
        await File.WriteAllBytesAsync(path, bytes);
        await _documents.CreateAsync(new Document
        {
            Id = "doc-index",
            CurrentPath = path,
            Filename = "indexed.md",
            Extension = ".md",
            DocumentType = "markdown"
        });

        var interrupted = new IncrementalIndexCoordinator(
            _documents,
            new DocumentMetadataRepository(_db),
            new ThrowingFtsIndex(),
            new MarkdownStructureParser());
        await interrupted.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Modified,
            DocumentId = "doc-index",
            Path = path
        });

        // (a) indexing never mutates the source; the crash point is byte-clean.
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));

        // (b) the interrupted index run is typed as error — never inferred as ready.
        Assert.Equal("error", interrupted.GetIndexState("doc-index"));
        Assert.Equal("error", (await _documents.GetByIdAsync("doc-index"))!.IndexStatus);

        // The document is fully reindexable afterwards; the source stays untouched.
        var recovered = new IncrementalIndexCoordinator(
            _documents,
            new DocumentMetadataRepository(_db),
            new FtsIndex(_db),
            new MarkdownStructureParser());
        await recovered.ReindexDocumentAsync("doc-index");

        Assert.Equal("ready", (await _documents.GetByIdAsync("doc-index"))!.IndexStatus);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var results = await new FtsIndex(_db).SearchAsync("searchable");
        Assert.Contains(results, r => r.DocumentId == "doc-index");
    }

    [Fact]
    public async Task InjectedValidationFailureAfterSnapshot_LeavesSourceUnchanged_WithNoTempLeftover()
    {
        var original = Encoding.UTF8.GetBytes("target content");
        var (planId, path) = await SeedPlanAsync("doc-validate", "validated.md", original, BatchPlanStatus.Previewed);
        var engine = new BatchApplyEngine(
            _plans,
            _documents,
            _snapshots,
            new WordEditStateProbe(),
            new AtomicFileReplacer(),
            new FakeHistoryRecorder(),
            new FakeReindexTrigger());

        static async Task Mutate(string tempPath, CancellationToken ct)
        {
            var text = await File.ReadAllTextAsync(tempPath, ct);
            await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), ct);
        }

        static Task Validate(string tempPath, CancellationToken ct) =>
            throw new InvalidDataException("injected crash during validation, after snapshot and temp mutation");

        var result = await engine.ApplyAsync(planId, Mutate, Validate);

        // (a) the source remains valid and byte-identical at this crash boundary.
        var failure = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, failure.Outcome);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.DoesNotContain(
            Directory.GetFiles(_sourceDir, "*", SearchOption.AllDirectories),
            f => Path.GetFileName(f).StartsWith("~apply-", StringComparison.Ordinal));

        // The recovery snapshot was created before the mutation and still holds the
        // exact original bytes — startup cleanup must never touch recovery data.
        var metadata = Assert.Single(await _history.GetAllMetadataAsync());
        var snapshotCopy = Path.Combine(
            _history.GetSnapshotRootPath(metadata.SnapshotId),
            Assert.Single(metadata.Files).SnapshotFileName);
        Assert.Equal(original, await File.ReadAllBytesAsync(snapshotCopy));
    }

    private StartupRecoveryService NewStartupRecovery() => new(_workspaces, _folderSources, _plans);

    private UndoService NewUndoService() => new(
        _history,
        _documents,
        new AtomicFileReplacer(),
        new WordEditStateProbe(),
        new FakeReindexTrigger(),
        new FakeUndoHistoryRecorder());

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private async Task<(string PlanId, string Path)> SeedPlanAsync(
        string documentId,
        string fileName,
        byte[] content,
        BatchPlanStatus status,
        string workspaceId = WorkspaceId)
    {
        var path = Path.Combine(_sourceDir, fileName);
        await File.WriteAllBytesAsync(path, content);
        await _documents.CreateAsync(new Document
        {
            Id = documentId,
            CurrentPath = path,
            Filename = fileName,
            Extension = Path.GetExtension(fileName),
            DocumentType = fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? "docx" : "markdown"
        });

        var plan = new BatchOperationPlan
        {
            WorkspaceId = workspaceId,
            Status = status,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            Files =
            {
                new BatchOperationPlanFile
                {
                    DocumentId = documentId,
                    ExpectedFingerprint = Convert.ToHexString(SHA256.HashData(content)),
                    Selected = true
                }
            }
        };
        await _plans.SavePlanAsync(plan);
        return (plan.Id, path);
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

    /// <summary>Simulates an interruption inside the FTS write of the indexing pipeline.</summary>
    private sealed class ThrowingFtsIndex : IFtsIndex
    {
        public Task IndexDocumentAsync(FtsIndexEntry entry, CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");

        public Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");

        public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");

        public Task ClearIndexAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");

        public Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");

        public Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");

        public Task<FtsIndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("injected interruption during indexing");
    }

    private sealed class FakeUndoHistoryRecorder : IUndoHistoryRecorder
    {
        public List<UndoRecord> Records { get; } = new();

        public Task RecordAsync(UndoRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Test-local real-filesystem replacement for <see cref="DocumentIDE.Infrastructure.History.HistoryStorage"/>,
    /// which hardcodes %LOCALAPPDATA%. Snapshot copies are real files; metadata is real JSON on disk.
    /// </summary>
    private sealed class FileHistoryStorage : IHistoryStorage
    {
        private readonly string _basePath;
        private readonly string _metadataPath;

        public FileHistoryStorage(string basePath)
        {
            _basePath = basePath;
            _metadataPath = Path.Combine(basePath, "metadata");
            Directory.CreateDirectory(_metadataPath);
        }

        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_basePath, "snapshots", snapshotId);

        public async Task SaveMetadataAsync(SnapshotMetadata metadata)
        {
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

        public Task<long> GetTotalSizeAsync() => Task.FromResult(0L);

        public Task<long> GetCapacityBytesAsync() => Task.FromResult(long.MaxValue / 2);
    }
}
