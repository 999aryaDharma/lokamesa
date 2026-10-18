using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DocumentIDE.Core.Context;

/// <summary>
/// Tokenizer / model estimation preset options.
/// </summary>
public enum TokenizerModel
{
    Generic,
    Gpt4,
    Claude35,
    Llama3
}

/// <summary>
/// Options for context export and token estimation.
/// </summary>
public class ContextExportOptions
{
    /// <summary>
    /// Gets or sets the tokenizer preset model. Default is Generic.
    /// </summary>
    public TokenizerModel Model { get; set; } = TokenizerModel.Generic;

    /// <summary>
    /// Custom label for the model/tokenizer if provided. Overrides standard preset label.
    /// </summary>
    public string? CustomModelLabel { get; set; }

    /// <summary>
    /// Optional target token budget limit.
    /// </summary>
    public int? TokenBudget { get; set; }

    /// <summary>
    /// Optional header string to prepend to the combined Markdown context.
    /// </summary>
    public string? HeaderText { get; set; }
}

/// <summary>
/// Item input representing a document or source for context export.
/// </summary>
public class ContextExportItem
{
    public string SourceId { get; set; } = string.Empty;
    public string TitleOrPath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Size contribution breakdown of a single source document.
/// </summary>
public class SourceContribution
{
    public string SourceId { get; set; } = string.Empty;
    public string TitleOrPath { get; set; } = string.Empty;
    public int CharacterCount { get; set; }
    public int WordCount { get; set; }
    public int EstimatedTokens { get; set; }
    public double PercentageOfTotal { get; set; }
}

/// <summary>
/// Result of token estimation for context items.
/// </summary>
public class ContextTokenEstimateResult
{
    public string ModelLabel { get; set; } = string.Empty;
    public int TotalEstimatedTokens { get; set; }
    public int TotalCharacterCount { get; set; }
    public int? TokenBudget { get; set; }
    public bool ExceedsBudget { get; set; }
    public string? WarningMessage { get; set; }
    public IReadOnlyList<SourceContribution> SourceContributions { get; set; } = Array.Empty<SourceContribution>();
}

/// <summary>
/// Result of exporting combined context.
/// </summary>
public class ContextExportResult
{
    public string MarkdownContent { get; set; } = string.Empty;
    public string? SavedFilePath { get; set; }
    public ContextTokenEstimateResult TokenEstimate { get; set; } = new();
    public IReadOnlyList<string> SourceOrder { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Service responsible for token estimation, source breakdown calculation, combined Markdown generation, and safe context export.
/// Implements CTX-04 specification.
/// </summary>
public class ContextExportService
{
    private static readonly Regex WordSplitRegex = new Regex(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Estimates token counts and breakdown per source item based on the selected tokenizer model profile.
    /// </summary>
    public ContextTokenEstimateResult EstimateTokens(IEnumerable<ContextExportItem> items, ContextExportOptions? options = null)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        options ??= new ContextExportOptions();

        double charsPerToken = GetCharsPerTokenRatio(options.Model);
        string modelLabel = !string.IsNullOrWhiteSpace(options.CustomModelLabel)
            ? options.CustomModelLabel
            : GetDefaultModelLabel(options.Model);

        var list = items.ToList();
        var contributions = new List<SourceContribution>();
        int totalChars = 0;
        int totalTokens = 0;

        foreach (var item in list)
        {
            string content = item.Content ?? string.Empty;
            int charCount = content.Length;
            int wordCount = string.IsNullOrWhiteSpace(content)
                ? 0
                : WordSplitRegex.Split(content.Trim()).Length;

            int estTokens = charCount == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(charCount / charsPerToken));

            totalChars += charCount;
            totalTokens += estTokens;

            contributions.Add(new SourceContribution
            {
                SourceId = item.SourceId,
                TitleOrPath = item.TitleOrPath,
                CharacterCount = charCount,
                WordCount = wordCount,
                EstimatedTokens = estTokens
            });
        }

        // Calculate percentage of total for each source
        foreach (var contrib in contributions)
        {
            contrib.PercentageOfTotal = totalTokens > 0
                ? Math.Round((contrib.EstimatedTokens / (double)totalTokens) * 100.0, 2)
                : 0.0;
        }

        bool exceedsBudget = false;
        string? warningMessage = null;

        if (options.TokenBudget.HasValue && options.TokenBudget.Value > 0 && totalTokens > options.TokenBudget.Value)
        {
            exceedsBudget = true;
            int excess = totalTokens - options.TokenBudget.Value;
            warningMessage = $"Total estimated tokens ({totalTokens:N0}) exceeds target budget ({options.TokenBudget.Value:N0}) by {excess:N0} tokens.";
        }

        return new ContextTokenEstimateResult
        {
            ModelLabel = modelLabel,
            TotalEstimatedTokens = totalTokens,
            TotalCharacterCount = totalChars,
            TokenBudget = options.TokenBudget,
            ExceedsBudget = exceedsBudget,
            WarningMessage = warningMessage,
            SourceContributions = contributions
        };
    }

    /// <summary>
    /// Generates combined Markdown content preserving reviewed source pack order.
    /// </summary>
    public string GenerateCombinedMarkdown(IEnumerable<ContextExportItem> items, ContextExportOptions? options = null)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        options ??= new ContextExportOptions();

        var pieces = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.HeaderText))
        {
            pieces.Add(options.HeaderText.Trim());
        }

