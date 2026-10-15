using System.Collections.Generic;
using System.Linq;

namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-02 — one input of a batch separate-PDF export.
/// <see cref="OutputFileName"/> defaults to the source stem with a
/// <c>.pdf</c> extension; sanitization and the <c>.pdf</c> suffix rules
/// are inherited from PDF-01's single export.
/// </summary>
public sealed record BatchPdfExportItem(
    string SourceDocxPath,
    string? OutputFileName = null);

/// <summary>
/// PDF-02 — ordered batch separate-PDF export request.
/// <para>
/// <see cref="OutputDirectory"/> is the user-chosen output folder (spec 09
/// §5); <see cref="CollisionBehavior"/> is the batch collision choice and
/// is applied per file, equivalent to "apply choice to remaining
/// collisions" (spec 09 §8).
/// </para>
/// </summary>
public sealed record BatchPdfExportRequest(
    IReadOnlyList<BatchPdfExportItem> Items,
    string OutputDirectory,
    PdfCollisionBehavior CollisionBehavior = PdfCollisionBehavior.Fail);

/// <summary>
/// PDF-02 — observable progress of a running batch export.
/// Reported before each file starts (announcing
/// <see cref="CurrentFileName"/>) and after each file completes.
/// </summary>
public sealed record BatchPdfExportProgress(
    int CompletedCount,
    int TotalCount,
    string? CurrentFileName)
{
    public double Percentage =>
        TotalCount <= 0 ? 100.0 : (CompletedCount * 100.0) / TotalCount;
}

/// <summary>
/// PDF-02 — typed per-file outcome of a batch export.
/// </summary>
public sealed record BatchPdfExportFileResult(
    string SourceDocxPath,
    PdfExportResult ExportResult);

/// <summary>
/// PDF-02 — aggregate result of a batch separate-PDF export.
/// <para>
/// <c>OutputCollision</c> outcomes count as <see cref="SkippedCount"/>
/// (spec 09 §8 Skip); source/Word/disk failures count as
/// <see cref="FailedCount"/>; files never started due to cancellation
/// count as <see cref="CancelledCount"/>. Completed outputs are kept:
/// a later unrelated failure never deletes them (spec 09 §13).
/// </para>
/// </summary>
public sealed record BatchPdfExportResult(
    IReadOnlyList<BatchPdfExportFileResult> Files)
{
    public int TotalCount => Files.Count;

    public int SucceededCount =>
        Files.Count(f => f.ExportResult.Status == PdfExportStatus.Succeeded);

    public int SkippedCount =>
        Files.Count(f => f.ExportResult.Status == PdfExportStatus.OutputCollision);

    public int FailedCount =>
        Files.Count(f =>
            f.ExportResult.Status is PdfExportStatus.SourceNotFound
                or PdfExportStatus.SourceInvalid
                or PdfExportStatus.ExportFailed
                or PdfExportStatus.DiskFull);

    public int CancelledCount =>
        Files.Count(f => f.ExportResult.Status == PdfExportStatus.Cancelled);

    public bool IsSuccess => FailedCount == 0 && CancelledCount == 0;

    /// <summary>Spec 09 §12 batch summary line.</summary>
    public string Summary
    {
        get
        {
            string summary =
                $"{SucceededCount} PDFs generated / {SkippedCount} skipped / {FailedCount} failed";
            return CancelledCount > 0
                ? $"{summary} / {CancelledCount} cancelled"
                : summary;
        }
    }
}
