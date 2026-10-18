using DocumentIDE.Infrastructure.Markdown;

namespace DocumentIDE.MarkdownPreview.Tests;

/// <summary>
/// REL-04 — positive controls for <see cref="MarkdownPreviewService.RenderAndSanitize"/>.
/// These guard against over-sanitization: legitimate Markdown must still render
/// (an over-strict sanitizer silently destroying content is a failure, not a fix).
/// </summary>
public sealed class MarkdownPreviewServiceRenderingTests
{
    private static readonly MarkdownPreviewService Service = new();

    [Fact]
    public void HeadingsEmphasisListsAndMarkdownLinks_ArePreserved()
    {
        string markdown =
            "## Heading\n\n" +
            "Some *emphasis* and **strong** text.\n\n" +
            "- one\n- two\n\n" +
            "[Docs](https://example.com/docs)\n";

        string html = Service.RenderAndSanitize(markdown);

        Assert.Contains("<h2>Heading</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<em>emphasis</em>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>strong</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<li>one</li>", html, StringComparison.Ordinal);
        Assert.Contains("<li>two</li>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://example.com/docs\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<a href=\"https://example.com\">ok</a>", "https://example.com")]
    [InlineData("<a href=\"http://example.com\">ok</a>", "http://example.com")]
    [InlineData("[docs](https://example.com/docs)", "https://example.com/docs")]
    public void HttpAndHttpsUrls_ArePreserved(string markdown, string expectedUrl)
    {
        string html = Service.RenderAndSanitize(markdown);

        Assert.Contains(expectedUrl, html, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownImageWithHttpsSource_IsPreserved()
    {
        string html = Service.RenderAndSanitize("![alt](https://example.com/i.png)");

        Assert.Contains("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.com/i.png", html, StringComparison.Ordinal);
    }
}
