using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocumentIDE.Core.Markdown;

public record ExplicitRelationCandidate(
    string SourceFilePath,
    string NormalizedTargetPath,
    MarkdownLinkType RelationType,
    string RawTarget,
    string? Anchor,
    string? Alias,
    int LineNumber
);

public record LinkResolutionResult(
    bool IsResolved,
    bool IsAmbiguous,
    string? ResolvedPath,
    IReadOnlyList<string> CandidatePaths
);

/// <summary>
/// Provides utilities to extract searchable representations of frontmatter and explicit link information
/// from a <see cref="MarkdownParseResult"/>. The indexer does **not** mutate the original markdown source.
/// </summary>
public static class MarkdownLinkIndexer
{
    /// <summary>
    /// Returns a dictionary of frontmatter key/value pairs that are suitable for search indexing.
    /// Collections are flattened into comma-separated strings.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetFrontmatterIndex(MarkdownParseResult result)
    {
        if (result.Frontmatter.StructuredData is null)
            return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in result.Frontmatter.StructuredData)
        {
            if (kv.Value is null) continue;

            if (kv.Value is string s)
            {
                dict[kv.Key] = s;
            }
            else if (kv.Value is IEnumerable enumerable && !(kv.Value is string))
            {
                var items = enumerable.Cast<object?>()
                    .Where(x => x != null)
                    .Select(x => x!.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x));
                dict[kv.Key] = string.Join(", ", items);
            }
            else
            {
                dict[kv.Key] = kv.Value.ToString() ?? string.Empty;
            }
        }

        return dict;
    }

    /// <summary>
    /// Returns all explicit link candidates (normal links, wikilinks, and frontmatter dependencies)
    /// extracted from the parse result.
    /// </summary>
    public static IReadOnlyList<MarkdownLinkInfo> GetExplicitLinks(MarkdownParseResult result)
    {
        return result.Links ?? new List<MarkdownLinkInfo>();
    }

    /// <summary>
    /// Normalizes a relative link target against a source physical file path.
    /// </summary>
    public static string NormalizeRelativeLink(string sourceFilePath, string relativeTarget)
    {
        if (string.IsNullOrWhiteSpace(relativeTarget))
            return relativeTarget;

        string targetWithoutAnchor = relativeTarget;
        int hashIdx = relativeTarget.IndexOf('#');
        if (hashIdx >= 0)
        {
            targetWithoutAnchor = relativeTarget.Substring(0, hashIdx);
        }

        if (Path.IsPathRooted(targetWithoutAnchor))
        {
            return Path.GetFullPath(targetWithoutAnchor);
        }

        string? dir = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrEmpty(dir))
        {
            return Path.GetFullPath(targetWithoutAnchor);
        }

        string combined = Path.Combine(dir, targetWithoutAnchor);
        return Path.GetFullPath(combined);
    }

    /// <summary>
    /// Extracts explicit relation candidates for a document at <paramref name="sourceFilePath"/>.
    /// </summary>
    public static IReadOnlyList<ExplicitRelationCandidate> GetExplicitRelationCandidates(string sourceFilePath, MarkdownParseResult parseResult)
    {
        var links = GetExplicitLinks(parseResult);
        var candidates = new List<ExplicitRelationCandidate>();

        foreach (var link in links)
        {
            string normalizedTarget = link.Target;
            if (link.Type == MarkdownLinkType.NormalLink && !string.IsNullOrWhiteSpace(link.Target))
            {
                normalizedTarget = NormalizeRelativeLink(sourceFilePath, link.Target);
            }

            candidates.Add(new ExplicitRelationCandidate(
                sourceFilePath,
                normalizedTarget,
                link.Type,
                link.RawTarget,
                link.Anchor,
                link.Alias,
                link.LineNumber));
        }

        return candidates;
    }

    /// <summary>
    /// Resolves a link target against a set of known workspace file paths.
    /// </summary>
    public static LinkResolutionResult ResolveLinkTarget(
        string sourceFilePath,
        string rawTarget,
        MarkdownLinkType linkType,
        IEnumerable<string> workspaceFiles)
    {
        var filesList = workspaceFiles?.ToList() ?? new List<string>();

        if (linkType == MarkdownLinkType.NormalLink || linkType == MarkdownLinkType.FrontmatterDependency)
        {
            string normalized = NormalizeRelativeLink(sourceFilePath, rawTarget);
            var match = filesList.FirstOrDefault(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return new LinkResolutionResult(true, false, match, new[] { match });
            }

            return new LinkResolutionResult(false, false, normalized, Array.Empty<string>());
        }
        else if (linkType == MarkdownLinkType.Wikilink)
        {
            var wikiRes = WikilinkResolver.ResolveWikilink(sourceFilePath, rawTarget, filesList);
            return new LinkResolutionResult(wikiRes.IsResolved, wikiRes.IsAmbiguous, wikiRes.ResolvedPath, wikiRes.CandidatePaths);
        }

        return new LinkResolutionResult(false, false, null, Array.Empty<string>());
    }
}

