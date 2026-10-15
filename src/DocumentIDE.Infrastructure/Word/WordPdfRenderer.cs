using System;
using System.IO;
using System.Reflection;
using DocumentIDE.Core.Pdf;

namespace DocumentIDE.Infrastructure.Word;

/// <summary>
/// PDF-01 — production <see cref="IWordPdfRenderer"/> using Word Desktop
/// automation (<c>Document.ExportAsFixedFormat</c>, PDF format).
/// <para>
/// Late-bound COM (no Interop assembly) so the privileged app keeps a
/// narrow dependency graph. MUST only ever run on the WORD-03 dedicated
/// STA queue worker; <see cref="PdfExportService"/> enforces this by
/// routing every call through <see cref="IWordAutomationQueue"/>.
/// </para>
/// </summary>
public sealed class WordPdfRenderer : IWordPdfRenderer
{
    /// <summary>WdExportFormat.wdExportFormatPDF.</summary>
    private const int WdExportFormatPdf = 17;

    public void ExportDocxToPdf(string sourceDocxPath, string outputPdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Word PDF export requires Windows with Microsoft Word installed.");
        }

        if (!File.Exists(sourceDocxPath))
        {
            throw new FileNotFoundException("Source DOCX not found.", sourceDocxPath);
        }

        Type? wordType = Type.GetTypeFromProgID("Word.Application");
        if (wordType == null)
        {
            throw new InvalidOperationException("Microsoft Word is not installed; PDF export is unavailable.");
        }

        object? app = null;
        object? documents = null;
        object? document = null;
        try
        {
            app = Activator.CreateInstance(wordType)
                ?? throw new InvalidOperationException("Could not start Microsoft Word.");
            SetProperty(app, "Visible", false);
            SetProperty(app, "DisplayAlerts", 0);

            documents = GetProperty(app, "Documents")
                ?? throw new InvalidOperationException("Could not access Word Documents collection.");
            document = InvokeMethod(documents, "Open", sourceDocxPath)
                ?? throw new InvalidOperationException($"Word could not open '{sourceDocxPath}'.");
            InvokeMethod(document, "ExportAsFixedFormat", outputPdfPath, WdExportFormatPdf);
        }
        finally
        {
            if (document != null)
            {
                try
                {
                    InvokeMethod(document, "Close", false);
                }
                catch
                {
                    // Best effort: release must proceed so the STA worker survives.
                }
            }

            WordAutomationQueue.TryReleaseComObject(document);
            WordAutomationQueue.TryReleaseComObject(documents);

            if (app != null)
            {
                try
                {
                    InvokeMethod(app, "Quit");
                }
                catch
                {
                    // Best effort: release must proceed so the STA worker survives.
                }

                WordAutomationQueue.TryReleaseComObject(app);
            }
        }
    }

    private static object? GetProperty(object target, string name)
    {
        return target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null);
    }

    private static void SetProperty(object target, string name, object? value)
    {
        target.GetType().InvokeMember(
            name,
            BindingFlags.SetProperty,
            binder: null,
            target,
            args: [value]);
    }

    private static object? InvokeMethod(object target, string name, params object?[] args)
    {
        return target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args);
    }
}
