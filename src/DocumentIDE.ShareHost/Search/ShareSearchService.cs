using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.ShareHost.Search;

/// <summary>
/// SHR-06 — a single share-scoped search hit. Carries only sanitized viewer
/// metadata (member document id, virtual display name, match source, text
/// snippet, virtual preview URL). Never carries a local filesystem path.
/// </summary>
public sealed record ShareSearchResult(
    string DocumentId,
    string DisplayName,
    string MatchSource,
    string Snippet,
    string PreviewUrl);

/// <summary>
/// SHR-06 — share-scoped search over explicit share membership only.
///
/// The searchable representation is built exclusively from prepared share
/// artifacts: the frozen manifest (member set + virtual display names) and
/// the per-member preview HTML artifacts (<c>previews/{docId}.html</c>).
/// The service never touches the owner workspace, index, or filesystem
/// outside the token-scoped share root, so nonmember content cannot match:
/// there is no "whole workspace then UI-filtered" stage. The ShareMember
/// constraint is enforced in this backend query: only manifest member
/// document IDs are iterated, and stray on-disk bytes for nonmember IDs
/// (if any) are never consulted.
/// </summary>
public sealed class ShareSearchService
{
    /// <summary>Cheap rejection budget for abusive query strings.</summary>
    public const int MaxQueryLength = 200;

    /// <summary>Prepared preview bytes read per member at most.</summary>
    public const long MaxPreviewBytesForSearch = 1_000_000;

    /// <summary>Snippet window around a body match.</summary>
    public const int MaxSnippetLength = 160;

    private static readonly Regex TagStripper = new("<[^>]*>", RegexOptions.Compiled);

    private readonly ShareTokenService _tokens;

    public ShareSearchService(ShareTokenService tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <summary>
    /// Searches only <paramref name="manifest"/> members for
    /// <paramref name="query"/>. Empty/oversized queries return no results
    /// instead of throwing. Results are deterministically ranked: exact
    /// filename match, then filename partial match, then body match,
    /// preserving manifest order within each tier.
    /// </summary>
    public IReadOnlyList<ShareSearchResult> Search(ShareManifest manifest, string? query)
    {
        if (manifest is null)
        {
            return Array.Empty<ShareSearchResult>();
        }

        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > MaxQueryLength)
        {
            return Array.Empty<ShareSearchResult>();
        }

        var ranked = new List<(int Tier, int Order, ShareSearchResult Hit)>();
        var members = manifest.Documents ?? new List<SanitizedDocumentMetadata>();
        for (var order = 0; order < members.Count; order++)
        {
            var doc = members[order];
            if (doc is null || string.IsNullOrWhiteSpace(doc.DocumentId))
            {
                continue;
            }

            var displayName = doc.DisplayName ?? string.Empty;
            if (displayName.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                ranked.Add((0, order, Hit(doc, manifest.ShareId, "filename-exact", displayName)));
            }
            else if (displayName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                ranked.Add((1, order, Hit(doc, manifest.ShareId, "filename-partial", displayName)));
            }
            else
            {
                var snippet = FindBodySnippet(manifest.ShareId, doc.DocumentId, normalized);
                if (snippet is not null)
                {
                    ranked.Add((2, order, Hit(doc, manifest.ShareId, "body", snippet)));
                }
            }
        }

        ranked.Sort((a, b) =>
            a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Order.CompareTo(b.Order));

        var results = new List<ShareSearchResult>(ranked.Count);
        foreach (var entry in ranked)
        {
            results.Add(entry.Hit);
        }

        return results;
    }

    private static ShareSearchResult Hit(
        SanitizedDocumentMetadata doc, string shareId, string matchSource, string snippet)
    {
        return new ShareSearchResult(
            doc.DocumentId,
            doc.DisplayName ?? string.Empty,
            matchSource,
            snippet,
            $"/share/{shareId}/documents/{doc.DocumentId}/preview");
    }

    /// <summary>
    /// Reads the member's prepared preview artifact and returns a snippet
    /// around the first case-insensitive match, or null when there is no
    /// match. Only the manifest member's own artifact path is resolved, so
    /// a nonmember ID can never contribute text here.
    /// </summary>
    private string? FindBodySnippet(string shareId, string documentId, string query)
    {
        string artifactPath;
        try
        {
            artifactPath = _tokens.GetArtifactPath(shareId, "previews/" + documentId + ".html");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        string html;
        try
        {
            if (!File.Exists(artifactPath))
            {
                return null;
            }

            if (new FileInfo(artifactPath).Length > MaxPreviewBytesForSearch)
            {
                return null;
            }

            html = File.ReadAllText(artifactPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var text = CollapseWhitespace(WebUtility.HtmlDecode(TagStripper.Replace(html, " ")));
        var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var start = Math.Max(0, at - MaxSnippetLength / 2);
        var length = Math.Min(MaxSnippetLength, text.Length - start);
        return text.Substring(start, length).Trim();
    }

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return WhitespaceCollapser.Replace(text, " ").Trim();
    }

    private static readonly Regex WhitespaceCollapser = new(@"\s+", RegexOptions.Compiled);
}
