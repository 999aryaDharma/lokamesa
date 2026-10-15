namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-01 — typed outcome of a single DOCX PDF export.
/// Failures are inspectable statuses, never silent fallbacks.
/// </summary>
public enum PdfExportStatus
{
    Succeeded = 0,
    OutputCollision = 1,
    SourceNotFound = 2,
    SourceInvalid = 3,
    ExportFailed = 4,
    DiskFull = 5,
    Cancelled = 6,
}
