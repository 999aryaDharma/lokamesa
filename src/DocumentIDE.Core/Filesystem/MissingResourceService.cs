using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Outcome of probing a single tracked document against the live filesystem.
/// <c>permission_denied</c> and <c>locked</c> are distinct from <c>missing</c>
/// (filesystem-sync spec §18): access denied must never be mislabelled deleted.
/// </summary>
public enum ResourceProbeStatus
{
    Available,
    Missing,
    PermissionDenied,
    Locked,
    SourceUnavailable,
    Unknown
}

public record ResourceProbeResult(
    string DocumentId,
    ResourceProbeStatus Status,
    string? Detail = null);

/// <summary>
/// Outcome of probing a folder source root. The typed status distinguishes a
/// disconnected drive (<see cref="UnavailableDrive"/>) from a renamed/deleted
/// path (<see cref="MissingPath"/>) and from an ACL problem
/// (<see cref="PermissionDenied"/>). Persisted <c>AvailabilityStatus</c> stays
/// <c>"missing"</c> for every inaccessible root so FS-01/FS-04 readers keep
/// working; the distinction lives in this typed result.
/// </summary>
public enum SourceProbeStatus
{
    Available,
    MissingPath,
    UnavailableDrive,
    PermissionDenied,
    UnknownSource
}

public record SourceProbeResult(
    string SourceId,
    SourceProbeStatus Status,
    string? Detail = null,
    int AffectedDocuments = 0);

public record LocateResult(
    string DocumentId,
    bool Success,
    bool RequiresConfirmation,
    string? NewPath = null,
    string? Reason = null);

public record RemoveReferenceResult(
    string DocumentId,
    bool Success,
    string? ErrorMessage = null);

/// <summary>
/// FS-06 — Missing File &amp; Folder Recovery.
///
/// Keeps missing documents/sources visible with last-known metadata and
/// explicit locate/remove actions (workspace-canvas spec §32,
/// filesystem-sync spec §§15–17):
/// <list type="bullet">
/// <item>Persist missing state instead of deleting workspace metadata.</item>
/// <item>Locate with identity/fingerprint verification; the stable
/// <c>DocumentId</c> is rebound in place so canvas positions, groups and
/// collections keyed by <c>DocumentId</c> are restored untouched.</item>
/// <item>Remove reference deletes only the database reference, never the
/// physical file.</item>
/// </list>
///
/// SAFETY: this service never writes, moves, renames or deletes a user file.
/// The only filesystem writes it performs are reads (stat/fingerprint). The
/// only database deletes are explicit <c>RemoveReference</c> calls.
/// </summary>
public class MissingResourceService
{
    private static readonly string[] SupportedExtensions = { ".docx", ".md" };

    private readonly IDocumentRepository _documentRepo;
    private readonly IFolderSourceRepository _folderRepo;
    private readonly IFileIdentityProvider _identityProvider;

    public MissingResourceService(
        IDocumentRepository documentRepo,
        IFolderSourceRepository folderRepo,
        IFileIdentityProvider identityProvider)
    {
        _documentRepo = documentRepo ?? throw new ArgumentNullException(nameof(documentRepo));
        _folderRepo = folderRepo ?? throw new ArgumentNullException(nameof(folderRepo));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
    }

