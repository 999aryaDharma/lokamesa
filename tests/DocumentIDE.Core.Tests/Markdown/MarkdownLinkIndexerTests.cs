using System.Collections.Generic;
using System.Linq;
using DocumentIDE.Core.Markdown;
using Xunit;

namespace DocumentIDE.Core.Tests.Markdown;

public class MarkdownLinkIndexerTests
{
    [Fact]
    public void GetFrontmatterIndex_ReturnsAllFrontmatterKeyValues()
    {
        // Arrange
        var content = @"---
title: Test Document
type: specification
version: 1.0
tags:
  - markdown
  - parser
depends_on:
  - FND-04
---
# Heading
Body text.
";
        var parser = new MarkdownStructureParser();
        var result = parser.Parse(content);

        // Act
        var index = MarkdownLinkIndexer.GetFrontmatterIndex(result);

        // Assert
        Assert.NotNull(index);
        Assert.Equal("Test Document", index["title"]);
        Assert.Equal("specification", index["type"]);
        Assert.Equal("1.0", index["version"]);
        // Tags and dependencies are collections; ToString will give string representation
        Assert.Contains("markdown", index["tags"]);
        Assert.Contains("parser", index["tags"]);
        Assert.Contains("FND-04", index["depends_on"]);
    }
}
