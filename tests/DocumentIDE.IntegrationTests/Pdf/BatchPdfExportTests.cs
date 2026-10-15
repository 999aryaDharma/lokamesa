using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Jobs;
using DocumentIDE.Core.Pdf;
using Xunit;

namespace DocumentIDE.IntegrationTests.Pdf;

// PDF-02 — Batch Separate PDF Export (P1).
// Red stage: these tests FAIL before BatchPdfExportService exists.
// All Word COM interaction must flow through the WORD-03 queue
// abstraction (IWordAutomationQueue) via PDF-01's PdfExportService;
// tests prove serialization with concurrency-tracking fakes.
public sealed class BatchPdfExportTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "pdf02-" + Guid.NewGuid().ToString("N"));
    private readonly string _outDir;

    public BatchPdfExportTests()
    {
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
    // multiple exports remain Word-concurrency=1.
    [Fact]
    public async Task MultipleExports_RemainWordConcurrencyOne()
    {
        string[] sources = Enumerable.Range(1, 5)
            .Select(i => WriteDocx($"doc{i}.docx"))
            .ToArray();
        var harness = CreateHarness();

        var result = await harness.Batch.ExportBatchAsync(
            new BatchPdfExportRequest(sources.Select(s => new BatchPdfExportItem(s)).ToList(), _outDir));

        Assert.Equal(5, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        // Every document went through the shared WORD-03 queue exactly once.
        Assert.Equal(5, harness.Queue.EnqueueCalls);
        Assert.Equal(5, harness.Renderer.Calls);
        Assert.True(harness.Renderer.AllObservedInsideQueue);
        // Word renders never overlapped: max concurrent renderer executions is 1.
        Assert.Equal(1, harness.Renderer.MaxConcurrentExecutions);
        foreach (string source in sources)
        {
            string expected = Path.Combine(_outDir, Path.GetFileNameWithoutExtension(source) + ".pdf");
            Assert.True(File.Exists(expected), $"Expected batch output: {expected}");
        }
    }

    [Fact]
    public async Task Batch_PreservesInputOrder_AndReportsProgressWithCurrentFilename()
    {
        string[] sources = new[] { "cover.docx", "site-a.docx", "site-b.docx" }
            .Select(WriteDocx)
            .ToArray();
        var harness = CreateHarness();
        var seen = new List<BatchPdfExportProgress>();
        IProgress<BatchPdfExportProgress> progress = new CollectingProgress(seen);

        var result = await harness.Batch.ExportBatchAsync(
            new BatchPdfExportRequest(sources.Select(s => new BatchPdfExportItem(s)).ToList(), _outDir),
            progress);

        Assert.Equal(sources, result.Files.Select(f => f.SourceDocxPath).ToArray());
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(seen);
        // Progress is monotonic and the final report is complete.
        for (int i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i].CompletedCount >= seen[i - 1].CompletedCount);
        }

        var last = seen[^1];
        Assert.Equal(3, last.TotalCount);
        Assert.Equal(3, last.CompletedCount);
        // Every file was announced as current at some point.
        foreach (string source in sources)
        {
            Assert.Contains(seen, p => string.Equals(p.CurrentFileName, Path.GetFileName(source), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Cancellation_StopsBeforeNextFile_AndKeepsCompletedOutputs()
    {
        string[] sources = new[] { "one.docx", "two.docx", "three.docx" }
            .Select(WriteDocx)
            .ToArray();
        var harness = CreateHarness();
        using var cts = new CancellationTokenSource();
        IProgress<BatchPdfExportProgress> progress = new CancellingProgress(cts);

        var result = await harness.Batch.ExportBatchAsync(
            new BatchPdfExportRequest(sources.Select(s => new BatchPdfExportItem(s)).ToList(), _outDir),
            progress,
            cts.Token);

        // Exactly one file completed; the rest stopped before the next file.
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(0, result.FailedCount);
        Assert.True(result.CancelledCount >= 1);
        Assert.Equal(1, harness.Renderer.Calls);
        string firstOutput = Path.Combine(_outDir, "one.pdf");
        Assert.True(File.Exists(firstOutput), "Completed output must survive cancellation.");
        Assert.False(File.Exists(Path.Combine(_outDir, "two.pdf")));
        Assert.False(File.Exists(Path.Combine(_outDir, "three.pdf")));
    }

    [Fact]
    public async Task PartialFailures_AreVisible_AndCompletedOutputsSurvive()
    {
        string good1 = WriteDocx("good1.docx");
        string bad = WriteDocx("bad.docx");
        string good2 = WriteDocx("good2.docx");
        var harness = CreateHarness(failSources: new[] { bad });

        var result = await harness.Batch.ExportBatchAsync(
            new BatchPdfExportRequest(
                new List<BatchPdfExportItem>
                {
                    new(good1), new(bad), new(good2),
                },
                _outDir));

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.False(result.IsSuccess);
        var failed = result.Files.Single(f => f.SourceDocxPath == bad);
        Assert.Equal(PdfExportStatus.ExportFailed, failed.ExportResult.Status);
        Assert.False(string.IsNullOrWhiteSpace(failed.ExportResult.ErrorMessage));
        // Completed outputs are never deleted on an unrelated later failure.
        Assert.True(File.Exists(Path.Combine(_outDir, "good1.pdf")));
        Assert.True(File.Exists(Path.Combine(_outDir, "good2.pdf")));
        Assert.Contains("1 failed", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollisionFail_MarksSkipped_AndNeverOverwrites()
    {
        string source = WriteDocx("report.docx");
        byte[] original = Encoding.UTF8.GetBytes("%PDF-1.7 original-user-pdf");
        await File.WriteAllBytesAsync(Path.Combine(_outDir, "report.pdf"), original);
        var harness = CreateHarness();

        var result = await harness.Batch.ExportBatchAsync(
            new BatchPdfExportRequest(
                new List<BatchPdfExportItem> { new(source) },
                _outDir,
                PdfCollisionBehavior.Fail));

        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(_outDir, "report.pdf")));
        Assert.Contains("1 skipped", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollisionRename_AppliesPerFile_AndKeepsOriginals()
    {
        string a = WriteDocx("a.docx");
        string b = WriteDocx("b.docx");
        byte[] originalA = Encoding.UTF8.GetBytes("keep-a");
        await File.WriteAllBytesAsync(Path.Combine(_outDir, "a.pdf"), originalA);
        var harness = CreateHarness();

        var result = await harness.Batch.ExportBatchAsync(
            new BatchPdfExportRequest(
                new List<BatchPdfExportItem> { new(a), new(b) },
                _outDir,
                PdfCollisionBehavior.Rename));

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(Path.Combine(_outDir, "a (1).pdf"), result.Files[0].ExportResult.OutputPath);
        Assert.Equal(Path.Combine(_outDir, "b.pdf"), result.Files[1].ExportResult.OutputPath);
        Assert.Equal(originalA, await File.ReadAllBytesAsync(Path.Combine(_outDir, "a.pdf")));
    }

    [Fact]
    public async Task BatchJob_IntegratesWithJobRuntime_AndReportsProgress()
    {
        string[] sources = new[] { "j1.docx", "j2.docx" }.Select(WriteDocx).ToArray();
        var harness = CreateHarness();
        var completion = new TaskCompletionSource<BatchPdfExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IJob job = harness.Batch.CreateBatchJob(
            new BatchPdfExportRequest(sources.Select(s => new BatchPdfExportItem(s)).ToList(), _outDir),
            completion: completion);

        var messages = new List<string?>();
        var context = new JobContext(
            Guid.NewGuid().ToString("N"),
            JobTypes.PdfExport,
            "batch",
            null,
            CancellationToken.None,
            (percentage, message) => messages.Add(message));

        await job.ExecuteAsync(context);
        var result = await completion.Task;

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(2, harness.Renderer.Calls);
        Assert.NotEmpty(messages);
    }

    private string WriteDocx(string fileName)
    {
        string path = Path.Combine(_tempRoot, fileName);
        File.WriteAllBytes(path, Docx.DocxTestFixtureBuilder.CreateValidDocxPackage());
        return path;
    }

    private static TestHarness CreateHarness(string[]? failSources = null)
    {
        var queue = new RecordingWordAutomationQueue();
        var renderer = new FakeWordPdfRenderer(queue, failSources);
        var single = new PdfExportService(queue, renderer);
        var batch = new BatchPdfExportService(single);
        return new TestHarness(batch, queue, renderer);
    }

    private sealed record TestHarness(
        BatchPdfExportService Batch,
        RecordingWordAutomationQueue Queue,
        FakeWordPdfRenderer Renderer);

    private sealed class CollectingProgress : IProgress<BatchPdfExportProgress>
    {
        private readonly List<BatchPdfExportProgress> _seen;

        public CollectingProgress(List<BatchPdfExportProgress> seen)
        {
            _seen = seen;
        }

        public void Report(BatchPdfExportProgress value)
        {
            lock (_seen)
            {
                _seen.Add(value);
            }
        }
    }

    private sealed class CancellingProgress : IProgress<BatchPdfExportProgress>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingProgress(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public void Report(BatchPdfExportProgress value)
        {
            // Synchronous: cancellation lands before the service starts the next file.
            if (value.CompletedCount >= 1)
            {
                _cts.Cancel();
            }
        }
    }

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
        private readonly HashSet<string> _failSources;
        private int _current;
        private int _maxConcurrent;

        public FakeWordPdfRenderer(RecordingWordAutomationQueue queue, string[]? failSources = null)
        {
            _queue = queue;
            _failSources = failSources != null
                ? new HashSet<string>(failSources, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public int Calls;
        public bool AllObservedInsideQueue = true;
        public int MaxConcurrentExecutions => _maxConcurrent;

        public void ExportDocxToPdf(string sourceDocxPath, string outputPdfPath)
        {
            Interlocked.Increment(ref Calls);
            if (!_queue.IsDispatching)
            {
                AllObservedInsideQueue = false;
            }

            int now = Interlocked.Increment(ref _current);
            int observedMax = Volatile.Read(ref _maxConcurrent);
            while (now > observedMax)
            {
                int original = Interlocked.CompareExchange(ref _maxConcurrent, now, observedMax);
                if (original == observedMax)
                {
                    break;
                }

                observedMax = original;
            }

            try
            {
                // Simulate Word render latency so overlap would be observable.
                Thread.Sleep(10);
                if (_failSources.Contains(sourceDocxPath))
                {
                    throw new InvalidOperationException($"Word cannot render '{sourceDocxPath}'.");
                }

                File.WriteAllBytes(outputPdfPath, Encoding.UTF8.GetBytes("%PDF-1.7 fake-pdf-for-tests"));
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}
