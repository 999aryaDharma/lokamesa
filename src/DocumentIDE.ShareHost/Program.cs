using System;
using System.IO;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Sharing;
using DocumentIDE.ShareHost;
using DocumentIDE.ShareHost.Endpoints;
using DocumentIDE.ShareHost.Graph;
using DocumentIDE.ShareHost.Search;
using DocumentIDE.ShareHost.Security;

var builder = WebApplication.CreateBuilder(args);

// Risk Reduction Note:
// Process separation isolates public HTTP endpoints from privileged mutation & Word automation services.
// This is a risk reduction boundary and is not claimed as a full OS sandbox until OS-level isolation (SHR-10) is applied.

// Bind exclusively to loopback interface for local ngrok tunnel exposure.
// The owner process reserves a free loopback port and passes it via
// DOCUMENTIDE_SHAREHOST_PORT so it knows the base URL to tunnel (B5); without
// that the host falls back to an OS-assigned ephemeral port.
var configuredPort = Environment.GetEnvironmentVariable("DOCUMENTIDE_SHAREHOST_PORT");
builder.WebHost.ConfigureKestrel(options =>
{
    if (int.TryParse(configuredPort, out var port) && port > 0)
    {
        options.Listen(System.Net.IPAddress.Loopback, port);
    }
    else
    {
        options.Listen(System.Net.IPAddress.Loopback, 0);
    }
});

builder.Services.AddSingleton<ShareTokenService>();
builder.Services.AddSingleton<RenderedHtmlSanitizer>();
builder.Services.AddSingleton<ShareSearchService>();
builder.Services.AddSingleton<ShareGraphService>();
builder.Services.AddShareRateLimits();


var app = builder.Build();
app.UseViewerSecurityHeaders();
app.UseRateLimiter();
// B2: every share-scoped request is authorized against the on-disk access
// policy, unconditionally (fail-closed). Requests without a shareId route
// value (the health route) pass through.
app.UseMiddleware<ShareTokenMiddleware>();

app.MapGet("/", () => Results.Ok(new { status = "DocumentIDE ShareHost Active", service = "ShareHost" }));

// B5: the read-only viewer index the owner "Copy link" URL opens. Without this
// route the copied /share/{shareId} link 404'd even for a valid token. The
// ShareTokenMiddleware gates it exactly like every other share-scoped route.
app.MapGet("/share/{shareId}", (string shareId, ShareTokenService tokenService) =>
{
    try
    {
        var manifest = LoadShareManifest(tokenService, shareId);
        if (manifest == null)
        {
            return Results.NotFound(new { error = "Share not found or invalid" });
        }

        return Results.Text(ViewerIndexPage.Render(shareId, manifest), "text/html", Encoding.UTF8);
    }
    catch (InvalidOperationException)
    {
        return Results.Problem(statusCode: 403, detail: "Access denied: invalid share identifier");
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Share not found or invalid" });
    }
});

