using System.Collections.Generic;
using System.IO;

using DocumentIDE.Core.Markdown;

using Xunit;

namespace DocumentIDE.Core.Tests.Markdown;

public class WikilinkResolverTests
{
    [Fact]
    public void UniqueStemResolves()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "readme.md");
        string targetPath = Path.Combine("C:", "workspace", "notes", "architecture.md");
        var workspaceFiles = new[]
        {
            targetPath,
            Path.Combine("C:", "workspace", "notes", "todo.md")
        };

        // Act
        var result = WikilinkResolver.ResolveWikilink(sourcePath, "[[architecture]]", workspaceFiles);

        // Assert
        Assert.True(result.IsResolved);
        Assert.False(result.IsAmbiguous);
        Assert.False(result.IsUnresolved);
        Assert.Equal(targetPath, result.ResolvedPath);
        Assert.Single(result.CandidatePaths);
        Assert.Equal(targetPath, result.CandidatePaths[0]);
    }

    [Fact]
    public void TwoSameStemDocsAreAmbiguous()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "readme.md");
        string candidateA = Path.Combine("C:", "workspace", "folderA", "architecture.md");
        string candidateB = Path.Combine("C:", "workspace", "folderB", "architecture.md");
        var workspaceFiles = new[] { candidateA, candidateB };

        // Act
        var result = WikilinkResolver.ResolveWikilink(sourcePath, "[[architecture]]", workspaceFiles);

        // Assert
        Assert.False(result.IsResolved);
        Assert.True(result.IsAmbiguous);
        Assert.False(result.IsUnresolved);
        Assert.Null(result.ResolvedPath);
        Assert.Equal(2, result.CandidatePaths.Count);
        Assert.Contains(candidateA, result.CandidatePaths);
        Assert.Contains(candidateB, result.CandidatePaths);

        Assert.NotNull(result.Diagnostic);
        Assert.Equal(WikilinkDiagnosticKind.Ambiguous, result.Diagnostic!.Kind);
        Assert.Equal(2, result.Diagnostic.CandidatePaths.Count);
    }

    [Fact]
    public void AliasPreservesTargetIdentity()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "notes", "guide.md");
        string targetPath = Path.Combine("C:", "workspace", "docs", "architecture.md");
        var workspaceFiles = new[] { targetPath };

        // Act
        var resultWithAlias = WikilinkResolver.ResolveWikilink(sourcePath, "[[architecture|Architecture Overview]]", workspaceFiles);
        var resultWithoutAlias = WikilinkResolver.ResolveWikilink(sourcePath, "[[architecture]]", workspaceFiles);

        // Assert
        Assert.True(resultWithAlias.IsResolved);
        Assert.Equal(targetPath, resultWithAlias.ResolvedPath);
        Assert.Equal("architecture", resultWithAlias.TargetIdentity);
        Assert.Equal("Architecture Overview", resultWithAlias.Alias);

        Assert.True(resultWithoutAlias.IsResolved);
        Assert.Equal(targetPath, resultWithoutAlias.ResolvedPath);
        Assert.Equal("architecture", resultWithoutAlias.TargetIdentity);

        Assert.True(resultWithoutAlias.IsResolved);
        Assert.Equal(targetPath, resultWithoutAlias.ResolvedPath);
        Assert.Equal("architecture", resultWithoutAlias.TargetIdentity);

        Assert.Equal(resultWithoutAlias.ResolvedPath, resultWithAlias.ResolvedPath);
    }

    [Fact]
    public void RelativeExactPathResolvesFirst()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "notes", "guide.md");
        string relativeTarget = Path.Combine("C:", "workspace", "specs", "spec-01.md");
        string stemMatchOtherFolder = Path.Combine("C:", "workspace", "other", "spec-01.md");
        var workspaceFiles = new[] { relativeTarget, stemMatchOtherFolder };

        // Act
        var result = WikilinkResolver.ResolveWikilink(sourcePath, "[[../specs/spec-01.md]]", workspaceFiles);

        // Assert
        Assert.True(result.IsResolved);
        Assert.False(result.IsAmbiguous);
        Assert.Equal(relativeTarget, result.ResolvedPath);
    }

    [Fact]
    public void UnresolvedLinkExposesDiagnostic()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "guide.md");
        var workspaceFiles = new[] { Path.Combine("C:", "workspace", "existing.md") };

        // Act
        var result = WikilinkResolver.ResolveWikilink(sourcePath, "[[missing-doc]]", workspaceFiles, lineNumber: 15);

        // Assert
        Assert.False(result.IsResolved);
        Assert.False(result.IsAmbiguous);
        Assert.True(result.IsUnresolved);
        Assert.Null(result.ResolvedPath);
        Assert.Empty(result.CandidatePaths);

        Assert.NotNull(result.Diagnostic);
        Assert.Equal(WikilinkDiagnosticKind.Unresolved, result.Diagnostic!.Kind);
        Assert.Equal(15, result.Diagnostic.LineNumber);
        Assert.Equal("[[missing-doc]]", result.Diagnostic.RawTarget);
    }

    [Fact]
    public void ResolveMarkdownLinkInfo_ResolvesCorrectly()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "main.md");
        string targetPath = Path.Combine("C:", "workspace", "docs", "design.md");
        var workspaceFiles = new[] { targetPath };
        var linkInfo = new MarkdownLinkInfo(
            MarkdownLinkType.Wikilink,
            "design#section-2|Design Specs",
            "design",
            "section-2",
            "Design Specs",
            LineNumber: 42,
            StartOffset: 10,
            EndOffset: 40);

        // Act
        var result = WikilinkResolver.ResolveWikilink(sourcePath, linkInfo, workspaceFiles);

        // Assert
        Assert.True(result.IsResolved);
        Assert.Equal(targetPath, result.ResolvedPath);
        Assert.Equal("design", result.TargetIdentity);
        Assert.Equal("section-2", result.Anchor);
        Assert.Equal("Design Specs", result.Alias);
        Assert.Equal(42, result.LineNumber);
    }

    [Fact]
    public void ResolveDocumentWikilinks_ExtractsResolutionsAndDiagnostics()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "readme.md");
        string targetDoc = Path.Combine("C:", "workspace", "notes", "architecture.md");
        var workspaceFiles = new[] { targetDoc };

        var parser = new MarkdownStructureParser();
        string content = @"# Title
