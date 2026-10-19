using System;
using System.Net;
using System.Text;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.ShareHost.Endpoints;

/// <summary>
/// B5 — the read-only HTML index served at <c>GET /share/{shareId}</c>.
///
/// The owner "Copy link" URL opens this route directly. Before B5 ShareHost had
/// no page route at all, so a copied link returned a bare 404 and the share was
/// unusable even though the tokens and artifacts were valid. The page lists only
/// the frozen manifest's member documents by their sanitized virtual display
/// names and links to the already-scoped sub-routes; it contains no script, no
/// inline style (the public CSP forbids both) and no physical path.
/// </summary>
public static class ViewerIndexPage
{
    public static string Render(string shareId, ShareManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var encodedShareId = Uri.EscapeDataString(shareId ?? string.Empty);
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.Append("<title>Shared documents</title>");
        builder.Append("</head><body>");
        builder.Append("<main><h1>Shared documents</h1>");
        builder.Append("<p>Read-only share (").Append(Html(manifest.Mode)).Append(").</p>");

        var documents = manifest.Documents;
        if (documents == null || documents.Count == 0)
        {
            builder.Append("<p>This share contains no documents.</p>");
        }
        else
        {
            builder.Append("<ul>");
            foreach (var document in documents)
            {
                if (document == null || string.IsNullOrWhiteSpace(document.DocumentId))
                {
                    continue;
                }

                var encodedDocId = Uri.EscapeDataString(document.DocumentId);
                builder.Append("<li>");
                builder.Append("<a href=\"/share/").Append(encodedShareId)
                       .Append("/documents/").Append(encodedDocId).Append("/preview\">")
                       .Append(Html(document.DisplayName)).Append("</a>");

                if (manifest.AllowOriginalDownload)
                {
                    builder.Append(" <a href=\"/share/").Append(encodedShareId)
                           .Append("/documents/").Append(encodedDocId).Append("/download-original\">download original</a>");
                }

                if (manifest.AllowPdfDownload)
                {
                    builder.Append(" <a href=\"/share/").Append(encodedShareId)
                           .Append("/documents/").Append(encodedDocId).Append("/download-pdf\">download PDF</a>");
                }

                builder.Append("</li>");
            }
            builder.Append("</ul>");
        }

        builder.Append("</main></body></html>");
        return builder.ToString();
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
