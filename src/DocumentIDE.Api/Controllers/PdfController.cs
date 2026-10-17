using DocumentIDE.Core.Pdf;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>G8 — single DOCX → PDF export request from the owner UI.</summary>
public sealed record SinglePdfExportRequest(
    string SourceDocxPath,
    string? OutputDirectory = null,
    string? OutputFileName = null,
    string? CollisionBehavior = null);

/// <summary>G8 — ordered batch separate-PDF export request.</summary>
public sealed record BatchPdfExportRequestDto(
    IReadOnlyList<string> SourceDocxPaths,
    string OutputDirectory,
    string? CollisionBehavior = null);

/// <summary>G8 — explicit user-ordered merge request (DOCX or PDF inputs).</summary>
public sealed record PdfMergeRequestDto(
    IReadOnlyList<string> OrderedSourcePaths,
    string OutputPath,
    string? CollisionBehavior = null);

/// <summary>
/// G8 — privileged PDF finalization surface.
///
/// Wires the verified PDF-01/02/03 engines to the owner application. Word COM
/// remains reachable ONLY through the WORD-03 queue (concurrency 1) inside the
/// engines; this controller adds no COM path of its own, never silently
/// overwrites an existing PDF, and surfaces typed engine statuses verbatim.
/// </summary>
[ApiController]
[Route("api/pdf")]
public sealed class PdfController(
    PdfExportService single,
    BatchPdfExportService batch,
    PdfMergeService merge) : ControllerBase
{
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] SinglePdfExportRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SourceDocxPath))
            return BadRequest(new { errorCode = "SourceRequired" });

        var result = await single.ExportSingleAsync(
            new PdfExportRequest(request.SourceDocxPath, request.OutputDirectory, request.OutputFileName, ParseCollision(request.CollisionBehavior)),
            cancellationToken);
        return Ok(ProjectSingle(result));
    }

    [HttpPost("export-batch")]
    public async Task<IActionResult> ExportBatch([FromBody] BatchPdfExportRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OutputDirectory) || request.SourceDocxPaths == null || request.SourceDocxPaths.Count == 0)
            return BadRequest(new { errorCode = "OutputDirectoryAndSourcesRequired" });

        var items = request.SourceDocxPaths.Select(path => new BatchPdfExportItem(path)).ToArray();
        var result = await batch.ExportBatchAsync(
            new BatchPdfExportRequest(items, request.OutputDirectory, ParseCollision(request.CollisionBehavior)),
            cancellationToken: cancellationToken);
        return Ok(ProjectBatch(result));
    }

    [HttpPost("merge")]
    public async Task<IActionResult> MergeDocuments([FromBody] PdfMergeRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OutputPath) || request.OrderedSourcePaths == null || request.OrderedSourcePaths.Count == 0)
            return BadRequest(new { errorCode = "OutputPathAndOrderedSourcesRequired" });

        var result = await merge.MergeDocumentsOrderedAsync(
            new OrderedMergeRequest(request.OrderedSourcePaths, request.OutputPath, ParseCollision(request.CollisionBehavior)),
            cancellationToken);
        return Ok(ProjectMerge(result));
    }

    [HttpPost("merge-pdfs")]
    public async Task<IActionResult> MergePdfs([FromBody] PdfMergeRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.OutputPath) || request.OrderedSourcePaths == null || request.OrderedSourcePaths.Count == 0)
            return BadRequest(new { errorCode = "OutputPathAndOrderedSourcesRequired" });

        var result = await merge.MergeOrderedAsync(
            new OrderedMergeRequest(request.OrderedSourcePaths, request.OutputPath, ParseCollision(request.CollisionBehavior)),
            cancellationToken);
        return Ok(ProjectMerge(result));
    }

    // Engine enums are serialized as readable names (matching the
    // BatchPreviewController status convention) so the owner UI never depends on
    // numeric enum ordinals that could shift between releases.
    private static object ProjectSingle(PdfExportResult result) => new
    {
        status = result.Status.ToString(),
        result.OutputPath,
        result.SourceFingerprintBefore,
        result.SourceFingerprintAfter,
        result.ErrorMessage,
        result.IsSuccess,
    };

    private static object ProjectBatch(BatchPdfExportResult result) => new
    {
        result.TotalCount,
        result.SucceededCount,
        result.SkippedCount,
        result.FailedCount,
        result.CancelledCount,
        result.IsSuccess,
        result.Summary,
        files = result.Files.Select(file => new
        {
            file.SourceDocxPath,
            status = file.ExportResult.Status.ToString(),
            file.ExportResult.OutputPath,
            file.ExportResult.ErrorMessage,
        }).ToArray(),
    };

    private static object ProjectMerge(OrderedMergeResult result) => new
    {
        status = result.Status.ToString(),
        result.OutputPath,
        result.PageCount,
        result.OrderedSources,
        result.ErrorMessage,
        result.IsSuccess,
    };

    /// <summary>Explicit collision policy only; default is Fail (never silently overwrite).</summary>
    private static PdfCollisionBehavior ParseCollision(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "overwrite" => PdfCollisionBehavior.Overwrite,
        "rename" => PdfCollisionBehavior.Rename,
        _ => PdfCollisionBehavior.Fail,
    };
}
