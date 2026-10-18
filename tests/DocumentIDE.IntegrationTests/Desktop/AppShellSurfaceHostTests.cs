using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// Contract surfaces the shipped (Lane C) app shell calls through the privileged
/// host: workspace bootstrap (documents + folder sources), the workspace graph
/// envelope, and Markdown source load/save. Each route was previously absent
/// from any host, so the mounted UI could not actually reach it.
///
/// Graph truthfulness (master §27.2): relation origin strings pass through
/// unchanged so explicit/manual edges stay solid and inferred/semantic edges
/// stay dashed — the view never falsifies a relationship.
/// </summary>
[Collection("HostFixtures")]
public sealed class AppShellSurfaceHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "app-shell-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task WorkspaceBootstrapSurfaces_AreReachable()
    {
        var db = await SeedAsync(markdownBody: "hello");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using (var documents = await client.GetAsync("/api/workspaces/ws/documents"))
        {
            Assert.Equal(HttpStatusCode.OK, documents.StatusCode);
            using var json = JsonDocument.Parse(await documents.Content.ReadAsStringAsync());
            var ids = json.RootElement.EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToList();
            Assert.Contains("doc", ids);
        }

        using var folderSources = await client.GetAsync("/api/workspaces/ws/folder-sources");
        Assert.Equal(HttpStatusCode.OK, folderSources.StatusCode);
        using var folderJson = JsonDocument.Parse(await folderSources.Content.ReadAsStringAsync());
        Assert.Contains(
            folderJson.RootElement.EnumerateArray(),
            source => source.GetProperty("id").GetString() == "folder");
    }

    [Fact]
    public async Task GraphSurface_ReturnsNodesAndTruthfulEdgeOrigins()
    {
        var db = await SeedAsync(markdownBody: "hello");
        // A second document plus one explicit and one inferred relation.
        await SeedDocumentAsync(db, "other", Path.Combine(_root, "other.md"), "other");
        var relations = new RelationRepository(db);
        await relations.AddAsync(new Relation
        {
            Id = "rel-explicit",
            WorkspaceId = "ws",
            SourceDocumentId = "doc",
            TargetDocumentId = "other",
            RelationType = RelationType.Wikilink,
            RelationOrigin = RelationOrigin.Explicit,
        });
        await relations.AddAsync(new Relation
        {
            Id = "rel-inferred",
            WorkspaceId = "ws",
            SourceDocumentId = "other",
            TargetDocumentId = "doc",
            RelationType = RelationType.SemanticSuggestion,
            RelationOrigin = RelationOrigin.Inferred,
            Confidence = 0.42,
        });

        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/workspaces/ws/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var nodes = json.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        Assert.Contains(nodes, n => n.GetProperty("documentId").GetString() == "doc");
        Assert.Contains(nodes, n => n.GetProperty("documentId").GetString() == "other");
        Assert.Contains(nodes, n => n.GetProperty("displayName").GetString() == "notes.md");

        var edges = json.RootElement.GetProperty("edges").EnumerateArray().ToList();
        Assert.Equal(2, edges.Count);
        var explicitEdge = edges.Single(e => e.GetProperty("sourceDocumentId").GetString() == "doc");
        Assert.Equal("Explicit", explicitEdge.GetProperty("relationOrigin").GetString());
        var inferredEdge = edges.Single(e => e.GetProperty("sourceDocumentId").GetString() == "other");
        Assert.Equal("Inferred", inferredEdge.GetProperty("relationOrigin").GetString());
    }

    [Fact]
    public async Task MarkdownSurface_LoadsSource_AndSavesThroughSafeWrite()
    {
        var db = await SeedAsync(markdownBody: "hello world");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);
        var path = Path.Combine(_root, "notes.md");

        using (var loaded = await client.GetAsync("/api/documents/doc/markdown"))
        {
            Assert.Equal(HttpStatusCode.OK, loaded.StatusCode);
            using var json = JsonDocument.Parse(await loaded.Content.ReadAsStringAsync());
            Assert.Equal("doc", json.RootElement.GetProperty("documentId").GetString());
            Assert.Equal("hello world", json.RootElement.GetProperty("source").GetString());
        }

        using (var saved = await client.PutAsJsonAsync("/api/documents/doc/markdown", new { source = "# Title" }))
        {
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            using var json = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
            Assert.Equal("doc", json.RootElement.GetProperty("documentId").GetString());
            Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("savedAt").GetString()));
        }

        Assert.Equal("# Title", await File.ReadAllTextAsync(path));

        using (var unknown = await client.GetAsync("/api/documents/missing/markdown"))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
    }

    [Fact]
    public async Task MarkdownSurface_RejectsNonMarkdownDocument()
    {
        var db = await SeedAsync(markdownBody: "hello");
        var docxPath = Path.Combine(_root, "letter.docx");
        await File.WriteAllBytesAsync(docxPath, new byte[] { 1, 2, 3 });
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = "docxdoc",
            CurrentPath = docxPath,
            Filename = "letter.docx",
            Extension = ".docx",
            DocumentType = "docx",
        });
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/docxdoc/markdown");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public void SessionBridge_InjectsWorkspaceIdApiUrlAndToken()
    {
        var script = new LocalSessionBridge("tok-123") { WorkspaceId = "ws-9" }
            .BuildInjectionScript("http://127.0.0.1:5311");

        Assert.Contains("__DOCUMENTIDE_API_URL__", script);
        Assert.Contains("http://127.0.0.1:5311", script);
        Assert.Contains("__DOCUMENTIDE_SESSION_TOKEN__", script);
        Assert.Contains("__DOCUMENTIDE_WORKSPACE_ID__", script);
        Assert.Contains("ws-9", script);
    }

    private async Task<AppDb> SeedAsync(string markdownBody)
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "App shell" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "Source" });
        await SeedDocumentAsync(db, "doc", Path.Combine(_root, "notes.md"), markdownBody);
        return db;
    }

    private static async Task SeedDocumentAsync(AppDb db, string id, string path, string body)
    {
        await File.WriteAllTextAsync(path, body);
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = id,
            CurrentPath = path,
            Filename = Path.GetFileName(path),
            Extension = ".md",
            DocumentType = "markdown",
        });
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }
}
