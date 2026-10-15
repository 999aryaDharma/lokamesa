using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;

namespace DocumentIDE.Core.Sharing;

/// <summary>
/// Owner-side Markdown render step for snapshot publishing (MD-02 pattern:
/// render Markdown, then sanitize with a maintained sanitizer).
/// Production wiring injects the owner-side preview pipeline; Core holds no
/// Markdown/HTML library dependency itself.
/// </summary>
public interface ISnapshotMarkdownRenderer
{
    string RenderAndSanitize(string markdown);
}

/// <summary>
/// One explicit share-member source as it exists at publication time.
/// DOCX previews arrive as already-rendered owner-side WORD-04 cache bytes;
/// the publisher copies them verbatim and never invokes Word.
/// SHR-07: when the share allows original/PDF download, the corresponding
/// owner-side bytes are frozen as <c>originals/</c>/<c>pdfs/</c> artifacts;
/// when the permission is OFF the bytes are never written to the share root.
/// </summary>
public sealed record SnapshotSourceDocument(
    string DocumentId,
    string DisplayName,
    string Extension,
    string? MarkdownSource = null,
    byte[]? DocxPreviewBytes = null,
    byte[]? OriginalBytes = null,
    byte[]? PdfBytes = null);

/// <summary>Outcome of a frozen snapshot publication.</summary>
public sealed record SnapshotPublishResult(
    string ShareId,
    string ShareRoot,
    ShareManifest Manifest,
    IReadOnlyList<string> ArtifactRelativePaths);

/// <summary>
/// SHR-04 — Snapshot Share publisher (owner side).
///
/// Freezes explicit member artifacts as they exist at publication time:
/// sanitized Markdown HTML (MD-02) and owner-rendered DOCX preview bytes
/// (WORD-04 cache). Only allowed public metadata is copied via
/// <see cref="ShareManifest.Create"/> (no physical paths). The manifest is
/// versioned and written atomically; republishing an already-frozen share is
/// rejected so later local edits can never silently rewrite the snapshot.
/// SHR-07: original/PDF download artifacts are prepared only as the share
/// policy requires — both permissions default OFF (see <see cref="Share"/>),
/// and when OFF no <c>originals/</c>/<c>pdfs/</c> bytes exist under the
/// share root, so the ShareHost permission gates have nothing to serve.
/// </summary>
public sealed class SnapshotSharePublisher
{
    /// <summary>Frozen snapshot format marker recorded in the manifest.</summary>
    public const string SnapshotFormatVersion = "shr04-v1";

    /// <summary>Maximum DOCX preview artifact accepted per document (10 MiB).</summary>
    public const long MaxDocxPreviewBytes = 10 * 1024 * 1024;

    /// <summary>Maximum original-download artifact accepted per document (10 MiB, SHR-07).</summary>
    public const long MaxOriginalArtifactBytes = 10 * 1024 * 1024;

    /// <summary>Maximum PDF-download artifact accepted per document (10 MiB, SHR-07).</summary>
    public const long MaxPdfArtifactBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> MarkdownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

