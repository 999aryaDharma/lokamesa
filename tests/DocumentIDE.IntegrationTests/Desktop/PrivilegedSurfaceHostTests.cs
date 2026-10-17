using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Search;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// G2/G3/G4/G8 — the shipped desktop host (<c>WebAppHost</c>) must actually serve
/// the privileged surfaces the web client calls, behind the FND-02/QA-03 guard:
/// batch apply/undo/history, realtime search, Word open and PDF finalization.
/// Before the fix these routes were deliberately absent from the desktop
/// controller allow-list, so every call was a 404.
/// </summary>
public sealed class PrivilegedSurfaceHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "privileged-surfaces-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SearchSurface_IsReachable_AndReturnsIndexedMatch()
    {
        var (db, docId, _) = await SeedAsync("markdown-token-xyz");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var emptyDoc = await client.GetAsync("/api/search?q=");
        Assert.Equal(HttpStatusCode.OK, emptyDoc.StatusCode);

        using var response = await client.GetAsync("/api/search?q=markdown-token-xyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = json.RootElement.GetProperty("results");
        Assert.Equal(docId, Assert.Single(results.EnumerateArray()).GetProperty("documentId").GetString());
        Assert.True(json.RootElement.TryGetProperty("totalCount", out _));
        Assert.True(json.RootElement.TryGetProperty("executionTimeMs", out _));
    }

    [Fact]
    public async Task WordSurface_IsReachable_AndValidatesRequest()
    {
        var (db, _, _) = await SeedAsync("word-surface");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        // An empty body reaches the controller (route is served) and is rejected by
        // its own validation, which is distinct from the pre-fix missing route (404).
        using var response = await client.PostAsJsonAsync("/api/word/open", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("status", out _), "Word launch result body must be returned.");
    }

    [Fact]
    public async Task PdfSurface_IsReachable_AndReportsTypedStatuses()
    {
        var (db, _, _) = await SeedAsync("pdf-surface");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var missingSource = await client.PostAsJsonAsync("/api/pdf/export", new { sourceDocxPath = Path.Combine(_root, "absent.docx") });
        Assert.Equal(HttpStatusCode.OK, missingSource.StatusCode);
        using var json = JsonDocument.Parse(await missingSource.Content.ReadAsStringAsync());
        Assert.Equal("SourceNotFound", json.RootElement.GetProperty("status").GetString());

        using var invalid = await client.PostAsJsonAsync("/api/pdf/export", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var invalidMerge = await client.PostAsJsonAsync("/api/pdf/merge", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalidMerge.StatusCode);
    }

    [Fact]
    public async Task BatchApplySurface_AppliesReviewedMutation_ThenUndoRestores_AndHistoryRecords()
    {
        var body = "target TARGET target";
        var (db, _, path) = await SeedAsync(body);
        await File.WriteAllTextAsync(path, body);
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        var planId = await PreviewAsync(client, db, "target");

        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
            using var json = JsonDocument.Parse(await apply.Content.ReadAsStringAsync());
            Assert.Equal("Completed", json.RootElement.GetProperty("status").GetString());
            var file = Assert.Single(json.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal("Success", file.GetProperty("outcome").GetString());
        }

        Assert.Equal("new TARGET new", await File.ReadAllTextAsync(path));

        using (var history = await client.GetAsync($"/api/batch-operations/{planId}/history"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            using var json = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
            var applyRecords = json.RootElement.GetProperty("apply").EnumerateArray().ToList();
            Assert.Equal("Success", Assert.Single(applyRecords).GetProperty("outcome").GetString());
        }

        using (var undo = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/undo", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
            using var json = JsonDocument.Parse(await undo.Content.ReadAsStringAsync());
            Assert.Equal("Completed", json.RootElement.GetProperty("status").GetString());
            Assert.Equal("Restored", Assert.Single(json.RootElement.GetProperty("files").EnumerateArray()).GetProperty("outcome").GetString());
        }

        Assert.Equal(body, await File.ReadAllTextAsync(path));

        using (var history = await client.GetAsync($"/api/batch-operations/{planId}/history"))
        {
            using var json = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
            Assert.Single(json.RootElement.GetProperty("undo").EnumerateArray());
        }
    }

    [Fact]
    public async Task BatchApply_UnknownPlan_Returns404_AndExpiredPlan_Returns409()
    {
        var (db, _, path) = await SeedAsync("target content");
        await File.WriteAllTextAsync(path, "target content");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using (var unknown = await client.PostAsJsonAsync("/api/batch-operations/does-not-exist/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        var planId = await PreviewAsync(client, db, "target");
        var plans = new BatchPlanRepository(db);
        var plan = (await plans.GetByIdAsync(planId))!;
        plan.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await plans.SavePlanAsync(plan);

        using var expired = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { });
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal("target content", await File.ReadAllTextAsync(path));
    }

    private async Task<(AppDb Db, string DocumentId, string Path)> SeedAsync(string documentBody)
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Privileged surfaces" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "Source" });
        var path = Path.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(path, documentBody);
        const string docId = "doc";
        await new DocumentRepository(db).CreateAsync(new Document { Id = docId, CurrentPath = path, Filename = "notes.md", Extension = ".md", DocumentType = "markdown" });

        // The row-aligned FTS index rides on the same AppDb the host serves.
        await new FtsIndex(db).IndexDocumentAsync(new FtsIndexEntry(docId, "notes.md", documentBody));
        return (db, docId, path);
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    private static async Task<string> PreviewAsync(HttpClient client, AppDb db, string find)
    {
        using var created = await client.PostAsJsonAsync("/api/batch-operations/preview", new
        {
            scope = new { workspaceId = "ws", type = 0, documentIds = new[] { "doc" }, includeSubfolders = true, excludedBranches = Array.Empty<string>() },
            find,
            replacement = "new"
        });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        using var creationJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var planId = creationJson.RootElement.GetProperty("planId").GetString()!;

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var response = await client.GetAsync($"/api/batch-operations/{planId}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var status = json.RootElement.GetProperty("status").GetString();
            if (status is not ("Pending" or "Running")) return planId;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Preview did not complete in time.");
    }
}
