using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Preview;
using Xunit;

namespace DocumentIDE.IntegrationTests.Preview;

/// <summary>
/// WORD-04 — High-Fidelity Quick Look.
///
/// RED STAGE: this suite must FAIL before QuickLookService exists.
/// Acceptance: lazy Word-rendered multi-page read-only preview, cache keyed by
/// DocumentId/fingerprint/renderer version, no eager render at startup, typed
/// Word failure outcomes, and a public viewer path that never touches Word.
/// </summary>
public sealed class QuickLookTests
{
    private sealed class FakeRenderer : IQuickLookRenderer
    {
        private readonly Func<QuickLookRenderCommand, QuickLookRenderOutput> _render;
        public int CallCount;
        public List<int> RenderedPages { get; } = new();

        public FakeRenderer(Func<QuickLookRenderCommand, QuickLookRenderOutput>? render = null)
        {
            _render = render ?? (cmd => new QuickLookRenderOutput(
                ArtifactId: $"artifact-{cmd.PageIndex}",
                TotalPages: 3,
                PageText: $"page {cmd.PageIndex} content alpha beta"));
        }

        public Task<QuickLookRenderOutput> RenderPageAsync(
            QuickLookRenderCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            lock (RenderedPages)
            {
                RenderedPages.Add(command.PageIndex);
            }
            return Task.FromResult(_render(command));
        }
    }

    private sealed class FakeEditingProbe : IQuickLookEditingProbe
    {
        private readonly QuickLookEditingInfo _info;
        public FakeEditingProbe(QuickLookEditingInfo? info = null)
        {
            _info = info ?? new QuickLookEditingInfo(false, "No Word editing signals detected.");
        }

        public QuickLookEditingInfo Check(string sourcePath) => _info;
    }

