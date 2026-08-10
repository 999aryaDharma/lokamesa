using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

public class DocumentIdentityService
{
    private readonly IDocumentRepository _documentRepo;
    private readonly IFileIdentityProvider _identityProvider;

    public DocumentIdentityService(IDocumentRepository documentRepo, IFileIdentityProvider identityProvider)
    {
        _documentRepo = documentRepo;
        _identityProvider = identityProvider;
    }

    public async Task<Document> ResolveDocumentAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        string canonicalPath = Path.GetFullPath(path);
        if (!File.Exists(canonicalPath))
        {
            throw new FileNotFoundException($"File not found: {canonicalPath}", canonicalPath);
        }

        var fileInfo = new FileInfo(canonicalPath);
        long sizeBytes = fileInfo.Length;
        string modifiedAt = fileInfo.LastWriteTimeUtc.ToString("O");
        string filename = fileInfo.Name;
        string extension = fileInfo.Extension;
        string documentType = extension.ToLowerInvariant() == ".docx" ? "docx" : "markdown";
        string contentFingerprint = ComputeFingerprint(canonicalPath);

        // 1. Probe NTFS file identity (volume + object IDs)
        var identity = _identityProvider.GetFileIdentity(canonicalPath);

        Document? document = null;

        if (identity != null)
        {
            // Try to find the document by its unique filesystem ID
            document = await _documentRepo.GetByVolumeAndObjectIdAsync(identity.VolumeId, identity.ObjectId);

            if (document != null)
            {
                // Compare path case-insensitively
                bool pathMatches = string.Equals(document.CurrentPath, canonicalPath, StringComparison.OrdinalIgnoreCase);

                if (!pathMatches)
                {
                    // This means the file has been renamed or moved!
                    document.LastKnownPath = document.CurrentPath;
                    document.CurrentPath = canonicalPath;
                }
                else if (document.CurrentPath != canonicalPath)
                {
                    // Case casing mismatch, update path to match exact casing
                    document.CurrentPath = canonicalPath;
                }

                // Update standard attributes
                document.Filename = filename;
                document.Extension = extension;
                document.SizeBytes = sizeBytes;
                document.ModifiedAt = modifiedAt;
                document.ContentFingerprint = contentFingerprint;
                document.AvailabilityStatus = "available";
                document.UpdatedAt = DateTime.UtcNow.ToString("O");

                await _documentRepo.UpdateAsync(document);
                return document;
            }
        }

        // 2. Try lookup by CurrentPath if not found by Volume/Object ID
        document = await _documentRepo.GetByPathAsync(canonicalPath);

        if (document != null)
        {
            // Check if this document has the same Volume/Object ID now (if probed)
            if (identity != null && !string.IsNullOrEmpty(document.FilesystemVolumeId) && !string.IsNullOrEmpty(document.FilesystemObjectId) &&
                (document.FilesystemVolumeId != identity.VolumeId || document.FilesystemObjectId != identity.ObjectId))
            {
                // Two distinct duplicate files receive distinct DocumentIds
                // Mark the old document at this path as missing, and clear its current path
                document.AvailabilityStatus = "missing";
                document.LastKnownPath = document.CurrentPath;
                document.CurrentPath = "";
                await _documentRepo.UpdateAsync(document);

                document = null; // Forces creation of a new document record
            }
            else
            {
                // Found by path and no identity mismatch (or no identity was registered in the database).
                // Update identity if we successfully probed it this time.
                if (identity != null)
                {
                    document.FilesystemVolumeId = identity.VolumeId;
                    document.FilesystemObjectId = identity.ObjectId;
                }

                document.Filename = filename;
                document.Extension = extension;
                document.SizeBytes = sizeBytes;
                document.ModifiedAt = modifiedAt;
                document.ContentFingerprint = contentFingerprint;
                document.AvailabilityStatus = "available";
                document.UpdatedAt = DateTime.UtcNow.ToString("O");

                await _documentRepo.UpdateAsync(document);
                return document;
            }
        }

        // 3. Fallback correlation: non-NTFS rename/move
        // If we didn't find the document by volume/object ID OR path, but we have a matching fingerprint
        if (document == null)
        {
            var candidates = await _documentRepo.GetDocumentsByFingerprintAsync(contentFingerprint);
            
            // Find candidates that are currently "missing" and whose old paths do not physically exist anymore
            var missingCandidates = candidates.Where(c => 
                c.AvailabilityStatus == "missing" && 
                !string.IsNullOrEmpty(c.LastKnownPath) && 
                !File.Exists(c.LastKnownPath)
            ).ToList();

            // Safely correlate only if there is exactly one unique missing candidate to avoid auto-merging ambiguous duplicates
            if (missingCandidates.Count == 1)
            {
                var correlated = missingCandidates[0];
                
                correlated.LastKnownPath = correlated.CurrentPath;
                if (string.IsNullOrEmpty(correlated.LastKnownPath))
                {
                    correlated.LastKnownPath = canonicalPath; // Fallback
                }
                correlated.CurrentPath = canonicalPath;
                correlated.Filename = filename;
                correlated.Extension = extension;
                correlated.SizeBytes = sizeBytes;
                correlated.ModifiedAt = modifiedAt;
                correlated.AvailabilityStatus = "available";
                
                if (identity != null)
                {
                    correlated.FilesystemVolumeId = identity.VolumeId;
                    correlated.FilesystemObjectId = identity.ObjectId;
                }
                
                correlated.UpdatedAt = DateTime.UtcNow.ToString("O");

                await _documentRepo.UpdateAsync(correlated);
                return correlated;
            }
        }

        // 4. Create new Document record if not resolved
        if (document == null)
        {
            document = new Document
            {
                Id = Guid.NewGuid().ToString(),
                FilesystemVolumeId = identity?.VolumeId,
                FilesystemObjectId = identity?.ObjectId,
                CurrentPath = canonicalPath,
                Filename = filename,
                Extension = extension,
                DocumentType = documentType,
                SizeBytes = sizeBytes,
                ModifiedAt = modifiedAt,
                ContentFingerprint = contentFingerprint,
                AvailabilityStatus = "available",
                IndexStatus = "pending",
                CreatedAt = DateTime.UtcNow.ToString("O"),
                UpdatedAt = DateTime.UtcNow.ToString("O")
            };
            await _documentRepo.CreateAsync(document);
        }

        return document;
    }

    private static string ComputeFingerprint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
