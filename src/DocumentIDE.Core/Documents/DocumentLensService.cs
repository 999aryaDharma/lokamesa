using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using DocumentIDE.Core.Docx;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Indexing;

namespace DocumentIDE.Core.Documents;

/// <summary>
/// Typed error codes for <see cref="DocumentLensService"/>. Lens failures are
/// always inspectable; the service never falls back silently to empty metadata.
/// </summary>
public enum DocumentLensErrorCode
{
    None = 0,
    FileNotFound,
    PackageValidationFailed,
    UnexpectedStructure,
    MetadataUnreadable
}

/// <summary>
/// A detected heading candidate. Heading detection is a derived view of the
/// OOXML paragraph styles / outline levels, not a source fact.
/// </summary>
public record DocumentLensHeading(int Level, string Text);

/// <summary>
/// Read-only DOCX "document lens" result: counts and summaries that let a user
/// understand a document without opening Word.
///
/// Fields named <c>*Summary</c> are derived values (paper/orientation/margins/
/// fonts) and must be presented as summaries, never as source facts.
/// </summary>
public class DocumentLensResult
{
    public required bool Success { get; init; }
    public DocumentLensErrorCode ErrorCode { get; init; } = DocumentLensErrorCode.None;
    public string? ErrorMessage { get; init; }
    public string? FilePath { get; init; }

    /// <summary>Word's last-saved page count (docProps/app.xml); null when absent.</summary>
    public int? PageCount { get; init; }
    public int? SectionCount { get; init; }
    public int? TableCount { get; init; }
    public int? ImageCount { get; init; }

    public string? PaperSummary { get; init; }
    public string? OrientationSummary { get; init; }
    public string? MarginSummary { get; init; }
    public string? PrimaryFontSummary { get; init; }
    public string? PrimaryFontSizeSummary { get; init; }

    public IReadOnlyList<DocumentLensHeading> Headings { get; init; } = Array.Empty<DocumentLensHeading>();
    public bool HeaderPresent { get; init; }
    public bool FooterPresent { get; init; }

    public static DocumentLensResult Failed(DocumentLensErrorCode errorCode, string message, string? filePath = null)
    {
        return new DocumentLensResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
            FilePath = filePath
        };
    }
}

/// <summary>
/// DOCX-04 — Document Lens Metadata.
///
/// Composes the pre-existing DOCX machinery: package preflight (IDX-01) plus the
/// IDX-02 <see cref="IDocxStructureParser"/> structural extraction, wrapped in a
/// typed, read-only result. This service never opens the package for update and
/// never mutates the source file.
/// </summary>
public class DocumentLensService
{
    private static readonly JsonSerializerOptions HeadingsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDocxStructureParser _parser;
    private readonly IDocxPackagePreflight? _preflight;

    public DocumentLensService(IDocxStructureParser parser, IDocxPackagePreflight? preflight = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _preflight = preflight;
    }

    /// <summary>
    /// Inspects a DOCX file and returns its document lens metadata, or a typed
    /// failure. Reads only: the source file is never opened for update.
    /// </summary>
    public DocumentLensResult Inspect(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return DocumentLensResult.Failed(
                DocumentLensErrorCode.FileNotFound,
                "A DOCX file path is required.");
        }

        if (!File.Exists(filePath))
        {
            return DocumentLensResult.Failed(
                DocumentLensErrorCode.FileNotFound,
                $"File not found at path: '{filePath}'.",
                filePath);
        }

        if (_preflight != null)
        {
            DocxPreflightResult preflightResult;
            try
            {
                preflightResult = _preflight.ValidatePackage(filePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return DocumentLensResult.Failed(
                    DocumentLensErrorCode.PackageValidationFailed,
                    $"DOCX package validation could not read the file: {ex.Message}",
                    filePath);
            }

            if (!preflightResult.IsValid)
            {
                return DocumentLensResult.Failed(
                    DocumentLensErrorCode.PackageValidationFailed,
                    $"DOCX package validation failed: {preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode}).",
                    filePath);
            }
        }

        DocxParseResult parseResult;
        try
        {
            parseResult = _parser.Parse(filePath);
        }
        catch (Exception ex)
        {
            // Deliberately broad: malformed or unexpected OOXML must surface as a
            // typed, inspectable result instead of crashing the caller.
            return DocumentLensResult.Failed(
                DocumentLensErrorCode.UnexpectedStructure,
                $"Unexpected DOCX structure or unreadable package: {ex.Message}",
                filePath);
        }

        IReadOnlyList<DocumentLensHeading> headings;
        try
        {
            headings = DeserializeHeadings(parseResult.Metadata.HeadingsJson);
        }
        catch (JsonException ex)
        {
            return DocumentLensResult.Failed(
                DocumentLensErrorCode.MetadataUnreadable,
                $"Derived heading metadata could not be read: {ex.Message}",
                filePath);
        }

        DocumentMetadata metadata = parseResult.Metadata;

        return new DocumentLensResult
        {
            Success = true,
            FilePath = filePath,
            PageCount = metadata.PageCount,
            SectionCount = metadata.SectionCount,
            TableCount = metadata.TableCount,
            ImageCount = metadata.ImageCount,
            PaperSummary = metadata.PaperSummary,
            OrientationSummary = metadata.OrientationSummary,
            MarginSummary = metadata.MarginSummary,
            PrimaryFontSummary = metadata.PrimaryFont,
            PrimaryFontSizeSummary = FormatFontSize(metadata.PrimaryFontSize),
            Headings = headings,
            HeaderPresent = metadata.HeaderPresent ?? false,
            FooterPresent = metadata.FooterPresent ?? false
        };
    }

    private static IReadOnlyList<DocumentLensHeading> DeserializeHeadings(string? headingsJson)
    {
        if (string.IsNullOrWhiteSpace(headingsJson))
        {
            return Array.Empty<DocumentLensHeading>();
        }

        var headings = JsonSerializer.Deserialize<List<DocumentLensHeading>>(headingsJson, HeadingsJsonOptions);
        return headings ?? (IReadOnlyList<DocumentLensHeading>)Array.Empty<DocumentLensHeading>();
    }

    private static string? FormatFontSize(double? points)
    {
        if (points == null) return null;
        return $"{points.Value.ToString("0.##", CultureInfo.InvariantCulture)} pt";
    }
}
