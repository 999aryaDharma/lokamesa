using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using DocumentIDE.Core.Context;

namespace DocumentIDE.Core.Tests.Context;

public class ContextExportTests
{
    [Fact]
    public void TokenEstimate_IsLabeledByTokenizerModel()
    {
        // Arrange
        var items = new List<ContextExportItem>
        {
            new ContextExportItem { SourceId = "doc1", TitleOrPath = "Doc1.md", Content = "Hello world! This is a test document for token estimation." },
            new ContextExportItem { SourceId = "doc2", TitleOrPath = "Doc2.md", Content = "Another paragraph of content to test token counting." }
        };

        var service = new ContextExportService();
        var options = new ContextExportOptions
        {
            Model = TokenizerModel.Gpt4
        };

        // Act
        var result = service.EstimateTokens(items, options);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("GPT-4", result.ModelLabel);
        Assert.True(result.TotalEstimatedTokens > 0);
        Assert.True(result.TotalCharacterCount > 0);
        Assert.Equal(2, result.SourceContributions.Count);
        Assert.Equal("doc1", result.SourceContributions[0].SourceId);
        Assert.True(result.SourceContributions[0].PercentageOfTotal > 0);
    }

    [Fact]
    public void TokenEstimate_CustomModelLabel_UsedWhenProvided()
    {
        // Arrange
        var items = new List<ContextExportItem>
        {
            new ContextExportItem { SourceId = "doc1", TitleOrPath = "Doc1.md", Content = "Test text." }
        };

        var service = new ContextExportService();
        var options = new ContextExportOptions
        {
            Model = TokenizerModel.Generic,
            CustomModelLabel = "Custom-Tokenizer-v2"
        };

        // Act
        var result = service.EstimateTokens(items, options);

        // Assert
        Assert.Equal("Custom-Tokenizer-v2", result.ModelLabel);
    }

    [Fact]
    public void ExportMatchesReviewedPackOrder()
    {
        // Arrange
        var items = new List<ContextExportItem>
        {
            new ContextExportItem { SourceId = "b", TitleOrPath = "B.md", Content = "# Document B\nContent B" },
            new ContextExportItem { SourceId = "a", TitleOrPath = "A.md", Content = "# Document A\nContent A" },
            new ContextExportItem { SourceId = "c", TitleOrPath = "C.md", Content = "# Document C\nContent C" }
        };

        var service = new ContextExportService();

        // Act
        var markdown = service.GenerateCombinedMarkdown(items);

        // Assert
        Assert.NotNull(markdown);
        int posB = markdown.IndexOf("Document B", StringComparison.Ordinal);
        int posA = markdown.IndexOf("Document A", StringComparison.Ordinal);
        int posC = markdown.IndexOf("Document C", StringComparison.Ordinal);

        Assert.True(posB >= 0 && posA >= 0 && posC >= 0);
        Assert.True(posB < posA, "Document B should appear before Document A in export output");
        Assert.True(posA < posC, "Document A should appear before Document C in export output");
    }

    [Fact]
    public void OriginalDocumentsUnchanged_AfterExport()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "DocIDETests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string sourceFilePath = Path.Combine(tempDir, "OriginalDoc.md");
            string originalContent = "# Original Document\nDo not modify this file!";
            File.WriteAllText(sourceFilePath, originalContent);

            string exportFilePath = Path.Combine(tempDir, "context.md");

            var items = new List<ContextExportItem>
            {
                new ContextExportItem { SourceId = "orig", TitleOrPath = sourceFilePath, Content = originalContent }
            };

            var service = new ContextExportService();

            // Act
            var exportResult = service.ExportToFile(items, exportFilePath);

            // Assert
            Assert.True(File.Exists(exportFilePath));
            Assert.True(File.Exists(sourceFilePath));
            Assert.Equal(originalContent, File.ReadAllText(sourceFilePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void WarnWhenTokenBudgetExceeded()
    {
        // Arrange
        var items = new List<ContextExportItem>
        {
            new ContextExportItem
            {
                SourceId = "doc1",
                TitleOrPath = "LargeDoc.md",
                Content = new string('A', 4000) // ~1000 tokens
            }
        };

        var service = new ContextExportService();
        var options = new ContextExportOptions
        {
            Model = TokenizerModel.Generic,
            TokenBudget = 500 // budget lower than estimated tokens (~1000)
        };

        // Act
        var result = service.EstimateTokens(items, options);

        // Assert
        Assert.True(result.ExceedsBudget);
        Assert.NotNull(result.WarningMessage);
        Assert.Contains("exceeds target budget", result.WarningMessage, StringComparison.OrdinalIgnoreCase);
    }
}