    /// <summary>
    /// Maps a filesystem failure to a typed probe status. Pure function so the
    /// missing vs permission-denied vs locked distinction is unit-testable
    /// without ACL tricks.
    /// </summary>
    public static ResourceProbeStatus ClassifyProbeFailure(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => ResourceProbeStatus.PermissionDenied,
            FileNotFoundException => ResourceProbeStatus.Missing,
            DirectoryNotFoundException => ResourceProbeStatus.Missing,
            IOException => ResourceProbeStatus.Locked,
            _ => ResourceProbeStatus.Unknown,
        };
    }

    /// <summary>
    /// Stats one tracked document. Missing state is persisted on the record
    /// (with <c>LastKnownPath</c> preserved); the record itself is never
    /// deleted. Never mutates user files.
    /// </summary>
    public async Task<ResourceProbeResult> ProbeDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));
        }

        var doc = await _documentRepo.GetByIdAsync(documentId);
        if (doc == null)
        {
            return new ResourceProbeResult(documentId, ResourceProbeStatus.Missing, "unknown-document");
        }

        if (string.IsNullOrWhiteSpace(doc.CurrentPath))
        {
            await MarkMissingAsync(doc, "empty-path");
            return new ResourceProbeResult(documentId, ResourceProbeStatus.Missing, "empty-path");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(doc.CurrentPath))
            {
                return new ResourceProbeResult(documentId, ResourceProbeStatus.Available);
            }

            string? parentDir = Path.GetDirectoryName(doc.CurrentPath);
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                await MarkMissingAsync(doc, "source-unavailable");
                return new ResourceProbeResult(documentId, ResourceProbeStatus.SourceUnavailable, "parent-directory-missing");
            }

            // Parent exists but the file does not. Distinguish "access denied"
            // from "deleted" by attempting a listing: an ACL failure throws
            // UnauthorizedAccessException instead of returning an empty set.
            try
            {
                _ = Directory.EnumerateFileSystemEntries(parentDir).Take(1).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                doc.AvailabilityStatus = "permission_denied";
                doc.UpdatedAt = DateTime.UtcNow.ToString("O");
                await _documentRepo.UpdateAsync(doc);
                return new ResourceProbeResult(documentId, ResourceProbeStatus.PermissionDenied, "parent-directory-access-denied");
            }

            await MarkMissingAsync(doc, "file-not-found");
            return new ResourceProbeResult(documentId, ResourceProbeStatus.Missing, "file-not-found");
        }
        catch (UnauthorizedAccessException)
        {
            doc.AvailabilityStatus = "permission_denied";
            doc.UpdatedAt = DateTime.UtcNow.ToString("O");
            await _documentRepo.UpdateAsync(doc);
            return new ResourceProbeResult(documentId, ResourceProbeStatus.PermissionDenied, "access-denied-during-probe");
        }
        catch (IOException ex)
        {
            doc.AvailabilityStatus = "locked";
            doc.UpdatedAt = DateTime.UtcNow.ToString("O");
            await _documentRepo.UpdateAsync(doc);
            return new ResourceProbeResult(documentId, ResourceProbeStatus.Locked, ex.Message);
        }
    }

    /// <summary>
    /// Rebinds a missing document to a user-chosen candidate path after
    /// identity/fingerprint verification (filesystem-sync spec §16: compare
    /// type, filename, size, fingerprint). The <c>DocumentId</c> is preserved
    /// in place, so canvas positions, groups and collections keyed by
    /// <c>DocumentId</c> are restored without any extra migration step.
    /// Uncertain candidates require <paramref name="confirmed"/>; without it
    /// the call fails closed with <c>RequiresConfirmation</c> and mutates
    /// nothing.
    /// </summary>
    public async Task<LocateResult> LocateAsync(
        string documentId,
        string candidatePath,
        bool confirmed = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return new LocateResult(documentId, false, false, null, "candidate-path-required");
        }

        var doc = await _documentRepo.GetByIdAsync(documentId);
        if (doc == null)
        {
            return new LocateResult(documentId, false, false, null, "unknown-document");
        }

        string canonicalCandidate;
        try
        {
            canonicalCandidate = Path.GetFullPath(candidatePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LocateResult(documentId, false, false, null, "invalid-candidate-path");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The file may disappear between stat and action: report typed, mutate nothing.
        if (!File.Exists(canonicalCandidate))
        {
            return new LocateResult(documentId, false, false, null, "candidate-not-found");
        }

        string candidateExtension = Path.GetExtension(canonicalCandidate);
        if (!SupportedExtensions.Contains(candidateExtension, StringComparer.OrdinalIgnoreCase))
        {
            return new LocateResult(documentId, false, false, null, "unsupported-type");
        }

        // Never steal a path/identity already tracked by a different document.
        var trackedByPath = await _documentRepo.GetByPathAsync(canonicalCandidate);
        if (trackedByPath != null && !string.Equals(trackedByPath.Id, documentId, StringComparison.Ordinal))
        {
            return new LocateResult(documentId, false, false, null, "target-already-tracked");
        }

        string candidateFingerprint;
        FileInfo candidateInfo;
        try
        {
            candidateInfo = new FileInfo(canonicalCandidate);
            candidateFingerprint = ComputeFingerprint(canonicalCandidate);
        }
        catch (UnauthorizedAccessException)
        {
            return new LocateResult(documentId, false, false, null, "candidate-permission-denied");
        }
        catch (IOException)
        {
            return new LocateResult(documentId, false, false, null, "candidate-locked");
        }

        var candidateIdentity = _identityProvider.GetFileIdentity(canonicalCandidate);
        if (candidateIdentity != null)
        {
            var trackedByIdentity = await _documentRepo.GetByVolumeAndObjectIdAsync(
                candidateIdentity.VolumeId, candidateIdentity.ObjectId);
            if (trackedByIdentity != null && !string.Equals(trackedByIdentity.Id, documentId, StringComparison.Ordinal))
            {
                return new LocateResult(documentId, false, false, null, "target-already-tracked");
            }
        }

        bool identityMatch = candidateIdentity != null
            && !string.IsNullOrEmpty(doc.FilesystemVolumeId)
            && !string.IsNullOrEmpty(doc.FilesystemObjectId)
            && string.Equals(doc.FilesystemVolumeId, candidateIdentity.VolumeId, StringComparison.Ordinal)
            && string.Equals(doc.FilesystemObjectId, candidateIdentity.ObjectId, StringComparison.Ordinal);

        bool fingerprintMatch = !string.IsNullOrEmpty(doc.ContentFingerprint)
            && string.Equals(doc.ContentFingerprint, candidateFingerprint, StringComparison.OrdinalIgnoreCase);

        bool extensionMatch = string.Equals(doc.Extension, candidateExtension, StringComparison.OrdinalIgnoreCase);
        bool filenameMatch = string.Equals(doc.Filename, candidateInfo.Name, StringComparison.OrdinalIgnoreCase);

        // Clearly the same file: stable NTFS identity, or identical content of
        // the same document type. Anything else needs explicit confirmation.
        bool certain = identityMatch || (fingerprintMatch && extensionMatch);

        if (!certain && !confirmed)
        {
            string reason = $"uncertain-match:filename={filenameMatch}:fingerprint={fingerprintMatch}:extension={extensionMatch}";
            return new LocateResult(documentId, false, true, null, reason);
        }

        string previousPath = doc.CurrentPath;
        if (!string.IsNullOrEmpty(previousPath))
        {
            doc.LastKnownPath = previousPath;
        }

        doc.CurrentPath = canonicalCandidate;
        doc.Filename = candidateInfo.Name;
        doc.Extension = candidateInfo.Extension;
        doc.SizeBytes = candidateInfo.Length;
        doc.ModifiedAt = candidateInfo.LastWriteTimeUtc.ToString("O");
        doc.ContentFingerprint = candidateFingerprint;
        doc.AvailabilityStatus = "available";
        doc.IndexStatus = "pending";
        if (candidateIdentity != null)
        {
            doc.FilesystemVolumeId = candidateIdentity.VolumeId;
            doc.FilesystemObjectId = candidateIdentity.ObjectId;
        }

        doc.UpdatedAt = DateTime.UtcNow.ToString("O");
        await _documentRepo.UpdateAsync(doc);

        // DocumentId unchanged: workspace_documents / group / collection rows
        // keyed by DocumentId keep pointing at this record, restoring prior
        // canvas/group state with no further writes.
        return new LocateResult(documentId, true, false, canonicalCandidate, null);
    }

    /// <summary>
    /// Removes the workspace reference to a document. Deletes only the
    /// database record (canvas rows cascade by FK); the physical file is never
    /// touched — this method performs zero filesystem writes.
    /// </summary>
    public async Task<RemoveReferenceResult> RemoveReferenceAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var doc = await _documentRepo.GetByIdAsync(documentId);
        if (doc == null)
        {
            return new RemoveReferenceResult(documentId, false, "unknown-document");
        }

        // Database reference only. No File.Delete / Directory.Delete anywhere
        // on this path: the physical file must survive verbatim.
        await _documentRepo.DeleteAsync(documentId);
        return new RemoveReferenceResult(documentId, true);
    }

    /// <summary>
    /// Probes a folder source root. An inaccessible root (disconnected drive,
    /// renamed path, denied ACL) marks the source and its child documents as
    /// missing while preserving every record — workspace layout is never
    /// destroyed. Never deletes records and never touches user files.
    /// </summary>
    public async Task<SourceProbeResult> ProbeSourceAsync(
        string workspaceId,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("Source ID cannot be null or empty.", nameof(sourceId));
        }

        var sources = await _folderRepo.GetByWorkspaceAsync(workspaceId);
        var source = sources.FirstOrDefault(s => string.Equals(s.Id, sourceId, StringComparison.Ordinal));
        if (source == null)
        {
            return new SourceProbeResult(sourceId, SourceProbeStatus.UnknownSource, "unknown-source");
        }

        if (string.IsNullOrWhiteSpace(source.CanonicalPath))
        {
            return new SourceProbeResult(sourceId, SourceProbeStatus.MissingPath, "empty-source-path");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (Directory.Exists(source.CanonicalPath))
            {
                if (source.AvailabilityStatus != "available")
                {
                    await _folderRepo.UpdateAvailabilityAsync(source.Id, "available");
                }

                return new SourceProbeResult(sourceId, SourceProbeStatus.Available);
            }

            // Root inaccessible: distinguish a disconnected drive from a
            // renamed/deleted folder for the typed outcome. Persisted status
            // stays "missing" for FS-01/FS-04 reader compatibility.
            string root = Path.GetPathRoot(source.CanonicalPath) ?? string.Empty;
            bool isDriveRoot = string.Equals(
                source.CanonicalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                root,
                StringComparison.OrdinalIgnoreCase);

            var status = isDriveRoot ? SourceProbeStatus.UnavailableDrive : SourceProbeStatus.MissingPath;
            string detail = isDriveRoot ? "drive-unavailable" : "path-not-found";

            if (source.AvailabilityStatus != "missing")
            {
                await _folderRepo.UpdateAvailabilityAsync(source.Id, "missing");
            }

            int affected = await MarkChildrenMissingAsync(source.CanonicalPath, cancellationToken);
            return new SourceProbeResult(sourceId, status, detail, affected);
        }
        catch (UnauthorizedAccessException)
        {
            return new SourceProbeResult(sourceId, SourceProbeStatus.PermissionDenied, "source-access-denied");
        }
        catch (IOException ex)
        {
            return new SourceProbeResult(sourceId, SourceProbeStatus.PermissionDenied, ex.Message);
        }
    }

    private async Task MarkMissingAsync(DocumentIDE.Core.Domain.Document doc, string detail)
    {
        if (string.IsNullOrEmpty(doc.LastKnownPath))
        {
            doc.LastKnownPath = doc.CurrentPath;
        }

        doc.AvailabilityStatus = "missing";
        doc.UpdatedAt = DateTime.UtcNow.ToString("O");
        await _documentRepo.UpdateAsync(doc);
    }

    private async Task<int> MarkChildrenMissingAsync(string canonicalSourcePath, CancellationToken cancellationToken)
    {
        var children = await _documentRepo.GetByFolderSourcePathAsync(canonicalSourcePath);
        int affected = 0;
        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.AvailabilityStatus != "missing")
            {
                if (string.IsNullOrEmpty(child.LastKnownPath))
                {
                    child.LastKnownPath = child.CurrentPath;
                }

                child.AvailabilityStatus = "missing";
                child.UpdatedAt = DateTime.UtcNow.ToString("O");
                await _documentRepo.UpdateAsync(child);
            }

            affected++;
        }

        return affected;
    }

    private static string ComputeFingerprint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
