using System;
using System.Collections.Generic;
using System.Linq;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Workspaces;

/// <summary>
/// CAN-04: Folder Picker & File Release — a file listed by a folder source
/// that can be released onto the workspace canvas.
/// Contains document identity + display name only; NEVER a physical path.
/// </summary>
public sealed class CanvasFileCandidate
{
    /// <summary>Stable document identity (FS-02).</summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>Exact file name (basename) shown by the folder picker.</summary>
    public string Filename { get; init; } = string.Empty;
}

/// <summary>
/// A candidate that could not be released, with a deterministic reason
/// (typed/inspectable outcome instead of a silent fallback).
/// </summary>
public sealed class CanvasReleaseRejection
{
    public CanvasReleaseRejection(string documentId, string filename, string reason)
    {
        DocumentId = documentId ?? string.Empty;
        Filename = filename ?? string.Empty;
        Reason = reason ?? string.Empty;
    }

    public string DocumentId { get; }

    public string Filename { get; }

    public string Reason { get; }
}

/// <summary>Outcome of releasing documents from a folder onto the canvas.</summary>
public sealed class CanvasReleaseResult
{
    /// <summary>Workspace documents created/activated by this release.</summary>
    public IReadOnlyList<WorkspaceDocument> Released { get; init; } = Array.Empty<WorkspaceDocument>();

    /// <summary>Deterministic per-candidate failure reasons.</summary>
    public IReadOnlyList<CanvasReleaseRejection> Rejected { get; init; } = Array.Empty<CanvasReleaseRejection>();
}

/// <summary>Deterministic rejection reasons for inspectable outcomes.</summary>
public static class CanvasReleaseRejectionReasons
{
    /// <summary>The file type is not releasable (only .docx and .md are supported).</summary>
    public const string UnsupportedFileType = "unsupported-file-type";

    /// <summary>The candidate has no stable document identity.</summary>
    public const string MissingDocumentId = "missing-document-id";

    /// <summary>The same DocumentId was listed more than once.</summary>
    public const string DuplicateDocumentId = "duplicate-document-id";

    /// <summary>A selected DocumentId is not present in the folder listing.</summary>
    public const string NotInFolderListing = "not-in-folder-listing";
}

/// <summary>
/// CAN-04: creates/activates WorkspaceDocument objects when files are released
/// from a folder picker onto the canvas.
///
/// CRITICAL SAFETY CONTRACT:
/// - This service NEVER touches the filesystem: no move, rename, copy, delete,
///   write, or read. It only creates workspace metadata objects.
/// - Candidates carry identity + file name only; physical paths are never
///   accepted, stored, or exposed.
/// - Canvas release is the opposite of a physical file operation: the local
///   filesystem remains the source of truth and no file is moved.
/// </summary>
public sealed class CanvasDocumentService
{
    /// <summary>
    /// Supported document types — mirrors FS-01 folder attachment
    /// (canonical format support): `.docx` and `.md`.
    /// </summary>
    private static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".md" };

    /// <summary>
    /// Creates/activates a WorkspaceDocument for every supported candidate in
    /// the folder listing. Never moves files — canvas objects are metadata.
    /// </summary>
    public CanvasReleaseResult ReleaseAll(string workspaceId, IReadOnlyList<CanvasFileCandidate> folderListing)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        if (folderListing is null)
        {
            throw new ArgumentNullException(nameof(folderListing));
        }

        var released = new List<WorkspaceDocument>(folderListing.Count);
        var rejected = new List<CanvasReleaseRejection>();
        var seenDocumentIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in folderListing)
        {
            if (string.IsNullOrWhiteSpace(candidate.DocumentId))
            {
                rejected.Add(new CanvasReleaseRejection(
                    candidate.DocumentId,
                    candidate.Filename,
                    CanvasReleaseRejectionReasons.MissingDocumentId));
                continue;
            }

            if (!IsSupportedFilename(candidate.Filename))
            {
                rejected.Add(new CanvasReleaseRejection(
                    candidate.DocumentId,
                    candidate.Filename,
                    CanvasReleaseRejectionReasons.UnsupportedFileType));
                continue;
            }

            if (!seenDocumentIds.Add(candidate.DocumentId))
            {
                rejected.Add(new CanvasReleaseRejection(
                    candidate.DocumentId,
                    candidate.Filename,
                    CanvasReleaseRejectionReasons.DuplicateDocumentId));
                continue;
            }

            released.Add(CreateWorkspaceDocument(workspaceId, candidate.DocumentId));
        }

        return new CanvasReleaseResult { Released = released, Rejected = rejected };
    }

    /// <summary>
    /// "Open selected": creates/activates WorkspaceDocuments only for the
    /// selected DocumentIds. Release order follows the folder listing.
    /// Never moves files — canvas objects are metadata.
    /// </summary>
    public CanvasReleaseResult ReleaseSelected(
        string workspaceId,
        IReadOnlyList<CanvasFileCandidate> folderListing,
        IReadOnlyCollection<string> selectedDocumentIds)
    {
        if (folderListing is null)
        {
            throw new ArgumentNullException(nameof(folderListing));
        }

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        if (selectedDocumentIds is null)
        {
            throw new ArgumentNullException(nameof(selectedDocumentIds));
        }

        var selected = new HashSet<string>(selectedDocumentIds, StringComparer.Ordinal);
        var selectedCandidates = folderListing
            .Where(candidate => selected.Contains(candidate.DocumentId))
            .ToList();

        var result = ReleaseAll(workspaceId, selectedCandidates);

        // Selected ids that are not part of the folder listing fail closed with
        // a deterministic, inspectable reason instead of being silently ignored.
        var listingDocumentIds = new HashSet<string>(
            folderListing
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.DocumentId))
                .Select(candidate => candidate.DocumentId),
            StringComparer.Ordinal);

        var rejected = result.Rejected.ToList();
        foreach (var selectedId in selectedDocumentIds.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(selectedId) || !listingDocumentIds.Contains(selectedId))
            {
                rejected.Add(new CanvasReleaseRejection(
                    selectedId,
                    string.Empty,
                    CanvasReleaseRejectionReasons.NotInFolderListing));
            }
        }

        return new CanvasReleaseResult { Released = result.Released, Rejected = rejected };
    }

    /// <summary>
    /// True when the file name has a supported extension (.docx / .md).
    /// Pure string check — never touches the filesystem.
    /// </summary>
    public static bool IsSupportedFilename(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        int dot = filename.LastIndexOf('.');
        if (dot < 0 || dot == filename.Length - 1)
        {
            return false;
        }

        return SupportedExtensions.Contains(filename[dot..]);
    }

    private static WorkspaceDocument CreateWorkspaceDocument(string workspaceId, string documentId)
    {
        // Creating the WorkspaceDocument IS the activation of the document on
        // the workspace canvas. No physical file is moved or modified.
        return new WorkspaceDocument
        {
            WorkspaceId = workspaceId,
            DocumentId = documentId,
            PositionMode = "auto",
            Pinned = false,
        };
    }
}
