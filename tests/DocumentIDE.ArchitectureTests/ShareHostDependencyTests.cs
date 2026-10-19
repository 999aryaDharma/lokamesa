using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocumentIDE.ArchitectureTests;

public class ShareHostDependencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ShareHostDependencyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void ShareHost_Assembly_DoesNotReference_PrivilegedAssemblies()
    {
        var shareHostAssembly = typeof(Program).Assembly;
        var referencedAssemblies = shareHostAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        var forbiddenAssemblies = new[]
        {
            "DocumentIDE.Infrastructure",
            "DocumentIDE.Api",
            "DocumentIDE.Desktop",
            "Microsoft.Office.Interop.Word"
        };

        foreach (var forbidden in forbiddenAssemblies)
        {
            Assert.DoesNotContain(forbidden, referencedAssemblies);
        }
    }

    [Fact]
    public void ShareHost_Types_DoNotDependOn_MutationOrWordServices()
    {
        var shareHostAssembly = typeof(Program).Assembly;
        var types = shareHostAssembly.GetTypes();

        var forbiddenTypeNameSubstrings = new[]
        {
            "Word",
            "Interop",
            "BatchMutation",
            "PhysicalFileOperation",
            "UnrestrictedFileSystem",
            "WorkspaceSecret"
        };

        foreach (var type in types)
        {
            foreach (var forbidden in forbiddenTypeNameSubstrings)
            {
                Assert.DoesNotContain(forbidden, type.Name, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var paramTypeName = param.ParameterType.FullName ?? string.Empty;
                    foreach (var forbidden in forbiddenTypeNameSubstrings)
                    {
                        Assert.DoesNotContain(forbidden, paramTypeName, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
    }

    [Fact]
    public void ShareHost_ProjectFile_HasNarrowDependencyGraph()
    {
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var repoRoot = FindRepoRoot(currentDir);
        var csprojPath = Path.Combine(repoRoot, "src", "DocumentIDE.ShareHost", "DocumentIDE.ShareHost.csproj");

        Assert.True(File.Exists(csprojPath), $"Expected project file at {csprojPath}");

        var content = File.ReadAllText(csprojPath);

        Assert.Contains("DocumentIDE.Core.csproj", content);
        Assert.DoesNotContain("DocumentIDE.Infrastructure.csproj", content);
        Assert.DoesNotContain("DocumentIDE.Api.csproj", content);
        Assert.DoesNotContain("DocumentIDE.Desktop.csproj", content);
    }

    [Fact]
    public async Task ShareHost_ArbitraryLocalPathRoutes_DoNotExist()
    {
        var client = _factory.CreateClient();

        // Requesting arbitrary physical path or mutation API routes must return 404 Not Found
        var routesToTest = new[]
        {
            "/file?path=C:\\Windows\\System32\\cmd.exe",
            "/file?path=/etc/passwd",
            "/api/documents",
            "/api/mutation",
            "/api/word/open",
            "/admin",
            "/system/exec"
        };

        foreach (var route in routesToTest)
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task ShareHost_ServesOnly_ShareOwnedArtifactDirectory()
    {
        var client = _factory.CreateClient();
        var shareId = "test-share-123";
        var tokenService = new ShareTokenService();
        var shareRoot = tokenService.EnsureShareRootExists(shareId);
        IssueAccessToken(client, tokenService, shareId);

        try
        {
            // Write a manifest and sample artifact under share root
            var manifest = new ShareManifest
            {
                ShareId = shareId,
                Mode = "Snapshot",
                AllowOriginalDownload = true,
                AllowPdfDownload = false,
                Documents = new System.Collections.Generic.List<SanitizedDocumentMetadata>
                {
                    new SanitizedDocumentMetadata
                    {
                        DocumentId = "doc-1",
                        DisplayName = "sample.md",
                        Extension = ".md",
                        SizeBytes = 100
                    }
                }
            };
            File.WriteAllText(Path.Combine(shareRoot, "manifest.json"), JsonSerializer.Serialize(manifest));
            File.WriteAllText(Path.Combine(shareRoot, "preview.html"), "<h1>Sample Preview</h1>");

            // Test GET manifest
            var manifestResp = await client.GetAsync($"/share/{shareId}/manifest");
            Assert.Equal(HttpStatusCode.OK, manifestResp.StatusCode);
            var manifestJson = await manifestResp.Content.ReadAsStringAsync();
            Assert.Contains("doc-1", manifestJson);

            // Test GET artifact
            var artifactResp = await client.GetAsync($"/share/{shareId}/artifacts/preview.html");
            Assert.Equal(HttpStatusCode.OK, artifactResp.StatusCode);
            var artifactContent = await artifactResp.Content.ReadAsStringAsync();
            Assert.Equal("<h1>Sample Preview</h1>", artifactContent);
        }
        finally
        {
            if (Directory.Exists(shareRoot))
            {
                Directory.Delete(shareRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShareHost_PathTraversalAttempt_ReturnsForbiddenOrNotFound()
    {
        var client = _factory.CreateClient();
        var shareId = "test-share-traversal";
        var tokenService = new ShareTokenService();
        var shareRoot = tokenService.EnsureShareRootExists(shareId);
        IssueAccessToken(client, tokenService, shareId);

        try
        {
            // Attempt path traversal via relative dots. HttpClient normalizes
            // the URI, so this resolves to /share/secret.txt: a share ID the
            // owner never registered. The fail-closed gate (B2) denies 401 for
            // any unknown share, so the traversal can never reach an artifact
            // handler — 200/exposure is what actually matters here.
            var response = await client.GetAsync($"/share/{shareId}/artifacts/../../secret.txt");

            // Must NOT return 200 OK or expose system files
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden
                    or HttpStatusCode.NotFound
                    or HttpStatusCode.Unauthorized,
                $"Expected Forbidden/NotFound/Unauthorized for path traversal, got {response.StatusCode}"
            );
        }
        finally
        {
            if (Directory.Exists(shareRoot))
            {
                Directory.Delete(shareRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ShareHost_DownloadPermissions_Enforced()
    {
        var client = _factory.CreateClient();
        var shareId = "test-share-perms";
        var tokenService = new ShareTokenService();
        var shareRoot = tokenService.EnsureShareRootExists(shareId);
        IssueAccessToken(client, tokenService, shareId);

        try
        {
            // Create manifest with downloads disabled
            var manifest = new ShareManifest
            {
                ShareId = shareId,
                AllowOriginalDownload = false,
                AllowPdfDownload = false,
                Documents = new System.Collections.Generic.List<SanitizedDocumentMetadata>
                {
                    new SanitizedDocumentMetadata
                    {
                        DocumentId = "doc-1",
                        DisplayName = "doc1.md"
                    }
                }
            };
            File.WriteAllText(Path.Combine(shareRoot, "manifest.json"), JsonSerializer.Serialize(manifest));

            // Original download must return 403 Forbidden
            var origResp = await client.GetAsync($"/share/{shareId}/documents/doc-1/download-original");
            Assert.Equal(HttpStatusCode.Forbidden, origResp.StatusCode);

            // PDF download must return 403 Forbidden
            var pdfResp = await client.GetAsync($"/share/{shareId}/documents/doc-1/download-pdf");
            Assert.Equal(HttpStatusCode.Forbidden, pdfResp.StatusCode);
        }
        finally
        {
            if (Directory.Exists(shareRoot))
            {
                Directory.Delete(shareRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// B2 — registers the on-disk access policy for a hand-written share package
    /// and attaches the matching token to the client, mirroring how the owner
    /// process authorizes a viewer. Without this the fail-closed gate denies 401.
    /// </summary>
    private static void IssueAccessToken(HttpClient client, ShareTokenService tokens, string shareId)
    {
        var token = tokens.GenerateToken();
        tokens.WriteAccessPolicy(
            ShareTokenService.PolicyFor(shareId, tokens.ComputeHash(token), expiresAt: null));
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Share-Token", token);
    }

    private static string FindRepoRoot(string startDir)    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DocumentIDE.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repository root containing DocumentIDE.sln");
    }
}