    private static readonly HashSet<string> DocxExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".docx" };

    private readonly ShareTokenService _tokens;
    private readonly ISnapshotMarkdownRenderer _markdown;

    public SnapshotSharePublisher(ShareTokenService tokens, ISnapshotMarkdownRenderer markdown)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
    }

    public SnapshotPublishResult Publish(Share share, IEnumerable<SnapshotSourceDocument> sources)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (sources == null) throw new ArgumentNullException(nameof(sources));
        if (string.IsNullOrWhiteSpace(share.Id))
            throw new ArgumentException("Share must have an ID.", nameof(share));
        if (share.Mode != ShareMode.Snapshot)
            throw new InvalidOperationException(
                $"SnapshotSharePublisher publishes Snapshot shares only (share '{share.Id}' is {share.Mode}).");

        var sourceList = sources.ToList();
        if (sourceList.Count == 0)
            throw new ArgumentException("Snapshot requires at least one member artifact.", nameof(sources));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sourceList)
        {
            if (source == null)
                throw new ArgumentException("Snapshot source must not be null.", nameof(sources));
            if (string.IsNullOrWhiteSpace(source.DocumentId))
                throw new ArgumentException("Snapshot source must have a document ID.", nameof(sources));
            if (source.DocumentId.Contains("..", StringComparison.Ordinal)
                || source.DocumentId.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new InvalidOperationException(
                    $"Invalid document ID '{source.DocumentId}': path separators and traversal are rejected.");
            if (!seen.Add(source.DocumentId))
                throw new ArgumentException(
                    $"Duplicate snapshot source for document '{source.DocumentId}'.", nameof(sources));
        }

        var shareRoot = _tokens.EnsureShareRootExists(share.Id);
        var manifestPath = Path.Combine(shareRoot, "manifest.json");
        if (File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"Share '{share.Id}' is already frozen: the published snapshot cannot be rewritten by later local edits. " +
                "Create a new share to publish a new snapshot.");
        }

        // Explicit membership is frozen at publication: only these sources
        // become members; later collection/workspace additions never auto-enter.
        var candidates = sourceList.Select(s => new Document
        {
            Id = s.DocumentId,
            Filename = s.DisplayName,
            Extension = s.Extension,
            DocumentType = DocxExtensions.Contains((s.Extension ?? string.Empty).Trim()) ? "docx" : "markdown"
        }).ToList();
        share.SnapshotMembersFromCollection(candidates);

        var manifest = ShareManifest.Create(share, candidates);
        manifest.SnapshotVersion = SnapshotFormatVersion;
        // SHR-07: the owner-chosen download policy is preserved in the frozen
        // manifest (both default OFF on Share). The ShareHost download routes
        // re-read these flags per request; OFF means 403 and no artifact bytes.
        manifest.AllowOriginalDownload = share.AllowOriginalDownload;
        manifest.AllowPdfDownload = share.AllowPdfDownload;

        var artifacts = new List<string>();
        foreach (var source in sourceList)
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
                        $"DOCX snapshot for '{source.DocumentId}' requires owner-side WORD-04 preview bytes; " +
                        "the publisher never renders via Word itself.");
                }
                if (source.DocxPreviewBytes.Length > MaxDocxPreviewBytes)
                {
                    throw new InvalidOperationException(
                        $"DOCX preview for '{source.DocumentId}' exceeds the snapshot artifact budget.");
                }
                payload = source.DocxPreviewBytes;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported snapshot extension '{source.Extension}' for '{source.DocumentId}': V1 snapshots cover Markdown and DOCX previews only.");
            }

            var artifactPath = _tokens.GetArtifactPath(share.Id, relativePath);
            WriteArtifactAtomically(artifactPath, payload);

            share.SnapshotArtifacts.Add(new ShareSnapshotArtifact
            {
                Id = Guid.NewGuid().ToString(),
                ShareId = share.Id,
                DocumentId = source.DocumentId,
                ArtifactType = "preview-html",
                RelativePath = relativePath,
                ContentHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
                SizeBytes = payload.Length,
                CreatedAt = DateTime.UtcNow.ToString("O")
            });
            artifacts.Add(relativePath);
        }

        // SHR-07: download artifacts are prepared only as the frozen policy
        // requires. Filenames match the ShareHost download routes exactly:
        // download-original serves originals/{DisplayName} and download-pdf
        // serves pdfs/{ChangeExtension(DisplayName, ".pdf")}, where
        // DisplayName is the sanitized manifest name (never a physical path).
        foreach (var source in sourceList)
        {
            var displayName = manifest.Documents
                .Find(d => string.Equals(d.DocumentId, source.DocumentId, StringComparison.Ordinal))
                ?.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new InvalidOperationException(
                    $"Snapshot member '{source.DocumentId}' has no sanitized manifest entry.");
            }

            if (manifest.AllowOriginalDownload)
            {
                if (source.OriginalBytes == null || source.OriginalBytes.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Original download is allowed for share '{share.Id}' but no owner-side " +
                        $"original bytes were provided for '{source.DocumentId}'.");
                }
                if (source.OriginalBytes.Length > MaxOriginalArtifactBytes)
                {
                    throw new InvalidOperationException(
                        $"Original artifact for '{source.DocumentId}' exceeds the snapshot artifact budget.");
                }
                var relativePath = "originals/" + displayName;
                WriteArtifactAtomically(_tokens.GetArtifactPath(share.Id, relativePath), source.OriginalBytes);
                share.SnapshotArtifacts.Add(new ShareSnapshotArtifact
                {
                    Id = Guid.NewGuid().ToString(),
                    ShareId = share.Id,
                    DocumentId = source.DocumentId,
                    ArtifactType = "original",
                    RelativePath = relativePath,
                    ContentHash = Convert.ToHexString(SHA256.HashData(source.OriginalBytes)).ToLowerInvariant(),
                    SizeBytes = source.OriginalBytes.Length,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                });
                artifacts.Add(relativePath);
            }

            if (manifest.AllowPdfDownload)
            {
                if (source.PdfBytes == null || source.PdfBytes.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"PDF download is allowed for share '{share.Id}' but no owner-side " +
                        $"PDF bytes were provided for '{source.DocumentId}'.");
                }
                if (source.PdfBytes.Length > MaxPdfArtifactBytes)
                {
                    throw new InvalidOperationException(
                        $"PDF artifact for '{source.DocumentId}' exceeds the snapshot artifact budget.");
                }
                var relativePath = "pdfs/" + Path.ChangeExtension(displayName, ".pdf");
                WriteArtifactAtomically(_tokens.GetArtifactPath(share.Id, relativePath), source.PdfBytes);
                share.SnapshotArtifacts.Add(new ShareSnapshotArtifact
                {
                    Id = Guid.NewGuid().ToString(),
                    ShareId = share.Id,
                    DocumentId = source.DocumentId,
                    ArtifactType = "pdf",
                    RelativePath = relativePath,
                    ContentHash = Convert.ToHexString(SHA256.HashData(source.PdfBytes)).ToLowerInvariant(),
                    SizeBytes = source.PdfBytes.Length,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                });
                artifacts.Add(relativePath);
            }
        }

        var manifestJson = JsonSerializer.Serialize(manifest);
        var manifestTemp = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(manifestTemp, manifestJson, Encoding.UTF8);
            File.Move(manifestTemp, manifestPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(manifestTemp))
            {
                try { File.Delete(manifestTemp); } catch { }
            }
        }

        return new SnapshotPublishResult(share.Id, shareRoot, manifest, artifacts);
    }

    /// <summary>
    /// Atomic write: temp file then move, so a crash can never leave a
    /// half-written frozen artifact behind.
    /// </summary>
    private static void WriteArtifactAtomically(string artifactPath, byte[] payload)
    {
        var artifactDir = Path.GetDirectoryName(artifactPath);
        if (!string.IsNullOrEmpty(artifactDir))
        {
            Directory.CreateDirectory(artifactDir);
        }

        var tempPath = artifactPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tempPath, payload);
            File.Move(tempPath, artifactPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
