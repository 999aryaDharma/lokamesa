using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DocumentIDE.Core.Markdown;
using Xunit;

namespace DocumentIDE.Core.Tests.Markdown;

public class MarkdownStructureParserChallengerTests
{
    private readonly MarkdownStructureParser _parser = new();

    [Fact]
    public void Task1_LargeDocument_10000Lines_PerformanceAndExtractionStress()
    {
        // Arrange
        var sb = new StringBuilder();

        // Frontmatter
        sb.AppendLine("---");
        sb.AppendLine("title: Large Stress Test Document");
        sb.AppendLine("tags:");
        sb.AppendLine("  - stress");
        sb.AppendLine("  - performance");
        sb.AppendLine("depends_on:");
        sb.AppendLine("  - DEP-01");
        sb.AppendLine("  - DEP-02");
        sb.AppendLine("---");

        int expectedHeadings = 0;
        int expectedCodeBlocks = 0;
        int expectedLinks = 2; // 2 frontmatter dependencies

        // Generate ~11,000 lines
        for (int i = 0; i < 2000; i++)
        {
            // Heading ATX
            sb.AppendLine($"# Section {i}");
            expectedHeadings++;

            sb.AppendLine($"This is paragraph {i} introducing [[Doc-{i}|Link {i}]] and [External](https://example.com/item/{i}#section).");
            expectedLinks += 2;

            // Subheading ATX
            sb.AppendLine($"## Subsection {i}.1");
            expectedHeadings++;

            if (i % 10 == 0)
            {
                // Code block backticks
                sb.AppendLine("```csharp");
                sb.AppendLine("// # Fake Heading in Code");
                sb.AppendLine($"string s = \"[fake](link)\";");
                sb.AppendLine("```");
                expectedCodeBlocks++;

                // Code block tildes
                sb.AppendLine("~~~python");
                sb.AppendLine("# Another fake heading");
                sb.AppendLine("~~~");
                expectedCodeBlocks++;
            }

            if (i % 5 == 0)
            {
                // Setext heading L1
                sb.AppendLine($"Setext Heading L1 {i}");
                sb.AppendLine("===");
                expectedHeadings++;

                // Setext heading L2
                sb.AppendLine($"Setext Heading L2 {i}");
                sb.AppendLine("---");
                expectedHeadings++;
            }

            sb.AppendLine($"Line filler content {i} with some details.");
        }

        string largeContent = sb.ToString();
        int totalLines = largeContent.Split('\n').Length;
        Assert.True(totalLines >= 10000, $"Generated {totalLines} lines, expected >= 10,000 lines.");

        // Act
        var sw = Stopwatch.StartNew();
        var result = _parser.Parse(largeContent);
        sw.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedHeadings, result.Headings.Count);
        Assert.Equal(expectedCodeBlocks, result.CodeBlockCount);
        Assert.Equal(expectedLinks, result.Links.Count);

        // Verification of SearchableBody exclusion of frontmatter
        Assert.DoesNotContain("title: Large Stress Test Document", result.SearchableBody);
        Assert.Contains("Setext Heading L1 0", result.SearchableBody);

