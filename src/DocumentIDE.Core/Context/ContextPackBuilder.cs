using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Search;

namespace DocumentIDE.Core.Context;

/// <summary>
/// Typed outcome of building a context pack from an explicit ordered document list.
/// </summary>
public enum ContextPackStatus
{
    Ok,
    InvalidRequest,
    EmptySelection,
    UnknownDocument,
    NoTextAvailable,
    DocumentTooLarge,
    PackTooLarge,
    BudgetExceeded,
}

/// <summary>
/// One reviewed source in the pack. <see cref="TextSource"/> is the honest origin
/// token: a Markdown document contributes its real source, while a DOCX
/// contributes only text the DOCX parser already indexed.
/// </summary>
public sealed record ContextPackDocument(
    string DocumentId,
    string Title,
    string TextSource,
    string ContentLabel,
    string Content,
    int CharacterCount,
    int ByteCount);

/// <summary>
/// Result of <see cref="ContextPackBuilder.BuildAsync"/>. A failure never carries
/// a partial pack or fabricated content: <see cref="Documents"/> is empty and
/// <see cref="Markdown"/> is empty on every non-<see cref="ContextPackStatus.Ok"/>
/// outcome.
/// </summary>
public sealed record ContextPackResult(
    ContextPackStatus Status,
    IReadOnlyList<ContextPackDocument> Documents,
    string Markdown,
    ContextTokenEstimateResult? TokenEstimate,
    string? DocumentId = null,
    string? Detail = null)
{
    public bool IsSuccess => Status == ContextPackStatus.Ok;

    public static ContextPackResult Failure(
        ContextPackStatus status,
        string? documentId = null,
        string? detail = null,
        ContextTokenEstimateResult? estimate = null) =>
        new(status, Array.Empty<ContextPackDocument>(), string.Empty, estimate, documentId, detail);

    public static ContextPackResult Success(
        IReadOnlyList<ContextPackDocument> documents,
        string markdown,
        ContextTokenEstimateResult estimate) =>
        new(ContextPackStatus.Ok, documents, markdown, estimate);
}

/// <summary>
/// UI-05 A — builds a bounded context pack over an explicit ordered document list.
///
/// Source honesty (master spec §33 / CTX-03):
/// <list type="bullet">
/// <item>A Markdown document contributes its real source, read from the file the
/// document record points at (the same authority as
/// <c>GET /api/documents/{id}/markdown</c>).</item>
/// <item>A DOCX contributes only the indexed <c>SearchableBody</c> produced by
/// the DOCX parser, prefixed with a label that says it is extracted text rather
/// than the document's own bytes. No indexed text is a typed
/// <see cref="ContextPackStatus.NoTextAvailable"/>, never an empty item.</item>
/// <item>Reads are bounded per document and for the whole pack; an over-cap pack
/// is refused typed instead of truncated or silently dropped.</item>
/// <item>The token estimate is the existing <see cref="ContextExportService"/>
/// estimate over the same text; it is never synthesized here.</item>
/// </list>
///
/// The builder performs no writes: no source mutation, no export destination and
/// no recovery snapshot are involved.
/// </summary>
public sealed class ContextPackBuilder
{
    /// <summary>Per-document byte cap (128 KB).</summary>
    public const int MaxDocumentBytes = 128 * 1024;

    /// <summary>Whole-pack byte cap (512 KB).</summary>
    public const int MaxTotalBytes = 512 * 1024;

    /// <summary>Upper bound on the explicit selection, so one request stays bounded.</summary>
    public const int MaxDocuments = 100;

    internal const string MarkdownSourceToken = "markdown-source";
    internal const string ExtractedDocxTextToken = "extracted-docx-text";

    private readonly IDocumentRepository _documents;
    private readonly IIndexedTextReader _indexedText;
    private readonly ContextExportService _export;

