using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DocumentIDE.Core.Markdown;
using Xunit;

namespace DocumentIDE.Core.Tests.Markdown;

public class MarkdownStructureParserStressTests
{
    private readonly MarkdownStructureParser _parser = new();

    #region 1. Malformed YAML Frontmatter Stress Tests

    [Fact]
    public void Parse_MalformedFrontmatter_UnclosedQuotes_DoesNotCrash()
    {
        var content = @"---
title: ""unclosed string delimiter
tags:
  - test
---
# Heading
Body text";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Heading", result.Headings[0].Text);
    }

    [Fact]
    public void Parse_MalformedFrontmatter_InvalidSyntax_PopulatesErrorAndParsesBody()
    {
        var content = @"---
[invalid yaml syntax :::
title: test
---
# Valid Body Heading
Some content here.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Valid Body Heading", result.Headings[0].Text);
    }

    [Fact]
    public void Parse_MalformedFrontmatter_NonMapRoot_ListOrScalar_HandlesGracefully()
    {
        var content = @"---
- item 1
- item 2
- item 3
---
# List Frontmatter Document
Body after list frontmatter.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.False(result.Frontmatter.HasError);
        Assert.Single(result.Headings);
        Assert.Equal("List Frontmatter Document", result.Headings[0].Text);
    }

    [Fact]
    public void Parse_MalformedFrontmatter_ControlCharactersAndTabs_DoesNotCrash()
    {
        var content = "---\n\ttitle:\t\"Tabbed Title\"\n\ttags:\n\t\t- tag1\n---\n# Tabbed Heading\nBody";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Tabbed Heading", result.Headings[0].Text);
    }

    #endregion

    #region 2. Code Block Hash Comments & Isolation Stress Tests

    [Fact]
    public void Parse_CodeBlock_HashComments_IgnoredAsHeadings()
    {
        var content = @"# Real Top Heading

```sh
#shell comment
#define FOO 100
### heading inside code block
```

# Real Bottom Heading";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal(2, result.Headings.Count);
        Assert.Equal("Real Top Heading", result.Headings[0].Text);
        Assert.Equal("Real Bottom Heading", result.Headings[1].Text);
        Assert.Equal(1, result.CodeBlockCount);
    }

    [Fact]
    public void Parse_CodeBlock_BackticksAndTildes_VaryingFenceLengths_CorrectlyIsolated()
    {
        var content = @"~~~~python
# Fake Python Heading
~~~~

````csharp
```
# Still inside 4-backtick fence
```
````

# Real Outer Heading";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Real Outer Heading", result.Headings[0].Text);
        Assert.Equal(2, result.CodeBlockCount);
    }

    [Fact]
    public void Parse_CodeBlock_UnclosedCodeBlockAtEOF_DoesNotCrashAndIgnoresSubsequentHeadings()
    {
        var content = @"# Heading 1

```
# Unclosed Code Block Heading
[unclosed link](http://example.com)
[[unclosed_wikilink]]";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Heading 1", result.Headings[0].Text);
        Assert.Empty(result.Links);
        Assert.Equal(1, result.CodeBlockCount);
    }

    [Fact]
    public void Parse_CodeBlock_FenceWithTrailingSpacesAndInfoStrings_IsolatedCorrectly()
    {
        var content = @"```csharp    
# Inside Code Fence With Trailing Spaces
```    

# Heading Outside";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Heading Outside", result.Headings[0].Text);
        Assert.Equal(1, result.CodeBlockCount);
    }

    #endregion

    #region 3. Complex Wikilinks Stress Tests

    [Fact]
    public void Parse_Wikilinks_ComplexTargetAnchorAlias_ParsedAccurately()
    {
        var content = @"See [[target#anchor|Alias Name]] for more details.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Links);

        var link = result.Links[0];
        Assert.Equal(MarkdownLinkType.Wikilink, link.Type);
        Assert.Equal("target#anchor|Alias Name", link.RawTarget);
        Assert.Equal("target", link.Target);
        Assert.Equal("anchor", link.Anchor);
        Assert.Equal("Alias Name", link.Alias);
    }

    [Fact]
    public void Parse_Wikilinks_NestedPath_ParsedAccurately()
    {
        var content = @"Link to [[nested/path/to/doc]] here.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Links);

        var link = result.Links[0];
        Assert.Equal(MarkdownLinkType.Wikilink, link.Type);
        Assert.Equal("nested/path/to/doc", link.Target);
        Assert.Null(link.Anchor);
        Assert.Null(link.Alias);
    }

    [Fact]
    public void Parse_Wikilinks_AnchorOnly_ParsedAccurately()
    {
        var content = @"Jump to [[#header-section]] in this file.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Links);

        var link = result.Links[0];
        Assert.Equal(MarkdownLinkType.Wikilink, link.Type);
        Assert.Equal(string.Empty, link.Target);
        Assert.Equal("header-section", link.Anchor);
    }

    [Fact]
    public void Parse_Wikilinks_SpecialCharactersInAlias_ParsedAccurately()
    {
        var content = @"Check [[doc_1|Alias with spaces & special # chars]]";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Links);

        var link = result.Links[0];
        Assert.Equal(MarkdownLinkType.Wikilink, link.Type);
        Assert.Equal("doc_1", link.Target);
        Assert.Equal("Alias with spaces & special # chars", link.Alias);
    }

    [Fact]
    public void Parse_Wikilinks_MultipleOnSameLine_ParsedAccurately()
    {
        var content = @"First [[doc1]] and second [[doc2#section|Doc 2]] on same line.";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal(2, result.Links.Count);

        Assert.Equal("doc1", result.Links[0].Target);
        Assert.Equal("doc2", result.Links[1].Target);
        Assert.Equal("section", result.Links[1].Anchor);
        Assert.Equal("Doc 2", result.Links[1].Alias);
    }

    [Fact]
    public void Parse_LinksWithQueryParamsAndAnchors_ParsedAccurately()
    {
        var content = @"Click [search query](https://example.com/search?q=foo&bar=1#results-section)";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Single(result.Links);

        var link = result.Links[0];
        Assert.Equal(MarkdownLinkType.NormalLink, link.Type);
        Assert.Equal("https://example.com/search?q=foo&bar=1#results-section", link.RawTarget);
        Assert.Equal("https://example.com/search?q=foo&bar=1", link.Target);
        Assert.Equal("results-section", link.Anchor);
    }

    #endregion

    #region 4. Line Endings, BOM & Zero-Byte Files Stress Tests

    [Fact]
    public void Parse_ZeroByteAndWhitespace_ReturnsEmptyParseResult()
    {
        var resultZero = _parser.Parse("");
        Assert.NotNull(resultZero);
        Assert.Empty(resultZero.Headings);
        Assert.Empty(resultZero.Links);
        Assert.Equal(0, resultZero.CodeBlockCount);

        var resultWs = _parser.Parse("   \r\n\t  \n  ");
        Assert.NotNull(resultWs);
        Assert.Empty(resultWs.Headings);
        Assert.Empty(resultWs.Links);
    }

    [Fact]
    public void Parse_MixedLineEndings_CRLF_LF_CR_CalculatesOffsetsAccurately()
    {
        var content = "# Heading 1\r\nText on line 2\n# Heading 2\rText on line 4\r\n[link](target.md)";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.True(result.Headings.Count >= 2);

        foreach (var heading in result.Headings)
        {
            Assert.True(heading.StartOffset >= 0);
            Assert.True(heading.EndOffset > heading.StartOffset);
            Assert.True(heading.EndOffset <= content.Length);
        }

        foreach (var link in result.Links)
        {
            Assert.True(link.StartOffset >= 0);
            Assert.True(link.EndOffset > link.StartOffset);
            Assert.True(link.EndOffset <= content.Length);
            string sliced = content.Substring(link.StartOffset, link.EndOffset - link.StartOffset);
            Assert.Contains(link.RawTarget, sliced);
        }
    }

    [Fact]
    public void Parse_UTF8BOM_WithCRLF_CalculatesLineStartAndEndOffsetsAccurately()
    {
        var content = "\uFEFF---\r\ntitle: BOM CRLF Document\r\n---\r\n# Main Heading\r\nBody text with [[wikilink_doc]]";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.False(result.Frontmatter.HasError);
        Assert.Equal("BOM CRLF Document", result.Frontmatter.StructuredData["title"]?.ToString());

        Assert.Single(result.Headings);
        var heading = result.Headings[0];
        Assert.Equal("Main Heading", heading.Text);

        string headingSub = content.Substring(heading.StartOffset, heading.EndOffset - heading.StartOffset);
        Assert.Contains("Main Heading", headingSub);

        Assert.Single(result.Links);
        var link = result.Links[0];
        string linkSub = content.Substring(link.StartOffset, link.EndOffset - link.StartOffset);
        Assert.Contains("wikilink_doc", linkSub);
    }

    #endregion

    #region 5. Out-of-Order Heading Levels & Outline Tree Stress Tests

    [Fact]
    public void Parse_OutOfOrderHeadings_H1FollowedByH4_BuildsHierarchyWithoutCrashing()
    {
        var content = @"# Level 1 Heading

#### Jumped to Level 4 Heading

## Level 2 Heading";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal(3, result.Headings.Count);

        var tree = result.OutlineTree;
        Assert.Single(tree);
        Assert.Equal("Level 1 Heading", tree[0].Heading.Text);
        Assert.Equal(2, tree[0].Children.Count);

        Assert.Equal("Jumped to Level 4 Heading", tree[0].Children[0].Heading.Text);
        Assert.Equal("Level 2 Heading", tree[0].Children[1].Heading.Text);
    }

    [Fact]
    public void Parse_OutOfOrderHeadings_H5FollowedByH1_CreatesMultipleRoots()
    {
        var content = @"##### Deep H5 Heading

# Root H1 Heading";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal(2, result.Headings.Count);

        var tree = result.OutlineTree;
        Assert.Equal(2, tree.Count);
        Assert.Equal("Deep H5 Heading", tree[0].Heading.Text);
        Assert.Equal("Root H1 Heading", tree[1].Heading.Text);
    }

    [Fact]
    public void Parse_ATXHeadings_TrailingHashes_TrimsHashesCorrectly()
    {
        var content = @"# Heading with trailing hashes ###
## Heading with space before hash #
### Heading without space before hash###";

        var result = _parser.Parse(content);

        Assert.NotNull(result);
        Assert.Equal(3, result.Headings.Count);

        Assert.Equal("Heading with trailing hashes", result.Headings[0].Text);
        Assert.Equal("Heading with space before hash", result.Headings[1].Text);
        Assert.Equal("Heading without space before hash", result.Headings[2].Text);
    }

    #endregion

    #region 6. Performance & Scale Stress Tests

    [Fact]
    public void Parse_LargeDocument_10000Lines_ParsesPerformantlyWithoutCrashing()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: Scale Test");
        sb.AppendLine("tags: [scale, stress]");
        sb.AppendLine("---");

        for (int i = 1; i <= 2000; i++)
        {
            sb.AppendLine($"# Heading Level 1 - {i}");
            sb.AppendLine($"Section content {i} with [link-{i}](https://example.com/item{i}) and [[wiki-{i}#sec|Wiki {i}]]");
            sb.AppendLine("```csharp");
            sb.AppendLine($"// # Fake code comment {i}");
            sb.AppendLine("```");
        }

        var text = sb.ToString();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = _parser.Parse(text);
        watch.Stop();

        Assert.NotNull(result);
        Assert.Equal(2000, result.Headings.Count);
        Assert.Equal(4000, result.Links.Count); // 2000 normal + 2000 wikilinks
        Assert.Equal(2000, result.CodeBlockCount);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"Parsing took {watch.ElapsedMilliseconds}ms, expected < 2000ms");
    }

    #endregion

    #region 7. Offset Integrity Verification Tests

    [Fact]
    public void Parse_HeadingAndLinkOffsets_MatchExactSubstringInRawSource()
    {
        var content = @"# Heading 1
Some paragraph text with a [normal link](https://example.com/page#anchor) here.

## Heading 2
Another paragraph with [[wikilink_target#sec1|Wiki Alias]] inside.";

        var result = _parser.Parse(content);

        foreach (var heading in result.Headings)
        {
            string rawSlice = content.Substring(heading.StartOffset, heading.EndOffset - heading.StartOffset);
            Assert.Contains(heading.Text, rawSlice);
        }

        foreach (var link in result.Links)
        {
            string rawSlice = content.Substring(link.StartOffset, link.EndOffset - link.StartOffset);
            Assert.True(rawSlice.Contains(link.RawTarget) || rawSlice.Contains(link.Target));
        }
    }

    #endregion
}
