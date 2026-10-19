using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// G5 — the share lifecycle (create Snapshot/Live, stop, revoke, regenerate,
/// membership) must be reachable through the privileged localhost host behind
/// the FND-02/QA-03 guard. Before the fix no share controller existed anywhere:
/// <c>ShareManager</c>/<c>ShareDialog</c> were callback-driven and no create/
/// revoke/member-management endpoint was served (readiness report §5 G5).
///
/// The owner-side state is persisted via <see cref="DocumentIDE.Core.Domain.Repositories.IShareRepository"/>;
/// stop is modelled as a past <c>ExpiresAt</c>, revoke as a cleared token hash,
/// regenerate as a rotated token (spec §17/§18). Raw tokens are returned only at
/// create/regenerate time — never re-exposed by read endpoints.
/// </summary>
[Collection("HostFixtures")]
public sealed class ShareLifecycleHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "share-lifecycle-" + Guid.NewGuid().ToString("N"));
    private readonly string _sharesRoot = Path.Combine(Path.GetTempPath(), "share-lifecycle-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(_root);
        TryDelete(_sharesRoot);
    }

    [Fact]
    public async Task CreateSnapshot_ReturnsActiveState_ThenStopAndRevokeAreObservable()
    {
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);
        using var client = AuthorizedClient(host);

        string shareId;
        using (var created = await client.PostAsJsonAsync("/api/shares", new
        {
            mode = "Snapshot",
            memberDocumentIds = new[] { "doc" },
            allowOriginalDownload = false,
            allowPdfDownload = false,
        }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var state = json.RootElement;
            shareId = state.GetProperty("shareId").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(shareId));
            Assert.Equal("Snapshot", state.GetProperty("mode").GetString());
            Assert.Equal("active", state.GetProperty("status").GetString());
            Assert.False(state.GetProperty("revoked").GetBoolean());
            // Raw token is returned exactly once, at creation.
            Assert.False(string.IsNullOrWhiteSpace(state.GetProperty("shareToken").GetString()));
            var member = Assert.Single(state.GetProperty("members").EnumerateArray());
            Assert.Equal("doc", member.GetProperty("documentId").GetString());
        }

        // A read never re-exposes the raw token (only its hash is persisted).
        using (var fetched = await client.GetAsync($"/api/shares/{shareId}"))
        {
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            using var json = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync());
            Assert.Equal(string.Empty, json.RootElement.GetProperty("shareToken").GetString());
            Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
        }

        using (var stopped = await client.PostAsJsonAsync($"/api/shares/{shareId}/stop", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
            using var json = JsonDocument.Parse(await stopped.Content.ReadAsStringAsync());
            Assert.Equal("stopped", json.RootElement.GetProperty("status").GetString());
        }

        using (var revoked = await client.PostAsJsonAsync($"/api/shares/{shareId}/revoke", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
            using var json = JsonDocument.Parse(await revoked.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("revoked").GetBoolean());
            Assert.Equal("stopped", json.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Regenerate_RotatesToken_AndReactivatesAStoppedShare()
    {
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);
        using var client = AuthorizedClient(host);

        var shareId = await CreateSnapshotAsync(client);
        using var stopped = await client.PostAsJsonAsync($"/api/shares/{shareId}/stop", new { });
        Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);

        using var regenerated = await client.PostAsJsonAsync($"/api/shares/{shareId}/regenerate", new { });
        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        using var json = JsonDocument.Parse(await regenerated.Content.ReadAsStringAsync());
        Assert.Equal("active", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("revoked").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("shareToken").GetString()));

        // The rotated hash is persisted, so the new token verifies and the old one
        // (unknown to this process) does not.
        var persisted = await new ShareRepository(db).GetByIdAsync(shareId);
        Assert.NotNull(persisted);
        Assert.False(string.IsNullOrEmpty(persisted!.TokenHash));
    }

    [Fact]
    public async Task StopRevokeRegenerate_UpdateTheOnDiskAccessPolicy()
    {
        // B3: ShareHost is a separate process that only reads the share package,
        // so every lifecycle transition must rewrite the on-disk access policy or
        // a stopped/revoked link would keep serving.
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);
        using var client = AuthorizedClient(host);
        var policyStore = new ShareTokenService(_sharesRoot);

        var shareId = await CreateSnapshotAsync(client);

        var afterCreate = policyStore.ReadAccessPolicy(shareId);
        Assert.NotNull(afterCreate);
        Assert.False(string.IsNullOrWhiteSpace(afterCreate!.TokenHash));
        Assert.Null(afterCreate.ExpiresAt);

        using (var stopped = await client.PostAsJsonAsync($"/api/shares/{shareId}/stop", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
        }
        var afterStop = policyStore.ReadAccessPolicy(shareId);
        Assert.NotNull(afterStop);
        Assert.NotNull(afterStop!.ExpiresAt);
        Assert.True(DateTimeOffset.Parse(afterStop.ExpiresAt!) <= DateTimeOffset.UtcNow);

        using (var regenerated = await client.PostAsJsonAsync($"/api/shares/{shareId}/regenerate", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        }
        var afterRegen = policyStore.ReadAccessPolicy(shareId);
        Assert.NotNull(afterRegen);
        Assert.Null(afterRegen!.ExpiresAt);
        Assert.False(string.IsNullOrWhiteSpace(afterRegen.TokenHash));
        Assert.NotEqual(afterStop.TokenHash, afterRegen.TokenHash);

        using (var revoked = await client.PostAsJsonAsync($"/api/shares/{shareId}/revoke", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        }
        var afterRevoke = policyStore.ReadAccessPolicy(shareId);
        Assert.NotNull(afterRevoke);
        Assert.True(string.IsNullOrEmpty(afterRevoke!.TokenHash));
    }

    [Fact]
    public async Task List_ReturnsCreatedShares()
    {
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);
        using var client = AuthorizedClient(host);

        var shareId = await CreateSnapshotAsync(client);
        using var list = await client.GetAsync("/api/shares");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        // shareApi.listShares consumes a bare array.
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        var shares = json.RootElement.EnumerateArray().ToList();
        Assert.Contains(shares, s => s.GetProperty("shareId").GetString() == shareId);
    }

    [Fact]
    public async Task LiveShare_SupportsExplicitMembershipChanges()
    {
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);
        using var client = AuthorizedClient(host);

        string shareId;
        using (var created = await client.PostAsJsonAsync("/api/shares", new
        {
            mode = "Live",
            memberDocumentIds = new[] { "doc" },
            allowOriginalDownload = false,
            allowPdfDownload = false,
        }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal("Live", json.RootElement.GetProperty("mode").GetString());
            shareId = json.RootElement.GetProperty("shareId").GetString()!;
        }

        // A second explicit member is added through the privileged endpoint.
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder2", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "Second" });
        await new DocumentRepository(db).CreateAsync(new Document { Id = "doc2", CurrentPath = Path.Combine(_root, "second.md"), Filename = "second.md", Extension = ".md", DocumentType = "markdown" });
        await File.WriteAllTextAsync(Path.Combine(_root, "second.md"), "second body");

        using (var added = await client.PostAsJsonAsync($"/api/shares/{shareId}/members", new { documentId = "doc2" }))
        {
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            using var json = JsonDocument.Parse(await added.Content.ReadAsStringAsync());
            var members = json.RootElement.GetProperty("members").EnumerateArray().ToList();
            Assert.Contains(members, m => m.GetProperty("documentId").GetString() == "doc2");
        }

        using (var removed = await client.DeleteAsync($"/api/shares/{shareId}/members/doc2"))
        {
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            using var json = JsonDocument.Parse(await removed.Content.ReadAsStringAsync());
            var members = json.RootElement.GetProperty("members").EnumerateArray().ToList();
            Assert.DoesNotContain(members, m => m.GetProperty("documentId").GetString() == "doc2");
        }
    }

    [Fact]
    public async Task Create_WithNoMembers_Returns400_AndUnknownShareReturns404()
    {
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);
        using var client = AuthorizedClient(host);

        using (var empty = await client.PostAsJsonAsync("/api/shares", new
        {
            mode = "Snapshot",
            memberDocumentIds = Array.Empty<string>(),
            allowOriginalDownload = false,
            allowPdfDownload = false,
        }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        }

        using (var unknown = await client.GetAsync("/api/shares/does-not-exist"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        using (var unknownStop = await client.PostAsJsonAsync("/api/shares/does-not-exist/stop", new { }))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknownStop.StatusCode);
        }
    }

    [Fact]
    public async Task Mutation_RequiresThePrivilegedGuard()
    {
        var (db, _) = await SeedAsync("share body");
        await using var host = await StartHostAsync(db);

        // No session token / Origin: the FND-02/QA-03 guard rejects the request
        // before the controller can create a share.
        using var unguarded = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        using var response = await unguarded.PostAsJsonAsync("/api/shares", new
        {
            mode = "Snapshot",
            memberDocumentIds = new[] { "doc" },
            allowOriginalDownload = false,
            allowPdfDownload = false,
        });
        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Expected the privileged guard to reject the request, got {(int)response.StatusCode}.");
    }

    private async Task<WebAppHost> StartHostAsync(AppDb db) =>
        await WebAppHost.StartAsync(database: db, configureServices: services =>
        {
            // Isolate share artifacts under a temp root so the test never writes
            // into the real %LOCALAPPDATA%\DocumentIDE\shares store.
            services.AddSingleton(new ShareTokenService(_sharesRoot));
        });

    private async Task<string> CreateSnapshotAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/shares", new
        {
            mode = "Snapshot",
            memberDocumentIds = new[] { "doc" },
            allowOriginalDownload = false,
            allowPdfDownload = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("shareId").GetString()!;
    }

    private async Task<(AppDb Db, string DocumentId)> SeedAsync(string documentBody)
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Share lifecycle" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "Source" });
        var path = Path.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(path, documentBody);
        const string docId = "doc";
        await new DocumentRepository(db).CreateAsync(new Document { Id = docId, CurrentPath = path, Filename = "notes.md", Extension = ".md", DocumentType = "markdown" });
        return (db, docId);
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
}
