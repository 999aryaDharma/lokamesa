extern alias ShareHostApp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ShareHostSecurity = ShareHostApp::DocumentIDE.ShareHost.Security;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// SHR-08 — public viewer CSP, HTML sanitization and rate-limit regression tests.
/// Boots the real ShareHost host (same mechanism as
/// tests/DocumentIDE.ArchitectureTests/ShareHostDependencyTests.cs) and exercises
/// real HTTP responses.
/// </summary>
public sealed class ShareWebSecurityTests : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokenService = new();
    private readonly List<string> _createdShareRoots = new();
    private string? _token;

    public ShareWebSecurityTests(WebApplicationFactory<ShareHostApp::Program> factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        foreach (var root in _createdShareRoots)
        {
            if (Directory.Exists(root))
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup; never mask a test failure with cleanup noise.
                }
            }
        }
    }

    [Fact]
    public async Task InjectedScript_IsNeutralized_BySanitization()
    {
        var shareId = NewShareId();
        var shareRoot = EnsureShareRoot(shareId);
        const string payload =
            "<h1>Rendered Preview</h1>" +
            "<script>window.__pwned = 1;</script>" +
            "<img src=\"x\" onerror=\"window.__pwned = 2\">";
        File.WriteAllText(Path.Combine(shareRoot, "preview.html"), payload);

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var response = await client.GetAsync($"/share/{shareId}/artifacts/preview.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h1>Rendered Preview</h1>", body);
    }

    [Fact]
    public async Task SecurityHeaders_EnforceCspBaseline_AndHardeningPolicies()
    {
        var shareId = NewShareId();
        var shareRoot = EnsureShareRoot(shareId);
        File.WriteAllText(Path.Combine(shareRoot, "preview.html"), "<h1>ok</h1>");

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var response = await client.GetAsync($"/share/{shareId}/artifacts/preview.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var csp = GetSingleHeader(response, "Content-Security-Policy");
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("connect-src 'self'", csp);
        Assert.Contains("img-src 'self' data:", csp);
        Assert.Contains("font-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("form-action 'none'", csp);

        // No unsafe-eval anywhere, and the script directive must not carry generic unsafe-inline.
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.OrdinalIgnoreCase);
        var scriptSrc = GetCspDirective(csp, "script-src");
        Assert.Contains("'self'", scriptSrc);
        Assert.DoesNotContain("unsafe-inline", scriptSrc, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("nosniff", GetSingleHeader(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", GetSingleHeader(response, "Referrer-Policy"));
        Assert.Equal("DENY", GetSingleHeader(response, "X-Frame-Options"));
    }

    [Fact]
    public async Task RateLimit_GeneralReads_Return429WithRetryInformation()
    {
        var shareId = CreateShareWithManifest();
        var clientIp = NewClientIp();
        using var client = _factory.CreateClient().WithShareToken(_token!);

        // Default general budget is 120 reads/minute/IP.
        for (var i = 0; i < 120; i++)
        {
            var response = await SendAsync(client, $"/share/{shareId}/manifest", clientIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await SendAsync(client, $"/share/{shareId}/manifest", clientIp);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var hasRetryAfter = limited.Headers.RetryAfter is not null ||
                            !string.IsNullOrWhiteSpace(GetSingleHeader(limited, "Retry-After"));
        Assert.True(hasRetryAfter, "429 response must include Retry-After retry information");
        var body = await limited.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("retryAfterSeconds", out var retryAfter));
        Assert.True(retryAfter.GetInt32() >= 1);

        // Security headers must also be present on rejected responses.
        Assert.Contains("default-src 'none'", GetSingleHeader(limited, "Content-Security-Policy"));
        Assert.Equal("nosniff", GetSingleHeader(limited, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task RateLimit_SearchReads_Return429()
    {
        var shareId = CreateShareWithManifest();
        var clientIp = NewClientIp();
        using var client = _factory.CreateClient().WithShareToken(_token!);

        // Share-scoped search route (GET /share/{token}/search); the default budget is
        // 30/minute/IP. Since SHR-06 landed, the route is implemented server-side and a
        // non-matching query returns 200 with empty results (the 404 expectation only
        // applied while the route was still reserved-but-unimplemented).
        for (var i = 0; i < 30; i++)
        {
            var response = await SendAsync(client, $"/share/{shareId}/search?q=hello", clientIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await SendAsync(client, $"/share/{shareId}/search?q=hello", clientIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task RateLimit_Downloads_Return429()
    {
        var shareId = CreateShareWithManifest(allowOriginalDownload: true);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        File.WriteAllText(Path.Combine(root, "originals", "doc.md"), "hello");
        var clientIp = NewClientIp();
        using var client = _factory.CreateClient().WithShareToken(_token!);

        var url = $"/share/{shareId}/documents/doc-1/download-original";

        // Default download budget is 10/minute/IP.
        for (var i = 0; i < 10; i++)
        {
            var response = await SendAsync(client, url, clientIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await SendAsync(client, url, clientIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task RateLimit_ConcurrentDownloads_CappedPerIp()
    {
        var shareId = CreateShareWithManifest(allowOriginalDownload: true);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        CreateSparseFile(Path.Combine(root, "originals", "doc.md"), 64L * 1024 * 1024);
        var clientIp = NewClientIp();
        using var client = _factory.CreateClient().WithShareToken(_token!);

        var url = $"/share/{shareId}/documents/doc-1/download-original";

        // Hold two downloads open without draining their bodies (default cap is 2/IP), then a
        // third download for the same IP must be rejected.
        using var held1 = await SendAsync(client, url, clientIp, HttpCompletionOption.ResponseHeadersRead);
        using var held2 = await SendAsync(client, url, clientIp, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, held1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, held2.StatusCode);

        var limited = await SendAsync(client, url, clientIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task RateLimit_GlobalExpensiveWork_IsCapped()
    {
        using var factory = new WebApplicationFactory<ShareHostApp::Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ShareHostSecurity.ShareRateLimitOptions>();
                services.AddSingleton(new ShareHostSecurity.ShareRateLimitOptions { GlobalExpensiveWorkConcurrency = 1 });
            }));

        var shareId = CreateShareWithManifest(allowOriginalDownload: true);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        CreateSparseFile(Path.Combine(root, "originals", "doc.md"), 64L * 1024 * 1024);
        File.WriteAllText(Path.Combine(root, "other.html"), "<h1>other</h1>");

        using var client = factory.CreateClient().WithShareToken(_token!);

        var held = await SendAsync(
            client,
            $"/share/{shareId}/documents/doc-1/download-original",
            NewClientIp(),
            HttpCompletionOption.ResponseHeadersRead);
        using (held)
        {
            Assert.Equal(HttpStatusCode.OK, held.StatusCode);

            // A different client IP must still be blocked by the process-wide expensive-work cap.
            var limited = await SendAsync(client, $"/share/{shareId}/artifacts/other.html", NewClientIp());
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }
    }

    [Fact]
    public async Task RateLimit_DownloadBuckets_AreIsolatedPerClientIp()
    {
        var shareId = CreateShareWithManifest(allowOriginalDownload: true);
        var root = _tokenService.GetShareRootPath(shareId);
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        File.WriteAllText(Path.Combine(root, "originals", "doc.md"), "hello");
        using var client = _factory.CreateClient().WithShareToken(_token!);
        var url = $"/share/{shareId}/documents/doc-1/download-original";

        var busyIp = NewClientIp();
        for (var i = 0; i < 10; i++)
        {
            var response = await SendAsync(client, url, busyIp);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await SendAsync(client, url, busyIp);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

        // A different client IP still has its full budget: limits are per client, server-side.
        var otherIp = await SendAsync(client, url, NewClientIp());
        Assert.Equal(HttpStatusCode.OK, otherIp.StatusCode);
    }

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>", "javascript:")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe><p>kept</p>", "<iframe")]
    [InlineData("<object data=\"payload\"></object><p>kept</p>", "<object")]
    [InlineData("<template><script>alert(1)</script></template><p>kept</p>", "<script")]
    [InlineData("<div style=\"background-image: url(javascript:alert(1))\">styled</div>", "javascript:")]
    public async Task Sanitizer_RemovesActiveContentPayloads(string payload, string forbidden)
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        File.WriteAllText(Path.Combine(root, "preview.html"), payload);

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var response = await client.GetAsync($"/share/{shareId}/artifacts/preview.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OversizedHtmlArtifact_IsRefusedInsteadOfServedUnsanitized()
    {
        var shareId = NewShareId();
        var shareRoot = EnsureShareRoot(shareId);
        var oversized = new byte[ShareHostSecurity.RenderedHtmlSanitizer.MaxHtmlArtifactBytes + 1];
        File.WriteAllBytes(Path.Combine(shareRoot, "huge.html"), oversized);

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var response = await client.GetAsync($"/share/{shareId}/artifacts/huge.html");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public void RateLimitDefaults_MatchSpecifiedBudgets()
    {
        var options = new ShareHostSecurity.ShareRateLimitOptions();

        Assert.Equal(120, options.GeneralReadsPerMinutePerIp);
        Assert.Equal(30, options.SearchesPerMinutePerIp);
        Assert.Equal(10, options.DownloadsPerMinutePerIp);
        Assert.Equal(2, options.ConcurrentDownloadsPerIp);
        Assert.True(options.GlobalExpensiveWorkConcurrency >= 1);
    }

    [Fact]
    public void ShareHost_CannotReachWordComOrPrivilegedMutationServices()
    {
        // Acceptance criterion (SHR-08): a public request cannot trigger Word COM. The public
        // process must not be able to resolve Word automation or privileged mutation services.
        var assembly = typeof(ShareHostApp::Program).Assembly;
        var referenced = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, name => name.Contains("Word", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, name => name.Contains("Interop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("DocumentIDE.Infrastructure", referenced, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentIDE.Api", referenced, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentIDE.Desktop", referenced, StringComparer.OrdinalIgnoreCase);
    }

    private string NewShareId() => $"shr08-{Guid.NewGuid():N}";

    /// <summary>Creates a share root with a manifest for the given download permissions.</summary>
    private string CreateShareWithManifest(bool allowOriginalDownload = true, string documentId = "doc-1", string displayName = "doc.md")
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
        _token = ShareHostTestSupport.IssueToken(_tokenService, shareId);
        return root;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string url,
        string clientIp,
        HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // ShareHost trusts X-Forwarded-For only from a local/tunnel peer; TestServer has no
        // remote address, so this header simulates distinct public clients.
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        return client.SendAsync(request, option);
    }

    private static int _clientCounter;

    private static string NewClientIp()
    {
        var n = Interlocked.Increment(ref _clientCounter);
        return $"198.18.{n / 254 % 254}.{n % 254 + 1}";
    }

    private static void CreateSparseFile(string path, long length)
    {
        using var stream = File.Create(path);
        stream.SetLength(length);
    }

    private static string GetSingleHeader(HttpResponseMessage response, string name)
    {
        return response.Headers.TryGetValues(name, out var values)
            ? string.Join(", ", values)
            : string.Empty;
    }

    private static string GetCspDirective(string csp, string directive)
    {
        foreach (var part in csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals(directive, StringComparison.Ordinal) ||
                part.StartsWith(directive + " ", StringComparison.Ordinal))
            {
                return part;
            }
        }

        return string.Empty;
    }
}
