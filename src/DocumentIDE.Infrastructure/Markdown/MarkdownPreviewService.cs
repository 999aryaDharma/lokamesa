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
            // REL-04: HtmlSanitizer's default allowed-tag set keeps <form>, so a
            // share-rendered document could post to an attacker-controlled action
            // URL (phishing / credential exfiltration). Forms are not part of the
            // markdown preview surface, so the element and its subtree are removed.
            sanitizer.AllowedTags.Remove("form");
            return sanitizer.Sanitize(html);
        }
    }
}
