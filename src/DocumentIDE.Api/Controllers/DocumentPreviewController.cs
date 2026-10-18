using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Indexing;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// UI-02 — read-only DOCX preview data source for the canvas file object.
///
/// Serves the app shell contract:
///   GET /api/documents/{documentId}/preview
///     -> 200 { documentId, kind: "docx", title, lines: [ … ] }
///     -> 404 { errorCode: "UnknownDocument" }
///     -> 409 { errorCode: "NotPreviewableDocument" }   // not a .docx
///     -> 409 { errorCode: "DocumentUnavailable" }      // file gone on disk
///
/// Document identity is the stable FS-02 document id, never a filesystem path,
/// so the route neither accepts nor returns an arbitrary local path. Lines are
/// the document's own words taken from the real DOCX parse (never synthesized
/// filler) and are bounded — at most <see cref="MaxLines"/> lines, each clipped
/// to <see cref="MaxLineLength"/> characters, with the whole JSON payload held
/// under <see cref="MaxPayloadBytes"/> — so a huge document cannot become an
/// unbounded dump (no zip-bomb amplification).
///
/// Strictly read-only: the file is opened for reading only by the parser; there
/// is no recovery snapshot, no temp file and no mutation pipeline here. This
/// route cannot change a byte on disk. Markdown keeps its own authority at
/// <c>GET /api/documents/{documentId}/markdown</c>; this route refuses it.
/// </summary>
[ApiController]
[Route("api/documents/{documentId}")]
public sealed class DocumentPreviewController(
    IDocumentRepository documents,
    IDocxStructureParser docxParser) : ControllerBase
{
    /// <summary>Maximum number of preview lines returned for one document.</summary>
    public const int MaxLines = 60;

    /// <summary>Maximum length of a single preview line, in characters.</summary>
    public const int MaxLineLength = 160;

    /// <summary>Maximum size of the whole JSON preview payload, in bytes (8 KB).</summary>
    public const int MaxPayloadBytes = 8 * 1024;

    // Matches the MVC System.Text.Json web defaults (camelCase, default encoder)
    // so the measured size is the size the host actually emits.
    private static readonly JsonSerializerOptions PayloadMeasureOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("preview")]
    public async Task<IActionResult> Get(string documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetByIdAsync(documentId);
        if (document == null) return NotFound(new { errorCode = "UnknownDocument" });
        if (!IsDocx(document)) return Conflict(new { errorCode = "NotPreviewableDocument" });
        if (string.IsNullOrWhiteSpace(document.CurrentPath) || !System.IO.File.Exists(document.CurrentPath))
        {
            return Conflict(new { errorCode = "DocumentUnavailable" });
        }

        DocxParseResult parse;
        try
        {
            parse = docxParser.Parse(document.CurrentPath);
        }
        catch (Exception)
        {
            // The file is present but not a readable DOCX package: it exists, so it
            // is not "unavailable", but it cannot yield a preview. Fail closed to a
            // typed conflict instead of an unhandled 500.
            return Conflict(new { errorCode = "NotPreviewableDocument" });
        }

        var title = string.IsNullOrWhiteSpace(document.Filename)
            ? Path.GetFileName(document.CurrentPath)
            : document.Filename;

        return Ok(BuildPayload(document.Id, title, CollectLines(parse)));
    }

    private static List<string> CollectLines(DocxParseResult parse)
    {
        // Primary source is the document's real body text. Headings add structure
        // and are only used when the body carries nothing (they already appear
        // inline in body order, so they are not duplicated).
        var lines = new List<string>();
        AppendBoundedLines(lines, parse.SearchableBody);
        if (lines.Count == 0) AppendBoundedLines(lines, parse.HeadingsText);
        return lines;
    }

    private static void AppendBoundedLines(List<string> lines, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            lines.Add(line.Length <= MaxLineLength ? line : line[..MaxLineLength]);
            if (lines.Count >= MaxLines) return;
        }
    }

    private static object BuildPayload(string documentId, string title, List<string> lines)
    {
        // The per-line caps alone do not guarantee the byte budget (60 × 160 chars
        // can exceed 8 KB), so keep dropping the trailing lines until the measured
        // payload fits. Dropping from the end preserves the page-1 reading order.
        while (lines.Count > 0 && MeasurePayload(documentId, title, lines) > MaxPayloadBytes)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new { documentId, kind = "docx", title, lines };
    }

    private static int MeasurePayload(string documentId, string title, List<string> lines)
    {
        var json = JsonSerializer.Serialize(new { documentId, kind = "docx", title, lines }, PayloadMeasureOptions);
        return Encoding.UTF8.GetByteCount(json);
    }

    private static bool IsDocx(Core.Domain.Document document)
    {
        var extension = document.Extension;
        if (string.IsNullOrWhiteSpace(extension)) extension = Path.GetExtension(document.CurrentPath);
        if (string.Equals(extension?.Trim(), ".docx", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(document.DocumentType, "docx", StringComparison.OrdinalIgnoreCase);
    }
}
