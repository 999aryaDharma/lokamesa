// src/DocumentIDE.Infrastructure/Markdown/MarkdownPreviewService.cs
using Markdig;
using Ganss.Xss;

namespace DocumentIDE.Infrastructure.Markdown
{
    /// <summary>
    /// Service that renders markdown to HTML and sanitizes the result.
    /// </summary>
    public class MarkdownPreviewService
    {
        /// <summary>
        /// Render the supplied markdown to HTML and sanitize it to remove unsafe tags/attributes.
        /// </summary>
        /// <param name="markdown">Markdown text.</param>
        /// <returns>Sanitized HTML.</returns>
        public string RenderAndSanitize(string markdown)
        {
            var html = Markdig.Markdown.ToHtml(markdown ?? string.Empty);
            var sanitizer = new HtmlSanitizer();
            return sanitizer.Sanitize(html);
        }
    }
}
