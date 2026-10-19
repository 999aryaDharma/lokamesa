using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// B5 — "Copy link" must hand the owner a real, absolute viewer URL.
///
/// Before B5 the share controller returned a relative <c>/share/{id}</c> path
/// that resolved against the *privileged app* origin (where no such route
/// exists), so the copied link always 404'd, and <c>tunnelError</c> was a
/// hardcoded <c>null</c> that hid a missing ngrok tunnel. These lanes drive the
/// real <see cref="DocumentIDE.Infrastructure.Sharing.ShareLinkService"/>
/// composition through the desktop host with a fake ShareHost process and a fake
/// transport, because the live ngrok tunnel/child process cannot run in CI.
/// </summary>
[Collection("HostFixtures")]
public sealed class SharePublicLinkHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private const string LocalShareHostUrl = "http://127.0.0.1:45999";
    private const string PublicTunnelUrl = "https://share-subdomain.ngrok-free.app";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "share-link-" + Guid.NewGuid().ToString("N"));
    private readonly string _sharesRoot = Path.Combine(Path.GetTempPath(), "share-link-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(_root);
        TryDelete(_sharesRoot);
    }

    [Fact]
    public async Task Create_ReturnsAbsolutePublicShareUrl_WithToken_AndNoTunnelError()
    {
        var db = await SeedAsync();
        await using var host = await StartHostAsync(
            db, new FakeShareHostProcess(LocalShareHostUrl), new FakeTransport(PublicTunnelUrl, error: null));
        using var client = AuthorizedClient(host);

        string shareId, token;
        using (var created = await client.PostAsJsonAsync("/api/shares", CreatePayload()))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            shareId = json.RootElement.GetProperty("shareId").GetString()!;
            token = json.RootElement.GetProperty("shareToken").GetString()!;
            var shareUrl = json.RootElement.GetProperty("shareUrl").GetString();

            Assert.Equal($"{PublicTunnelUrl}/share/{Uri.EscapeDataString(shareId)}?token={token}", shareUrl);
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("tunnelError").ValueKind);
            Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
        }

        Assert.False(string.IsNullOrWhiteSpace(shareId));
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public async Task Create_WhenTunnelUnavailable_FallsBackToLocalUrl_AndReportsTunnelError()
    {
        var db = await SeedAsync();
        await using var host = await StartHostAsync(
            db, new FakeShareHostProcess(LocalShareHostUrl), new FakeTransport(baseUrl: null, error: "ngrok authtoken missing"));
        using var client = AuthorizedClient(host);

        using var created = await client.PostAsJsonAsync("/api/shares", CreatePayload());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var shareId = json.RootElement.GetProperty("shareId").GetString()!;
        var token = json.RootElement.GetProperty("shareToken").GetString()!;
        var shareUrl = json.RootElement.GetProperty("shareUrl").GetString();

        // Still an absolute, functional loopback viewer URL rather than a
        // relative path; the failure is surfaced instead of hidden.
        Assert.Equal($"{LocalShareHostUrl}/share/{Uri.EscapeDataString(shareId)}?token={token}", shareUrl);
        var tunnelError = json.RootElement.GetProperty("tunnelError").GetString();
        Assert.False(string.IsNullOrWhiteSpace(tunnelError));
        // The owner UI only surfaces tunnelError when the status says so.
        Assert.Equal("tunnel-error", json.RootElement.GetProperty("status").GetString());
    }

    private static object CreatePayload() => new
    {
        mode = "Snapshot",
        memberDocumentIds = new[] { "doc" },
        allowOriginalDownload = false,
        allowPdfDownload = false,
    };

    private async Task<WebAppHost> StartHostAsync(AppDb db, IShareHostProcess shareHost, IShareTransport transport) =>
        await WebAppHost.StartAsync(database: db, configureServices: services =>
        {
            services.AddSingleton(new ShareTokenService(_sharesRoot));
            services.AddSingleton(shareHost);
            services.AddSingleton(transport);
        });

    private async Task<AppDb> SeedAsync()
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Share link" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "Source" });
        var path = Path.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(path, "body");
        await new DocumentRepository(db).CreateAsync(new Document { Id = "doc", CurrentPath = path, Filename = "notes.md", Extension = ".md", DocumentType = "markdown" });
        return db;
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeShareHostProcess(string localBaseUrl) : IShareHostProcess
    {
        public string? LocalBaseUrl => localBaseUrl;
        public Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(localBaseUrl);
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTransport(string? baseUrl, string? error) : IShareTransport
    {
        public Task<PublicEndpoint> StartAsync(string localUrl, string secretToken, CancellationToken cancellationToken = default)
        {
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }
            return Task.FromResult(new PublicEndpoint { BaseUrl = baseUrl!, SecretPath = string.Empty });
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public TransportStatus GetStatus() => new()
        {
            State = error == null ? TransportStatusState.Running : TransportStatusState.Failed,
            PublicUrl = baseUrl,
            ErrorMessage = error,
        };
    }
}
