namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-01 — explicit output-collision policy for single DOCX PDF export.
/// Spec 09 §8: never silently overwrite.
/// </summary>
public enum PdfCollisionBehavior
{
    /// <summary>Refuse to write; return <see cref="PdfExportStatus.OutputCollision"/>.</summary>
    Fail = 0,

    /// <summary>Replace the existing PDF. Caller opted in explicitly.</summary>
    Overwrite = 1,

    /// <summary>Keep the existing PDF; write to a free <c>name (n).pdf</c> sibling.</summary>
    Rename = 2,
}
