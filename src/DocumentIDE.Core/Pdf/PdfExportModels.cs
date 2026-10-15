namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-01 — request for a single DOCX → PDF export.
/// <para>
/// Output selection: <see cref="OutputDirectory"/> defaults to the source
/// directory; <see cref="OutputFileName"/> defaults to the source stem with
/// a <c>.pdf</c> extension (e.g. <c>BA-NUSA-DUA.docx → BA-NUSA-DUA.pdf</c>).
/// </para>
/// </summary>
public sealed record PdfExportRequest(
    string SourceDocxPath,
    string? OutputDirectory = null,
    string? OutputFileName = null,
    PdfCollisionBehavior CollisionBehavior = PdfCollisionBehavior.Fail);

/// <summary>
/// PDF-01 — typed result of a single DOCX → PDF export, including the
/// source fingerprints taken before/after export (spec 09 §14: export
/// must not mutate the DOCX).
/// </summary>
public sealed record PdfExportResult(
    PdfExportStatus Status,
    string? OutputPath,
    string? SourceFingerprintBefore,
    string? SourceFingerprintAfter,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == PdfExportStatus.Succeeded;
}
