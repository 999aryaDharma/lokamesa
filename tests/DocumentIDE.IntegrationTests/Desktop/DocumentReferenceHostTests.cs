using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// UI-05 B — <c>DELETE /api/documents/{documentId}/reference</c> removes only the
/// database reference. The physical file is never touched: the test asserts it
/// still exists and hashes byte-identically after the call. Unknown ids and the
/// privileged guard are typed outcomes.
/// </summary>
[Collection("HostFixtures")]
public sealed class DocumentReferenceHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "doc-reference-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task RemovesTheReference_AndLeavesTheFileByteIdentical()
    {
        const string body = "# Keep this file\nRemove only the database reference.";
        var db = await SeedAsync("doc-1", "keep.md", body);
        var path = Path.Combine(_root, "keep.md");
        var hashBefore = ComputeSha256(path);
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.DeleteAsync("/api/documents/doc-1/reference");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal("doc-1", json.RootElement.GetProperty("documentId").GetString());
            Assert.True(json.RootElement.GetProperty("removed").GetBoolean());
        }

        Assert.Null(await new DocumentRepository(db).GetByIdAsync("doc-1"));
        Assert.True(File.Exists(path));
        Assert.Equal(hashBefore, ComputeSha256(path));
        Assert.Equal(body, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task RemovingAnUnknownReference_IsTyped()
    {
        var db = await SeedAsync("doc-1", "keep.md", "body");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.DeleteAsync("/api/documents/does-not-exist/reference");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UnknownDocument", json.RootElement.GetProperty("errorCode").GetString());
        Assert.NotNull(await new DocumentRepository(db).GetByIdAsync("doc-1"));
    }

    [Fact]
    public async Task Guards_401WithoutToken_403HostileOrigin_403HostileHost()
    {
        var db = await SeedAsync("doc-1", "keep.md", "body");
        await using var host = await WebAppHost.StartAsync(database: db);

        using (var unauthorized = Client(host, token: null, AllowedOrigin))
        using (var response = await unauthorized.DeleteAsync("/api/documents/doc-1/reference"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var hostileOrigin = Client(host, host.SessionToken, "http://malicious.example"))
        using (var response = await hostileOrigin.DeleteAsync("/api/documents/doc-1/reference"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var hostileHost = Client(host, host.SessionToken, AllowedOrigin))
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/documents/doc-1/reference");
            request.Headers.Host = "attacker.example";
            using var response = await hostileHost.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // The guard rejected every call before the controller ran.
        Assert.NotNull(await new DocumentRepository(db).GetByIdAsync("doc-1"));
    }

    private async Task<AppDb> SeedAsync(string documentId, string fileName, string body)
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Reference" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource
        {
            Id = "folder",
            WorkspaceId = "ws",
            CanonicalPath = _root,
            DisplayName = "Source",
        });
        var path = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(path, body);
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = documentId,
            CurrentPath = path,
            Filename = fileName,
            Extension = ".md",
            DocumentType = "markdown",
        });
        return db;
    }

    private static HttpClient Client(WebAppHost host, string? token, string origin)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        if (token != null)
        {
            client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", token);
        }
        client.DefaultRequestHeaders.Add("Origin", origin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    private static HttpClient AuthorizedClient(WebAppHost host) => Client(host, host.SessionToken, AllowedOrigin);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
