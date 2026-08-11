using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DocumentIDE.Core.History
{
    /// <summary>
    /// Service responsible for creating per‑operation recovery snapshots.
    /// Each snapshot records metadata (size, fingerprint, expiry) and copies only the files
    /// that were touched by the operation. Snapshots are stored under a configurable root
    /// (defaults to `%LOCALAPPDATA%\DocumentIDE\snapshots`).
    /// </summary>


    public class RecoverySnapshotService
    {
        private readonly IHistoryStorage _storage;
        private readonly IDateTimeProvider _clock;

        public RecoverySnapshotService(IHistoryStorage storage, IDateTimeProvider? clock = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _clock = clock ?? new SystemDateTimeProvider();
        }

        /// <summary>
        /// Creates a snapshot for a given operation.
        /// </summary>
        /// <param name="operationId">Unique identifier of the operation.</param>
        /// <param name="affectedFilePaths">Absolute paths of files that will be mutated.</param>
        /// <returns>Metadata about the created snapshot.</returns>
        public async Task<SnapshotMetadata> CreateSnapshotAsync(string operationId, IEnumerable<string> affectedFilePaths)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

            var files = affectedFilePaths?.ToList() ?? new List<string>();
            if (!files.Any())
                throw new ArgumentException("At least one affected file must be provided.", nameof(affectedFilePaths));

            // Compute total size of affected files without copying
            long totalSize = 0;
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                if (!info.Exists)
                    throw new FileNotFoundException($"Affected file not found: {file}");
                totalSize += info.Length;
            }

            // Pre‑flight capacity checks
            var existingSize = await _storage.GetTotalSizeAsync();
            var capacityBytes = await _storage.GetCapacityBytesAsync();
            if ((existingSize + totalSize) * 100.0 / capacityBytes > 80)
                Console.WriteLine("[Warning] Snapshot storage usage exceeds 80% of capacity.");
            if (existingSize + totalSize > capacityBytes)
                throw new InvalidOperationException("Insufficient capacity for new snapshot.");

            var snapshotId = Guid.NewGuid().ToString("N");
            var expiry = _clock.UtcNow.AddDays(30);
            var snapshotRoot = _storage.GetSnapshotRootPath(snapshotId);
            Directory.CreateDirectory(snapshotRoot);

            // Copy affected files into snapshot directory
            foreach (var file in files)
            {
                var sourceInfo = new FileInfo(file);
                var targetPath = Path.Combine(snapshotRoot, sourceInfo.Name);
                await CopyFileAsync(sourceInfo.FullName, targetPath);
            }

            var metadata = new SnapshotMetadata
            {
                SnapshotId = snapshotId,
                OperationId = operationId,
                CreatedUtc = _clock.UtcNow,
                ExpiryUtc = expiry,
                SizeBytes = totalSize,
                FileFingerprints = files.Select(ComputeFingerprint).ToList()
            };

            await _storage.SaveMetadataAsync(metadata);
            return metadata;
        }

        private static async Task CopyFileAsync(string source, string destination)
        {
            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await sourceStream.CopyToAsync(destStream);
        }

        private static string ComputeFingerprint(string path)
        {
            // Placeholder – a real implementation would hash file content.
            return Path.GetFileName(path).GetHashCode().ToString("X8");
        }
    }

    public interface IHistoryStorage
    {
        string GetSnapshotRootPath(string snapshotId);
        Task SaveMetadataAsync(SnapshotMetadata metadata);
        Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync();
        Task DeleteSnapshotAsync(string snapshotId);
        Task<long> GetTotalSizeAsync();
        Task<long> GetCapacityBytesAsync();

    }

    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
    }

    internal sealed class SystemDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }

    public class SnapshotMetadata
    {
        public string SnapshotId { get; set; } = default!;
        public string OperationId { get; set; } = default!;
        public DateTime CreatedUtc { get; set; }
        public DateTime ExpiryUtc { get; set; }
        public long SizeBytes { get; set; }
        public List<string> FileFingerprints { get; set; } = new();
    }
}
