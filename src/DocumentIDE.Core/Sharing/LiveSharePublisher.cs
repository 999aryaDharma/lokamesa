using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Core.Sharing;

/// <summary>
/// One explicit live-share member source as saved at refresh time.
/// DOCX previews arrive as already-rendered owner-side WORD-04 cache bytes;
/// the publisher copies them verbatim and never invokes Word.
/// </summary>
public sealed record LiveSourceDocument(
    string DocumentId,
    string DisplayName,
    string Extension,
    string? MarkdownSource = null,
    byte[]? DocxPreviewBytes = null);

/// <summary>Outcome of a live-share publication.</summary>
public sealed record LivePublishResult(
    string ShareId,
    string ShareRoot,
    ShareManifest Manifest,
    IReadOnlyList<string> ArtifactRelativePaths);

/// <summary>
/// Deterministic outcome of handling one FS-03 file-change event against the
/// current member set. Unknown (nonmember) documents are never published;
/// only saved changes to existing members refresh artifacts.
/// </summary>
public enum LiveRefreshOutcome
{
    /// <summary>Member artifact and versioned manifest were refreshed.</summary>
    Refreshed,
    /// <summary>Document is not a share member: nothing published.</summary>
    IgnoredNonMember,
    /// <summary>Change kind carries no new content (e.g. Deleted): kept as-is.</summary>
    IgnoredChangeKind,
    /// <summary>Member, but the owner-side resolver supplied no content.</summary>
    IgnoredNoResolvedContent,
}

/// <summary>Record of one watcher event handled for a live share.</summary>
public sealed record LiveWatcherResult(
    FileChangeType ChangeType,
    string? DocumentId,
    LiveRefreshOutcome Outcome,
    int LiveRevision);

/// <summary>
/// SHR-05 — Membership-safe Live Share publisher (owner side).
///
/// Live means content updates for existing members only: the owner process
/// subscribes to FS-03 watcher changes, refreshes artifacts after saved
/// member-file changes, and rewrites the manifest with a bumped
/// <c>LiveRevision</c> atomically (temp + move). New files in a shared folder
/// and later collection membership changes never become public automatically;
/// adding or removing members requires the explicit owner operations
/// <see cref="AddMember"/> / <see cref="RemoveMember"/>.
/// </summary>
public sealed class LiveSharePublisher
{
    /// <summary>Live share format marker (manifest <c>Mode</c> is "Live").</summary>
    public const string LiveFormatVersion = "shr05-v1";

    /// <summary>Maximum DOCX preview artifact accepted per document (10 MiB).</summary>
    public const long MaxDocxPreviewBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> MarkdownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

