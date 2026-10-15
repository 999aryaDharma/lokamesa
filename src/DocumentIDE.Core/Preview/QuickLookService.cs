using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Preview;

/// <summary>
/// Typed Quick Look failure codes. Failures are always inspectable; the
/// service never falls back silently and never throws for Word/COM states.
/// </summary>
public enum QuickLookErrorCode
{
    None = 0,
    InvalidRequest,
    FileMissing,
    WordUnavailable,
    WordBusy,
    WordModal,
    ComFailure,
    CacheMiss
}

/// <summary>Classified Word render failure behind the render adapter.</summary>
public enum WordRenderFailureKind
{
    Unavailable,
    Busy,
    Modal,
    ComFailure
}

/// <summary>
/// Typed Word render failure. Throwing this from an <see cref="IQuickLookRenderer"/>
/// lets <see cref="QuickLookService"/> map Word unavailable/busy/modal/COM states
/// to inspectable <see cref="QuickLookErrorCode"/> outcomes.
/// </summary>
public sealed class WordRenderException(WordRenderFailureKind kind, string message)
    : Exception(message)
{
    public WordRenderFailureKind Kind { get; } = kind;
}

/// <summary>
/// Preview cache key: DocumentId + content fingerprint + renderer version.
/// A source change (new fingerprint) addresses a different entry, so stale
/// pages can never be served as current.
/// </summary>
public sealed record QuickLookCacheKey(
    string DocumentId,
    string ContentFingerprint,
    string RendererVersion)
{
    public string ToKeyString() =>
        $"{DocumentId}|{ContentFingerprint}|{RendererVersion}";
}

/// <summary>Read-only Quick Look page request. PageIndex is zero-based.</summary>
public sealed record QuickLookRequest(
    string DocumentId,
    string SourcePath,
    string ContentFingerprint,
    int PageIndex = 0,
    IReadOnlyList<string>? SearchTerms = null);

/// <summary>One case-insensitive search hit inside a preview page.</summary>
public sealed record QuickLookMatch(int Start, int Length, string Term);

/// <summary>
/// A rendered preview page. <see cref="ArtifactId"/> is an opaque temp-artifact
/// identifier — never a source path — so cache entries cannot leak local paths.
/// </summary>
public sealed record QuickLookPage(
    int PageIndex,
    string ArtifactId,
    string? PageText,
    IReadOnlyList<QuickLookMatch> Matches);

/// <summary>Inspectable Quick Look outcome (ready page or typed failure).</summary>
public sealed record QuickLookResult(
    bool Success,
    QuickLookErrorCode ErrorCode,
    string? Message,
    QuickLookCacheKey CacheKey,
    IReadOnlyList<QuickLookPage> Pages,
    int TotalPages,
    bool FromCache,
    bool EditingInWord = false,
    string? EditingSummary = null);

/// <summary>
/// Lazy Word-to-temp-artifact page render. Production implementations route
/// through the WORD-03 dedicated STA queue; this interface keeps Core free of
/// any Word/COM dependency so the public viewer boundary stays provable.
/// </summary>
public interface IQuickLookRenderer
{
    Task<QuickLookRenderOutput> RenderPageAsync(
        QuickLookRenderCommand command, CancellationToken cancellationToken = default);
}

/// <summary>Lazy per-page render command. Only the requested page is rendered.</summary>
public sealed record QuickLookRenderCommand(
    string SourcePath,
    string TempDirectory,
    int PageIndex);

/// <summary>Renderer output for one page. PageText may be null when unavailable.</summary>
public sealed record QuickLookRenderOutput(
    string ArtifactId,
    int TotalPages,
    string? PageText);

/// <summary>WORD-02 editing-state surface for Quick Look preflight.</summary>
public sealed record QuickLookEditingInfo(bool IsEditing, string Summary);

/// <summary>Editing-state probe. Adapters wrap the WORD-02 detector.</summary>
public interface IQuickLookEditingProbe
{
    QuickLookEditingInfo Check(string sourcePath);
}

/// <summary>One cached preview page entry.</summary>
public sealed record QuickLookCacheEntry(
    QuickLookCacheKey Key,
    QuickLookPage Page,
    int TotalPages,
    DateTimeOffset CachedAt);