        foreach (var item in items)
        {
            string content = item.Content ?? string.Empty;
            pieces.Add(content);
        }

        return string.Join("\n\n", pieces).TrimEnd();
    }

    /// <summary>
    /// Safely exports combined context Markdown to a specified file path.
    /// Original documents remain untouched. Write operation uses safe atomic replacement.
    /// </summary>
    public ContextExportResult ExportToFile(IEnumerable<ContextExportItem> items, string destinationFilePath, ContextExportOptions? options = null)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (string.IsNullOrWhiteSpace(destinationFilePath)) throw new ArgumentException("Destination file path must be specified.", nameof(destinationFilePath));

        var list = items.ToList();
        options ??= new ContextExportOptions();

        string markdownContent = GenerateCombinedMarkdown(list, options);
        var estimate = EstimateTokens(list, options);

        string directory = Path.GetDirectoryName(destinationFilePath) ?? Environment.CurrentDirectory;
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Safe atomic write using temporary file
        string tempFilePath = Path.Combine(directory, $".{Path.GetFileName(destinationFilePath)}.tmp.{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(tempFilePath, markdownContent);
            if (File.Exists(destinationFilePath))
            {
                File.Delete(destinationFilePath);
            }
            File.Move(tempFilePath, destinationFilePath);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
        }

        return new ContextExportResult
        {
            MarkdownContent = markdownContent,
            SavedFilePath = destinationFilePath,
            TokenEstimate = estimate,
            SourceOrder = list.Select(i => i.SourceId).ToList()
        };
    }

    private static double GetCharsPerTokenRatio(TokenizerModel model) => model switch
    {
        TokenizerModel.Gpt4 => 3.75,
        TokenizerModel.Claude35 => 3.8,
        TokenizerModel.Llama3 => 3.6,
        TokenizerModel.Generic => 4.0,
        _ => 4.0
    };

    private static string GetDefaultModelLabel(TokenizerModel model) => model switch
    {
        TokenizerModel.Gpt4 => "GPT-4 (cl100k_base approx)",
        TokenizerModel.Claude35 => "Claude 3.5 Sonnet (approx)",
        TokenizerModel.Llama3 => "Llama 3 (approx)",
        TokenizerModel.Generic => "Generic Tokenizer (4 chars/token)",
        _ => "Generic Tokenizer"
    };
}
