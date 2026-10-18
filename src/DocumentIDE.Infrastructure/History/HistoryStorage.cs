using System;
using DocumentIDE.Core.History;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace DocumentIDE.Infrastructure.History
{
    /// <summary>
    /// Concrete implementation of <see cref="DocumentIDE.Core.History.IHistoryStorage"/>.
    /// Snapshots are stored under the local application data folder:
    ///   %LOCALAPPDATA%\DocumentIDE\snapshots\{snapshotId}
    /// Metadata for each snapshot is stored as a JSON file under:
    ///   %LOCALAPPDATA%\DocumentIDE\snapshots\metadata\{snapshotId}.json
    /// </summary>
    public class HistoryStorage : DocumentIDE.Core.History.IHistoryStorage
    {
        private readonly string _basePath;
        private readonly string _metadataPath;
        private readonly long _capacityBytes;
        private readonly IDateTimeProvider _clock;

        // Default capacity is 5 GiB (5 * 1024^3 bytes)
        private const long DefaultCapacityBytes = 5L * 1024 * 1024 * 1024;

        public HistoryStorage(long capacityBytes = DefaultCapacityBytes, IDateTimeProvider? clock = null, string? root = null)
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _basePath = root is null
                ? Path.Combine(localApp, "DocumentIDE", "snapshots")
                : Path.Combine(root, "snapshots");
            _metadataPath = Path.Combine(_basePath, "metadata");
            Directory.CreateDirectory(_basePath);
            Directory.CreateDirectory(_metadataPath);
            _capacityBytes = capacityBytes;
            _clock = clock ?? new DefaultDateTimeProvider();
        }

        public Task<long> GetCapacityBytesAsync()
        {
            return Task.FromResult(_capacityBytes);
        }

        // Simple default implementation using DateTime.UtcNow
        private sealed class DefaultDateTimeProvider : IDateTimeProvider
        {
            public DateTime UtcNow => DateTime.UtcNow;
        }

        public string GetSnapshotRootPath(string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(snapshotId))
                throw new ArgumentException("SnapshotId cannot be empty.", nameof(snapshotId));
            return Path.Combine(_basePath, snapshotId);
        }

        private static long GetDirectorySize(string path)
        {
            var dir = new DirectoryInfo(path);
            return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        // Returns total size of all snapshot directories (excluding metadata)
        public async Task<long> GetTotalSizeAsync()
        {
            return await Task.Run(() =>
            {
                long total = 0;
                if (Directory.Exists(_basePath))
                {
                    foreach (var dir in Directory.GetDirectories(_basePath))
                    {
                        total += GetDirectorySize(dir);
                    }
                }
                return total;
            });
        }

        // Returns usage percentage of the configured capacity (0‑100)
        public async Task<double> GetCapacityUsageAsync()
        {
            var total = await GetTotalSizeAsync();
            return total * 100.0 / _capacityBytes;
        }

        public async Task SaveMetadataAsync(DocumentIDE.Core.History.SnapshotMetadata metadata)
        {
            // Ensure we do not exceed capacity before committing metadata
            var totalSize = await GetTotalSizeAsync();
            if (_capacityBytes > 0 && totalSize + metadata.SizeBytes > _capacityBytes)
                throw new InvalidOperationException("Insufficient capacity for new snapshot.");

            var filePath = Path.Combine(_metadataPath, $"{metadata.SnapshotId}.json");
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }

        public async Task<IReadOnlyCollection<DocumentIDE.Core.History.SnapshotMetadata>> GetAllMetadataAsync()
        {
            var list = new List<DocumentIDE.Core.History.SnapshotMetadata>();
            var files = Directory.GetFiles(_metadataPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var meta = JsonSerializer.Deserialize<DocumentIDE.Core.History.SnapshotMetadata>(json);
                    if (meta != null) list.Add(meta);
                }
                catch
                {
                    // ignore malformed entries
                }
            }
            return list.AsReadOnly();
        }

        public Task DeleteSnapshotAsync(string snapshotId)
        {
            var snapshotDir = GetSnapshotRootPath(snapshotId);
            var metaFile = Path.Combine(_metadataPath, $"{snapshotId}.json");
            if (Directory.Exists(snapshotDir)) Directory.Delete(snapshotDir, recursive: true);
            if (File.Exists(metaFile)) File.Delete(metaFile);
            return Task.CompletedTask;
        }

        // Deletes snapshots whose expiry has passed
        public async Task CleanupExpiredAsync()
        {
            var metas = await GetAllMetadataAsync();
            var now = _clock.UtcNow;
            foreach (var meta in metas)
            {
                if (meta.ExpiryUtc < now)
                {
                    await DeleteSnapshotAsync(meta.SnapshotId);
                }
            }
        }
    }
}
