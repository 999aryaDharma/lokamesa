using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-01 — single DOCX → PDF export orchestrator.
/// <para>
/// Flow: select DOCX → resolve output path/name → explicit collision
/// handling → fingerprint source → Word <c>ExportAsFixedFormat</c> via the
/// WORD-03 queue ONLY → verify output → fingerprint source again → typed
/// result. The DOCX is never mutated; the queue + renderer seams are the
/// sole route to Word COM.
/// </para>
/// </summary>
public sealed class PdfExportService
{
    /// <summary>HRESULT for Win32 ERROR_DISK_FULL (0x70).</summary>
    public const int DiskFullHResult = unchecked((int)0x80070070);

    private readonly IWordAutomationQueue _queue;
    private readonly IWordPdfRenderer _renderer;

    public PdfExportService(IWordAutomationQueue queue, IWordPdfRenderer renderer)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>
    /// Default output file name for a source (spec 09 §4):
    /// <c>BA-NUSA-DUA.docx → BA-NUSA-DUA.pdf</c>.
    /// </summary>
    public static string GetDefaultOutputFileName(string sourceDocxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocxPath);
        return Path.GetFileNameWithoutExtension(sourceDocxPath) + ".pdf";
    }

    /// <summary>SHA-256 fingerprint (uppercase hex) of a file's bytes.</summary>
    public static string ComputeFingerprint(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    public async Task<PdfExportResult> ExportSingleAsync(
        PdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SourceDocxPath) || !File.Exists(request.SourceDocxPath))
        {
            return new PdfExportResult(
                PdfExportStatus.SourceNotFound, null, null, null,
                $"Source DOCX not found: '{request.SourceDocxPath}'.");
        }

        string extension = Path.GetExtension(request.SourceDocxPath);
        if (!string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            return new PdfExportResult(
                PdfExportStatus.SourceInvalid, null, null, null,
                $"Only .docx sources can be exported to PDF (got '{extension}').");
        }

        string fullSource = Path.GetFullPath(request.SourceDocxPath);
        string fingerprintBefore;
        try
        {
            fingerprintBefore = ComputeFingerprint(fullSource);
        }
        catch (Exception ex)
        {
            return new PdfExportResult(
                PdfExportStatus.ExportFailed, null, null, null,
                $"Could not fingerprint source DOCX: {ex.Message}");
        }

        string outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? Path.GetDirectoryName(fullSource)!
            : request.OutputDirectory;
        string fileName = string.IsNullOrWhiteSpace(request.OutputFileName)
            ? GetDefaultOutputFileName(fullSource)
            : request.OutputFileName;
        fileName = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new PdfExportResult(
                PdfExportStatus.SourceInvalid, null, fingerprintBefore, fingerprintBefore,
                "Resolved output file name is empty after sanitization.");
        }

        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".pdf";
        }

        string targetPath;
        try
        {
            Directory.CreateDirectory(outputDirectory);
            targetPath = Path.GetFullPath(Path.Combine(outputDirectory, fileName));
        }
        catch (Exception ex)
        {
            return new PdfExportResult(
                PdfExportStatus.ExportFailed, null, fingerprintBefore, fingerprintBefore,
                $"Could not resolve output path: {ex.Message}");
        }

        bool existedBefore = File.Exists(targetPath);
        if (existedBefore)
        {
            switch (request.CollisionBehavior)
            {
                case PdfCollisionBehavior.Fail:
                    // Never silently overwrite (spec 09 §8).
                    return new PdfExportResult(
                        PdfExportStatus.OutputCollision, targetPath, fingerprintBefore, fingerprintBefore,
                        $"Output PDF already exists: '{targetPath}'. Choose Overwrite or Rename explicitly.");
                case PdfCollisionBehavior.Overwrite:
                    break;
                case PdfCollisionBehavior.Rename:
                    targetPath = FindFreeSiblingPath(outputDirectory, fileName);
                    existedBefore = false;
                    break;
                default:
                    return new PdfExportResult(
                        PdfExportStatus.ExportFailed, targetPath, fingerprintBefore, fingerprintBefore,
                        $"Unknown collision behavior: '{request.CollisionBehavior}'.");
            }
        }

        // Word COM ONLY via the WORD-03 queue. The renderer seam performs
        // ExportAsFixedFormat; this service never touches COM directly.
        try
        {
            string capturedSource = fullSource;
            string capturedTarget = targetPath;
            await _queue.EnqueueAsync(() =>
            {
                _renderer.ExportDocxToPdf(capturedSource, capturedTarget);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DeletePartialOutput(targetPath, existedBefore);
            return new PdfExportResult(
                PdfExportStatus.Cancelled, targetPath, fingerprintBefore, fingerprintBefore,
                "PDF export was cancelled before completion.");
        }
        catch (IOException ex) when (ex.HResult == DiskFullHResult)
        {
            DeletePartialOutput(targetPath, existedBefore);
            return new PdfExportResult(
                PdfExportStatus.DiskFull, targetPath, fingerprintBefore, fingerprintBefore,
                $"Disk full while writing PDF: {ex.Message}");
        }
        catch (Exception ex)
        {
            DeletePartialOutput(targetPath, existedBefore);
            return new PdfExportResult(
                PdfExportStatus.ExportFailed, targetPath, fingerprintBefore, fingerprintBefore,
                $"Word PDF export failed: {ex.Message}");
        }

        if (!File.Exists(targetPath))
        {
            return new PdfExportResult(
                PdfExportStatus.ExportFailed, targetPath, fingerprintBefore, fingerprintBefore,
                "Word export completed but no PDF was written.");
        }

        string fingerprintAfter;
        try
        {
            fingerprintAfter = ComputeFingerprint(fullSource);
        }
        catch (Exception ex)
        {
            return new PdfExportResult(
                PdfExportStatus.ExportFailed, targetPath, fingerprintBefore, null,
                $"Could not re-fingerprint source DOCX: {ex.Message}");
        }

        if (!string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal))
        {
            return new PdfExportResult(
                PdfExportStatus.ExportFailed, targetPath, fingerprintBefore, fingerprintAfter,
                "Source DOCX changed during export; PDF output is not trustworthy.");
        }

        return new PdfExportResult(
            PdfExportStatus.Succeeded, targetPath, fingerprintBefore, fingerprintAfter);
    }

    private static string FindFreeSiblingPath(string directory, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int counter = 1; counter < 10000; counter++)
        {
            string candidate = Path.GetFullPath(
                Path.Combine(directory, $"{stem} ({counter}){extension}"));
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a free output name for '{fileName}'.");
    }

    private static void DeletePartialOutput(string targetPath, bool existedBefore)
    {
        // Never delete a pre-existing user PDF; only clean artifacts this
        // call may have created.
        if (existedBefore)
        {
            return;
        }

        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
        catch
        {
            // Best effort: cleanup must not mask the typed failure outcome.
        }
    }

    /// <summary>
    /// Spec 09 §15: preserve the meaningful source name; replace invalid
    /// Windows filename characters only where necessary.
    /// </summary>
    internal static string SanitizeFileName(string fileName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(fileName.Length);
        foreach (char c in fileName.Trim())
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString().Trim();
    }
}
