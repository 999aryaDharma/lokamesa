extern alias ShareHostApp;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// B2/B3 — the public ShareHost must authenticate every <c>/share/{id}/*</c>
/// request against the share package's own access policy (token hash + expiry).
///
/// ShareHost is a separate process and only sees the on-disk share package, so
/// the owner writes <c>access.json</c> next to <c>manifest.json</c>. Verification
/// is fail-closed: a missing policy, a cleared hash (revoke), a past expiry
/// (stop), a missing token, or a mismatched token all deny.
///
/// This file was previously empty while the readiness report cited it as token
/// security evidence; it now boot the real ShareHost pipeline like the other
/// share lanes.
/// </summary>
public sealed class ShareTokenTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokens = new();
    private readonly List<string> _createdShareRoots = new();

    public ShareTokenTests(WebApplicationFactory<ShareHostApp::Program> factory)
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

    [Fact]
    public async Task RequestWithoutToken_IsRejected()
    {
        var (shareId, _) = CreateShare("token-abc");
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/share/{shareId}/manifest");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithWrongToken_IsRejected()
    {
        var (shareId, _) = CreateShare("token-abc");
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/share/{shareId}/manifest");
        request.Headers.TryAddWithoutValidation("X-Share-Token", "the-wrong-token");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithValidHeaderToken_Succeeds()
    {
        var (shareId, token) = CreateShare("token-abc");
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/share/{shareId}/manifest");
        request.Headers.TryAddWithoutValidation("X-Share-Token", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("doc-1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RequestWithValidQueryToken_Succeeds()
    {
        var (shareId, token) = CreateShare("token-abc");
        using var client = _factory.CreateClient();

        // The owner "Copy link" shape is /share/{id}?token=..., so the query
        // token must be accepted as well as the header.
        var response = await client.GetAsync($"/share/{shareId}/manifest?token={token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StoppedShare_RejectsEvenTheValidToken()
    {
        var (shareId, token) = CreateShare("token-abc");
        WritePolicy(shareId, _tokens.ComputeHash(token), expired: true);
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/share/{shareId}/manifest");
        request.Headers.TryAddWithoutValidation("X-Share-Token", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokedShare_RejectsEvenTheValidToken()
    {
        var (shareId, token) = CreateShare("token-abc");
        // Revoke clears the stored hash.
        WritePolicy(shareId, string.Empty, expired: true);
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/share/{shareId}/manifest");
        request.Headers.TryAddWithoutValidation("X-Share-Token", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShareWithoutAccessPolicy_FailsClosed()
    {
        // A manifest exists but the owner never wrote a policy: deny rather than
        // fall through to an unauthenticated read.
        var shareId = $"b2b3-nopolicy-{Guid.NewGuid():N}";
        var root = _tokens.EnsureShareRootExists(shareId);
        _createdShareRoots.Add(root);
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(Manifest(shareId)));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/share/{shareId}/manifest");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AccessPolicyFile_CannotBeServedAsAnArtifact()
    {
        var (shareId, token) = CreateShare("token-abc");
        using var client = _factory.CreateClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/share/{shareId}/artifacts/access.json");
        request.Headers.TryAddWithoutValidation("X-Share-Token", token);
        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.NotFound,
            $"access.json must never be served, got {response.StatusCode}");
    }

    [Fact]
    public async Task HealthRoute_DoesNotRequireAToken()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private (string ShareId, string Token) CreateShare(string token)
    {
        var shareId = $"b2b3-{Guid.NewGuid():N}";
        var root = _tokens.EnsureShareRootExists(shareId);
        _createdShareRoots.Add(root);
        File.WriteAllText(Path.Combine(root, "manifest.json"),
            JsonSerializer.Serialize(Manifest(shareId)));
        WritePolicy(shareId, _tokens.ComputeHash(token), expired: false);
        return (shareId, token);
    }

    private void WritePolicy(string shareId, string tokenHash, bool expired) =>
        _tokens.WriteAccessPolicy(new ShareAccessPolicy
        {
            ShareId = shareId,
            TokenHash = tokenHash,
            ExpiresAt = expired
                ? DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O")
                : null,
        });

    private static ShareManifest Manifest(string shareId) => new()
    {
        ShareId = shareId,
        Mode = "Snapshot",
        AllowOriginalDownload = false,
        AllowPdfDownload = false,
        Documents = new List<SanitizedDocumentMetadata>
        {
            new()
            {
                DocumentId = "doc-1",
                DisplayName = "doc.md",
                Extension = ".md",
                SizeBytes = 10,
            },
        },
    };
}