/// <summary>Preview page cache keyed by <see cref="QuickLookCacheKey"/> + page.</summary>
public interface IQuickLookCache
{
    Task<QuickLookCacheEntry?> GetAsync(
        QuickLookCacheKey key, int pageIndex, CancellationToken cancellationToken = default);
    Task SaveAsync(QuickLookCacheEntry entry, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>Thread-safe in-memory preview page cache.</summary>
public sealed class InMemoryQuickLookCache : IQuickLookCache
{
    private readonly Dictionary<string, QuickLookCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    private static string EntryKey(QuickLookCacheKey key, int pageIndex) =>
        $"{key.ToKeyString()}|page-{pageIndex}";

    public Task<QuickLookCacheEntry?> GetAsync(
        QuickLookCacheKey key, int pageIndex, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _entries.TryGetValue(EntryKey(key, pageIndex), out var entry);
            return Task.FromResult(entry);
        }
    }

    public Task SaveAsync(QuickLookCacheEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            _entries[EntryKey(entry.Key, entry.Page.PageIndex)] = entry;
        }
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string documentId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var doomed = new List<string>();
            foreach (var kvp in _entries)
            {
                if (string.Equals(kvp.Value.Key.DocumentId, documentId, StringComparison.Ordinal))
                {
                    doomed.Add(kvp.Key);
                }
            }
            foreach (string k in doomed)
            {
                _entries.Remove(k);
            }
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// WORD-04 — High-Fidelity Quick Look.
///
/// Read-only multi-page preview over lazily Word-rendered temp artifacts:
/// nothing is rendered at construction or startup — the renderer runs only on
/// a cache miss for the exact page requested. The source file is only ever
/// read; preview never edits it. Editing-in-Word state (WORD-02) is surfaced
/// on the result without blocking the read-only preview.
/// </summary>
public sealed class QuickLookService
{
    /// <summary>Renderer version component of the preview cache key.</summary>
    public const string RendererVersion = "word-pdf-v1";

    private readonly IQuickLookRenderer _renderer;
    private readonly IQuickLookCache _cache;
    private readonly IQuickLookEditingProbe? _editingProbe;

    public QuickLookService(
        IQuickLookRenderer renderer,
        IQuickLookCache cache,
        IQuickLookEditingProbe? editingProbe = null)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _editingProbe = editingProbe;
    }

    /// <summary>
    /// Returns the requested preview page. Lazy: the renderer is invoked only
    /// when the page is absent from the cache. Never mutates the source file.
    /// </summary>
    public async Task<QuickLookResult> GetPageAsync(
        QuickLookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DocumentId)
            || string.IsNullOrWhiteSpace(request.SourcePath)
            || string.IsNullOrWhiteSpace(request.ContentFingerprint)
            || request.PageIndex < 0)
        {
            return Failed(
                QuickLookErrorCode.InvalidRequest,
                "A document id, source path, content fingerprint, and non-negative page index are required.",
                FallbackKey(request));
        }

        if (!File.Exists(request.SourcePath))
        {
            return Failed(
                QuickLookErrorCode.FileMissing,
                $"Document file at '{request.SourcePath}' does not exist on disk.",
                CacheKeyFor(request));
        }

        QuickLookCacheKey key = CacheKeyFor(request);
        QuickLookEditingInfo editing = CheckEditing(request.SourcePath);

        QuickLookCacheEntry? cached = await _cache.GetAsync(key, request.PageIndex, cancellationToken);
        if (cached != null)
        {
            return new QuickLookResult(
                true, QuickLookErrorCode.None, null, key,
                new[] { WithMatches(cached.Page, request.SearchTerms) },
                cached.TotalPages, true, editing.IsEditing, editing.Summary);
        }

        QuickLookRenderOutput output;
        try
        {
            output = await _renderer.RenderPageAsync(
                new QuickLookRenderCommand(request.SourcePath, Path.GetTempPath(), request.PageIndex),
                cancellationToken);
        }
        catch (WordRenderException ex)
        {
            return Failed(MapFailure(ex.Kind), ex.Message, key, editing);
        }
        catch (Exception ex)
        {
            return Failed(QuickLookErrorCode.ComFailure,
                $"Quick Look render failed: {ex.Message}", key, editing);
        }

        var page = new QuickLookPage(
            request.PageIndex,
            MakeOpaqueArtifactId(request, output.ArtifactId),
            output.PageText,
            FindMatches(output.PageText, request.SearchTerms));

        await _cache.SaveAsync(
            new QuickLookCacheEntry(key, page, output.TotalPages, DateTimeOffset.UtcNow),
            cancellationToken);