// Manifest endpoint
app.MapGet("/share/{shareId}/manifest", (string shareId, ShareTokenService tokenService) =>
{
    try
    {
        var rootPath = tokenService.GetShareRootPath(shareId);
        var manifestPath = Path.Combine(rootPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return Results.NotFound(new { error = "Share manifest not found" });
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ShareManifest>(json);
        return Results.Ok(manifest);
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Share not found or invalid" });
    }
});

// Documents list endpoint
app.MapGet("/share/{shareId}/documents", (string shareId, ShareTokenService tokenService) =>
{
    try
    {
        var rootPath = tokenService.GetShareRootPath(shareId);
        var manifestPath = Path.Combine(rootPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return Results.NotFound(new { error = "Share manifest not found" });
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ShareManifest>(json);
        return Results.Ok(manifest?.Documents);
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Share not found or invalid" });
    }
});

// Document by ID endpoint
app.MapGet("/share/{shareId}/documents/{docId}", (string shareId, string docId, ShareTokenService tokenService) =>
{
    try
    {
        var rootPath = tokenService.GetShareRootPath(shareId);
        var manifestPath = Path.Combine(rootPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return Results.NotFound(new { error = "Share manifest not found" });
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ShareManifest>(json);
        var doc = manifest?.Documents?.Find(d => d.DocumentId == docId);

        if (doc == null)
        {
            return Results.NotFound(new { error = "Document not found in share" });
        }

        return Results.Ok(doc);
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Share not found or invalid" });
    }
});

// Artifact file retrieval endpoint (strictly contained under share root)
app.MapGet("/share/{shareId}/artifacts/{*relativePath}", (string shareId, string relativePath, ShareTokenService tokenService, RenderedHtmlSanitizer htmlSanitizer) =>
{
    try
    {
        var artifactPath = tokenService.GetArtifactPath(shareId, relativePath);

        // B2: the access policy carries the token hash and must never be served,
        // not even to an authorized viewer.
        if (string.Equals(
                Path.GetFileName(artifactPath),
                ShareTokenService.AccessFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(statusCode: 403, detail: "Access denied: protected artifact");
        }

        if (!File.Exists(artifactPath))
        {
            return Results.NotFound(new { error = "Artifact not found" });
        }

        // QA-05: download-gated directories must not bypass permissions via the generic
        // artifact route. When a manifest exists and the permission is OFF, originals/
        // and pdfs/ artifacts return forbidden instead of the file bytes.
        var normalizedRelative = relativePath.Replace('\\', '/').TrimStart('/');
        var shareRootForGate = tokenService.GetShareRootPath(shareId);
        var gateManifestPath = Path.Combine(shareRootForGate, "manifest.json");
        if (File.Exists(gateManifestPath))
        {
            var gateManifest = JsonSerializer.Deserialize<ShareManifest>(File.ReadAllText(gateManifestPath));
            if (normalizedRelative.StartsWith("originals/", StringComparison.OrdinalIgnoreCase) &&
                gateManifest?.AllowOriginalDownload != true)
            {
                return Results.Problem(statusCode: 403, detail: "Original download is disabled for this share");
            }
            if (normalizedRelative.StartsWith("pdfs/", StringComparison.OrdinalIgnoreCase) &&
                gateManifest?.AllowPdfDownload != true)
            {
                return Results.Problem(statusCode: 403, detail: "PDF download is disabled for this share");
            }
        }

        var contentType = GetContentType(artifactPath);

        if (contentType == "text/html")
        {
            // SHR-08: rendered Markdown/HTML preview artifacts are sanitized with the
            // maintained sanitizer at the public boundary, even if the owner-side artifact
            // was already rendered sanitized.
            var artifactBytes = new FileInfo(artifactPath).Length;
            if (artifactBytes > RenderedHtmlSanitizer.MaxHtmlArtifactBytes)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    detail: "HTML artifact exceeds the safe sanitization budget");
            }

            var sanitized = htmlSanitizer.Sanitize(File.ReadAllText(artifactPath));
            return Results.Text(sanitized, "text/html", Encoding.UTF8);
        }

        return Results.File(artifactPath, contentType);
    }
    catch (InvalidOperationException)
    {
        // Path traversal attempt detected
        return Results.Problem(statusCode: 403, detail: "Access denied: Invalid artifact path");
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Artifact resolution failed" });
    }
});

// Download original document endpoint
app.MapGet("/share/{shareId}/documents/{docId}/download-original", (string shareId, string docId, ShareTokenService tokenService) =>
{
    try
    {
        var rootPath = tokenService.GetShareRootPath(shareId);
        var manifestPath = Path.Combine(rootPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return Results.NotFound(new { error = "Share manifest not found" });
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ShareManifest>(json);

        if (manifest == null || !manifest.AllowOriginalDownload)
        {
            return Results.Problem(statusCode: 403, detail: "Original download is disabled for this share");
        }

        var doc = manifest.Documents?.Find(d => d.DocumentId == docId);
        if (doc == null)
        {
            return Results.NotFound(new { error = "Document not found in share" });
        }

        var artifactPath = tokenService.GetArtifactPath(shareId, Path.Combine("originals", doc.DisplayName));
        if (!File.Exists(artifactPath))
        {
            return Results.NotFound(new { error = "Original file artifact not found" });
        }

        return Results.File(artifactPath, "application/octet-stream", fileDownloadName: doc.DisplayName);
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Download failed" });
    }
});

// Download PDF endpoint
app.MapGet("/share/{shareId}/documents/{docId}/download-pdf", (string shareId, string docId, ShareTokenService tokenService) =>
{
    try
    {
        var rootPath = tokenService.GetShareRootPath(shareId);
        var manifestPath = Path.Combine(rootPath, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            return Results.NotFound(new { error = "Share manifest not found" });
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ShareManifest>(json);

        if (manifest == null || !manifest.AllowPdfDownload)
        {
            return Results.Problem(statusCode: 403, detail: "PDF download is disabled for this share");
        }

        var doc = manifest.Documents?.Find(d => d.DocumentId == docId);
        if (doc == null)
        {
            return Results.NotFound(new { error = "Document not found in share" });
        }

        var pdfFileName = Path.ChangeExtension(doc.DisplayName, ".pdf");
        var artifactPath = tokenService.GetArtifactPath(shareId, Path.Combine("pdfs", pdfFileName));
        if (!File.Exists(artifactPath))
        {
            return Results.NotFound(new { error = "PDF artifact not found" });
        }

        return Results.File(artifactPath, "application/pdf", fileDownloadName: pdfFileName);
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Download failed" });
    }
});

SnapshotEndpoints.MapSnapshotShareEndpoints(app);

// SHR-06: share-scoped viewer search. Scope is enforced server-side by
// ShareSearchService against the frozen manifest member set: only member
// documents are iterated, so nonmember content can never match.
app.MapGet("/share/{shareId}/search", (string shareId, string? q, ShareTokenService tokenService, ShareSearchService search) =>
{
    try
    {
        var manifest = LoadShareManifest(tokenService, shareId);
        if (manifest == null)
        {
            return Results.NotFound(new { error = "Share not found or invalid" });
        }

        var results = search.Search(manifest, q);
        return Results.Ok(new { results });
    }
    catch (InvalidOperationException)
    {
        return Results.Problem(statusCode: 403, detail: "Access denied: invalid share identifier");
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Share not found or invalid" });
    }
});

// SHR-06: share-scoped viewer graph. ShareGraphService serves member nodes
// plus only relations where both endpoints are members; edges to private
// nodes are omitted entirely.
app.MapGet("/share/{shareId}/graph", (string shareId, ShareTokenService tokenService, ShareGraphService graph) =>
{
    try
    {
        var manifest = LoadShareManifest(tokenService, shareId);
        if (manifest == null)
        {
            return Results.NotFound(new { error = "Share not found or invalid" });
        }

        var payload = graph.BuildGraph(manifest);
        return Results.Ok(new { nodes = payload.Nodes, edges = payload.Edges });
    }
    catch (InvalidOperationException)
    {
        return Results.Problem(statusCode: 403, detail: "Access denied: invalid share identifier");
    }
    catch (Exception)
    {
        return Results.NotFound(new { error = "Share not found or invalid" });
    }
});

app.Run();

static ShareManifest? LoadShareManifest(ShareTokenService tokenService, string shareId)
{
    var rootPath = tokenService.GetShareRootPath(shareId);
    var manifestPath = Path.Combine(rootPath, "manifest.json");
    if (!File.Exists(manifestPath))
    {
        return null;
    }

    return JsonSerializer.Deserialize<ShareManifest>(File.ReadAllText(manifestPath));
}

static string GetContentType(string filePath)
{
    var ext = Path.GetExtension(filePath).ToLowerInvariant();
    return ext switch
    {
        ".html" or ".htm" => "text/html",
        ".css" => "text/css",
        ".js" => "text/javascript",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        ".md" or ".txt" => "text/plain",
        _ => "application/octet-stream",
    };
}

public partial class Program { }
