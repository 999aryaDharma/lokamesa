using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Preview;
using DocumentIDE.Infrastructure.Word;
using Xunit;

namespace DocumentIDE.IntegrationTests.Word;

/// <summary>
/// WORD-04 — lazy Word-to-temp-artifact render behind the WORD-03 queue.
/// RED STAGE: fails before WordQueueQuickLookRenderer exists.
/// </summary>
public sealed class WordQueueQuickLookRendererTests
{
    private static WordQueueQuickLookRenderer CreateRenderer(
        WordAutomationQueue queue,
        Func<QuickLookRenderCommand, QuickLookRenderOutput>? export = null,
        Func<bool>? isWordAvailable = null)
    {
        return new WordQueueQuickLookRenderer(queue, export, isWordAvailable);
    }

    private static QuickLookRenderCommand Command() =>
        new(SourcePath: "doc.docx", TempDirectory: System.IO.Path.GetTempPath(), PageIndex: 0);

    [Fact]
    public void ConstructingRenderer_EnqueuesNothing()
    {
        using var queue = new WordAutomationQueue();
        _ = CreateRenderer(queue);

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task RenderPage_RunsOnDedicatedQueueWorker()
    {
        using var queue = new WordAutomationQueue();
        int exportThreadId = -1;
        var renderer = CreateRenderer(queue,
            export: cmd =>
            {
                exportThreadId = Thread.CurrentThread.ManagedThreadId;
                return new QuickLookRenderOutput("artifact-0", 2, "hello");
            },
            isWordAvailable: () => true);

        QuickLookRenderOutput output = await renderer.RenderPageAsync(Command());

        Assert.Equal("artifact-0", output.ArtifactId);
        Assert.Equal(queue.WorkerThreadId, exportThreadId);
        Assert.Equal(1, queue.MaxObservedConcurrency);
    }

    [Fact]
    public async Task WordUnavailable_ThrowsTypedWithoutEnqueue()
    {
        using var queue = new WordAutomationQueue();
        bool exportRan = false;
        var renderer = CreateRenderer(queue,
            export: _ =>
            {
                exportRan = true;
                return new QuickLookRenderOutput("x", 1, null);
            },
            isWordAvailable: () => false);

        var ex = await Assert.ThrowsAsync<WordRenderException>(
            () => renderer.RenderPageAsync(Command()));

        Assert.Equal(WordRenderFailureKind.Unavailable, ex.Kind);
        Assert.Contains("Microsoft Word is required", ex.Message);
        Assert.False(exportRan);
    }

    [Fact]
    public async Task ComFailure_ThrowsTyped()
    {
        using var queue = new WordAutomationQueue();
        var renderer = CreateRenderer(queue,
            export: _ => throw new COMException("RPC_E_CALL_REJECTED simulated"),
            isWordAvailable: () => true);

        var ex = await Assert.ThrowsAsync<WordRenderException>(
            () => renderer.RenderPageAsync(Command()));

        Assert.Equal(WordRenderFailureKind.ComFailure, ex.Kind);
    }

    [Theory]
    [InlineData("Word is busy processing another document", WordRenderFailureKind.Busy)]
    [InlineData("Word is showing a modal dialog", WordRenderFailureKind.Modal)]
    public async Task BusyAndModal_ThrowTyped(string message, WordRenderFailureKind expected)
    {
        using var queue = new WordAutomationQueue();
        var renderer = CreateRenderer(queue,
            export: _ => throw new InvalidOperationException(message),
            isWordAvailable: () => true);

        var ex = await Assert.ThrowsAsync<WordRenderException>(
            () => renderer.RenderPageAsync(Command()));

        Assert.Equal(expected, ex.Kind);
    }

    [Fact]
    public async Task QuickLookService_MapsAdapterFailures_ToTypedOutcomes()
    {
        using var queue = new WordAutomationQueue();
        var adapter = CreateRenderer(queue,
            export: _ => throw new InvalidOperationException("Word is busy processing another document"),
            isWordAvailable: () => true);
        var service = new QuickLookService(adapter, new InMemoryQuickLookCache());
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"ql-adapter-{Guid.NewGuid():N}.docx");
        await System.IO.File.WriteAllBytesAsync(path, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        try
        {
            QuickLookResult result = await service.GetPageAsync(
                new QuickLookRequest("doc-adapter", path, "fp-1", 0));

            Assert.False(result.Success);
            Assert.Equal(QuickLookErrorCode.WordBusy, result.ErrorCode);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }
}
