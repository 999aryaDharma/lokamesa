extern alias ShareHostApp;

using System.Net;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ShareHostSecurity = ShareHostApp::DocumentIDE.ShareHost.Security;

namespace DocumentIDE.SecurityTests;

/// <summary>
/// QA-05 — Public Share Security Audit lane.
///
/// Boots the real ShareHost process pipeline (same mechanism as
/// tests/DocumentIDE.IntegrationTests/Sharing/ShareWebSecurityTests.cs and
/// tests/DocumentIDE.ArchitectureTests/ShareHostDependencyTests.cs) and attacks
/// it across six vectors: token cross-share scope, traversal/absolute-path,
/// Live membership leakage, sanitizer/CSP script payloads, download/rate-limit
/// bypass, and the ShareHost privileged-service dependency boundary, plus
/// stop/revoke behavior.
///
/// This lane does not duplicate the LocalApi (privileged desktop API) harness
/// in LocalApiAttackSurfaceTests.cs or the OS-isolation spike harness in
/// ShareHostIsolationTests.cs; it reuses their assertion patterns against the
/// public ShareHost surface only.
/// </summary>
public sealed class PublicShareAttackSurfaceTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokenService = new();
    private readonly List<string> _createdShareRoots = new();
    private readonly Dictionary<string, string> _tokensByShare = new(StringComparer.Ordinal);

    public PublicShareAttackSurfaceTests(WebApplicationFactory<ShareHostApp::Program> factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        foreach (var root in _createdShareRoots)
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    // Step 1: token cross-share access. No private document becomes reachable.

    [Fact]
    public async Task TokenCrossShare_DocumentOfShareB_IsNotReachableViaShareA()
    {
        var shareA = CreateShareWithManifest(documentId: "doc-a", displayName: "a.md");
        var shareB = CreateShareWithManifest(documentId: "doc-b", displayName: "b.md");
        using var clientA = WithToken(_factory.CreateClient(), _tokensByShare[shareA]);
        using var clientB = WithToken(_factory.CreateClient(), _tokensByShare[shareB]);

        var crossAB = await clientA.GetAsync($"/share/{shareA}/documents/doc-b");
        var crossBA = await clientB.GetAsync($"/share/{shareB}/documents/doc-a");

        Assert.Equal(HttpStatusCode.NotFound, crossAB.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossBA.StatusCode);

        var manifestA = await clientA.GetAsync($"/share/{shareA}/manifest");
        Assert.Equal(HttpStatusCode.OK, manifestA.StatusCode);
        var manifestBody = await manifestA.Content.ReadAsStringAsync();
        Assert.Contains("doc-a", manifestBody);
        Assert.DoesNotContain("doc-b", manifestBody);
    }

    [Fact]
    public async Task UnknownShareId_ReturnsUnauthorized_WithoutLeakingPaths()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/share/qa05-unknown-{Guid.NewGuid():N}/manifest");

        // B2 fail-closed: an unknown share has no access policy, so the gate
        // denies before the route runs — this also avoids leaking existence.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("LocalApplicationData", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentIDE", body);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    public async Task MalformedShareIds_AreRejectedWithoutSuccess(string evilShareId)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/share/{Uri.EscapeDataString(evilShareId)}/manifest");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // Step 2: traversal / absolute-path payloads.

    [Theory]
    [InlineData("..%2Fmanifest.json")]
    [InlineData("%2e%2e%2fmanifest.json")]
    [InlineData("..%5C..%5Cmanifest.json")]
    [InlineData("C%3A%5CWindows%5CSystem32%5Ccmd.exe")]
    [InlineData("%2Fetc%2Fpasswd")]
    [InlineData("originals%2F..%2F..%2Fmanifest.json")]
    public async Task ArtifactTraversal_Payloads_NeverSucceed(string encodedPayload)
    {
        var shareId = CreateShareWithManifest();
        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);

        var response = await client.GetAsync($"/share/{shareId}/artifacts/{encodedPayload}");

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.BadRequest,
            $"Traversal payload must fail, got {response.StatusCode}");
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("/etc/shadow")]
    [InlineData("..\\secret.txt")]
    [InlineData("subfolder/../../secret.txt")]
    public void ArtifactPathResolution_RejectsAbsoluteAndTraversalPaths(string evilPath)
    {
        Assert.Throws<InvalidOperationException>(
            () => _tokenService.GetArtifactPath("valid-share-id-123", evilPath));
    }

    // Step 3: Live membership leakage. New files / later members never auto-publish.

    [Fact]
    public void ManifestCreate_DoesNotAutoPublish_NewFilesOrLaterMembers()
    {
        var share = new Share
        {
            Id = $"qa05-{Guid.NewGuid():N}",
            Mode = ShareMode.Live,
            AccessMode = AccessMode.SecretToken
        };
        share.Members.Add(new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = "doc-member",
            DisplayPath = "member.md"
        });

        // Simulate a newly created file in the shared folder and a later Collection
        // membership change: both appear as candidates but were never added as members.
        var candidates = new[]
        {
            new Document { Id = "doc-member", CurrentPath = @"C:\work\member.md", Filename = "member.md" },
            new Document { Id = "doc-new-file", CurrentPath = @"C:\work\new-file.md", Filename = "new-file.md" },
            new Document { Id = "doc-later-member", CurrentPath = @"C:\work\later.md", Filename = "later.md" }
        };

        var manifest = ShareManifest.Create(share, candidates);

        var published = Assert.Single(manifest.Documents);
        Assert.Equal("doc-member", published.DocumentId);
    }

    [Fact]
    public async Task PublicManifest_ExposesNoPhysicalPaths()
    {
        var shareId = CreateShareWithManifest(documentId: "doc-1", displayName: "notes.md");
        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);

        var response = await client.GetAsync($"/share/{shareId}/manifest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalApplicationData", body, StringComparison.OrdinalIgnoreCase);
    }

    // Step 4: sanitizer / CSP script payloads. Injected script does not execute.

    [Theory]
    [InlineData("<script>window.__pwned = 1;</script><p>kept</p>", "<script")]
    [InlineData("<svg onload=\"window.__pwned = 2\"></svg><p>kept</p>", "onload")]
    [InlineData("<img src=\"x\" onerror=\"window.__pwned = 3\"><p>kept</p>", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a><p>kept</p>", "javascript:")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\"><p>kept</p>", "refresh")]
    [InlineData("<form action=\"https://evil.example\"><input type=\"submit\"></form><p>kept</p>", "<form")]
    public async Task InjectedScript_Payloads_AreNeutralized(string payload, string forbidden)
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        File.WriteAllText(Path.Combine(root, "preview.html"), payload);

        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);
        var response = await client.GetAsync($"/share/{shareId}/artifacts/preview.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kept", body);
    }

    [Fact]
    public async Task CspBaseline_IsPresent_OnSuccessAndErrorResponses()
    {
        var shareId = CreateShareWithManifest();
        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);

        var ok = await client.GetAsync($"/share/{shareId}/manifest");
        var notFound = await client.GetAsync($"/share/{shareId}/documents/no-such-doc");
        var forbidden = await client.GetAsync($"/share/{shareId}/documents/doc-1/download-pdf");

        foreach (var response in new[] { ok, notFound, forbidden })
        {
            var csp = GetSingleHeader(response, "Content-Security-Policy");
            Assert.Contains("default-src 'none'", csp);
            Assert.DoesNotContain("unsafe-eval", csp, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Equal("nosniff", GetSingleHeader(response, "X-Content-Type-Options"));
            Assert.Equal("DENY", GetSingleHeader(response, "X-Frame-Options"));
            response.Dispose();
        }
    }

    // Step 5: download bypass / rate-limit bypass.

    [Fact]
    public async Task DownloadOriginal_Off_CannotBeBypassed_ViaDownloadRoute()
    {
        var shareId = CreateShareWithManifest(allowOriginalDownload: false);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        File.WriteAllText(Path.Combine(root, "originals", "doc.md"), "private-bytes");

        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);
        var response = await client.GetAsync($"/share/{shareId}/documents/doc-1/download-original");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadOriginal_Off_CannotBeBypassed_ViaArtifactRoute()
    {
        var shareId = CreateShareWithManifest(allowOriginalDownload: false);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        File.WriteAllText(Path.Combine(root, "originals", "doc.md"), "private-bytes");

        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);
        var response = await client.GetAsync($"/share/{shareId}/artifacts/originals/doc.md");

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"Download-OFF original must not be served via artifact route, got {response.StatusCode}");
    }

    [Fact]
    public async Task DownloadPdf_Off_CannotBeBypassed()
    {
        var shareId = CreateShareWithManifest(allowOriginalDownload: false);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "pdfs"));
        File.WriteAllText(Path.Combine(root, "pdfs", "doc.pdf"), "private-pdf-bytes");

        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);
        var response = await client.GetAsync($"/share/{shareId}/documents/doc-1/download-pdf");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_GeneralReads_AreEnforcedServerSide_WithRetryInformation()
    {
        using var factory = new WebApplicationFactory<ShareHostApp::Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ShareHostSecurity.ShareRateLimitOptions>();
                services.AddSingleton(new ShareHostSecurity.ShareRateLimitOptions
                {
                    GeneralReadsPerMinutePerIp = 5
                });
            }));

        var shareId = CreateShareWithManifest();
        var clientIp = $"198.19.{Random.Shared.Next(1, 254)}.{Random.Shared.Next(1, 254)}";
        using var client = WithToken(factory.CreateClient(), _tokensByShare[shareId]);

        for (var i = 0; i < 5; i++)
        {
            var response = await SendAsAsync(client, $"/share/{shareId}/manifest", clientIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Flooding past the budget must fail server-side, not succeed silently.
        var limited = await SendAsAsync(client, $"/share/{shareId}/manifest", clientIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var body = await limited.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("retryAfterSeconds", out var retryAfter));
        Assert.True(retryAfter.GetInt32() >= 1);
    }

    // Revoke / stop behavior. A revoked share fails everywhere.

    [Fact]
    public async Task RevokedShare_Fails_OnEveryPublicRoute()
    {
        var shareId = CreateShareWithManifest();
        var root = _tokenService.GetShareRootPath(shareId);
        File.WriteAllText(Path.Combine(root, "preview.html"), "<h1>was public</h1>");
        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);

        var before = await client.GetAsync($"/share/{shareId}/manifest");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Simulate Stop/Remove: the share record and its artifacts are withdrawn.
        Directory.Delete(root, recursive: true);
        _createdShareRoots.Remove(root);

        // B2 fail-closed: with the package (and its access policy) gone the gate
        // denies every route — a withdrawn share cannot keep serving.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/share/{shareId}/manifest")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/share/{shareId}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/share/{shareId}/documents/doc-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/share/{shareId}/artifacts/preview.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/share/{shareId}/documents/doc-1/download-original")).StatusCode);
    }

    // Step 6: ShareHost architecture dependencies. ShareHost lacks privileged services.

    [Fact]
    public void ShareHost_DoesNotReference_PrivilegedAssemblies()
    {
        var assembly = typeof(ShareHostApp::Program).Assembly;
        var referenced = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, n => n.Contains("Word", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("Interop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("DocumentIDE.Infrastructure", referenced, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentIDE.Api", referenced, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentIDE.Desktop", referenced, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShareHost_Types_DoNotDependOn_MutationOrWordServices()
    {
        var assembly = typeof(ShareHostApp::Program).Assembly;
        var forbidden = new[]
        {
            "Word", "Interop", "BatchMutation", "PhysicalFileOperation",
            "UnrestrictedFileSystem", "WorkspaceSecret"
        };

        foreach (var type in assembly.GetTypes())
        {
            foreach (var name in forbidden)
            {
                Assert.DoesNotContain(name, type.Name, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var paramType = param.ParameterType.FullName ?? string.Empty;
                    foreach (var name in forbidden)
                    {
                        Assert.DoesNotContain(name, paramType, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
    }

    [Fact]
    public void ShareHost_ProjectFile_HasNarrowDependencyGraph()
    {
        var repoRoot = FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);
        var csproj = Path.Combine(repoRoot, "src", "DocumentIDE.ShareHost", "DocumentIDE.ShareHost.csproj");

        Assert.True(File.Exists(csproj), $"Expected project file at {csproj}");
        var content = File.ReadAllText(csproj);

        Assert.Contains("DocumentIDE.Core.csproj", content);
        Assert.DoesNotContain("DocumentIDE.Infrastructure.csproj", content);
        Assert.DoesNotContain("DocumentIDE.Api.csproj", content);
        Assert.DoesNotContain("DocumentIDE.Desktop.csproj", content);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task NoMutationEndpoints_AreAvailablePublicly(string method)
    {
        var shareId = CreateShareWithManifest();
        using var client = WithToken(_factory.CreateClient(), _tokensByShare[shareId]);

        foreach (var url in new[]
                 {
                     $"/share/{shareId}/manifest",
                     $"/share/{shareId}/documents/doc-1",
                     "/api/documents",
                     "/api/mutation",
                     "/api/word/open"
                 })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            var response = await client.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.NotFound ||
                response.StatusCode == HttpStatusCode.MethodNotAllowed,
                $"{method} {url} must not succeed, got {response.StatusCode}");
        }
    }

    // Residual same-user OS risk documented consistently (per SHR-10 spike).

    [Fact]
    public void IsolationResidualRisk_IsDocumentedConsistentlyWithShr10()
    {
        var report = new ShareHostIsolationEvaluator().EvaluateIsolationPosture();

        Assert.True(report.IsProcessSeparated);
        Assert.False(report.IsOsSandbox);
        Assert.Contains("process separation", report.ResidualRisk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not an OS sandbox", report.ResidualRisk, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arbitrary local", report.ResidualRisk, StringComparison.OrdinalIgnoreCase);
    }

    private string NewShareId() => $"qa05-{Guid.NewGuid():N}";

    private string CreateShareWithManifest(
        bool allowOriginalDownload = true,
        string documentId = "doc-1",
        string displayName = "doc.md")
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        var manifest = new ShareManifest
        {
            ShareId = shareId,
            Mode = "Snapshot",
            AllowOriginalDownload = allowOriginalDownload,
            AllowPdfDownload = false,
            Documents = new List<SanitizedDocumentMetadata>
            {
                new SanitizedDocumentMetadata
                {
                    DocumentId = documentId,
                    DisplayName = displayName,
                    Extension = Path.GetExtension(displayName),
                    SizeBytes = 10
                }
            }
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));
        return shareId;
    }

    private string EnsureShareRoot(string shareId)
    {
        var root = _tokenService.EnsureShareRootExists(shareId);
        _createdShareRoots.Add(root);
        // B2: register the access policy so the fail-closed gate authorizes the viewer.
        var token = _tokenService.GenerateToken();
        _tokenService.WriteAccessPolicy(
            ShareTokenService.PolicyFor(shareId, _tokenService.ComputeHash(token), expiresAt: null));
        _tokensByShare[shareId] = token;
        return root;
    }

    /// <summary>Attaches the share's token to the client's requests.</summary>
    private static HttpClient WithToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Share-Token", token);
        return client;
    }

    private static Task<HttpResponseMessage> SendAsAsync(HttpClient client, string url, string clientIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        return client.SendAsync(request);
    }

    private static string GetSingleHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : string.Empty;

    private static string FindRepoRoot(string startDir)
    {
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
