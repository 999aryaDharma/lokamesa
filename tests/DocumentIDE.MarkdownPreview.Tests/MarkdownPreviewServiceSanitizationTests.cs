using DocumentIDE.Infrastructure.Markdown;

namespace DocumentIDE.MarkdownPreview.Tests;

/// <summary>
/// REL-04 — direct security tests for the REAL sanitizer behind frozen/live share
/// rendering (<see cref="MarkdownPreviewService.RenderAndSanitize"/>, MD-02).
///
/// Markdig's default pipeline passes raw HTML through, so the HtmlSanitizer step
/// is the control under test here — not the renderer and not a fake. Every vector
/// below is a malicious payload that must not survive sanitization.
/// </summary>
public sealed class MarkdownPreviewServiceSanitizationTests
{
    private static readonly MarkdownPreviewService Service = new();

    [Fact]
    public void ScriptTag_IsRemoved_AndItsBodyIsDropped()
    {
        string html = Service.RenderAndSanitize("Safe **bold**\n\n<script>alert('pwned')</script>\n\nTailed");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert('pwned')", html);
        // The surrounding legitimate markdown must still render.
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<div onclick=\"alert('click')\">c</div>", "onclick")]
    [InlineData("<img src=\"x\" onerror=\"alert('err')\">", "onerror")]
    [InlineData("<body onload=\"alert('load')\">", "onload")]
    [InlineData("hello <img src=\"x\" onerror=\"alert('inline')\"> world", "onerror")]
    public void EventHandlerAttribute_IsRemoved(string markdown, string attribute)
    {
        string html = Service.RenderAndSanitize(markdown);

        Assert.DoesNotContain(attribute, html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", html);
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert('js')\">click</a>")]
    [InlineData("<img src=\"javascript:alert('jsimg')\">")]
    [InlineData("<a href=\"JaVaScRiPt:alert('mixed')\">x</a>")]
    [InlineData("<a href=\"java&#115;cript:alert('entity')\">x</a>")]
    public void JavascriptUrl_InHrefOrSrc_IsNeutralized(string markdown)
    {
        string html = Service.RenderAndSanitize(markdown);

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", html);
    }

    [Fact]
    public void Iframe_IsRemoved()
    {
        string html = Service.RenderAndSanitize("<iframe src=\"https://evil.example\"></iframe>");

        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", html);
    }

    [Fact]
    public void MetaRefresh_IsRemoved()
    {
        string html = Service.RenderAndSanitize(
            "<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\">");

        Assert.DoesNotContain("<meta", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http-equiv", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", html);
    }

    /// <summary>
    /// A surviving <c>&lt;form action="https://evil.example"&gt;</c> is a phishing /
    /// credential-exfiltration vector on the public-share render path, so the form
    /// element (and the submission target) must not survive sanitization.
    /// </summary>
    [Fact]
    public void Form_AndItsSubmissionTarget_AreRemoved()
    {
        string html = Service.RenderAndSanitize(
            "<form action=\"https://evil.example\"><input name=\"x\"></form>");

        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.example", html);
    }

    [Fact]
    public void RawHtmlEmbeddedInMarkdown_IsSanitized()
    {
        string markdown =
            "# Title\n\n" +
            "<div>\n" +
            "<script>alert('raw')</script>\n" +
            "<img src=\"x\" onerror=\"alert('rawimg')\">\n" +
            "</div>\n";

        string html = Service.RenderAndSanitize(markdown);

        Assert.Contains("Title", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", html);
    }

    [Fact]
    public void HtmlInsideFencedCodeBlock_StaysEscapedText()
    {
        string html = Service.RenderAndSanitize("```\n<script>alert('code')</script>\n```\n");

        // The markup must be visible as escaped text, never live markup.
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullInput_DoesNotThrow()
    {
        string html = Service.RenderAndSanitize(null!);

        Assert.NotNull(html);
    }

    [Fact]
    public void EmptyInput_DoesNotThrow()
    {
        string html = Service.RenderAndSanitize(string.Empty);

        Assert.NotNull(html);
    }
}
