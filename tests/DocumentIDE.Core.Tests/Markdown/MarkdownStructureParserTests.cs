using System;
using System.Collections.Generic;
using System.Linq;
using DocumentIDE.Core.Markdown;
using Xunit;

namespace DocumentIDE.Core.Tests.Markdown;

public class MarkdownStructureParserTests
{
    [Fact]
    public void Parse_ValidYAMLFrontmatter_ExtractsMetadataTagsAndExplicitDependencies()
    {
        // Arrange
        var content = @"---
title: Document IDE Spec
type: specification
version: 2
tags:
  - markdown
  - parser
depends_on:
  - FND-04
  - FS-02
---
# Main Heading
This is body text.";

        var parser = new MarkdownStructureParser();

        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1.0.0", result.ParserVersion);
        Assert.False(result.Frontmatter.HasError);
        Assert.Null(result.Frontmatter.ErrorMessage);
        Assert.NotNull(result.Frontmatter.StructuredData);
        Assert.Equal("Document IDE Spec", result.Frontmatter.StructuredData["title"]?.ToString());
        Assert.Equal("specification", result.Frontmatter.StructuredData["type"]?.ToString());

        Assert.Contains("markdown", result.Frontmatter.Tags);
        Assert.Contains("parser", result.Frontmatter.Tags);
        Assert.Contains("markdown", result.Metadata.Tags);

        Assert.Contains("FND-04", result.Frontmatter.ExplicitDependencies);
        Assert.Contains("FS-02", result.Frontmatter.ExplicitDependencies);
        Assert.Contains("FND-04", result.Metadata.ExplicitDependencies);

        // Explicit dependencies added to links as FrontmatterDependency
        Assert.Contains(result.Links, l => l.Type == MarkdownLinkType.FrontmatterDependency && l.Target == "FND-04");
        Assert.Contains(result.Links, l => l.Type == MarkdownLinkType.FrontmatterDependency && l.Target == "FS-02");
    }

    [Fact]
    public void Parse_MalformedFrontmatter_PopulatesErrorMessageAndParsesBody()
    {
        // Arrange - unclosed frontmatter
        var content = @"---
title: Unclosed Frontmatter
tags: [invalid

# Body Heading
This body content must still be parsed correctly.";

        var parser = new MarkdownStructureParser();

        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Frontmatter.HasError);
        Assert.NotNull(result.Frontmatter.ErrorMessage);

        // Headings and body are still extracted!
        Assert.NotEmpty(result.Headings);
        Assert.Equal("Body Heading", result.Headings[0].Text);
        Assert.Contains("This body content must still be parsed correctly.", result.SearchableBody);
    }

    [Fact]
    public void Parse_HeadingsAndOutline_ExtractsATXAndSetextWithOffsetsAndTree()
    {
        // Arrange
        var content = @"# Title H1

## Section 1.1

### Subsection 1.1.1

## Section 1.2

Title Setext L1
===

Title Setext L2
---";

        var parser = new MarkdownStructureParser();

        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(6, result.Headings.Count);

        // ATX Level 1
        Assert.Equal(1, result.Headings[0].Level);
        Assert.Equal("Title H1", result.Headings[0].Text);
        Assert.Equal("title-h1", result.Headings[0].Slug);
        Assert.Equal(1, result.Headings[0].LineNumber);

        // ATX Level 2
        Assert.Equal(2, result.Headings[1].Level);
        Assert.Equal("Section 1.1", result.Headings[1].Text);
        Assert.Equal("section-11", result.Headings[1].Slug);

        // ATX Level 3
        Assert.Equal(3, result.Headings[2].Level);
        Assert.Equal("Subsection 1.1.1", result.Headings[2].Text);

        // Setext Level 1
        Assert.Equal(1, result.Headings[4].Level);
        Assert.Equal("Title Setext L1", result.Headings[4].Text);

        // Setext Level 2
        Assert.Equal(2, result.Headings[5].Level);
        Assert.Equal("Title Setext L2", result.Headings[5].Text);

        // Outline Tree structure
        Assert.NotEmpty(result.OutlineTree);
        var root1 = result.OutlineTree[0];
        Assert.Equal("Title H1", root1.Heading.Text);
        Assert.Equal(2, root1.Children.Count); // Section 1.1 and Section 1.2
        Assert.Equal("Section 1.1", root1.Children[0].Heading.Text);
        Assert.Single(root1.Children[0].Children); // Subsection 1.1.1
        Assert.Equal("Subsection 1.1.1", root1.Children[0].Children[0].Heading.Text);
    }

