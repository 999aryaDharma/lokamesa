using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Pdf;
using Xunit;

namespace DocumentIDE.IntegrationTests.Pdf;

// PDF-03 — Streaming Ordered PDF Merge (P1 performance).
// Red stage: these tests FAIL before PdfMergeService exists (CS0246).
// Merge inputs are minimal valid single-page PDFs so order/streaming/
// no-rasterization properties can be asserted without Word installed.
// DOCX rendering still flows through PDF-01/PDF-02 services (fake Word
// renderer writes minimal PDFs keyed by source name in these tests).
public sealed class PdfMergeMemoryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "pdf03-" + Guid.NewGuid().ToString("N"));
    private readonly string _outDir;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PdfMergeMemoryTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_tempRoot);
        _outDir = Path.Combine(_tempRoot, "out");
        Directory.CreateDirectory(_outDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    // First behavior (sprint Red requirement):
    // output page order matches explicit user order, never an inferred order.
    [Fact]
    public async Task Merge_OutputPageOrder_MatchesExplicitUserOrder()
    {
        string alpha = WriteMinimalPdf("alpha.pdf", "MERGE-ORDER-ALPHA-001");
        string beta = WriteMinimalPdf("beta.pdf", "MERGE-ORDER-BETA-002");
        string gamma = WriteMinimalPdf("gamma.pdf", "MERGE-ORDER-GAMMA-003");
        var service = new PdfMergeService();
        string output = Path.Combine(_outDir, "merged.pdf");

        // User drags gamma first, then alpha, then beta: output must follow.
        var result = await service.MergeOrderedAsync(
            new OrderedMergeRequest(
                new List<string> { gamma, alpha, beta },
                output,
                PdfCollisionBehavior.Fail));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.Equal(3, result.PageCount);
        string merged = await File.ReadAllTextAsync(output, Encoding.ASCII);
        int gammaAt = merged.IndexOf("MERGE-ORDER-GAMMA-003", StringComparison.Ordinal);
        int alphaAt = merged.IndexOf("MERGE-ORDER-ALPHA-001", StringComparison.Ordinal);
        int betaAt = merged.IndexOf("MERGE-ORDER-BETA-002", StringComparison.Ordinal);
        Assert.True(gammaAt >= 0 && alphaAt >= 0 && betaAt >= 0, "All page markers must survive the merge.");
        Assert.True(gammaAt < alphaAt && alphaAt < betaAt, "Page order must match the explicit user order.");
    }

    [Fact]
    public async Task Merge_PreservesVectorContent_WithoutRasterization()
    {
        string first = WriteMinimalPdf("first.pdf", "VECTOR-MARKER-ONE", pageWidth: 595, pageHeight: 842);
        string second = WriteMinimalPdf("second.pdf", "VECTOR-MARKER-TWO", pageWidth: 612, pageHeight: 792);
        var service = new PdfMergeService();
        string output = Path.Combine(_outDir, "vector.pdf");

        var result = await service.MergeOrderedAsync(
            new OrderedMergeRequest(
                new List<string> { first, second },
                output,
                PdfCollisionBehavior.Fail));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        string merged = await File.ReadAllTextAsync(output, Encoding.ASCII);
        // Text operators survive: pages were appended, not rasterized to images.
        Assert.Contains("VECTOR-MARKER-ONE", merged, StringComparison.Ordinal);
        Assert.Contains("VECTOR-MARKER-TWO", merged, StringComparison.Ordinal);
        Assert.Contains("Tj", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Image", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("/DCTDecode", merged, StringComparison.Ordinal);
        // Source page dimensions are preserved per page.
        Assert.Contains("595 842", merged, StringComparison.Ordinal);
        Assert.Contains("612 792", merged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes()
    {
        const int fileCount = 6;
        const int payloadBytes = 4 * 1024 * 1024;
        var inputs = new List<string>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            inputs.Add(WriteMinimalPdf($"big-{i:00}.pdf", $"BIG-PAYLOAD-MARKER-{i:00}", paddingBytes: payloadBytes));
        }
        long totalSourceBytes = inputs.Sum(p => new FileInfo(p).Length);
        var service = new PdfMergeService();
        string output = Path.Combine(_outDir, "big-merged.pdf");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long beforeBytes = GC.GetTotalMemory(forceFullCollection: false);
        OrderedMergeResult? mergeResult = null;
        long peakAdditional = await service.MeasurePeakAdditionalBytesAsync(
            async () =>
            {
                mergeResult = await service.MergeOrderedAsync(
                    new OrderedMergeRequest(inputs, output, PdfCollisionBehavior.Fail));
            });
        long afterBytes = GC.GetTotalMemory(forceFullCollection: false);
        _ = beforeBytes;
        _ = afterBytes;

        // Streaming bound: peak additional managed memory must stay well
        // below the total corpus size (sequential append with bounded
        // buffers, never the whole corpus in RAM).
        Assert.True(
            peakAdditional < totalSourceBytes / 2,
            $"Peak additional bytes {peakAdditional:N0} must be < half of total source bytes {totalSourceBytes:N0}.");
        Assert.NotNull(mergeResult);
        Assert.Equal(PdfExportStatus.Succeeded, mergeResult.Status);
        Assert.Equal(fileCount, mergeResult.PageCount);
        _output.WriteLine(
            $"PDF-03 peak-memory: additional={peakAdditional:N0} bytes, " +
            $"totalSources={totalSourceBytes:N0} bytes, " +
            $"ratio={(double)peakAdditional / totalSourceBytes:P1}.");
        string merged = await File.ReadAllTextAsync(output, Encoding.ASCII);
        for (int i = 0; i < fileCount; i++)
        {
            Assert.Contains($"BIG-PAYLOAD-MARKER-{i:00}", merged, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Merge_RequiresExplicitOrder_EmptyRequestRejected()
    {
        var service = new PdfMergeService();
        string output = Path.Combine(_outDir, "empty.pdf");

        await Assert.ThrowsAsync<ArgumentException>(() => service.MergeOrderedAsync(
            new OrderedMergeRequest(new List<string>(), output, PdfCollisionBehavior.Fail)));
    }

    [Fact]
    public async Task MergeDocuments_CleansTempJobDir_OnSuccess()
    {
        var harness = CreateRenderHarness();
        var service = new PdfMergeService(harness.Single);
        string output = Path.Combine(_outDir, "docs-merged.pdf");

        var result = await service.MergeDocumentsOrderedAsync(
            new OrderedMergeRequest(
                new List<string> { harness.WriteDocx("cover.docx"), harness.WriteDocx("body.docx") },
                output,
                PdfCollisionBehavior.Fail));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.NotNull(result.TempJobDirectory);
        Assert.False(Directory.Exists(result.TempJobDirectory), "Temp job dir must be cleaned after success.");
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task MergeDocuments_CleansTempJobDir_OnRenderFailure()
    {
        string good = Path.Combine(_tempRoot, "good.docx");
        string bad = Path.Combine(_tempRoot, "bad.docx");
        File.WriteAllBytes(good, Docx.DocxTestFixtureBuilder.CreateValidDocxPackage());
        File.WriteAllBytes(bad, Docx.DocxTestFixtureBuilder.CreateValidDocxPackage());
        var harness = CreateRenderHarness(failSources: new[] { bad });
        var service = new PdfMergeService(harness.Single);
        string output = Path.Combine(_outDir, "failed-merge.pdf");

        var result = await service.MergeDocumentsOrderedAsync(
            new OrderedMergeRequest(
                new List<string> { good, bad },
                output,
                PdfCollisionBehavior.Fail));

        Assert.Equal(PdfExportStatus.ExportFailed, result.Status);
        Assert.NotNull(result.TempJobDirectory);
        Assert.False(Directory.Exists(result.TempJobDirectory), "Temp job dir must be cleaned after failure.");
        Assert.False(File.Exists(output), "No merged output may remain after a failed merge.");
    }

    private string WriteMinimalPdf(string fileName, string marker, int pageWidth = 595, int pageHeight = 842, int paddingBytes = 0)
    {
        string path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, MinimalPdfBuilder.CreateSinglePage(marker, pageWidth, pageHeight, paddingBytes));
        return path;
    }

    private RenderHarness CreateRenderHarness(string[]? failSources = null)
    {
        var queue = new RecordingWordAutomationQueue();
        var renderer = new MarkerWordPdfRenderer(queue, failSources);
        var single = new PdfExportService(queue, renderer);
        return new RenderHarness(single, queue, renderer, _tempRoot);
    }

    private sealed class RenderHarness
    {
        private readonly string _root;

        public RenderHarness(
            PdfExportService single,
            RecordingWordAutomationQueue queue,
            MarkerWordPdfRenderer renderer,
            string root)
        {
            Single = single;
            Queue = queue;
            Renderer = renderer;
            _root = root;
        }

        public PdfExportService Single { get; }

        public RecordingWordAutomationQueue Queue { get; }

        public MarkerWordPdfRenderer Renderer { get; }

        public string WriteDocx(string fileName)
        {
            string path = Path.Combine(_root, fileName);
            File.WriteAllBytes(path, Docx.DocxTestFixtureBuilder.CreateValidDocxPackage());
            return path;
        }
    }

    private sealed class RecordingWordAutomationQueue : IWordAutomationQueue
    {
        public Task<T> EnqueueAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            return Task.FromResult(operation());
        }
    }

    private sealed class MarkerWordPdfRenderer : IWordPdfRenderer
    {
        private readonly RecordingWordAutomationQueue _queue;
        private readonly HashSet<string> _failSources;

        public MarkerWordPdfRenderer(RecordingWordAutomationQueue queue, string[]? failSources = null)
        {
            _queue = queue;
            _failSources = failSources != null
                ? new HashSet<string>(failSources, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void ExportDocxToPdf(string sourceDocxPath, string outputPdfPath)
        {
            if (_failSources.Contains(sourceDocxPath))
            {
                throw new InvalidOperationException($"Word cannot render '{sourceDocxPath}'.");
            }

            string marker = "DOCX-RENDER-" + Path.GetFileNameWithoutExtension(sourceDocxPath).ToUpperInvariant();
            File.WriteAllBytes(outputPdfPath, MinimalPdfBuilder.CreateSinglePage(marker, 595, 842, 0));
        }
    }

    /// <summary>
    /// Builds a minimal valid single-page PDF with a literal-text marker in
    /// an uncompressed content stream, plus optional zero-padding comment
    /// lines to reach large corpus sizes for the memory test.
    /// </summary>
    private static class MinimalPdfBuilder
    {
        public static byte[] CreateSinglePage(string marker, int pageWidth, int pageHeight, int paddingBytes)
        {
            string content = $"BT /F1 12 Tf 72 720 Td ({marker}) Tj ET\n";
            byte[] contentBytes = Encoding.ASCII.GetBytes(content);
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                $"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
                $"<< /Length {contentBytes.Length} >>\nstream\n{content}endstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            };

            var sb = new StringBuilder();
            sb.Append("%PDF-1.7\n");
            var offsets = new List<long>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(sb.Length);
                sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            if (paddingBytes > 0)
            {
                // Padding lives in comments: never parsed as objects.
                int emitted = 0;
                while (emitted < paddingBytes)
                {
                    int chunk = Math.Min(1024, paddingBytes - emitted);
                    sb.Append('%');
                    sb.Append('0', chunk - 1);
                    sb.Append('\n');
                    emitted += chunk;
                }
            }
            long xrefAt = sb.Length;
            sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (long offset in offsets)
            {
                sb.Append($"{offset:0000000000} 00000 n \n");
            }
            sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefAt}\n%%EOF");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }
    }
}
