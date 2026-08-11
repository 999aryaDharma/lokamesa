using System;
using System.IO;
using System.Text.Json;
using DocumentIDE.Core.Sharing;

var builder = WebApplication.CreateBuilder(args);

// Risk Reduction Note:
// Process separation isolates public HTTP endpoints from privileged mutation & Word automation services.
// This is a risk reduction boundary and is not claimed as a full OS sandbox until OS-level isolation (SHR-10) is applied.

// Bind exclusively to loopback interface for local ngrok tunnel exposure
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 0);
});

builder.Services.AddSingleton<ShareTokenService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { status = "DocumentIDE ShareHost Active", service = "ShareHost" }));

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
app.MapGet("/share/{shareId}/artifacts/{*relativePath}", (string shareId, string relativePath, ShareTokenService tokenService) =>
{
    try
    {
        var artifactPath = tokenService.GetArtifactPath(shareId, relativePath);

        if (!File.Exists(artifactPath))
        {
            return Results.NotFound(new { error = "Artifact not found" });
        }

        var contentType = GetContentType(artifactPath);
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

app.Run();

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
