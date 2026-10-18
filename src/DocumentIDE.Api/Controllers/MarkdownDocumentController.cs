using System.Text;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.History;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// MD-01/MD-02 — privileged Markdown source editing data source.
///
/// Serves the app shell contract:
///   GET /api/documents/{documentId}/markdown -> { documentId, source }
///   PUT /api/documents/{documentId}/markdown { source } -> { documentId, savedAt }
///
/// Document identity is the stable FS-02 document id, never a filesystem path,
/// so the editor never exposes an arbitrary local path. The <c>.md</c> file on
/// disk stays the source of truth and every save flows through the mandated
/// mutation pipeline: recovery snapshot first, then temp mutation, validation
/// and an atomic in-place replace. If the recovery snapshot cannot be created
/// the mutation is blocked (fail closed) — a save can never run unrecoverably.
/// </summary>
[ApiController]
[Route("api/documents/{documentId}")]
public sealed class MarkdownDocumentController(
    IDocumentRepository documents,
    RecoverySnapshotService snapshots,
    IAtomicFileReplacer replacer) : ControllerBase
{
    public sealed record SaveMarkdownRequest(string Source);

    private static readonly HashSet<string> MarkdownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

    [HttpGet("markdown")]
    public async Task<IActionResult> Get(string documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetByIdAsync(documentId);
        if (document == null) return NotFound(new { errorCode = "UnknownDocument" });
        if (!IsMarkdown(document)) return Conflict(new { errorCode = "NotMarkdownDocument" });
        if (string.IsNullOrWhiteSpace(document.CurrentPath) || !System.IO.File.Exists(document.CurrentPath))
        {
            return Conflict(new { errorCode = "DocumentUnavailable" });
        }

        var source = await System.IO.File.ReadAllTextAsync(document.CurrentPath, cancellationToken);
        return Ok(new { documentId = document.Id, source });
    }

    [HttpPut("markdown")]
    public async Task<IActionResult> Save(string documentId, SaveMarkdownRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.Source == null)
        {
            return BadRequest(new { errorCode = "SourceRequired" });
        }

        var document = await documents.GetByIdAsync(documentId);
        if (document == null) return NotFound(new { errorCode = "UnknownDocument" });
        if (!IsMarkdown(document)) return Conflict(new { errorCode = "NotMarkdownDocument" });
        if (string.IsNullOrWhiteSpace(document.CurrentPath) || !System.IO.File.Exists(document.CurrentPath))
        {
            return Conflict(new { errorCode = "DocumentUnavailable" });
        }

        // Non-negotiable: recovery snapshot precedes any source mutation, and if
        // it cannot be created the save is blocked.
        try
        {
            await snapshots.CreateSnapshotAsync(
                Guid.NewGuid().ToString("N"),
                new[] { new SnapshotSourceFile(document.Id, document.CurrentPath) });
        }
        catch (Exception ex)
        {
            return Conflict(new { errorCode = "RecoveryUnavailable", detail = ex.Message });
        }

        PreparedFileReplacement? prepared = null;
        try
        {
            prepared = await replacer.PrepareAsync(
                document.CurrentPath,
                (tempPath, token) => System.IO.File.WriteAllTextAsync(tempPath, request.Source, new UTF8Encoding(false), token),
                (tempPath, token) => ValidateRoundTripAsync(tempPath, request.Source, token),
                cancellationToken);
            await replacer.CommitAsync(prepared, cancellationToken);
        }
        catch (Exception ex)
        {
            if (prepared != null)
            {
                await replacer.RollbackAsync(prepared, cancellationToken);
            }
            return Conflict(new { errorCode = "SaveFailed", detail = ex.Message });
        }

        return Ok(new { documentId = document.Id, savedAt = DateTimeOffset.UtcNow.ToString("O") });
    }

    private static bool IsMarkdown(Core.Domain.Document document)
    {
        var extension = (document.Extension ?? Path.GetExtension(document.CurrentPath) ?? string.Empty).Trim();
        if (MarkdownExtensions.Contains(extension)) return true;
        return string.Equals(document.DocumentType, "markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ValidateRoundTripAsync(string tempPath, string source, CancellationToken cancellationToken)
    {
        var written = await System.IO.File.ReadAllTextAsync(tempPath, cancellationToken);
        if (!string.Equals(written, source, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Written markdown did not round-trip; aborting replace.");
        }
    }
}
