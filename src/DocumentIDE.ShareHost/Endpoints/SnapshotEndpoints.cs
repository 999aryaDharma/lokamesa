using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Sharing;
using DocumentIDE.ShareHost.Security;

namespace DocumentIDE.ShareHost.Endpoints;

/// <summary>
/// SHR-04 — Snapshot Share viewer endpoints (public ShareHost side).
///
/// Serves only frozen owner-published artifacts scoped to the share token in
/// the URL: the frozen manifest (<c>/snapshot</c>) and per-member frozen
/// preview HTML (<c>/documents/{id}/preview</c>). Membership is enforced at
/// the backend against the frozen manifest — nonmember IDs return 404 and
/// never reveal whether the ID exists elsewhere. Preview HTML is re-sanitized
/// at the public boundary. Original files are never served here: original
/// download stays absent (403 via the download route) unless a later
/// permission explicitly enables it (SHR-07).
///
/// This class references only <c>DocumentIDE.Core</c> services and the
/// maintained HTML sanitizer. It never invokes Word/COM automation, batch
/// mutation, or unrestricted filesystem access: DOCX previews arrive as
/// owner-side WORD-04 cache bytes frozen at publish time.
/// </summary>
public static class SnapshotEndpoints
{
    public static void MapSnapshotShareEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/share/{shareId}/snapshot", (
            string shareId,
            ShareTokenService tokenService) =>
        {
            try
            {
                var manifest = LoadManifest(tokenService, shareId);
                if (manifest == null)
                {
                    return Results.NotFound(new { error = "Share snapshot not found" });
                }

                return Results.Ok(new
                {
                    manifest.ShareId,
                    manifest.Mode,
                    manifest.SnapshotVersion,
                    manifest.CreatedAt,
                    manifest.AllowOriginalDownload,
                    manifest.AllowPdfDownload,
                    documents = manifest.Documents
                });
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(statusCode: 403, detail: "Access denied: invalid share identifier");
            }
            catch (Exception)
            {
                return Results.NotFound(new { error = "Share snapshot not found" });
            }
        });

        app.MapGet("/share/{shareId}/documents/{docId}/preview", (
            string shareId,
            string docId,
            ShareTokenService tokenService,
            RenderedHtmlSanitizer htmlSanitizer) =>
        {
            try
            {
                var manifest = LoadManifest(tokenService, shareId);
                if (manifest == null)
                {
                    return Results.NotFound(new { error = "Share snapshot not found" });
                }

                // Token-scoped membership: only frozen member IDs resolve.
                var doc = manifest.Documents?.Find(d =>
                    string.Equals(d.DocumentId, docId, StringComparison.Ordinal));
                if (doc == null)
                {
                    return Results.NotFound(new { error = "Document not found in share" });
                }

                var artifactPath = tokenService.GetArtifactPath(shareId, "previews/" + doc.DocumentId + ".html");
                if (!File.Exists(artifactPath))
                {
                    return Results.NotFound(new { error = "Preview artifact not found" });
                }

                var artifactBytes = new FileInfo(artifactPath).Length;
                if (artifactBytes > RenderedHtmlSanitizer.MaxHtmlArtifactBytes)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status413PayloadTooLarge,
                        detail: "Preview artifact exceeds the safe sanitization budget");
                }

                var sanitized = htmlSanitizer.Sanitize(File.ReadAllText(artifactPath, Encoding.UTF8));
                return Results.Text(sanitized, "text/html", Encoding.UTF8);
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(statusCode: 403, detail: "Access denied: invalid artifact path");
            }
            catch (Exception)
            {
                return Results.NotFound(new { error = "Preview resolution failed" });
            }
        });
    }

    private static ShareManifest? LoadManifest(ShareTokenService tokenService, string shareId)
    {
        var rootPath = tokenService.GetShareRootPath(shareId);
        var manifestPath = Path.Combine(rootPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ShareManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
    }
}
