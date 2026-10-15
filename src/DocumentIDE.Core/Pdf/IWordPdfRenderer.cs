namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-01 — seam for the raw Word <c>ExportAsFixedFormat</c> COM call.
/// Implementations perform Word automation and nothing else; callers must
/// invoke this ONLY inside <see cref="IWordAutomationQueue.EnqueueAsync{T}"/>
/// so Word COM stays serialized on the dedicated STA worker (WORD-03).
/// </summary>
public interface IWordPdfRenderer
{
    /// <summary>
    /// Renders <paramref name="sourceDocxPath"/> to PDF at
    /// <paramref name="outputPdfPath"/> using Word. Must run on the
    /// WORD-03 queue worker thread. Throws on Word/COM failure.
    /// </summary>
    void ExportDocxToPdf(string sourceDocxPath, string outputPdfPath);
}
