using Ganss.Xss;

namespace DocumentIDE.ShareHost.Security
{
    /// <summary>
    /// Sanitizes rendered HTML (for example rendered Markdown preview artifacts) before it is
    /// served to a public viewer. Reuses the maintained Ganss.Xss <see cref="HtmlSanitizer"/>
    /// library — the same maintained sanitizer family used by the owner-side Markdown preview
    /// pipeline (MD-02) — with its default safe policy plus an explicit denylist so a future
    /// default widening cannot silently re-enable active content.
    /// </summary>
    /// <remarks>
    /// ShareHost cannot reference <c>DocumentIDE.Infrastructure</c> (SHR-02 dependency
    /// boundary), so this class reuses the maintained sanitizer package directly instead of
    /// calling the owner-side <c>MarkdownPreviewService</c> wrapper.
    /// </remarks>
    public sealed class RenderedHtmlSanitizer
    {
        /// <summary>
        /// Largest HTML artifact (bytes on disk) that will be re-sanitized. Larger artifacts are
        /// refused instead of being served unbounded through the public viewer.
        /// </summary>
        public const long MaxHtmlArtifactBytes = 10 * 1024 * 1024;

        private readonly HtmlSanitizer _sanitizer = CreateSanitizer();

        /// <summary>Sanitizes a rendered HTML fragment/document for public delivery.</summary>
        public string Sanitize(string html) => _sanitizer.Sanitize(html ?? string.Empty);

        private static HtmlSanitizer CreateSanitizer()
        {
            var sanitizer = new HtmlSanitizer();

            // The maintained library default allow-list already excludes these elements;
            // removing them explicitly is defense in depth against a future default change
            // that could re-enable scripting-capable or embedded active content.
            // QA-05: "form"/"button" are also removed. The public viewer is read-only and
            // the viewer CSP already declares form-action 'none', so interactive forms have
            // no legitimate role here; stripping them keeps the sanitizer consistent with
            // the CSP intent. "input" stays allowed so owner-rendered GFM task-list
            // checkboxes (disabled) keep their fidelity.
            foreach (var tag in new[]
                     {
                         "script", "iframe", "frame", "frameset", "object", "embed", "applet", "template", "style",
                         "form", "button"
                     })
            {
                sanitizer.AllowedTags.Remove(tag);
            }

            return sanitizer;
        }
    }
}