    public ContextPackBuilder(
        IDocumentRepository documents,
        IIndexedTextReader indexedText,
        ContextExportService export)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _indexedText = indexedText ?? throw new ArgumentNullException(nameof(indexedText));
        _export = export ?? throw new ArgumentNullException(nameof(export));
    }

    public async Task<ContextPackResult> BuildAsync(
        IReadOnlyList<string>? documentIds,
        int? tokenBudget = null,
        CancellationToken cancellationToken = default)
    {
        if (documentIds == null || documentIds.Count == 0)
        {
            return ContextPackResult.Failure(ContextPackStatus.EmptySelection, detail: "no documents selected");
        }

        if (documentIds.Count > MaxDocuments)
        {
            return ContextPackResult.Failure(
                ContextPackStatus.InvalidRequest,
                detail: $"selection exceeds the {MaxDocuments}-document limit");
        }

        if (tokenBudget.HasValue && tokenBudget.Value < 0)
        {
            return ContextPackResult.Failure(ContextPackStatus.InvalidRequest, detail: "token budget cannot be negative");
        }

        var documents = new List<ContextPackDocument>(documentIds.Count);
        var exportItems = new List<ContextExportItem>(documentIds.Count);

        foreach (var documentId in documentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(documentId))
            {
                return ContextPackResult.Failure(ContextPackStatus.UnknownDocument, documentId: documentId);
            }

            var document = await _documents.GetByIdAsync(documentId);
            if (document == null)
            {
                return ContextPackResult.Failure(ContextPackStatus.UnknownDocument, documentId);
            }

            var read = await ReadDocumentTextAsync(document, cancellationToken);
            if (read.TooLarge)
            {
                return ContextPackResult.Failure(
                    ContextPackStatus.DocumentTooLarge,
                    document.Id,
                    $"document exceeds the {MaxDocumentBytes}-byte per-document cap");
            }

            if (string.IsNullOrWhiteSpace(read.Text))
            {
                return ContextPackResult.Failure(ContextPackStatus.NoTextAvailable, document.Id, read.Detail);
            }

            var title = string.IsNullOrWhiteSpace(document.Filename)
                ? Path.GetFileName(document.CurrentPath)
                : document.Filename;

            // The DOCX label is part of the emitted content and the copied
            // Markdown, so extracted text can never be mistaken for the file's
            // own bytes.
            var content = read.IsExtracted
                ? $"> Extracted DOCX text (from the document index, not the original .docx bytes): {title}\n\n{read.Text}"
                : read.Text!;

            var byteCount = Encoding.UTF8.GetByteCount(content);
            if (byteCount > MaxDocumentBytes)
            {
                return ContextPackResult.Failure(
                    ContextPackStatus.DocumentTooLarge,
                    document.Id,
                    $"document exceeds the {MaxDocumentBytes}-byte per-document cap");
            }

            documents.Add(new ContextPackDocument(
                document.Id,
                title,
                read.TextSource,
                read.ContentLabel,
                content,
                content.Length,
                byteCount));

            exportItems.Add(new ContextExportItem
            {
                SourceId = document.Id,
                TitleOrPath = title,
                Content = content,
            });
        }

        var markdown = _export.GenerateCombinedMarkdown(exportItems);
        if (Encoding.UTF8.GetByteCount(markdown) > MaxTotalBytes)
        {
            return ContextPackResult.Failure(
                ContextPackStatus.PackTooLarge,
                detail: $"pack exceeds the {MaxTotalBytes}-byte total cap");
        }

        var options = new ContextExportOptions { TokenBudget = tokenBudget is > 0 ? tokenBudget : null };
        var estimate = _export.EstimateTokens(exportItems, options);
        if (estimate.ExceedsBudget)
        {
            return ContextPackResult.Failure(
                ContextPackStatus.BudgetExceeded,
                detail: estimate.WarningMessage,
                estimate: estimate);
        }

        return ContextPackResult.Success(documents, markdown, estimate);
    }

    private async Task<TextRead> ReadDocumentTextAsync(Document document, CancellationToken cancellationToken)
    {
        if (IsDocx(document))
        {
            var indexedBody = await _indexedText.GetIndexedBodyAsync(document.Id, cancellationToken);
            if (string.IsNullOrWhiteSpace(indexedBody))
            {
                return TextRead.Unavailable("no-indexed-text");
            }

            return new TextRead(
                indexedBody,
                IsExtracted: true,
                TextSource: ExtractedDocxTextToken,
                ContentLabel: "Extracted DOCX text",
                TooLarge: false,
                Detail: null);
        }

        if (!IsMarkdown(document))
        {
            return TextRead.Unavailable("unsupported-format");
        }

        if (string.IsNullOrWhiteSpace(document.CurrentPath) || !File.Exists(document.CurrentPath))
        {
            return TextRead.Unavailable("document-unavailable");
        }

        try
        {
            var source = await ReadBoundedUtf8Async(document.CurrentPath, MaxDocumentBytes, cancellationToken);
            if (source == null)
            {
                return TextRead.OverCap();
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                return TextRead.Unavailable("empty-markdown-source");
            }

            return new TextRead(
                source,
                IsExtracted: false,
                TextSource: MarkdownSourceToken,
                ContentLabel: "Markdown source",
                TooLarge: false,
                Detail: null);
        }
        catch (UnauthorizedAccessException)
        {
            return TextRead.Unavailable("document-access-denied");
        }
        catch (IOException)
        {
            return TextRead.Unavailable("document-locked");
        }
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> UTF-8 bytes. Returns <c>null</c>
    /// when the file is larger, so a grown/replaced file cannot bypass the cap.
    /// </summary>
    private static async Task<string?> ReadBoundedUtf8Async(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 8192,
            useAsync: true);

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0) break;

            total += read;
            if (total > maxBytes) return null;

            buffer.Write(chunk, 0, read);
        }

        var bytes = buffer.ToArray();
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes, offset, bytes.Length - offset);
    }

    private static bool IsDocx(Document document)
    {
        var extension = string.IsNullOrWhiteSpace(document.Extension)
            ? Path.GetExtension(document.CurrentPath)
            : document.Extension;
        if (string.Equals(extension?.Trim(), ".docx", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(document.DocumentType, "docx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarkdown(Document document)
    {
        var extension = string.IsNullOrWhiteSpace(document.Extension)
            ? Path.GetExtension(document.CurrentPath)
            : document.Extension;
        var trimmed = extension?.Trim() ?? string.Empty;
        if (trimmed.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(document.DocumentType, "markdown", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TextRead(
        string? Text,
        bool IsExtracted,
        string TextSource,
        string ContentLabel,
        bool TooLarge,
        string? Detail)
    {
        public static TextRead Unavailable(string detail) =>
            new(null, false, string.Empty, string.Empty, false, detail);

        public static TextRead OverCap() =>
            new(null, false, string.Empty, string.Empty, true, "document-too-large");
    }
}
