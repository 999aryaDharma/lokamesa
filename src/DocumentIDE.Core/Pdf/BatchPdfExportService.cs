using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Jobs;

namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-02 — ordered batch separate-PDF export orchestrator.
/// <para>
/// Each document is exported strictly in input order through PDF-01's
/// <see cref="PdfExportService.ExportSingleAsync"/>, which serializes Word
/// renders on the shared WORD-03 queue (concurrency exactly 1). No COM
/// threading is reimplemented here: this service awaits one single export
/// at a time and never overlaps queue submissions.
/// </para>
/// <para>
/// Cancellation applies between documents, never mid-COM-call: the token
/// is checked before each file starts (remaining files are recorded as
/// <see cref="PdfExportStatus.Cancelled"/>), and the same token flows
/// into the single export, whose queue seam (WORD-03) only cancels
/// between queued items. Completed user outputs are never deleted.
/// </para>
/// </summary>
public sealed class BatchPdfExportService
{
    /// <summary>FND-04 job type used by <see cref="CreateBatchJob"/>.</summary>
    public const string BatchJobType = JobTypes.PdfExport;

    private readonly PdfExportService _single;

    public BatchPdfExportService(PdfExportService single)
    {
        _single = single ?? throw new ArgumentNullException(nameof(single));
    }

    public async Task<BatchPdfExportResult> ExportBatchAsync(
        BatchPdfExportRequest request,
        IProgress<BatchPdfExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            throw new ArgumentException("Batch export requires an explicit output directory.", nameof(request));
        }

        var files = new List<BatchPdfExportFileResult>(request.Items.Count);
        int total = request.Items.Count;

        for (int index = 0; index < total; index++)
        {
            BatchPdfExportItem item = request.Items[index];
            string currentName = DisplayName(item);

            if (cancellationToken.IsCancellationRequested)
            {
                MarkRemainingCancelled(request, files, index);
                progress?.Report(new BatchPdfExportProgress(files.Count, total, currentName));
                break;
            }

            progress?.Report(new BatchPdfExportProgress(files.Count, total, currentName));

            PdfExportResult singleResult = await _single.ExportSingleAsync(
                new PdfExportRequest(
                    item.SourceDocxPath,
                    request.OutputDirectory,
                    item.OutputFileName,
                    request.CollisionBehavior),
                cancellationToken).ConfigureAwait(false);

            files.Add(new BatchPdfExportFileResult(item.SourceDocxPath, singleResult));
            progress?.Report(new BatchPdfExportProgress(files.Count, total, currentName));

            if (singleResult.Status == PdfExportStatus.Cancelled)
            {
                // Stops before the next queued document (spec 09 §13).
                MarkRemainingCancelled(request, files, index + 1);
                break;
            }
        }

        return new BatchPdfExportResult(files);
    }

    /// <summary>
    /// Wraps a batch export as an FND-04 background job (<c>PdfExport</c>
    /// job type). Progress is mirrored to the job context
    /// (<c>ReportProgress(percentage, currentFileName)</c>) and to the
    /// optional <paramref name="progress"/> observer; the final aggregate
    /// is delivered to <paramref name="completion"/> when provided.
    /// </summary>
    public IJob CreateBatchJob(
        BatchPdfExportRequest request,
        IProgress<BatchPdfExportProgress>? progress = null,
        TaskCompletionSource<BatchPdfExportResult>? completion = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new DelegateJob(async context =>
        {
            ArgumentNullException.ThrowIfNull(context);
            try
            {
                var fanout = new JobProgressFanout(context, progress);
                BatchPdfExportResult result = await ExportBatchAsync(
                    request,
                    fanout,
                    context.CancellationToken).ConfigureAwait(false);
                completion?.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion?.TrySetException(ex);
                throw;
            }
        });
    }

    private static void MarkRemainingCancelled(
        BatchPdfExportRequest request,
        List<BatchPdfExportFileResult> files,
        int fromIndex)
    {
        for (int i = fromIndex; i < request.Items.Count; i++)
        {
            files.Add(new BatchPdfExportFileResult(
                request.Items[i].SourceDocxPath,
                new PdfExportResult(
                    PdfExportStatus.Cancelled,
                    OutputPath: null,
                    SourceFingerprintBefore: null,
                    SourceFingerprintAfter: null,
                    ErrorMessage: "Batch export was cancelled before this file started.")));
        }
    }

    private static string DisplayName(BatchPdfExportItem item)
    {
        try
        {
            string name = Path.GetFileName(item.SourceDocxPath);
            return string.IsNullOrWhiteSpace(name) ? item.SourceDocxPath : name;
        }
        catch
        {
            return item.SourceDocxPath;
        }
    }

    private sealed class JobProgressFanout : IProgress<BatchPdfExportProgress>
    {
        private readonly JobContext _context;
        private readonly IProgress<BatchPdfExportProgress>? _observer;

        public JobProgressFanout(JobContext context, IProgress<BatchPdfExportProgress>? observer)
        {
            _context = context;
            _observer = observer;
        }

        public void Report(BatchPdfExportProgress value)
        {
            _context.ReportProgress(value.Percentage, value.CurrentFileName);
            _observer?.Report(value);
        }
    }
}
