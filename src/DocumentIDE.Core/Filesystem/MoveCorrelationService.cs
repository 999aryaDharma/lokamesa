using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

public class MoveCorrelationService : IMoveCorrelationService
{
    private readonly IDocumentRepository _documentRepo;
    private readonly IFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly IFolderSourceRepository _folderRepo;

    private readonly ConcurrentDictionary<string, PhysicalMoveIntent> _activeIntents = new(StringComparer.OrdinalIgnoreCase);

    public MoveCorrelationService(
        IDocumentRepository documentRepo,
        IFileIdentityProvider identityProvider,
        DocumentIdentityService identityService,
        IFolderSourceRepository folderRepo)
    {
        _documentRepo = documentRepo ?? throw new ArgumentNullException(nameof(documentRepo));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _folderRepo = folderRepo ?? throw new ArgumentNullException(nameof(folderRepo));
    }

    public void RecordMoveIntent(string documentId, string sourcePath, string destinationPath, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("DocumentId required", nameof(documentId));
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("SourcePath required", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("DestinationPath required", nameof(destinationPath));

        string canonicalSource = Path.GetFullPath(sourcePath);
        string canonicalDest = Path.GetFullPath(destinationPath);
        TimeSpan intentTtl = ttl ?? TimeSpan.FromSeconds(30);

        string intentId = Guid.NewGuid().ToString();
        var intent = new PhysicalMoveIntent(
            IntentId: intentId,
            DocumentId: documentId,
            SourcePath: canonicalSource,
            DestinationPath: canonicalDest,
            StartedAt: DateTimeOffset.UtcNow,
            Ttl: intentTtl);

        CleanupExpiredIntents();
        _activeIntents[canonicalSource] = intent;
        _activeIntents[canonicalDest] = intent;
    }

    public PhysicalMoveIntent? FindActiveIntentBySource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        CleanupExpiredIntents();
        string canonical = Path.GetFullPath(sourcePath);
        if (_activeIntents.TryGetValue(canonical, out var intent) && !intent.IsExpired(DateTimeOffset.UtcNow))
        {
            return intent;
        }
        return null;
    }

