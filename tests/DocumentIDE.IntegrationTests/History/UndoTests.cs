using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.History;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.History
{
    /// <summary>
    /// HIST-02 — Conflict-Aware Undo.
    /// Cycle 1: recovery snapshots record per-file identity, snapshot copies and real
    /// content fingerprints so a later undo can compare and restore deterministically.
    /// Cycle 2: the apply engine records the post-operation fingerprint for every file it mutates.
    /// </summary>
    public sealed class UndoTests : IDisposable
    {
        private readonly string _root;
        private readonly FakeHistoryStorage _storage;
        private readonly AppDb _db;
        private readonly BatchPlanRepository _plans;
        private readonly DocumentRepository _documents;
        private readonly FakeHistoryRecorder _recorder;
        private readonly FakeUndoHistoryRecorder _undoRecorder = new();
        private readonly FakeReindexTrigger _reindex;
        private readonly BatchApplyEngine _engine;

        public UndoTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "hist02-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _db = new AppDb(Path.Combine(_root, "state.db"));
            _db.InitializeAsync().GetAwaiter().GetResult();
            _plans = new BatchPlanRepository(_db);
            _documents = new DocumentRepository(_db);
            new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "hist02" }).GetAwaiter().GetResult();
            _storage = new FakeHistoryStorage(_root);
            _recorder = new FakeHistoryRecorder();
            _reindex = new FakeReindexTrigger();
            _engine = new BatchApplyEngine(
                _plans, _documents, new RecoverySnapshotService(_storage),
                new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_root, true); } catch { /* best effort */ }
        }

        [Fact]
        public async Task SnapshotRecordsPerFileIdentityAndFingerprints()
        {
            var path = Path.Combine(_root, "notes.md");
            await File.WriteAllTextAsync(path, "original");
            var service = new RecoverySnapshotService(_storage);

            var metadata = await service.CreateSnapshotAsync("op-1", new[] { new SnapshotSourceFile("doc-1", path) });

            var file = Assert.Single(metadata.Files);
            Assert.Equal("doc-1", file.DocumentId);
            Assert.Equal(path, file.OriginalPath);
            Assert.Equal(Sha256Hex(Encoding.UTF8.GetBytes("original")), file.OriginalFingerprint);
            Assert.Equal("notes.md", file.SnapshotFileName);
            Assert.Null(file.PostOperationFingerprint);

            var copy = Path.Combine(_storage.GetSnapshotRootPath(metadata.SnapshotId), file.SnapshotFileName);
            Assert.Equal(Encoding.UTF8.GetBytes("original"), await File.ReadAllBytesAsync(copy));
        }

        [Fact]
        public async Task SnapshotDuplicateFileNamesGetDistinctCopies()
        {
            var dirA = Path.Combine(_root, "a");
            var dirB = Path.Combine(_root, "b");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            var pathA = Path.Combine(dirA, "index.md");
            var pathB = Path.Combine(dirB, "index.md");
            await File.WriteAllTextAsync(pathA, "content A");
            await File.WriteAllTextAsync(pathB, "content B");
            var service = new RecoverySnapshotService(_storage);

            var metadata = await service.CreateSnapshotAsync(
                "op-2",
                new[] { new SnapshotSourceFile("doc-a", pathA), new SnapshotSourceFile("doc-b", pathB) });

            Assert.Equal(2, metadata.Files.Count);
            Assert.Equal(2, metadata.Files.Select(f => f.SnapshotFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var root = _storage.GetSnapshotRootPath(metadata.SnapshotId);
            var byDoc = metadata.Files.ToDictionary(f => f.DocumentId, StringComparer.Ordinal);
            Assert.Equal(Encoding.UTF8.GetBytes("content A"), await File.ReadAllBytesAsync(Path.Combine(root, byDoc["doc-a"].SnapshotFileName)));
            Assert.Equal(Encoding.UTF8.GetBytes("content B"), await File.ReadAllBytesAsync(Path.Combine(root, byDoc["doc-b"].SnapshotFileName)));
        }

        [Fact]
        public async Task Apply_RecordsPostOperationFingerprintForUndo()
        {
            var original = Encoding.UTF8.GetBytes("target content");
            var (planId, path) = await SeedAsync("doc", "notes.md", original);

            async Task ReplaceTarget(string tempPath, CancellationToken token)
            {
                var text = await File.ReadAllTextAsync(tempPath, token);
                await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
            }

            var result = await _engine.ApplyAsync(planId, ReplaceTarget);
            Assert.Equal(BatchApplyFileOutcome.Success, Assert.Single(result.Files).Outcome);

            var metadata = Assert.Single(await _storage.GetAllMetadataAsync());
            var file = Assert.Single(metadata.Files);
            Assert.Equal("doc", file.DocumentId);
            Assert.Equal(path, file.OriginalPath);
            Assert.Equal(Sha256Hex(original), file.OriginalFingerprint);

            var mutatedBytes = await File.ReadAllBytesAsync(path);
            Assert.Equal(Sha256Hex(mutatedBytes), file.PostOperationFingerprint);
            Assert.NotEqual(file.OriginalFingerprint, file.PostOperationFingerprint);
        }

        [Fact]
        public async Task Undo_UnchangedFileRestoresOriginalBytes()
        {
            var original = Encoding.UTF8.GetBytes("target content");
            var (planId, path) = await SeedAsync("doc", "notes.md", original);
            await ApplyReplaceAsync(planId);
            Assert.Equal("replaced content", await File.ReadAllTextAsync(path));
            _reindex.DocumentIds.Clear();

            var result = await CreateUndoService().UndoAsync(planId);

            Assert.Equal(UndoResultStatus.Completed, result.Status);
            var file = Assert.Single(result.Files);
            Assert.Equal("doc", file.DocumentId);
            Assert.Equal(UndoFileOutcome.Restored, file.Outcome);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Equal(new[] { "doc" }, _reindex.DocumentIds);
            var record = Assert.Single(_undoRecorder.Records);
            Assert.Equal(planId, record.OperationId);
            Assert.Equal(UndoResultStatus.Completed, record.Status);
        }

        [Fact]
        public async Task Undo_SecondUndoIsIdempotent()
        {
            var original = Encoding.UTF8.GetBytes("target content");
            var (planId, path) = await SeedAsync("doc", "notes.md", original);
            await ApplyReplaceAsync(planId);
            var undo = CreateUndoService();
            await undo.UndoAsync(planId);
            _reindex.DocumentIds.Clear();
            _undoRecorder.Records.Clear();

            var result = await undo.UndoAsync(planId);

            Assert.Equal(UndoResultStatus.Completed, result.Status);
            var file = Assert.Single(result.Files);
            Assert.Equal(UndoFileOutcome.AlreadyRestored, file.Outcome);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Empty(_reindex.DocumentIds);
        }

        [Fact]
        public async Task Undo_ExternallyEditedFileIsNotOverwritten()
        {
            var original = Encoding.UTF8.GetBytes("target content");
            var (planId, path) = await SeedAsync("doc", "notes.md", original);
            await ApplyReplaceAsync(planId);
            _reindex.DocumentIds.Clear();

            // External edit after the operation: newer content must survive the undo.
            var newer = Encoding.UTF8.GetBytes("newer external work");
            await File.WriteAllBytesAsync(path, newer);

            var result = await CreateUndoService().UndoAsync(planId);

            Assert.Equal(UndoResultStatus.Blocked, result.Status);
            var file = Assert.Single(result.Files);
            Assert.Equal(UndoFileOutcome.Conflict, file.Outcome);
            Assert.Equal("ExternallyModified", file.ReasonCode);
            Assert.Equal(newer, await File.ReadAllBytesAsync(path));
            Assert.Empty(_reindex.DocumentIds);
            Assert.Empty(_undoRecorder.Records);
        }

        [Fact]
        public async Task Undo_BatchReportsPartialAndConflictOutcomes()
        {
            var one = Encoding.UTF8.GetBytes("target one");
            var two = Encoding.UTF8.GetBytes("target two");
            var (planId, onePath) = await SeedAsync("doc1", "one.md", one);
            var (_, twoPath) = await SeedAsync("doc2", "two.md", two, planId);
            await ApplyReplaceAsync(planId, docIds: new[] { "doc1", "doc2" });
            _reindex.DocumentIds.Clear();

            var newer = Encoding.UTF8.GetBytes("doc2 external edit");
            await File.WriteAllBytesAsync(twoPath, newer);

            var result = await CreateUndoService().UndoAsync(planId);

            Assert.Equal(UndoResultStatus.Partial, result.Status);
            Assert.Equal(2, result.Files.Count);
            var first = result.Files.Single(f => f.DocumentId == "doc1");
            var second = result.Files.Single(f => f.DocumentId == "doc2");
            Assert.Equal(UndoFileOutcome.Restored, first.Outcome);
            Assert.Equal(one, await File.ReadAllBytesAsync(onePath));
            Assert.Equal(UndoFileOutcome.Conflict, second.Outcome);
            Assert.Equal("ExternallyModified", second.ReasonCode);
            Assert.Equal(newer, await File.ReadAllBytesAsync(twoPath));
            Assert.Equal(new[] { "doc1" }, _reindex.DocumentIds);
            var record = Assert.Single(_undoRecorder.Records);
            Assert.Equal(UndoResultStatus.Partial, record.Status);
        }

        private async Task ApplyReplaceAsync(string planId, string[]? docIds = null)
        {
            async Task ReplaceTarget(string tempPath, CancellationToken token)
            {
                var text = await File.ReadAllTextAsync(tempPath, token);
                await File.WriteAllTextAsync(tempPath, text.Replace("target", "replaced"), token);
            }

            var result = await _engine.ApplyAsync(planId, ReplaceTarget);
            Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
            foreach (var file in result.Files)
                Assert.Equal(BatchApplyFileOutcome.Success, file.Outcome);
        }

        private UndoService CreateUndoService() =>
            new(_storage, _documents, new AtomicFileReplacer(), _reindex, _undoRecorder);

        private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

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
                ExpectedFingerprint = Sha256Hex(content),
                Selected = true
            });
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

        private sealed class FakeUndoHistoryRecorder : IUndoHistoryRecorder
        {
            public List<UndoRecord> Records { get; } = new();
            public Task RecordAsync(UndoRecord record, CancellationToken cancellationToken = default)
            {
                Records.Add(record);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeHistoryStorage : IHistoryStorage
        {
            private readonly string _snapshotRoot;
            private readonly Dictionary<string, SnapshotMetadata> _metadata = new(StringComparer.Ordinal);
            private readonly long _capacityBytes;

            public FakeHistoryStorage(string root, long capacityBytes = 5L * 1024 * 1024 * 1024)
            {
                _snapshotRoot = Path.Combine(root, "snapshots");
                _capacityBytes = capacityBytes;
            }

            public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_snapshotRoot, snapshotId);

            public Task SaveMetadataAsync(SnapshotMetadata metadata)
            {
                _metadata[metadata.SnapshotId] = metadata;
                return Task.CompletedTask;
            }

            public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() =>
                Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(_metadata.Values.ToList());

            public Task DeleteSnapshotAsync(string snapshotId)
            {
                _metadata.Remove(snapshotId);
                return Task.CompletedTask;
            }

            public Task<long> GetTotalSizeAsync()
            {
                if (!Directory.Exists(_snapshotRoot)) return Task.FromResult(0L);
                long total = 0;
                foreach (var file in Directory.GetFiles(_snapshotRoot, "*", SearchOption.AllDirectories))
                    total += new FileInfo(file).Length;
                return Task.FromResult(total);
            }

            public Task<long> GetCapacityBytesAsync() => Task.FromResult(_capacityBytes);
        }
    }
}
