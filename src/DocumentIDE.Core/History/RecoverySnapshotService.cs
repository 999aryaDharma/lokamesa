using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DocumentIDE.Core.History
{
    /// <summary>
    /// Service responsible for creating per‑operation recovery snapshots.
    /// Each snapshot records per-file metadata (identity, original path, content fingerprint,
    /// post-operation fingerprint) and copies only the files that were touched by the operation.
    /// Snapshots are stored under a configurable root (defaults to `%LOCALAPPDATA%\DocumentIDE\snapshots`).
    /// </summary>

    /// <summary>A file to include in a recovery snapshot, with the document identity that owns it.</summary>
    public sealed record SnapshotSourceFile(string DocumentId, string Path);

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
        /// <param name="affectedFiles">Files (with their document identity) that will be mutated.</param>
        /// <returns>Metadata about the created snapshot.</returns>
        public async Task<SnapshotMetadata> CreateSnapshotAsync(string operationId, IEnumerable<SnapshotSourceFile> affectedFiles)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));

            var files = affectedFiles?.ToList() ?? throw new ArgumentNullException(nameof(affectedFiles));
            if (!files.Any())
                throw new ArgumentException("At least one affected file must be provided.", nameof(affectedFiles));

            // Compute total size of affected files without copying
            long totalSize = 0;
            foreach (var file in files)
            {
                var info = new FileInfo(file.Path);
                if (!info.Exists)
                    throw new FileNotFoundException($"Affected file not found: {file.Path}");
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

            // Copy affected files into snapshot directory. Duplicate file names (same file
            // name in different folders) get distinct snapshot names so every record maps
            // to exactly one copy and a restore can never pick the wrong file.
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var records = new List<SnapshotFileRecord>(files.Count);
            foreach (var file in files)
            {
                var sourceInfo = new FileInfo(file.Path);
                var snapshotFileName = AllocateSnapshotFileName(sourceInfo.Name, usedNames);
                var targetPath = Path.Combine(snapshotRoot, snapshotFileName);
                await CopyFileAsync(sourceInfo.FullName, targetPath);

                records.Add(new SnapshotFileRecord
                {
                    DocumentId = file.DocumentId,
                    OriginalPath = sourceInfo.FullName,
                    SnapshotFileName = snapshotFileName,
                    OriginalFingerprint = await ComputeFingerprintAsync(sourceInfo.FullName)
                });
            }

            var metadata = new SnapshotMetadata
            {
                SnapshotId = snapshotId,
                OperationId = operationId,
                CreatedUtc = _clock.UtcNow,
                ExpiryUtc = expiry,
                SizeBytes = totalSize,
                Files = records
            };

            await _storage.SaveMetadataAsync(metadata);
            return metadata;
        }

        /// <summary>
        /// HIST-01 compatibility overload: snapshots by path only. Such snapshots carry no
        /// document identity, so a conflict-aware undo cannot verify or restore them and
        /// fails closed with a typed UnknownDocument outcome.
        /// </summary>
        public Task<SnapshotMetadata> CreateSnapshotAsync(string operationId, IEnumerable<string> affectedFilePaths)
        {
            ArgumentNullException.ThrowIfNull(affectedFilePaths);
            return CreateSnapshotAsync(operationId, affectedFilePaths.Select(path => new SnapshotSourceFile(string.Empty, path)));
        }

        /// <summary>
        /// Records the content fingerprint of a document *after* a successful mutation.
        /// Undo compares the current file against this value to decide between restore and conflict.
        /// </summary>
        public async Task RecordPostOperationFingerprintAsync(SnapshotMetadata metadata, string documentId, string postOperationFingerprint)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));
            if (string.IsNullOrWhiteSpace(postOperationFingerprint))
                throw new ArgumentException("Post-operation fingerprint cannot be empty.", nameof(postOperationFingerprint));

            var record = metadata.Files.FirstOrDefault(f => string.Equals(f.DocumentId, documentId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Snapshot has no recorded file for document '{documentId}'.");
            record.PostOperationFingerprint = postOperationFingerprint;
            await _storage.SaveMetadataAsync(metadata);
        }

        private static string AllocateSnapshotFileName(string sourceName, HashSet<string> usedNames)
        {
            var candidate = sourceName;
            var stem = Path.GetFileNameWithoutExtension(sourceName);
            var extension = Path.GetExtension(sourceName);
            for (var index = 1; !usedNames.Add(candidate); index++)
                candidate = $"{stem}-{index}{extension}";
            return candidate;
        }

        private static async Task CopyFileAsync(string source, string destination)
        {
            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var destStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await sourceStream.CopyToAsync(destStream);
        }

        private static async Task<string> ComputeFingerprintAsync(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var hash = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(hash);
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

        /// <summary>Per-file records: identity, original path, snapshot copy, fingerprints.</summary>
        public List<SnapshotFileRecord> Files { get; set; } = new();
    }

    public class SnapshotFileRecord
    {
        public string DocumentId { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string SnapshotFileName { get; set; } = string.Empty;
        public string OriginalFingerprint { get; set; } = string.Empty;
        public string? PostOperationFingerprint { get; set; }
    }
}
