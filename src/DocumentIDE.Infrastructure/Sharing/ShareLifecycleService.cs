using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.Infrastructure.Sharing;

/// <summary>
/// G5 — typed owner-side share lifecycle failure. The controller maps
/// <see cref="Code"/> to an HTTP status; the message is the code so existing
/// <c>InvalidOperationException</c>-style handling keeps working.
/// </summary>
public sealed class ShareLifecycleException(string code)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Owner request to create a share (SHR-11 create dialog payload).</summary>
public sealed record ShareCreateRequest(
    ShareMode Mode,
    IReadOnlyList<string> MemberDocumentIds,
    bool AllowOriginalDownload,
    bool AllowPdfDownload);

/// <summary>
/// A created/regenerated share plus the one-time raw token. Only the token
/// <em>hash</em> is persisted (<see cref="Share.TokenHash"/>); the raw value is
/// returned to the owner exactly once at create/regenerate time.
/// </summary>
public sealed record ShareLifecycleResult(Share Share, string RawToken);

/// <summary>
/// G5 — owner-side share lifecycle (privileged host only).
///
/// Persists the <see cref="Share"/> aggregate via <see cref="IShareRepository"/>
/// and freezes/publishes artifacts through the existing SHR-04/SHR-05
/// publishers, reusing the app-owned <c>%LOCALAPPDATA%\DocumentIDE\shares</c>
/// root resolved by <see cref="ShareTokenService"/>. Nothing here is exposed by
/// the public ShareHost: it only writes the owner-owned package that a later
/// transport (SHR-09) exposes.
///
/// Lifecycle state is modelled on the existing columns (no new schema): stop is
/// a past <c>ExpiresAt</c>, revoke clears <c>TokenHash</c>, regenerate rotates
/// the token and clears the expiry. V1 membership mutation is explicit and
/// Live-only; a frozen Snapshot rejects membership changes.
/// </summary>
public sealed class ShareLifecycleService(
    IShareRepository shares,
    IDocumentRepository documents,
    ShareTokenService tokens,
    ISnapshotMarkdownRenderer markdown)
{
    private static readonly HashSet<string> MarkdownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

    private static readonly HashSet<string> DocxExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".docx" };

    public async Task<ShareLifecycleResult> CreateAsync(ShareCreateRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var memberIds = (request.MemberDocumentIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (memberIds.Count == 0)
        {
            throw new ShareLifecycleException("NoMembers");
        }

        var sources = new List<ResolvedSource>(memberIds.Count);
        foreach (var id in memberIds)
        {
            var document = await documents.GetByIdAsync(id)
                ?? throw new ShareLifecycleException("UnknownDocument");
            sources.Add(await BuildSourceAsync(document, request.AllowOriginalDownload));
        }

        var rawToken = tokens.GenerateToken();
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = request.Mode,
            TokenHash = tokens.ComputeHash(rawToken),
            AllowOriginalDownload = request.AllowOriginalDownload,
            AllowPdfDownload = request.AllowPdfDownload,
        };

        try
        {
            if (request.Mode == ShareMode.Snapshot)
            {
                var publisher = new SnapshotSharePublisher(tokens, markdown);
                publisher.Publish(share, sources.Select(source => source.ToSnapshot()));
            }
            else
            {
                var publisher = new LiveSharePublisher(tokens, markdown);
                publisher.Publish(share, sources.Select(source => source.ToLive()));
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WORD-04 preview bytes", StringComparison.Ordinal))
        {
            // DOCX previews must come from the owner-side WORD-04 cache; the
            // privileged host does not render DOCX through Word for a share.
            throw new ShareLifecycleException("DocxPreviewUnavailable");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("PDF bytes were provided", StringComparison.Ordinal))
        {
            throw new ShareLifecycleException("PdfArtifactUnavailable");
        }

        await shares.CreateAsync(share);
        return new ShareLifecycleResult(share, rawToken);
    }

    public Task<Share?> GetAsync(string shareId) => shares.GetByIdAsync(shareId);

    public Task<IReadOnlyList<Share>> ListAsync() => shares.GetAllAsync();

    public async Task<Share?> StopAsync(string shareId)
    {
        var share = await shares.GetByIdAsync(shareId);
        if (share == null) return null;

        share.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O");
        await shares.UpdateAsync(share);
        return share;
    }

    public async Task<Share?> RevokeAsync(string shareId)
    {
        var share = await shares.GetByIdAsync(shareId);
        if (share == null) return null;

        // Spec §18: a revoked link must never verify again, so the stored token
        // hash is cleared and the share is also marked stopped.
        share.TokenHash = string.Empty;
        share.ExpiresAt ??= DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O");
        await shares.UpdateAsync(share);
        return share;
    }

    public async Task<ShareLifecycleResult?> RegenerateAsync(string shareId)
    {
        var share = await shares.GetByIdAsync(shareId);
        if (share == null) return null;

        var rawToken = tokens.GenerateToken();
        share.TokenHash = tokens.ComputeHash(rawToken);
        share.ExpiresAt = null;
        await shares.UpdateAsync(share);
        return new ShareLifecycleResult(share, rawToken);
    }

    public async Task<Share?> AddMemberAsync(string shareId, string documentId)
    {
        var share = await shares.GetByIdAsync(shareId);
        if (share == null) return null;
        RequireLiveForMembership(share);
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ShareLifecycleException("UnknownDocument");
        }
        if (share.Members.Any(m => string.Equals(m.DocumentId, documentId, StringComparison.Ordinal)))
        {
            return share;
        }

        var document = await documents.GetByIdAsync(documentId)
            ?? throw new ShareLifecycleException("UnknownDocument");
        var source = await BuildSourceAsync(document, includeOriginal: false);

        var publisher = new LiveSharePublisher(tokens, markdown);
        publisher.AddMember(share, source.ToLive());
        await shares.AddMemberAsync(share.Members[^1]);
        return await shares.GetByIdAsync(shareId);
    }

    public async Task<Share?> RemoveMemberAsync(string shareId, string documentId)
    {
        var share = await shares.GetByIdAsync(shareId);
        if (share == null) return null;
        RequireLiveForMembership(share);

        var publisher = new LiveSharePublisher(tokens, markdown);
        try
        {
            publisher.RemoveMember(share, documentId);
        }
        catch (InvalidOperationException)
        {
            throw new ShareLifecycleException("NotAMember");
        }

        await shares.RemoveMemberAsync(shareId, documentId);
        return await shares.GetByIdAsync(shareId);
    }

    private static void RequireLiveForMembership(Share share)
    {
        if (share.Mode != ShareMode.Live)
        {
            throw new ShareLifecycleException("SnapshotMembershipFrozen");
        }
    }

    private static async Task<ResolvedSource> BuildSourceAsync(Document document, bool includeOriginal)
    {
        if (string.IsNullOrWhiteSpace(document.CurrentPath) || !File.Exists(document.CurrentPath))
        {
            throw new ShareLifecycleException("DocumentUnavailable");
        }

        var extension = (document.Extension ?? Path.GetExtension(document.CurrentPath) ?? string.Empty).Trim();
        string? markdownSource = null;
        if (MarkdownExtensions.Contains(extension))
        {
            markdownSource = await File.ReadAllTextAsync(document.CurrentPath);
        }
        else if (!DocxExtensions.Contains(extension))
        {
            throw new ShareLifecycleException("UnsupportedMemberType");
        }

        byte[]? originalBytes = includeOriginal
            ? await File.ReadAllBytesAsync(document.CurrentPath)
            : null;

        var displayName = string.IsNullOrWhiteSpace(document.Filename)
            ? Path.GetFileName(document.CurrentPath)
            : document.Filename!;

        return new ResolvedSource(document.Id, displayName, extension, markdownSource, originalBytes, DocxPreviewBytes: null);
    }

    private sealed record ResolvedSource(
        string DocumentId,
        string DisplayName,
        string Extension,
        string? MarkdownSource,
        byte[]? OriginalBytes,
        byte[]? DocxPreviewBytes)
    {
        public SnapshotSourceDocument ToSnapshot() =>
            new(DocumentId, DisplayName, Extension, MarkdownSource, DocxPreviewBytes, OriginalBytes, PdfBytes: null);

        public LiveSourceDocument ToLive() =>
            new(DocumentId, DisplayName, Extension, MarkdownSource, DocxPreviewBytes);
    }
}