        return new QuickLookResult(
            true, QuickLookErrorCode.None, null, key,
            new[] { page }, output.TotalPages, false, editing.IsEditing, editing.Summary);
    }

    /// <summary>Drops every cached page for a document (e.g. after source change).</summary>
    public Task InvalidateAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        return _cache.InvalidateAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// Case-insensitive search-term hits inside a preview page. A null/empty
    /// page text (renderer without text layer) yields no matches, never an
    /// error — highlighting is best-effort where possible.
    /// </summary>
    public static IReadOnlyList<QuickLookMatch> FindMatches(
        string? pageText, IReadOnlyList<string>? terms)
    {
        if (string.IsNullOrEmpty(pageText) || terms == null || terms.Count == 0)
        {
            return Array.Empty<QuickLookMatch>();
        }

        var matches = new List<QuickLookMatch>();
        foreach (string rawTerm in terms)
        {
            if (string.IsNullOrWhiteSpace(rawTerm))
            {
                continue;
            }
            string term = rawTerm.Trim();
            int from = 0;
            while (true)
            {
                int index = pageText.IndexOf(term, from, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    break;
                }
                matches.Add(new QuickLookMatch(index, term.Length, term));
                from = index + term.Length;
            }
        }

        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        return matches;
    }

    private static QuickLookCacheKey CacheKeyFor(QuickLookRequest request) =>
        new(request.DocumentId, request.ContentFingerprint, RendererVersion);

    private static QuickLookCacheKey FallbackKey(QuickLookRequest request) =>
        new(request.DocumentId ?? string.Empty,
            request.ContentFingerprint ?? string.Empty,
            RendererVersion);

    private QuickLookEditingInfo CheckEditing(string sourcePath)
    {
        if (_editingProbe == null)
        {
            return new QuickLookEditingInfo(false, "No Word editing probe configured.");
        }
        try
        {
            return _editingProbe.Check(sourcePath);
        }
        catch (Exception ex)
        {
            return new QuickLookEditingInfo(false, $"Editing probe failed: {ex.Message}");
        }
    }

    private static QuickLookResult Failed(
        QuickLookErrorCode code, string message, QuickLookCacheKey key,
        QuickLookEditingInfo? editing = null) =>
        new(false, code, message, key, Array.Empty<QuickLookPage>(), 0, false,
            editing?.IsEditing ?? false, editing?.Summary);

    private static QuickLookPage WithMatches(QuickLookPage page, IReadOnlyList<string>? terms)
    {
        if (terms == null || terms.Count == 0)
        {
            return page;
        }
        return page with { Matches = FindMatches(page.PageText, terms) };
    }

    private static QuickLookErrorCode MapFailure(WordRenderFailureKind kind) =>
        kind switch
        {
            WordRenderFailureKind.Unavailable => QuickLookErrorCode.WordUnavailable,
            WordRenderFailureKind.Busy => QuickLookErrorCode.WordBusy,
            WordRenderFailureKind.Modal => QuickLookErrorCode.WordModal,
            _ => QuickLookErrorCode.ComFailure,
        };

    /// <summary>
    /// Keeps artifact identifiers opaque: the renderer-supplied id is namespaced
    /// per preview key so cached entries cannot be mistaken for — or resolve to —
    /// source paths.
    /// </summary>
    private static string MakeOpaqueArtifactId(QuickLookRequest request, string rendererArtifactId) =>
        $"ql:{request.DocumentId}:{request.ContentFingerprint}:{RendererVersion}:p{request.PageIndex}:{rendererArtifactId}";
}

/// <summary>
/// Public-viewer Quick Look reader. Cache-only by construction: it holds no
/// renderer and no Word reference, so shared/public viewing can never invoke
/// the privileged Word automation queue directly.
/// </summary>
public sealed class PublicQuickLookReader
{
    private readonly IQuickLookCache _cache;

    public PublicQuickLookReader(IQuickLookCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<QuickLookResult> GetCachedPageAsync(
        string documentId,
        string contentFingerprint,
        int pageIndex,
        IReadOnlyList<string>? searchTerms,
        CancellationToken cancellationToken = default)
    {
        var key = new QuickLookCacheKey(
            documentId ?? string.Empty,
            contentFingerprint ?? string.Empty,
            QuickLookService.RendererVersion);

        if (string.IsNullOrWhiteSpace(documentId)
            || string.IsNullOrWhiteSpace(contentFingerprint)
            || pageIndex < 0)
        {
            return new QuickLookResult(
                false, QuickLookErrorCode.InvalidRequest,
                "A document id, content fingerprint, and non-negative page index are required.",
                key, Array.Empty<QuickLookPage>(), 0, false);
        }

        QuickLookCacheEntry? cached = await _cache.GetAsync(key, pageIndex, cancellationToken);
        if (cached == null)
        {
            return new QuickLookResult(
                false, QuickLookErrorCode.CacheMiss,
                "No cached Quick Look page for this document and fingerprint.",
                key, Array.Empty<QuickLookPage>(), 0, false);
        }

        QuickLookPage page = searchTerms == null || searchTerms.Count == 0
            ? cached.Page
            : cached.Page with { Matches = QuickLookService.FindMatches(cached.Page.PageText, searchTerms) };

        return new QuickLookResult(
            true, QuickLookErrorCode.None, null, key,
            new[] { page }, cached.TotalPages, true);
    }
}