    public PhysicalMoveIntent? FindActiveIntentByDestination(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) return null;
        CleanupExpiredIntents();
        string canonical = Path.GetFullPath(destinationPath);
        if (_activeIntents.TryGetValue(canonical, out var intent) && !intent.IsExpired(DateTimeOffset.UtcNow))
        {
            return intent;
        }
        return null;
    }

    public void CompleteIntent(string intentId)
    {
        var keysToRemove = _activeIntents
            .Where(kvp => kvp.Value.IntentId == intentId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _activeIntents.TryRemove(key, out _);
        }
    }

    private void CleanupExpiredIntents()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredKeys = _activeIntents
            .Where(kvp => kvp.Value.IsExpired(now))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _activeIntents.TryRemove(key, out _);
        }
    }

    public async Task<Document?> CorrelateSameVolumeMoveAsync(string newPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPath)) return null;
        string canonicalPath = Path.GetFullPath(newPath);
        if (!File.Exists(canonicalPath)) return null;

        // 1. Same-volume NTFS VolumeId + ObjectId lookup
        var identity = _identityProvider.GetFileIdentity(canonicalPath);
        if (identity != null)
        {
            var existingDoc = await _documentRepo.GetByVolumeAndObjectIdAsync(identity.VolumeId, identity.ObjectId);
            if (existingDoc != null)
            {
                if (!string.Equals(existingDoc.CurrentPath, canonicalPath, StringComparison.OrdinalIgnoreCase))
                {
                    existingDoc.LastKnownPath = existingDoc.CurrentPath;
                    existingDoc.CurrentPath = canonicalPath;
                }

                var fileInfo = new FileInfo(canonicalPath);
                existingDoc.Filename = fileInfo.Name;
                existingDoc.Extension = fileInfo.Extension;
                existingDoc.SizeBytes = fileInfo.Length;
                existingDoc.ModifiedAt = fileInfo.LastWriteTimeUtc.ToString("O");
                existingDoc.ContentFingerprint = ComputeFingerprint(canonicalPath);
                existingDoc.AvailabilityStatus = "available";
                existingDoc.UpdatedAt = DateTime.UtcNow.ToString("O");

                await _documentRepo.UpdateAsync(existingDoc);
                return existingDoc;
            }
        }

        // 2. Intent journal correlation for app-originated moves
        var activeIntent = FindActiveIntentByDestination(canonicalPath);
        if (activeIntent != null)
        {
            var docFromIntent = await _documentRepo.GetByIdAsync(activeIntent.DocumentId);
            if (docFromIntent != null)
            {
                docFromIntent.LastKnownPath = docFromIntent.CurrentPath;
                docFromIntent.CurrentPath = canonicalPath;
                var fileInfo = new FileInfo(canonicalPath);
                docFromIntent.Filename = fileInfo.Name;
                docFromIntent.Extension = fileInfo.Extension;
                docFromIntent.SizeBytes = fileInfo.Length;
                docFromIntent.ModifiedAt = fileInfo.LastWriteTimeUtc.ToString("O");
                docFromIntent.ContentFingerprint = ComputeFingerprint(canonicalPath);
                if (identity != null)
                {
                    docFromIntent.FilesystemVolumeId = identity.VolumeId;
                    docFromIntent.FilesystemObjectId = identity.ObjectId;
                }
                docFromIntent.AvailabilityStatus = "available";
                docFromIntent.UpdatedAt = DateTime.UtcNow.ToString("O");

                await _documentRepo.UpdateAsync(docFromIntent);
                CompleteIntent(activeIntent.IntentId);
                return docFromIntent;
            }
        }

        // Fall back to DocumentIdentityService
        return await _identityService.ResolveDocumentAsync(canonicalPath);
    }

    public async Task<RebindCandidateListDto> GetRebindCandidatesAsync(string documentId, string? searchDirectory = null, CancellationToken cancellationToken = default)
    {
        var doc = await _documentRepo.GetByIdAsync(documentId);
        if (doc == null)
        {
            return new RebindCandidateListDto(documentId, "", false, new List<RebindCandidateDto>());
        }

        string lastKnownPath = doc.LastKnownPath ?? doc.CurrentPath;
        bool sourceStillExists = !string.IsNullOrEmpty(lastKnownPath) && File.Exists(lastKnownPath);

        var candidateFiles = new List<string>();

        if (!string.IsNullOrWhiteSpace(searchDirectory) && Directory.Exists(searchDirectory))
        {
            candidateFiles.AddRange(Directory.EnumerateFiles(searchDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => IsSupportedDocumentExtension(f) && !string.Equals(Path.GetFullPath(f), Path.GetFullPath(lastKnownPath), StringComparison.OrdinalIgnoreCase)));
        }

        // Also search in document's folder source directories if available
        if (!string.IsNullOrEmpty(lastKnownPath))
        {
            string? parentDir = Path.GetDirectoryName(lastKnownPath);
            if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
            {
                candidateFiles.AddRange(Directory.EnumerateFiles(parentDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => IsSupportedDocumentExtension(f) && !string.Equals(Path.GetFullPath(f), Path.GetFullPath(lastKnownPath), StringComparison.OrdinalIgnoreCase)));
            }
        }

        var distinctCandidates = candidateFiles.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var candidateDtos = new List<RebindCandidateDto>();

        foreach (var file in distinctCandidates)
        {
            if (!File.Exists(file)) continue;

            try
            {
                var fi = new FileInfo(file);
                string candidateFingerprint = ComputeFingerprint(file);
                var score = MoveCorrelationScorer.ScoreCandidate(
                    doc,
                    candidateFilename: fi.Name,
                    candidateSizeBytes: fi.Length,
                    candidateFingerprint: candidateFingerprint,
                    candidateModifiedAt: fi.LastWriteTimeUtc,
                    candidatePath: file);

                if (score.MatchScore >= 40)
                {
                    bool isCandidateAmbiguous = score.IsAmbiguous || sourceStillExists;

                    candidateDtos.Add(new RebindCandidateDto(
                        TargetPath: file,
                        Filename: fi.Name,
                        SizeBytes: fi.Length,
                        ModifiedAt: fi.LastWriteTimeUtc.ToString("O"),
                        ContentFingerprint: candidateFingerprint,
                        MatchScore: score.MatchScore,
                        ExactFingerprintMatch: score.ExactFingerprintMatch,
                        SameFilenameMatch: score.SameFilenameMatch,
                        MatchReason: score.Reason,
                        IsAmbiguous: isCandidateAmbiguous
                    ));
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        // Sort candidates by match score descending
        candidateDtos = candidateDtos.OrderByDescending(c => c.MatchScore).ToList();

        // Ambiguity determination rules:
        // Rule A: Source file still exists (it's a COPY, not a move)
        // Rule B: Multiple candidates with score >= 50 or exact fingerprint match
        // Rule C: Highest score candidate has IsAmbiguous == true (score < 75)
        bool overallIsAmbiguous = sourceStillExists ||
                                  candidateDtos.Count(c => c.MatchScore >= 50 || c.ExactFingerprintMatch) > 1 ||
                                  (candidateDtos.Count > 0 && candidateDtos[0].IsAmbiguous);

        return new RebindCandidateListDto(
            DocumentId: documentId,
            LastKnownPath: lastKnownPath,
            IsAmbiguous: overallIsAmbiguous,
            Candidates: candidateDtos
        );
    }

    public async Task<RebindResultDto> RebindDocumentAsync(string documentId, string targetPath, bool forceRebind = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return new RebindResultDto(documentId, "", targetPath, "error", false, "DocumentId required");
        if (string.IsNullOrWhiteSpace(targetPath)) return new RebindResultDto(documentId, "", targetPath, "error", false, "TargetPath required");

        string canonicalTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(canonicalTargetPath))
        {
            return new RebindResultDto(documentId, "", canonicalTargetPath, "missing", false, $"File not found at target path: {canonicalTargetPath}");
        }

        var doc = await _documentRepo.GetByIdAsync(documentId);
        if (doc == null)
        {
            return new RebindResultDto(documentId, "", canonicalTargetPath, "missing", false, $"Document not found for ID: {documentId}");
        }

        string searchDir = Path.GetDirectoryName(canonicalTargetPath) ?? "";
        var candidatesResult = await GetRebindCandidatesAsync(documentId, searchDir, cancellationToken);

        if (candidatesResult.IsAmbiguous && !forceRebind)
        {
            throw new InvalidOperationException("Rebind refused: target file is ambiguous. Explicit user confirmation (forceRebind) is required.");
        }

        string previousPath = doc.CurrentPath;
        doc.LastKnownPath = string.IsNullOrEmpty(previousPath) ? doc.LastKnownPath : previousPath;
        doc.CurrentPath = canonicalTargetPath;

        var fi = new FileInfo(canonicalTargetPath);
        doc.Filename = fi.Name;
        doc.Extension = fi.Extension;
        doc.SizeBytes = fi.Length;
        doc.ModifiedAt = fi.LastWriteTimeUtc.ToString("O");
        doc.ContentFingerprint = ComputeFingerprint(canonicalTargetPath);
        doc.AvailabilityStatus = "available";

        var identity = _identityProvider.GetFileIdentity(canonicalTargetPath);
        if (identity != null)
        {
            doc.FilesystemVolumeId = identity.VolumeId;
            doc.FilesystemObjectId = identity.ObjectId;
        }

        doc.UpdatedAt = DateTime.UtcNow.ToString("O");
        await _documentRepo.UpdateAsync(doc);

        return new RebindResultDto(
            DocumentId: doc.Id,
            PreviousPath: previousPath,
            NewPath: canonicalTargetPath,
            AvailabilityStatus: "available",
            Success: true
        );
    }

    private static string ComputeFingerprint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsSupportedDocumentExtension(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase);
    }
}