        // Verify execution time is sub-second (e.g. < 1000 ms)
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Parse took {sw.ElapsedMilliseconds} ms, expected < 1000 ms.");
    }

    [Fact]
    public void Task1_MultipleCodeBlocks_TildesAndBackticks_IsolatedCorrectly()
    {
        var content = @"# Main Title

```csharp
# Ignored ATX 1
[[Ignored Wikilink 1]]
[Ignored Normal Link 1](http://test.org)
```

~~~python
# Ignored ATX 2
[[Ignored Wikilink 2]]
~~~

````markdown
```csharp
# Ignored Nested Heading
```
````

~~~
# Ignored Empty Language Fence Heading
~~~

# Valid ATX Heading Outside Code
Check [[Valid Wikilink]] and [Valid Link](http://real.org).";

        var result = _parser.Parse(content);

        Assert.Equal(2, result.Headings.Count);
        Assert.Equal("Main Title", result.Headings[0].Text);
        Assert.Equal("Valid ATX Heading Outside Code", result.Headings[1].Text);

        Assert.Equal(4, result.CodeBlockCount);

        var validWikilinks = result.Links.Where(l => l.Type == MarkdownLinkType.Wikilink).ToList();
        Assert.Single(validWikilinks);
        Assert.Equal("Valid Wikilink", validWikilinks[0].Target);

        var validNormalLinks = result.Links.Where(l => l.Type == MarkdownLinkType.NormalLink).ToList();
        Assert.Single(validNormalLinks);
        Assert.Equal("http://real.org", validNormalLinks[0].Target);
    }

    [Fact]
    public void Task1_Frontmatter_TagsDependenciesAndArbitraryPrimitives()
    {
        var content = @"---
title: ""Document IDE: Frontmatter Test""
version: 1.5
is_draft: false
priority: 10
author_info:
  name: Alice
tags:
  - markdown
  - parser
  - test
depends_on:
  - FND-01
  - FND-02
  - FS-01
custom_inline_tags: [tag1, tag2]
---
# Document Body
Content goes here.";

        var result = _parser.Parse(content);

        Assert.False(result.Frontmatter.HasError);
        Assert.Null(result.Frontmatter.ErrorMessage);

        var data = result.Frontmatter.StructuredData;
        Assert.Equal("Document IDE: Frontmatter Test", data["title"]?.ToString());
        Assert.Equal("1.5", data["version"]?.ToString());
        Assert.Equal("false", data["is_draft"]?.ToString());
        Assert.Equal("10", data["priority"]?.ToString());

        // Tags
        Assert.Contains("markdown", result.Frontmatter.Tags);
        Assert.Contains("parser", result.Frontmatter.Tags);
        Assert.Contains("test", result.Frontmatter.Tags);

        // Dependencies
        Assert.Equal(3, result.Frontmatter.ExplicitDependencies.Count);
        Assert.Contains("FND-01", result.Frontmatter.ExplicitDependencies);
        Assert.Contains("FND-02", result.Frontmatter.ExplicitDependencies);
        Assert.Contains("FS-01", result.Frontmatter.ExplicitDependencies);

        // Links count should include frontmatter dependencies
        Assert.Contains(result.Links, l => l.Type == MarkdownLinkType.FrontmatterDependency && l.Target == "FND-01");
    }

    [Fact]
    public void Task1_UTF8AndSpecialCharacters_InHeadingsAndWikilinks()
    {
        var content = @"# 第一章 Overview 🚀

## Überraschung & Äpfel: Modern Parsing (100%)

Setext UTF8 Heading: Résumé & Étape 1
===

Check wikilinks: [[08-spec#Section|Aliäs & ""Quotes"" 🚀]] and [[中文 文档|中文 别名]].
Check link: [🚀 Rocket Launch](https://example.com/space?launch=1&type=heavy#orbit).";

        var result = _parser.Parse(content);

        Assert.Equal(3, result.Headings.Count);

        // Heading 1
        Assert.Equal("第一章 Overview 🚀", result.Headings[0].Text);
        Assert.Equal("第一章-overview", result.Headings[0].Slug);

        // Heading 2
        Assert.Equal("Überraschung & Äpfel: Modern Parsing (100%)", result.Headings[1].Text);
        Assert.Equal("überraschung-äpfel-modern-parsing-100", result.Headings[1].Slug);

        // Heading 3 (Setext)
        Assert.Equal("Setext UTF8 Heading: Résumé & Étape 1", result.Headings[2].Text);
        Assert.Equal("setext-utf8-heading-résumé-étape-1", result.Headings[2].Slug);

        // Wikilinks
        var wiki1 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.Wikilink && l.Target == "08-spec");
        Assert.NotNull(wiki1);
        Assert.Equal("Section", wiki1.Anchor);
        Assert.Equal("Aliäs & \"Quotes\" 🚀", wiki1.Alias);

        var wiki2 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.Wikilink && l.Target == "中文 文档");
        Assert.NotNull(wiki2);
        Assert.Equal("中文 别名", wiki2.Alias);

        // Normal link
        var norm1 = result.Links.FirstOrDefault(l => l.Type == MarkdownLinkType.NormalLink && l.Alias == "🚀 Rocket Launch");
        Assert.NotNull(norm1);
        Assert.Equal("https://example.com/space?launch=1&type=heavy", norm1.Target);
        Assert.Equal("orbit", norm1.Anchor);
    }

    [Fact]
    public void Boundary_OffsetAccuracy_CanExtractOriginalSubstrings()
    {
        var content = @"---
title: Offset Test
---
# First Heading
Some body text with [[Wikilink Target|Alias]].
## Second Heading
More body text.";

        var result = _parser.Parse(content);

        foreach (var heading in result.Headings)
        {
            string rawSlice = content.Substring(heading.StartOffset, heading.EndOffset - heading.StartOffset);
            Assert.Contains(heading.Text, rawSlice);
        }

        foreach (var link in result.Links.Where(l => l.Type != MarkdownLinkType.FrontmatterDependency))
        {
            string rawSlice = content.Substring(link.StartOffset, link.EndOffset - link.StartOffset);
            Assert.Contains(link.RawTarget, rawSlice);
        }
    }
}
