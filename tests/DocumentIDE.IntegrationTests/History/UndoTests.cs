using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.History;
using Xunit;

namespace DocumentIDE.IntegrationTests.History
{
    /// <summary>
    /// HIST-02 — Conflict-Aware Undo.
    /// Cycle 1: recovery snapshots record per-file identity, snapshot copies and real
    /// content fingerprints so a later undo can compare and restore deterministically.
    /// </summary>
    public sealed class UndoTests : IDisposable
    {
        private readonly string _root;
        private readonly FakeHistoryStorage _storage;

        public UndoTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "hist02-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _storage = new FakeHistoryStorage(_root);
        }

        public void Dispose()
        {
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

        private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

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
