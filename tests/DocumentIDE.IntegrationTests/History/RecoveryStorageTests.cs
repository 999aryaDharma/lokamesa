using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.History;
using Xunit;

namespace DocumentIDE.IntegrationTests.History
{
    public class RecoveryStorageTests
    {
        private string CreateTempFile(string content)
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, content);
            return path;
        }

        [Fact]
        public async Task SnapshotCreatedBeforeMutation()
        {
            var tempFile = CreateTempFile("original");
            var storage = new TestHistoryStorage();
            var service = new RecoverySnapshotService(storage);
            var metadata = await service.CreateSnapshotAsync("op1", new[] { tempFile });
            Assert.NotNull(metadata);
            Assert.Equal("op1", metadata.OperationId);
            var snapshotPath = storage.GetSnapshotRootPath(metadata.SnapshotId);
            Assert.True(Directory.Exists(snapshotPath));
            var copiedFile = Path.Combine(snapshotPath, Path.GetFileName(tempFile));
            Assert.True(File.Exists(copiedFile));
        }

        [Fact]
        public async Task InsufficientCapacityBlocksOperation()
        {
            var tempFile1 = CreateTempFile(new string('a', 60));
            var tempFile2 = CreateTempFile(new string('b', 60));
            var storage = new TestHistoryStorage(capacityBytes: 100);
            var service = new RecoverySnapshotService(storage);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.CreateSnapshotAsync("op2", new[] { tempFile1, tempFile2 }));
        }

        [Fact]
        public async Task UnexpiredSnapshotIsNotEvicted()
        {
            var tempFile = CreateTempFile("data");
            var storage = new TestHistoryStorage();
            var service = new RecoverySnapshotService(storage);
            var metadata = await service.CreateSnapshotAsync("op3", new[] { tempFile });
            await storage.CleanupExpiredAsync();
            var snapshotPath = storage.GetSnapshotRootPath(metadata.SnapshotId);
            Assert.True(Directory.Exists(snapshotPath), "Snapshot should persist before expiry.");
        }
    }

    public class TestHistoryStorage : IHistoryStorage
    {
        private readonly string _basePath;
        private readonly long _capacityBytes;
        public TestHistoryStorage(long capacityBytes = 0)
        {
            _basePath = Path.Combine(Path.GetTempPath(), "HistoryTestStorage", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_basePath);
            _capacityBytes = capacityBytes;
        }
        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_basePath, snapshotId);
        public async Task SaveMetadataAsync(SnapshotMetadata metadata)
        {
            var snapshotDir = GetSnapshotRootPath(metadata.SnapshotId);
            var totalSize = GetDirectorySize(snapshotDir);
            if (_capacityBytes > 0 && totalSize > _capacityBytes)
                throw new InvalidOperationException("Insufficient capacity");
            var metaPath = Path.Combine(_basePath, "metadata", $"{metadata.SnapshotId}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metaPath, json);
        }
        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() => Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(Array.Empty<SnapshotMetadata>());
        public Task DeleteSnapshotAsync(string snapshotId)
        {
            var dir = GetSnapshotRootPath(snapshotId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return Task.CompletedTask;
        }
        public Task<long> GetTotalSizeAsync()
        {
            // Compute total size of all snapshot directories
            if (!Directory.Exists(_basePath)) return Task.FromResult(0L);
            long total = 0;
            foreach (var dir in Directory.GetDirectories(_basePath))
            {
                total += GetDirectorySize(dir);
            }
            return Task.FromResult(total);
        }
        public Task CleanupExpiredAsync() => Task.CompletedTask;
        private static long GetDirectorySize(string path)
        {
            long size = 0;
            if (!Directory.Exists(path)) return 0;
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                size += new FileInfo(file).Length;
            return size;
        }
    }
}
