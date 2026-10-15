using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Pdf;
using Xunit;

namespace DocumentIDE.IntegrationTests.Pdf;

// PDF-01 — Single DOCX PDF Export (P0).
// Red stage: these tests FAIL before PdfExportService exists.
// All Word COM interaction must flow through the WORD-03 queue
// abstraction (IWordAutomationQueue); tests prove it with fakes.
public sealed class SinglePdfExportTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "pdf01-" + Guid.NewGuid().ToString("N"));

    public SinglePdfExportTests()
    {
        Directory.CreateDirectory(_tempRoot);
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

    // First behavior (sprint Red requirement): generated PDF exists.
    [Fact]
    public async Task GeneratedPdfExists()
    {
        string source = WriteDocx("report.docx");
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.NotNull(result.OutputPath);
        Assert.True(File.Exists(result.OutputPath), "Generated PDF must exist.");
        Assert.True(new FileInfo(result.OutputPath!).Length > 0, "Generated PDF must be non-empty.");
    }

    [Fact]
    public async Task DefaultOutputName_DerivesPdfNextToSource()
    {
        string source = WriteDocx("BA-NUSA-DUA.docx");
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.Equal(
            Path.Combine(_tempRoot, "BA-NUSA-DUA.pdf"),
            result.OutputPath);
    }

    [Fact]
    public async Task CustomOutputDirectoryAndName_AreRespected()
    {
        string source = WriteDocx("report.docx");
        string outDir = Path.Combine(_tempRoot, "out");
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source, OutputDirectory: outDir, OutputFileName: "final.pdf"));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(outDir, "final.pdf"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath!));
    }

    [Fact]
    public async Task DocxFingerprintUnchanged()
    {
        string source = WriteDocx("report.docx");
        string before = Sha256Of(source);
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.Equal(before, Sha256Of(source));
        Assert.Equal(before, result.SourceFingerprintBefore);
        Assert.Equal(before, result.SourceFingerprintAfter);
    }

    [Fact]
    public async Task ExistingPdf_IsNotSilentlyOverwritten()
    {
        string source = WriteDocx("report.docx");
        byte[] original = Encoding.UTF8.GetBytes("%PDF-1.7 original-user-pdf");
        string existing = Path.Combine(_tempRoot, "report.pdf");
        await File.WriteAllBytesAsync(existing, original);
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.OutputCollision, result.Status);
        Assert.Equal(original, await File.ReadAllBytesAsync(existing));
        Assert.Equal(0, harness.Renderer.Calls);
        Assert.Equal(0, harness.Queue.EnqueueCalls);
    }

    [Fact]
    public async Task CollisionOverwrite_ReplacesExisting_WhenExplicit()
    {
        string source = WriteDocx("report.docx");
        string existing = Path.Combine(_tempRoot, "report.pdf");
        await File.WriteAllBytesAsync(existing, Encoding.UTF8.GetBytes("old"));
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source, CollisionBehavior: PdfCollisionBehavior.Overwrite));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.NotEqual("old", await File.ReadAllTextAsync(existing));
    }

    [Fact]
    public async Task CollisionRename_CreatesSuffixedFile_AndKeepsOriginal()
    {
        string source = WriteDocx("report.docx");
        byte[] original = Encoding.UTF8.GetBytes("keep-me");
        string existing = Path.Combine(_tempRoot, "report.pdf");
        await File.WriteAllBytesAsync(existing, original);
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source, CollisionBehavior: PdfCollisionBehavior.Rename));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(_tempRoot, "report (1).pdf"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath!));
        Assert.Equal(original, await File.ReadAllBytesAsync(existing));
    }

    [Fact]
    public async Task WordCom_GoesOnlyThroughQueue()
    {
        string source = WriteDocx("report.docx");
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.Succeeded, result.Status);
        // The renderer (Word ExportAsFixedFormat seam) ran exactly once,
        // strictly inside a queue dispatch, via exactly one queue enqueue.
        Assert.Equal(1, harness.Renderer.Calls);
        Assert.True(harness.Renderer.ObservedInsideQueue, "Word COM call must execute inside the WORD-03 queue dispatch.");
        Assert.Equal(1, harness.Queue.EnqueueCalls);
    }

    [Fact]
    public async Task ExportFailure_ReturnsTypedExportFailed_AndCleansPartialOutput()
    {
        string source = WriteDocx("report.docx");
        var harness = CreateHarness();
        harness.Renderer.Throw = new InvalidOperationException("Word is busy");
        harness.Renderer.WritePartialBeforeThrow = true;

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.ExportFailed, result.Status);
        Assert.Contains("Word is busy", result.ErrorMessage);
        Assert.False(
            result.OutputPath != null && File.Exists(result.OutputPath),
            "Incomplete PDF artifact must be cleaned after failure.");
    }

    [Fact]
    public async Task DiskFull_ReturnsTypedDiskFull()
    {
        string source = WriteDocx("report.docx");
        var harness = CreateHarness();
        harness.Renderer.Throw = new IOException(
            "There is not enough space on the disk.", unchecked((int)0x80070070));

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.DiskFull, result.Status);
    }

    [Fact]
    public async Task MissingSource_ReturnsTypedSourceNotFound()
    {
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(Path.Combine(_tempRoot, "absent.docx")));

        Assert.Equal(PdfExportStatus.SourceNotFound, result.Status);
        Assert.Equal(0, harness.Renderer.Calls);
    }

    [Fact]
    public async Task NonDocxSource_ReturnsTypedSourceInvalid()
    {
        string source = Path.Combine(_tempRoot, "notes.txt");
        await File.WriteAllTextAsync(source, "plain text");
        var harness = CreateHarness();

        var result = await harness.Service.ExportSingleAsync(
            new PdfExportRequest(source));

        Assert.Equal(PdfExportStatus.SourceInvalid, result.Status);
        Assert.Equal(0, harness.Renderer.Calls);
    }

    private string WriteDocx(string fileName)
    {
        string path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, Docx.DocxTestFixtureBuilder.CreateValidDocxPackage());
        return path;
    }

    private static string Sha256Of(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static TestHarness CreateHarness()
    {
        var queue = new RecordingWordAutomationQueue();
        var renderer = new FakeWordPdfRenderer(queue);
        var service = new PdfExportService(queue, renderer);
        return new TestHarness(service, queue, renderer);
    }

    private sealed record TestHarness(
        PdfExportService Service,
        RecordingWordAutomationQueue Queue,
        FakeWordPdfRenderer Renderer);

    private sealed class RecordingWordAutomationQueue : IWordAutomationQueue
    {
        public int EnqueueCalls;
        public bool IsDispatching;

        public Task<T> EnqueueAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref EnqueueCalls);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            IsDispatching = true;
            try
            {
                return Task.FromResult(operation());
            }
            finally
            {
                IsDispatching = false;
            }
        }
    }

    private sealed class FakeWordPdfRenderer : IWordPdfRenderer
    {
        private readonly RecordingWordAutomationQueue _queue;

        public FakeWordPdfRenderer(RecordingWordAutomationQueue queue)
        {
            _queue = queue;
        }

        public int Calls;
        public bool ObservedInsideQueue;
        public Exception? Throw;
        public bool WritePartialBeforeThrow;

        public void ExportDocxToPdf(string sourceDocxPath, string outputPdfPath)
        {
            Calls++;
            ObservedInsideQueue = _queue.IsDispatching;
            if (Throw != null)
            {
                if (WritePartialBeforeThrow)
                {
                    File.WriteAllBytes(outputPdfPath, Encoding.UTF8.GetBytes("%PDF-1.7 partial"));
                }

                throw Throw;
            }

            File.WriteAllBytes(outputPdfPath, Encoding.UTF8.GetBytes("%PDF-1.7 fake-pdf-for-tests"));
        }
    }
}