Link to [[architecture|Arch]] and [[missing-link]].
";
        var parseResult = parser.Parse(content);

        // Act
        var resolutions = WikilinkResolver.ResolveDocumentWikilinks(sourcePath, parseResult, workspaceFiles);
        var diagnostics = WikilinkResolver.GetDiagnostics(resolutions);

        // Assert
        Assert.Equal(2, resolutions.Count);

        var resolved = resolutions.Single(r => r.IsResolved);
        Assert.Equal(targetDoc, resolved.ResolvedPath);
        Assert.Equal("Arch", resolved.Alias);

        var unresolved = resolutions.Single(r => r.IsUnresolved);
        Assert.Equal("missing-link", unresolved.TargetIdentity);

        Assert.Single(diagnostics);
        Assert.Equal(WikilinkDiagnosticKind.Unresolved, diagnostics[0].Kind);
    }

    [Fact]
    public void ExtractBacklinkInputs_ReturnsResolvedInboundRelations()
    {
        // Arrange
        string sourcePath = Path.Combine("C:", "workspace", "readme.md");
        string targetDoc = Path.Combine("C:", "workspace", "notes", "architecture.md");
        var workspaceFiles = new[] { targetDoc };

        var parser = new MarkdownStructureParser();
        string content = @"# Title
Check [[architecture#overview|Architecture Spec]].
Check [[missing]].
";
        var parseResult = parser.Parse(content);

        // Act
        var backlinkInputs = WikilinkResolver.ExtractBacklinkInputs(sourcePath, parseResult, workspaceFiles);

        // Assert
        Assert.Single(backlinkInputs);
        var backlink = backlinkInputs[0];
        Assert.Equal(sourcePath, backlink.SourceFilePath);
        Assert.Equal(targetDoc, backlink.TargetFilePath);
        Assert.Equal("architecture#overview|Architecture Spec", backlink.RawTarget);
        Assert.Equal("Architecture Spec", backlink.Alias);
        Assert.Equal("overview", backlink.Anchor);
    }
}
