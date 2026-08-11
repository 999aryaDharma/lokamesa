using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.Markdown;

public enum MarkdownLinkType
{
    NormalLink,
    Wikilink,
    FrontmatterDependency
}

public record MarkdownFrontmatterResult(
    string? RawText,
    IReadOnlyDictionary<string, object?> StructuredData,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ExplicitDependencies,
    bool HasError,
    string? ErrorMessage
);

public record MarkdownHeadingInfo(
    int Level,
    string Text,
    string Slug,
    int LineNumber,
    int StartOffset,
    int EndOffset
);

public record MarkdownLinkInfo(
    MarkdownLinkType Type,
    string RawTarget,
    string Target,
    string? Anchor,
    string? Alias,
    int LineNumber,
    int StartOffset,
    int EndOffset
);

public class MarkdownOutlineNode
{
    public MarkdownHeadingInfo Heading { get; init; } = null!;
    public List<MarkdownOutlineNode> Children { get; init; } = new();

    public MarkdownOutlineNode() { }

    public MarkdownOutlineNode(MarkdownHeadingInfo heading, List<MarkdownOutlineNode>? children = null)
    {
        Heading = heading;
        Children = children ?? new List<MarkdownOutlineNode>();
    }
}

public record MarkdownMetadata(
    MarkdownFrontmatterResult Frontmatter,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ExplicitDependencies,
    int CodeBlockCount,
    int HeadingCount,
    int LinkCount
);

public record MarkdownParseResult(
    string RawSource,
    string SearchableBody,
    MarkdownFrontmatterResult Frontmatter,
    IReadOnlyList<MarkdownHeadingInfo> Headings,
    IReadOnlyList<MarkdownOutlineNode> OutlineTree,
    IReadOnlyList<MarkdownLinkInfo> Links,
    MarkdownMetadata Metadata,
    int CodeBlockCount,
    string ParserVersion = "1.0.0"
);
