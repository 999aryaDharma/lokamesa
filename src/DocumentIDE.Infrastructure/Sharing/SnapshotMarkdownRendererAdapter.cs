using DocumentIDE.Core.Sharing;
using DocumentIDE.Infrastructure.Markdown;

namespace DocumentIDE.Infrastructure.Sharing;

/// <summary>
/// Owner-side SHR-04/SHR-05 markdown seam: adapts the maintained
/// <see cref="MarkdownPreviewService"/> (Markdig + HtmlSanitizer, MD-02) to the
/// Core <see cref="ISnapshotMarkdownRenderer"/> contract so frozen/live share
/// artifacts are rendered and sanitized by the same pipeline as the canvas
/// preview — never by an ad-hoc renderer.
/// </summary>
public sealed class SnapshotMarkdownRendererAdapter(MarkdownPreviewService renderer) : ISnapshotMarkdownRenderer
{
    public string RenderAndSanitize(string markdown) => renderer.RenderAndSanitize(markdown);
}