    private static string WriteTempSource(byte[]? bytes = null)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ql-{Guid.NewGuid():N}.docx");
        File.WriteAllBytes(path, bytes ?? new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03 });
        return path;
    }

    private static QuickLookRequest Request(string sourcePath, string fingerprint = "fp-1", int pageIndex = 0)
    {
        return new QuickLookRequest(
            DocumentId: "doc-ql-1",
            SourcePath: sourcePath,
            ContentFingerprint: fingerprint,
            PageIndex: pageIndex);
    }

    private static QuickLookService CreateService(
        FakeRenderer? renderer = null,
        IQuickLookCache? cache = null,
        IQuickLookEditingProbe? probe = null)
    {
        return new QuickLookService(
            renderer ?? new FakeRenderer(),
            cache ?? new InMemoryQuickLookCache(),
            probe ?? new FakeEditingProbe());
    }

    [Fact]
    public async Task PreviewNeverEditsSource()
    {
        string path = WriteTempSource();
        try
        {
            byte[] before = await File.ReadAllBytesAsync(path);
            var service = CreateService();

            QuickLookResult result = await service.GetPageAsync(Request(path));

            Assert.True(result.Success);
            byte[] after = await File.ReadAllBytesAsync(path);
            Assert.Equal(before, after);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConstructingService_PerformsNoEagerRender()
    {
        var renderer = new FakeRenderer();
        _ = CreateService(renderer);

        Assert.Equal(0, renderer.CallCount);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CacheHit_DoesNotReRender()
    {
        var renderer = new FakeRenderer();
        var service = CreateService(renderer);
        string path = WriteTempSource();
        try
        {
            QuickLookResult first = await service.GetPageAsync(Request(path));
            QuickLookResult second = await service.GetPageAsync(Request(path));

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.True(second.FromCache);
            Assert.Equal(1, renderer.CallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CacheKey_IncludesDocumentIdFingerprintAndRendererVersion()
    {
        var keyA = new QuickLookCacheKey("doc-a", "fp-1", QuickLookService.RendererVersion);
        var keyB = new QuickLookCacheKey("doc-b", "fp-1", QuickLookService.RendererVersion);
        var keyC = new QuickLookCacheKey("doc-a", "fp-2", QuickLookService.RendererVersion);

        Assert.NotEqual(keyA.ToKeyString(), keyB.ToKeyString());
        Assert.NotEqual(keyA.ToKeyString(), keyC.ToKeyString());
        Assert.Contains(QuickLookService.RendererVersion, keyA.ToKeyString());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CacheInvalidatesOnFingerprintChange()
    {
        var renderer = new FakeRenderer();
        var cache = new InMemoryQuickLookCache();
        var service = CreateService(renderer, cache);
        string path = WriteTempSource();
        try
        {
            QuickLookResult first = await service.GetPageAsync(Request(path, "fp-1"));
            Assert.True(first.Success);
            Assert.False(first.FromCache);

            QuickLookResult second = await service.GetPageAsync(Request(path, "fp-2"));
            Assert.True(second.Success);
            Assert.False(second.FromCache);
            Assert.Equal(2, renderer.CallCount);

            await service.InvalidateAsync("doc-ql-1");
            QuickLookResult third = await service.GetPageAsync(Request(path, "fp-2"));
            Assert.False(third.FromCache);
            Assert.Equal(3, renderer.CallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MultiPageRender_IsLazyPerPage()
    {
        var renderer = new FakeRenderer();
        var service = CreateService(renderer);
        string path = WriteTempSource();
        try
        {
            QuickLookResult page0 = await service.GetPageAsync(Request(path, "fp-1", 0));

            Assert.True(page0.Success);
            Assert.Equal(1, renderer.CallCount);
            Assert.DoesNotContain(1, renderer.RenderedPages);
            Assert.Equal(3, page0.TotalPages);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(WordRenderFailureKind.Unavailable, QuickLookErrorCode.WordUnavailable)]
    [InlineData(WordRenderFailureKind.Busy, QuickLookErrorCode.WordBusy)]
    [InlineData(WordRenderFailureKind.Modal, QuickLookErrorCode.WordModal)]
    [InlineData(WordRenderFailureKind.ComFailure, QuickLookErrorCode.ComFailure)]
    public async Task WordFailures_ProduceTypedOutcomes(
        WordRenderFailureKind kind, QuickLookErrorCode expected)
    {
        var renderer = new FakeRenderer(_ => throw new WordRenderException(kind, $"simulated {kind}"));
        var service = CreateService(renderer);
        string path = WriteTempSource();
        try
        {
            QuickLookResult result = await service.GetPageAsync(Request(path));

            Assert.False(result.Success);
            Assert.Equal(expected, result.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EditingInWord_IsSurfacedWithoutBlockingReadOnlyPreview()
    {
        var probe = new FakeEditingProbe(new QuickLookEditingInfo(
            true, "Likely editing in Word (2 of 3 signals positive)."));
        var service = CreateService(probe: probe);
        string path = WriteTempSource();
        try
        {
            QuickLookResult result = await service.GetPageAsync(Request(path));

            Assert.True(result.Success);
            Assert.True(result.EditingInWord);
            Assert.Contains("Word", result.EditingSummary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MissingFile_ProducesTypedOutcome()
    {
        var service = CreateService();
        string path = Path.Combine(Path.GetTempPath(), $"ql-missing-{Guid.NewGuid():N}.docx");

        QuickLookResult result = await service.GetPageAsync(Request(path));

        Assert.False(result.Success);
        Assert.Equal(QuickLookErrorCode.FileMissing, result.ErrorCode);
    }

    [Fact]
    public void SearchHighlight_FindsCaseInsensitiveMatches()
    {
        var matches = QuickLookService.FindMatches(
            "Alpha beta ALPHA gamma", new[] { "alpha" });

        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal("alpha", m.Term));
    }

    [Fact]
    public void SearchHighlight_WithoutPageText_ReturnsNoMatches()
    {
        var matches = QuickLookService.FindMatches(null, new[] { "alpha" });

        Assert.Empty(matches);
    }

    [Fact]
    public async Task ArtifactId_IsOpaqueAndNeverTheSourcePath()
    {
        var service = CreateService();
        string path = WriteTempSource();
        try
        {
            QuickLookResult result = await service.GetPageAsync(Request(path));

            Assert.True(result.Success);
            string artifactId = Assert.Single(result.Pages).ArtifactId;
            Assert.NotEqual(path, artifactId);
            Assert.DoesNotContain(path, artifactId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PublicViewer_ServesCacheOnly_AndNeverCallsRenderer()
    {
        var renderer = new FakeRenderer();
        var cache = new InMemoryQuickLookCache();
        var service = CreateService(renderer, cache);
        string path = WriteTempSource();
        try
        {
            QuickLookResult rendered = await service.GetPageAsync(Request(path));
            Assert.True(rendered.Success);
            int callsAfterPrivilegedRender = renderer.CallCount;

            var publicReader = new PublicQuickLookReader(cache);
            QuickLookResult hit = await publicReader.GetCachedPageAsync("doc-ql-1", "fp-1", 0, null);
            Assert.True(hit.Success);
            Assert.True(hit.FromCache);

            QuickLookResult miss = await publicReader.GetCachedPageAsync("doc-ql-1", "fp-changed", 0, null);
            Assert.False(miss.Success);
            Assert.Equal(QuickLookErrorCode.CacheMiss, miss.ErrorCode);

            Assert.Equal(callsAfterPrivilegedRender, renderer.CallCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PublicViewer_DoesNotReferenceWordAutomation()
    {
        Assembly core = typeof(QuickLookService).Assembly;
        Assert.DoesNotContain(
            core.GetReferencedAssemblies(),
            name => name.Name != null && name.Name.Contains("Infrastructure", StringComparison.Ordinal));

        var ctorParams = typeof(PublicQuickLookReader).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.FullName ?? string.Empty);
        Assert.DoesNotContain(ctorParams, t => t.Contains("Word", StringComparison.Ordinal));

        Assembly shareHost = Assembly.Load("DocumentIDE.ShareHost");
        Assert.DoesNotContain(
            shareHost.GetReferencedAssemblies(),
            name => string.Equals(name.Name, "DocumentIDE.Infrastructure", StringComparison.Ordinal));
    }
}