    private static readonly HashSet<string> DocxExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".docx" };

    private readonly ShareTokenService _tokens;
    private readonly ISnapshotMarkdownRenderer _markdown;
    private readonly object _gate = new();

    public LiveSharePublisher(ShareTokenService tokens, ISnapshotMarkdownRenderer markdown)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
    }

    /// <summary>Application-owned share root for this publisher's token service.</summary>
    public string ShareRootFor(string shareId) => _tokens.GetShareRootPath(shareId);

    /// <summary>
    /// Initial live publication: freezes the explicit member set and writes
    /// revision 1 of the manifest and artifacts atomically.
    /// </summary>
    public LivePublishResult Publish(Share share, IEnumerable<LiveSourceDocument> sources)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (sources == null) throw new ArgumentNullException(nameof(sources));
        RequireLiveShare(share);

        var sourceList = ValidateSources(sources);

        lock (_gate)
        {
            var shareRoot = _tokens.EnsureShareRootExists(share.Id);
            var manifestPath = Path.Combine(shareRoot, "manifest.json");
            if (File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"Share '{share.Id}' is already published: create a new share instead of " +
                    "republishing over a live package.");
            }

            share.SnapshotMembersFromCollection(ToCandidateDocuments(sourceList));
            var manifest = BuildManifest(share);
            manifest.LiveRevision = 1;

            var artifacts = new List<string>();
            foreach (var source in sourceList)
            {
                artifacts.Add(WriteMemberArtifact(share, source));
            }

            WriteManifestAtomically(manifestPath, manifest);
            return new LivePublishResult(share.Id, shareRoot, manifest, artifacts);
        }
    }

    /// <summary>
    /// Refreshes one existing member's artifact after a saved local change and
    /// bumps <c>LiveRevision</c>. Throws for nonmembers: refreshing an unknown
    /// document is a membership decision, not a content update.
    /// </summary>
    /// <returns>The new live revision.</returns>
    public int RefreshMember(Share share, LiveSourceDocument updated)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (updated == null) throw new ArgumentNullException(nameof(updated));
        RequireLiveShare(share);
        ValidateDocumentId(updated.DocumentId);

        lock (_gate)
        {
            if (!IsMember(share, updated.DocumentId))
            {
                throw new InvalidOperationException(
                    $"Document '{updated.DocumentId}' is not a share member of '{share.Id}': " +
                    "live refresh never auto-publishes new membership. Use AddMember for an explicit owner decision.");
            }

            WriteMemberArtifact(share, updated);
            return BumpRevision(share);
        }
    }

    /// <summary>
    /// Reports the current collection without changing membership: later
    /// collection additions never auto-publish. Returns the member count,
    /// which is always unchanged by this call.
    /// </summary>
    public int NoteCollectionChanged(Share share, IEnumerable<Document> currentCollection)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (currentCollection == null) throw new ArgumentNullException(nameof(currentCollection));
        RequireLiveShare(share);

        lock (_gate)
        {
            // Deliberate no-op on membership: consume the enumeration so lazy
            // query errors surface deterministically, then change nothing.
            foreach (var _ in currentCollection) { }
            return share.Members.Count;
        }
    }

    /// <summary>
    /// Explicit owner operation: adds a member, publishes its artifact, and
    /// bumps <c>LiveRevision</c>.
    /// </summary>
    /// <returns>The new live revision.</returns>
    public int AddMember(Share share, LiveSourceDocument newMember)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (newMember == null) throw new ArgumentNullException(nameof(newMember));
        RequireLiveShare(share);
        ValidateDocumentId(newMember.DocumentId);

        lock (_gate)
        {
            if (IsMember(share, newMember.DocumentId))
            {
                throw new InvalidOperationException(
                    $"Document '{newMember.DocumentId}' is already a share member of '{share.Id}'.");
            }

            share.Members.Add(new ShareMember
            {
                Id = Guid.NewGuid().ToString(),
                ShareId = share.Id,
                DocumentId = newMember.DocumentId,
                DisplayPath = newMember.DisplayName,
                AddedAt = DateTime.UtcNow.ToString("O")
            });
            WriteMemberArtifact(share, newMember);
            return BumpRevision(share);
        }
    }

    /// <summary>
    /// Explicit owner operation: removes a member and its artifact, and bumps
    /// <c>LiveRevision</c>. Only app-owned share artifacts are deleted; the
    /// owner's source files are never touched.
    /// </summary>
    /// <returns>The new live revision.</returns>
    public int RemoveMember(Share share, string documentId)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));
        RequireLiveShare(share);

        lock (_gate)
        {
            var member = share.Members.FirstOrDefault(m =>
                string.Equals(m.DocumentId, documentId, StringComparison.Ordinal));
            if (member == null)
            {
                throw new InvalidOperationException(
                    $"Document '{documentId}' is not a share member of '{share.Id}'.");
            }

            share.Members.Remove(member);
            share.SnapshotArtifacts.RemoveAll(a =>
                string.Equals(a.DocumentId, documentId, StringComparison.Ordinal));

            var artifactPath = _tokens.GetArtifactPath(share.Id, "previews/" + documentId + ".html");
            if (File.Exists(artifactPath))
            {
                File.Delete(artifactPath);
            }

            return BumpRevision(share);
        }
    }

    /// <summary>
    /// Handles one FS-03 <see cref="FileChangeEvent"/> against the current
    /// member set. Member saves (Created/Modified/Renamed) refresh the
    /// artifact via <paramref name="resolveCurrent"/>; anything else —
    /// unknown documents, deletions, unresolvable content — is ignored with a
    /// typed outcome and never publishes.
    /// </summary>
    public LiveWatcherResult HandleFileChange(
        Share share,
        FileChangeEvent change,
        Func<string?, LiveSourceDocument?> resolveCurrent)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (change == null) throw new ArgumentNullException(nameof(change));
        if (resolveCurrent == null) throw new ArgumentNullException(nameof(resolveCurrent));
        RequireLiveShare(share);

        if (change.ChangeType == FileChangeType.Deleted)
        {
            return new LiveWatcherResult(change.ChangeType, change.DocumentId,
                LiveRefreshOutcome.IgnoredChangeKind, ReadRevision(share));
        }

        if (string.IsNullOrWhiteSpace(change.DocumentId) || !IsMember(share, change.DocumentId!))
        {
            return new LiveWatcherResult(change.ChangeType, change.DocumentId,
                LiveRefreshOutcome.IgnoredNonMember, ReadRevision(share));
        }

        var current = resolveCurrent(change.DocumentId);
        if (current == null)
        {
            return new LiveWatcherResult(change.ChangeType, change.DocumentId,
                LiveRefreshOutcome.IgnoredNoResolvedContent, ReadRevision(share));
        }

        if (!string.Equals(current.DocumentId, change.DocumentId, StringComparison.Ordinal))
        {
            return new LiveWatcherResult(change.ChangeType, change.DocumentId,
                LiveRefreshOutcome.IgnoredNonMember, ReadRevision(share));
        }

        var revision = RefreshMember(share, current);
        return new LiveWatcherResult(change.ChangeType, change.DocumentId,
            LiveRefreshOutcome.Refreshed, revision);
    }

    /// <summary>
    /// Subscribes the live share to verified FS-03 watcher changes delivered
    /// by <paramref name="coordinator"/>. Each <c>OnFileChanged</c> event is
    /// routed through <see cref="HandleFileChange"/> so the current member set
    /// is the only publish scope. Returns a subscription that detaches on dispose.
    /// </summary>
    public IDisposable Subscribe(
        Share share,
        FileChangeCoordinator coordinator,
        Func<string?, LiveSourceDocument?> resolveCurrent,
        Action<LiveWatcherResult>? onHandled = null)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
        if (resolveCurrent == null) throw new ArgumentNullException(nameof(resolveCurrent));

        EventHandler<FileChangeEvent> handler = (_, change) =>
        {
            var result = HandleFileChange(share, change, resolveCurrent);
            onHandled?.Invoke(result);
        };
        coordinator.OnFileChanged += handler;
        return new LiveShareSubscription(coordinator, handler);
    }

    private static void RequireLiveShare(Share share)
    {
        if (string.IsNullOrWhiteSpace(share.Id))
            throw new ArgumentException("Share must have an ID.", nameof(share));
        if (share.Mode != ShareMode.Live)
            throw new InvalidOperationException(
                $"LiveSharePublisher publishes Live shares only (share '{share.Id}' is {share.Mode}).");
    }

    private static List<LiveSourceDocument> ValidateSources(IEnumerable<LiveSourceDocument> sources)
    {
        var sourceList = sources.ToList();
        if (sourceList.Count == 0)
            throw new ArgumentException("Live share requires at least one member artifact.", nameof(sources));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sourceList)
        {
            if (source == null)
                throw new ArgumentException("Live source must not be null.", nameof(sources));
            ValidateDocumentId(source.DocumentId);
            if (!seen.Add(source.DocumentId))
                throw new ArgumentException(
                    $"Duplicate live source for document '{source.DocumentId}'.", nameof(sources));
        }

        return sourceList;
    }

    private static void ValidateDocumentId(string documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Live source must have a document ID.", nameof(documentId));
        if (documentId.Contains("..", StringComparison.Ordinal)
            || documentId.IndexOfAny(new[] { '/', '\\' }) >= 0)
            throw new InvalidOperationException(
                $"Invalid document ID '{documentId}': path separators and traversal are rejected.");
    }

    private static bool IsMember(Share share, string documentId) =>
        share.Members.Any(m => string.Equals(m.DocumentId, documentId, StringComparison.Ordinal));

    private static List<Document> ToCandidateDocuments(IEnumerable<LiveSourceDocument> sources) =>
        sources.Select(s => new Document
        {
            Id = s.DocumentId,
            Filename = s.DisplayName,
            Extension = s.Extension,
            DocumentType = DocxExtensions.Contains((s.Extension ?? string.Empty).Trim()) ? "docx" : "markdown"
        }).ToList();

    private ShareManifest BuildManifest(Share share)
    {
        // Membership-derived candidates always pass ShareManifest.Create's
        // explicit-membership filter; nonmembers can never slip in here
        // because only current Members are projected.
        var candidates = share.Members.Select(m => new Document
        {
            Id = m.DocumentId,
            Filename = m.DisplayPath,
            Extension = Path.GetExtension(m.DisplayPath ?? string.Empty),
            DocumentType = DocxExtensions.Contains(
                (Path.GetExtension(m.DisplayPath ?? string.Empty) ?? string.Empty).Trim())
                ? "docx" : "markdown"
        }).ToList();

        var manifest = ShareManifest.Create(share, candidates);
        manifest.AllowOriginalDownload = false;
        manifest.AllowPdfDownload = false;
        return manifest;
    }

    private string WriteMemberArtifact(Share share, LiveSourceDocument source)
    {
        var ext = (source.Extension ?? string.Empty).Trim();
        string relativePath = "previews/" + source.DocumentId + ".html";
        byte[] payload;

        if (MarkdownExtensions.Contains(ext))
        {
            var html = _markdown.RenderAndSanitize(source.MarkdownSource ?? string.Empty)
                ?? string.Empty;
            payload = Encoding.UTF8.GetBytes(html);
        }
        else if (DocxExtensions.Contains(ext))
        {
            if (source.DocxPreviewBytes == null || source.DocxPreviewBytes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"DOCX live preview for '{source.DocumentId}' requires owner-side WORD-04 preview bytes; " +
                    "the publisher never renders via Word itself.");
            }
            if (source.DocxPreviewBytes.Length > MaxDocxPreviewBytes)
            {
                throw new InvalidOperationException(
                    $"DOCX preview for '{source.DocumentId}' exceeds the live artifact budget.");
            }
            payload = source.DocxPreviewBytes;
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported live extension '{source.Extension}' for '{source.DocumentId}': V1 live shares cover Markdown and DOCX previews only.");
        }

        var artifactPath = _tokens.GetArtifactPath(share.Id, relativePath);
        var artifactDir = Path.GetDirectoryName(artifactPath);
        if (!string.IsNullOrEmpty(artifactDir))
        {
            Directory.CreateDirectory(artifactDir);
        }

        // Atomic replace: temp file then move over the previous revision, so
        // a crash can never leave a half-written live artifact behind.
        // (Snapshot SHR-04 used overwrite:false because frozen artifacts are
        // write-once; live artifacts are rewritten by design on every refresh.)
        var tempPath = artifactPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tempPath, payload);
            File.Move(tempPath, artifactPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        var existing = share.SnapshotArtifacts.FirstOrDefault(a =>
            string.Equals(a.DocumentId, source.DocumentId, StringComparison.Ordinal));
        var record = new ShareSnapshotArtifact
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = source.DocumentId,
            ArtifactType = "preview-html",
            RelativePath = relativePath,
            ContentHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            SizeBytes = payload.Length,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        if (existing == null)
        {
            share.SnapshotArtifacts.Add(record);
        }
        else
        {
            share.SnapshotArtifacts[share.SnapshotArtifacts.IndexOf(existing)] = record;
        }

        return relativePath;
    }

    private int BumpRevision(Share share)
    {
        var manifest = BuildManifest(share);
        manifest.LiveRevision = ReadRevision(share) + 1;
        var manifestPath = Path.Combine(_tokens.GetShareRootPath(share.Id), "manifest.json");
        WriteManifestAtomically(manifestPath, manifest);
        return manifest.LiveRevision;
    }

    private int ReadRevision(Share share)
    {
        var manifestPath = Path.Combine(_tokens.GetShareRootPath(share.Id), "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return 0;
        }

        try
        {
            return JsonSerializer.Deserialize<ShareManifest>(
                File.ReadAllText(manifestPath, Encoding.UTF8))?.LiveRevision ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static void WriteManifestAtomically(string manifestPath, ShareManifest manifest)
    {
        // Atomic write: temp file then move, so viewers never observe a
        // half-written manifest revision.
        var manifestJson = JsonSerializer.Serialize(manifest);
        var manifestTemp = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(manifestTemp, manifestJson, Encoding.UTF8);
            File.Move(manifestTemp, manifestPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(manifestTemp))
            {
                try { File.Delete(manifestTemp); } catch { }
            }
        }
    }

    private sealed class LiveShareSubscription(
        FileChangeCoordinator coordinator,
        EventHandler<FileChangeEvent> handler) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            coordinator.OnFileChanged -= handler;
        }
    }
}