    [Fact]
    public void Parse_LinksAndWikilinks_ExtractsNormalAndWikilinks()
    {
        // Arrange
        var content = @"Check out [Document IDE](https://example.com/docs#overview) and [Relative Link](docs/readme.md).
Also see [[FS-02-stable-identity]] and [[08-markdown-spec#Section 2|Graph Spec]].";

        var parser = new MarkdownStructureParser();

        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(4, result.Links.Count);

        // Standard Link 1
        var normalLink1 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.NormalLink && l.Alias == "Document IDE");
        Assert.NotNull(normalLink1);
        Assert.Equal("https://example.com/docs#overview", normalLink1.RawTarget);
        Assert.Equal("https://example.com/docs", normalLink1.Target);
        Assert.Equal("overview", normalLink1.Anchor);

        // Standard Link 2
        var normalLink2 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.NormalLink && l.Alias == "Relative Link");
        Assert.NotNull(normalLink2);
        Assert.Equal("docs/readme.md", normalLink2.Target);

        // Wikilink 1
        var wikiLink1 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.Wikilink && l.Target == "FS-02-stable-identity");
        Assert.NotNull(wikiLink1);
        Assert.Null(wikiLink1.Alias);
        Assert.Null(wikiLink1.Anchor);

        // Wikilink 2 with Anchor and Alias
        var wikiLink2 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.Wikilink && l.Target == "08-markdown-spec");
        Assert.NotNull(wikiLink2);
        Assert.Equal("Graph Spec", wikiLink2.Alias);
        Assert.Equal("Section 2", wikiLink2.Anchor);
    }

    [Fact]
    public void Parse_CodeBlockIsolation_IgnoresHeadingsAndLinksInsideCodeFences()
    {
        // Arrange
        var content = @"# Real Heading

```csharp
// # Fake Heading inside code
string link = ""[fake](http://example.com)"";
string wikilink = ""[[fake_wikilink]]"";
```

~~~python
# Another fake heading
~~~

[inline link](url) outside code.";

        var parser = new MarkdownStructureParser();

        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Single(result.Headings);
        Assert.Equal("Real Heading", result.Headings[0].Text);

        Assert.Single(result.Links);
        Assert.Equal("inline link", result.Links[0].Alias);

        Assert.Equal(2, result.CodeBlockCount);
        Assert.Equal(2, result.Metadata.CodeBlockCount);
    }

    [Fact]
    public void Parse_SearchableBody_StripsFrontmatterAndPreservesRawSource()
    {
        // Arrange
        var content = @"---
title: Test Document
tags: [test]
---
# Main Section
This is body text.";

        var parser = new MarkdownStructureParser();

        // Act
        var result = parser.Parse(content);

        // Assert
        Assert.Equal(content, result.RawSource);
        Assert.DoesNotContain("---", result.SearchableBody);
        Assert.DoesNotContain("title: Test Document", result.SearchableBody);
        Assert.Contains("# Main Section", result.SearchableBody);
        Assert.Contains("This is body text.", result.SearchableBody);
    }

    [Fact]
    public void Parse_EdgeCases_EmptyFile_UTF8BOM_LineEndings()
    {
        var parser = new MarkdownStructureParser();

        // Empty string
        var emptyResult = parser.Parse("");
        Assert.NotNull(emptyResult);
        Assert.Empty(emptyResult.Headings);
        Assert.Empty(emptyResult.Links);
        Assert.Equal("1.0.0", emptyResult.ParserVersion);

        // UTF-8 BOM
        var bomContent = "\uFEFF---\ntitle: BOM Test\n---\n# BOM Heading";
        var bomResult = parser.Parse(bomContent);
        Assert.False(bomResult.Frontmatter.HasError);
        Assert.Equal("BOM Test", bomResult.Frontmatter.StructuredData["title"]?.ToString());
        Assert.Single(bomResult.Headings);
        Assert.Equal("BOM Heading", bomResult.Headings[0].Text);

        // CRLF Line endings
        var crlfContent = "---\r\ntitle: CRLF Test\r\n---\r\n# Heading 1\r\nLine 2";
        var crlfResult = parser.Parse(crlfContent);
        Assert.False(crlfResult.Frontmatter.HasError);
        Assert.Single(crlfResult.Headings);
        Assert.Equal(4, crlfResult.Headings[0].LineNumber);
    }

    [Fact]
    public void Parse_TextReader_ReturnsIdenticalResult()
    {
        var content = "# Stream Heading\nBody text with [link](http://test.org)";
        var parser = new MarkdownStructureParser();

        using var reader = new System.IO.StringReader(content);
        var result = parser.Parse(reader);

        Assert.NotNull(result);
        Assert.Single(result.Headings);
        Assert.Equal("Stream Heading", result.Headings[0].Text);
        Assert.Single(result.Links);
        Assert.Equal("http://test.org", result.Links[0].Target);
    }

    [Fact]
    public void Parse_HashesWithoutSpace_NotParsedAsHeadings()
    {
        var content = "This is #tag and #123 in body text, not a heading.";
        var parser = new MarkdownStructureParser();

        var result = parser.Parse(content);

        Assert.Empty(result.Headings);
    }

    [Fact]
    public void Parse_NestedCodeFences_IgnoredProperly()
    {
        var content = @"````markdown
```csharp
# Inside nested code fence
```
````
# Outside Heading";

        var parser = new MarkdownStructureParser();

        var result = parser.Parse(content);

        Assert.Single(result.Headings);
        Assert.Equal("Outside Heading", result.Headings[0].Text);
        Assert.Equal(1, result.CodeBlockCount);
    }
}
