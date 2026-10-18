using System.Collections.Generic;
using System.IO;
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
        Assert.Contains("markdown", index["tags"]);
        Assert.Contains("parser", index["tags"]);
        Assert.Contains("FND-04", index["depends_on"]);
    }

    [Fact]
    public void NormalizeRelativeLink_ResolvesPathRelativeToSourcePath()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "docs", "guide.md");
        string relativeTarget = "../architecture.md";

        // Act
        string normalized = MarkdownLinkIndexer.NormalizeRelativeLink(sourcePath, relativeTarget);

        // Assert
        Assert.Equal(Path.Combine("C:", "workspace", "architecture.md"), normalized);
    }

    [Fact]
    public void ResolveTarget_ResolvesUniqueRelativeAndWikilinks()
    {
        // Arrange
        var workspaceFiles = new[]
        {
            Path.Combine("C:", "workspace", "architecture.md"),
            Path.Combine("C:", "workspace", "specs", "spec-01.md"),
            Path.Combine("C:", "workspace", "notes", "todo.md")
        };
        string sourcePath = Path.Combine("C:", "workspace", "specs", "spec-01.md");

        // Act
        var relativeResult = MarkdownLinkIndexer.ResolveLinkTarget(sourcePath, "../architecture.md", MarkdownLinkType.NormalLink, workspaceFiles);
        var wikiResult = MarkdownLinkIndexer.ResolveLinkTarget(sourcePath, "todo", MarkdownLinkType.Wikilink, workspaceFiles);

        // Assert
        Assert.True(relativeResult.IsResolved);
        Assert.Equal(Path.Combine("C:", "workspace", "architecture.md"), relativeResult.ResolvedPath);

        Assert.True(wikiResult.IsResolved);
        Assert.Equal(Path.Combine("C:", "workspace", "notes", "todo.md"), wikiResult.ResolvedPath);
    }

    [Fact]
    public void ResolveWikilinkTarget_ReturnsAmbiguousWhenMultipleMatchesExist()
    {
        // Arrange
        var workspaceFiles = new[]
        {
            Path.Combine("C:", "workspace", "folderA", "architecture.md"),
            Path.Combine("C:", "workspace", "folderB", "architecture.md")
        };
        string sourcePath = Path.Combine("C:", "workspace", "main.md");

        // Act
        var wikiResult = MarkdownLinkIndexer.ResolveLinkTarget(sourcePath, "architecture", MarkdownLinkType.Wikilink, workspaceFiles);

        // Assert
        Assert.False(wikiResult.IsResolved);
        Assert.True(wikiResult.IsAmbiguous);
        Assert.Equal(2, wikiResult.CandidatePaths.Count);
    }

    [Fact]
    public void GetExplicitRelationCandidates_EmitsAllCandidatesWithoutMutatingSource()
    {
        // Arrange
        var content = @"---
depends_on:
  - FND-04.md
---
# Test Document
Here is a link to [Architecture](../architecture.md) and a wikilink [[todo]].
";
        string sourcePath = Path.Combine("C:", "workspace", "docs", "spec.md");
        var parser = new MarkdownStructureParser();
        var parseResult = parser.Parse(content);

        // Act
        var candidates = MarkdownLinkIndexer.GetExplicitRelationCandidates(sourcePath, parseResult);

        // Assert
        Assert.Equal(3, candidates.Count);

        // Check frontmatter dependency
        var dep = candidates.Single(c => c.RelationType == MarkdownLinkType.FrontmatterDependency);
        Assert.Equal("FND-04.md", dep.RawTarget);

        // Check normal link
        var norm = candidates.Single(c => c.RelationType == MarkdownLinkType.NormalLink);
        Assert.Equal("../architecture.md", norm.RawTarget);
        Assert.Equal(Path.Combine("C:", "workspace", "architecture.md"), norm.NormalizedTargetPath);

        // Check wikilink
        var wiki = candidates.Single(c => c.RelationType == MarkdownLinkType.Wikilink);
        Assert.Equal("todo", wiki.RawTarget);

        // Verify source content was not modified
        Assert.Equal(content, parseResult.RawSource);
    }
}

