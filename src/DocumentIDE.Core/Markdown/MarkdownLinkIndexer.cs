using System.Collections.Generic;
using System.Linq;

namespace DocumentIDE.Core.Markdown;

/// <summary>
/// Provides utilities to extract searchable representations of frontmatter and explicit link information
/// from a <see cref="MarkdownParseResult"/>. The indexer does **not** mutate the original markdown source.
/// </summary>
public static class MarkdownLinkIndexer
{
    /// <summary>
    /// Returns a dictionary of frontmatter key/value pairs that are suitable for search indexing.
    /// The values are converted to simple strings using <c>ToString()</c>.
    /// </summary>
    /// <param name="result">The parsed markdown result.</param>
    /// <returns>A read‑only dictionary of frontmatter entries.</returns>
    public static IReadOnlyDictionary<string, string> GetFrontmatterIndex(MarkdownParseResult result)
    {
        if (result.Frontmatter.StructuredData is null)
            return new Dictionary<string, string>();

        return result.Frontmatter.StructuredData
            .Where(kv => kv.Value != null)
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToString() ?? string.Empty);
    }

    /// <summary>
    /// Returns all explicit link candidates (normal links, wikilinks, and frontmatter dependencies)
    /// extracted from the parse result.
    /// </summary>
    public static IReadOnlyList<MarkdownLinkInfo> GetExplicitLinks(MarkdownParseResult result)
    {
        return result.Links ?? new List<MarkdownLinkInfo>();
    }
}
