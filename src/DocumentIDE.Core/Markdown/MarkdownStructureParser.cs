using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DocumentIDE.Core.Markdown;

public class MarkdownStructureParser : IMarkdownStructureParser
{
    public const string CurrentParserVersion = "1.0.0";

    private static readonly Regex StandardLinkRegex = new Regex(
        @"!?\[(?<text>[^\]]*)\]\((?<target>[^\)]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex WikilinkRegex = new Regex(
        @"\[\[(?<content>[^\]]+)\]\]",
        RegexOptions.Compiled);

    public MarkdownParseResult Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        string content = reader.ReadToEnd();
        return Parse(content);
    }

    public MarkdownParseResult Parse(string markdownContent)
    {
        string rawSource = markdownContent ?? string.Empty;
        if (string.IsNullOrEmpty(rawSource))
        {
            var emptyFrontmatter = new MarkdownFrontmatterResult(
                null,
                new Dictionary<string, object?>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                null);

            var emptyMetadata = new MarkdownMetadata(
                emptyFrontmatter,
                Array.Empty<string>(),
                Array.Empty<string>(),
                0,
                0,
                0);

            return new MarkdownParseResult(
                rawSource,
                string.Empty,
                emptyFrontmatter,
                Array.Empty<MarkdownHeadingInfo>(),
                Array.Empty<MarkdownOutlineNode>(),
                Array.Empty<MarkdownLinkInfo>(),
                emptyMetadata,
                0,
                CurrentParserVersion);
        }

        // Handle UTF-8 BOM if present at start of string
        string processingText = rawSource;
        if (processingText.Length > 0 && processingText[0] == '\uFEFF')
        {
            processingText = processingText.Substring(1);
        }

        // Split into lines while retaining offset indexing
        var (lines, lineOffsets) = SplitLinesWithOffsets(rawSource, processingText);

        // Step 1: Frontmatter Parsing
        var (frontmatterResult, bodyStartLineIndex, searchableBody) = ParseFrontmatter(lines, lineOffsets, rawSource, processingText);

        // Step 2: Body Scanning for Code Blocks, Headings, Links
        var headings = new List<MarkdownHeadingInfo>();
        var links = new List<MarkdownLinkInfo>();
        int codeBlockCount = 0;

        // Add explicit frontmatter dependencies to links
        foreach (var dep in frontmatterResult.ExplicitDependencies)
        {
            links.Add(new MarkdownLinkInfo(
                MarkdownLinkType.FrontmatterDependency,
                dep,
                dep,
                null,
                null,
                1,
                0,
                0));
        }

        bool inCodeBlock = false;
        char codeFenceChar = '\0';
        int codeFenceLength = 0;

        for (int i = bodyStartLineIndex; i < lines.Count; i++)
        {
            string line = lines[i];
            int lineNumber = i + 1;
            int lineStartOffset = lineOffsets[i];
            string trimmedLine = line.TrimStart();

            // Code Fence Check
            if (IsCodeFence(trimmedLine, out char fenceChar, out int fenceLen))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeFenceChar = fenceChar;
                    codeFenceLength = fenceLen;
                    codeBlockCount++;
                    continue;
                }
                else if (fenceChar == codeFenceChar && fenceLen >= codeFenceLength)
                {
                    inCodeBlock = false;
                    codeFenceChar = '\0';
                    codeFenceLength = 0;
                    continue;
                }
            }

            if (inCodeBlock)
            {
                // Skip heading and link processing inside code fences
                continue;
            }

            // ATX Heading Check (# H1 .. ###### H6)
            if (TryParseAtxHeading(line, lineNumber, lineStartOffset, out var atxHeading))
            {
                headings.Add(atxHeading!);
                continue;
            }

            // Setext Heading Check (Underline === or ---)
            if (i > bodyStartLineIndex && TryParseSetextHeading(lines, i, lineOffsets, out var setextHeading))
            {
                headings.Add(setextHeading!);
                continue;
            }

            // Extract Normal Links and Wikilinks on active line
            ExtractLinksFromLine(line, lineNumber, lineStartOffset, links);
        }

        // Build Outline Tree
        var outlineTree = BuildOutlineTree(headings);

        // Construct Metadata
        var metadata = new MarkdownMetadata(
            frontmatterResult,
            frontmatterResult.Tags,
            frontmatterResult.ExplicitDependencies,
            codeBlockCount,
            headings.Count,
            links.Count);

        return new MarkdownParseResult(
            rawSource,
            searchableBody,
            frontmatterResult,
            headings,
            outlineTree,
            links,
            metadata,
            codeBlockCount,
            CurrentParserVersion);
    }

    private static (List<string> Lines, List<int> LineOffsets) SplitLinesWithOffsets(string rawSource, string processingText)
    {
        var lines = new List<string>();
        var lineOffsets = new List<int>();

        int start = 0;
        int bomOffset = rawSource.Length - processingText.Length;

        for (int i = 0; i < processingText.Length; i++)
        {
            if (processingText[i] == '\r')
            {
                int len = i - start;
                lines.Add(processingText.Substring(start, len));
                lineOffsets.Add(start + bomOffset);

                if (i + 1 < processingText.Length && processingText[i + 1] == '\n')
                {
                    i++;
                }
                start = i + 1;
            }
            else if (processingText[i] == '\n')
            {
                int len = i - start;
                lines.Add(processingText.Substring(start, len));
                lineOffsets.Add(start + bomOffset);
                start = i + 1;
            }
        }

        if (start <= processingText.Length)
        {
            lines.Add(processingText.Substring(start));
            lineOffsets.Add(start + bomOffset);
        }

        return (lines, lineOffsets);
    }

    private (MarkdownFrontmatterResult Result, int BodyStartLineIndex, string SearchableBody) ParseFrontmatter(
        List<string> lines,
        List<int> lineOffsets,
        string rawSource,
        string processingText)
    {
        if (lines.Count == 0 || !lines[0].TrimEnd().Equals("---"))
        {
            var noFrontmatter = new MarkdownFrontmatterResult(
                null,
                new Dictionary<string, object?>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                null);
            return (noFrontmatter, 0, processingText);
        }

        // Locate closing frontmatter delimiter `---` or `...`
        int closingLineIndex = -1;
        for (int i = 1; i < lines.Count; i++)
        {
            string lineTrim = lines[i].TrimEnd();
            if (lineTrim.Equals("---") || lineTrim.Equals("..."))
            {
                closingLineIndex = i;
                break;
            }
        }

        if (closingLineIndex == -1)
        {
            var malformedResult = new MarkdownFrontmatterResult(
                processingText,
                new Dictionary<string, object?>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                true,
                "Unclosed YAML frontmatter delimiter.");

            // Body starts at 0 since delimiter was not properly closed
            return (malformedResult, 0, processingText);
        }

        // Extract raw frontmatter text
        var yamlLines = lines.GetRange(1, closingLineIndex - 1);
        string rawYaml = string.Join("\n", yamlLines);

        var structuredData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<string>();
        var dependencies = new List<string>();
        bool hasError = false;
        string? errorMessage = null;

        try
        {
            ParseSimpleYaml(yamlLines, structuredData, tags, dependencies);
        }
        catch (Exception ex)
        {
            hasError = true;
            errorMessage = $"YAML Frontmatter Parse Error: {ex.Message}";
        }

        var frontmatterResult = new MarkdownFrontmatterResult(
            rawYaml,
            structuredData,
            tags.Distinct().ToList(),
            dependencies.Distinct().ToList(),
            hasError,
            errorMessage);

        int bodyStartLineIndex = closingLineIndex + 1;
        int bodyStartOffset = bodyStartLineIndex < lines.Count ? lineOffsets[bodyStartLineIndex] : rawSource.Length;
        string searchableBody = rawSource.Substring(bodyStartOffset);

        return (frontmatterResult, bodyStartLineIndex, searchableBody);
    }

    private static void ParseSimpleYaml(
        List<string> yamlLines,
        Dictionary<string, object?> structuredData,
        List<string> tags,
        List<string> dependencies)
    {
        string? currentListKey = null;

        foreach (var rawLine in yamlLines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            if (line.StartsWith("- ") && currentListKey != null)
            {
                string itemVal = StripQuotes(line.Substring(2).Trim());
                if (!string.IsNullOrEmpty(itemVal))
                {
                    if (structuredData.TryGetValue(currentListKey, out var existingObj) && existingObj is List<string> list)
                    {
                        list.Add(itemVal);
                    }
                    else
                    {
                        var newList = new List<string> { itemVal };
                        structuredData[currentListKey] = newList;
                    }

                    if (currentListKey.Equals("tags", StringComparison.OrdinalIgnoreCase) || currentListKey.Equals("tag", StringComparison.OrdinalIgnoreCase))
                    {
                        tags.Add(itemVal);
                    }
                    else if (currentListKey.Equals("depends_on", StringComparison.OrdinalIgnoreCase) || currentListKey.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
                    {
                        dependencies.Add(itemVal);
                    }
                }
                continue;
            }

            int colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
            {
                string key = line.Substring(0, colonIdx).Trim();
                string val = line.Substring(colonIdx + 1).Trim();

                if (string.IsNullOrEmpty(val))
                {
                    currentListKey = key;
                    structuredData[key] = new List<string>();
                    continue;
                }

                currentListKey = null;

                // Handle inline list e.g. [item1, item2]
                if (val.StartsWith("[") && val.EndsWith("]"))
                {
                    string inner = val.Substring(1, val.Length - 2);
                    var items = inner.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => StripQuotes(s.Trim()))
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();

                    structuredData[key] = items;

                    if (key.Equals("tags", StringComparison.OrdinalIgnoreCase) || key.Equals("tag", StringComparison.OrdinalIgnoreCase))
                    {
                        tags.AddRange(items);
                    }
                    else if (key.Equals("depends_on", StringComparison.OrdinalIgnoreCase) || key.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
                    {
                        dependencies.AddRange(items);
                    }
                }
                else
                {
                    string cleanVal = StripQuotes(val);
                    structuredData[key] = cleanVal;
                }
            }
        }
    }

    private static string StripQuotes(string val)
    {
        if (string.IsNullOrEmpty(val)) return val;
        if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
        {
            return val.Substring(1, val.Length - 2);
        }
        return val;
    }

    private static bool IsCodeFence(string trimmedLine, out char fenceChar, out int fenceLen)
    {
        fenceChar = '\0';
        fenceLen = 0;

        if (trimmedLine.Length < 3) return false;

        char firstChar = trimmedLine[0];
        if (firstChar != '`' && firstChar != '~') return false;

        int len = 0;
        while (len < trimmedLine.Length && trimmedLine[len] == firstChar)
        {
            len++;
        }

        if (len >= 3)
        {
            fenceChar = firstChar;
            fenceLen = len;
            return true;
        }

        return false;
    }

    private static bool TryParseAtxHeading(string line, int lineNumber, int lineStartOffset, out MarkdownHeadingInfo? heading)
    {
        heading = null;
        string trimmed = line.TrimStart();

        if (!trimmed.StartsWith("#")) return false;

        int level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level < 1 || level > 6) return false;

        // CommonMark requirement: ATX heading requires space or tab after # (or end of line)
        if (level < trimmed.Length && trimmed[level] != ' ' && trimmed[level] != '\t')
        {
            return false;
        }

        string rawTitle = level < trimmed.Length ? trimmed.Substring(level).Trim() : string.Empty;

        // Trim trailing #s if present (e.g. # Heading #)
        rawTitle = TrimTrailingHashes(rawTitle);

        string slug = GenerateSlug(rawTitle);
        int startOffset = lineStartOffset + (line.Length - trimmed.Length);
        int endOffset = lineStartOffset + line.Length;

        heading = new MarkdownHeadingInfo(level, rawTitle, slug, lineNumber, startOffset, endOffset);
        return true;
    }

    private static bool TryParseSetextHeading(List<string> lines, int currentLineIndex, List<int> lineOffsets, out MarkdownHeadingInfo? heading)
    {
        heading = null;
        string currentLine = lines[currentLineIndex].Trim();

        if (currentLine.Length < 3) return false;

        bool isLevel1 = currentLine.All(c => c == '=');
        bool isLevel2 = currentLine.All(c => c == '-');

        if (!isLevel1 && !isLevel2) return false;

        string prevLine = lines[currentLineIndex - 1].Trim();
        if (string.IsNullOrEmpty(prevLine) || prevLine.StartsWith("#") || prevLine.Equals("---") || prevLine.Equals("..."))
        {
            return false;
        }

        int level = isLevel1 ? 1 : 2;
        int prevLineNumber = currentLineIndex; // 1-indexed (currentLineIndex - 1 + 1)
        int prevLineStartOffset = lineOffsets[currentLineIndex - 1];
        int endOffset = lineOffsets[currentLineIndex] + lines[currentLineIndex].Length;
        string slug = GenerateSlug(prevLine);

        heading = new MarkdownHeadingInfo(level, prevLine, slug, prevLineNumber, prevLineStartOffset, endOffset);
        return true;
    }

    private static string TrimTrailingHashes(string title)
    {
        if (string.IsNullOrEmpty(title)) return title;
        int idx = title.Length - 1;
        while (idx >= 0 && title[idx] == '#')
        {
            idx--;
        }
        if (idx < title.Length - 1)
        {
            return title.Substring(0, idx + 1).TrimEnd();
        }
        return title;
    }

    public static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var sb = new StringBuilder();
        bool lastWasDash = false;

        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasDash = false;
            }
            else if (c == ' ' || c == '_' || c == '-')
            {
                if (!lastWasDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }
        }

        string slug = sb.ToString().TrimEnd('-');
        return slug;
    }

    private static void ExtractLinksFromLine(string line, int lineNumber, int lineStartOffset, List<MarkdownLinkInfo> links)
    {
        // Standard Links
        var standardMatches = StandardLinkRegex.Matches(line);
        foreach (Match match in standardMatches)
        {
            string alias = match.Groups["text"].Value;
            string rawTarget = match.Groups["target"].Value;

            string target = rawTarget;
            string? anchor = null;

            int hashIdx = rawTarget.IndexOf('#');
            if (hashIdx >= 0)
            {
                target = rawTarget.Substring(0, hashIdx);
                anchor = rawTarget.Substring(hashIdx + 1);
            }

            int startOffset = lineStartOffset + match.Index;
            int endOffset = startOffset + match.Length;

            links.Add(new MarkdownLinkInfo(
                MarkdownLinkType.NormalLink,
                rawTarget,
                target,
                anchor,
                alias,
                lineNumber,
                startOffset,
                endOffset));
        }

        // Wikilinks
        var wikiMatches = WikilinkRegex.Matches(line);
        foreach (Match match in wikiMatches)
        {
            string content = match.Groups["content"].Value;
            string targetWithAnchor = content;
            string? alias = null;

            int pipeIdx = content.IndexOf('|');
            if (pipeIdx >= 0)
            {
                targetWithAnchor = content.Substring(0, pipeIdx).Trim();
                alias = content.Substring(pipeIdx + 1).Trim();
            }

            string target = targetWithAnchor;
            string? anchor = null;

            int hashIdx = targetWithAnchor.IndexOf('#');
            if (hashIdx >= 0)
            {
                target = targetWithAnchor.Substring(0, hashIdx).Trim();
                anchor = targetWithAnchor.Substring(hashIdx + 1).Trim();
            }

            int startOffset = lineStartOffset + match.Index;
            int endOffset = startOffset + match.Length;

            links.Add(new MarkdownLinkInfo(
                MarkdownLinkType.Wikilink,
                content,
                target,
                anchor,
                alias,
                lineNumber,
                startOffset,
                endOffset));
        }
    }

    public static List<MarkdownOutlineNode> BuildOutlineTree(IReadOnlyList<MarkdownHeadingInfo> headings)
    {
        var roots = new List<MarkdownOutlineNode>();
        var stack = new Stack<(MarkdownOutlineNode Node, int Level)>();

        foreach (var heading in headings)
        {
            var node = new MarkdownOutlineNode(heading);

            while (stack.Count > 0 && stack.Peek().Level >= heading.Level)
            {
                stack.Pop();
            }

            if (stack.Count > 0)
            {
                stack.Peek().Node.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }

            stack.Push((node, heading.Level));
        }

        return roots;
    }
}
