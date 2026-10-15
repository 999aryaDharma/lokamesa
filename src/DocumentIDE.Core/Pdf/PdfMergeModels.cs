using System.Collections.Generic;

namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-03 — explicit user-ordered merge request.
/// <para>
/// <see cref="OrderedSourcePaths"/> is the merge order exactly as arranged
/// by the user (spec 09 §6-§7 drag-reorder list). The service preserves
/// this sequence verbatim and never sorts, infers, or otherwise reorders
/// it: there is no silent inferred merge order. For
/// <c>MergeDocumentsOrderedAsync</c> the entries are DOCX paths (rendered
/// first via PDF-01/PDF-02); for <c>MergeOrderedAsync</c> they are PDF paths.
/// </para>
/// </summary>
public sealed record OrderedMergeRequest(
    IReadOnlyList<string> OrderedSourcePaths,
    string OutputPath,
    PdfCollisionBehavior CollisionBehavior = PdfCollisionBehavior.Fail);

/// <summary>
/// PDF-03 — typed result of an explicit-order merge.
/// <para>
/// <see cref="OrderedSources"/> echoes the request order so callers can
/// verify order preservation. <see cref="TempJobDirectory"/> reports the
/// per-job intermediate directory (spec 09 §10), already cleaned when the
/// call returns; it exists for diagnostics, not for reuse.
/// </para>
/// </summary>
public sealed record OrderedMergeResult(
    PdfExportStatus Status,
    string? OutputPath,
    int PageCount,
    IReadOnlyList<string> OrderedSources,
    string? ErrorMessage = null,
    string? TempJobDirectory = null)
{
    public bool IsSuccess => Status == PdfExportStatus.Succeeded;
}
