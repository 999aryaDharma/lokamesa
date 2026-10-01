using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;

namespace DocumentIDE.IntegrationTests.Desktop;

public sealed class BatchPreviewHostTests
{
    [Fact]
    public async Task NativeHost_AuthorizesPreview_WithoutSourceMutation_OrUnrelatedControllers()
    {
        var root = Path.Combine(Path.GetTempPath(), "desktop-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = new AppDb(Path.Combine(root, "state.db"));
            await db.InitializeAsync();
            await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Desktop preview" });
            await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = root, DisplayName = "Source" });
            var path = Path.Combine(root, "notes.md");
            await File.WriteAllTextAsync(path, "target TARGET target");
            var before = await File.ReadAllBytesAsync(path);
            await new DocumentRepository(db).CreateAsync(new Document { Id = "doc", CurrentPath = path, Filename = "notes.md", Extension = ".md" });

            await using var host = await WebAppHost.StartAsync(database: db);
            using var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
            Assert.True(host.Port > 0);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/batch-operations/workspaces")).StatusCode);
            client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
            foreach (var origin in new[] { "https://attacker.example", "null", "https://sub.appassets.html2sav", "http://appassets.html2sav", "http://localhost:5173", "https://appassets.html2sav/", "https://appassets.html2sav:443" })
            {
                using var hostileOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/batch-operations/workspaces");
                hostileOrigin.Headers.Add("Origin", origin);
                hostileOrigin.Headers.Add("Sec-Fetch-Site", "cross-site");
                using var rejected = await client.SendAsync(hostileOrigin);
                Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
                Assert.False(rejected.Headers.Contains("Access-Control-Allow-Origin"));
            }
            using (var hostileHost = new HttpRequestMessage(HttpMethod.Get, "/api/batch-operations/workspaces"))
            {
                hostileHost.Headers.Host = "attacker.example";
                hostileHost.Headers.Add("Origin", "https://appassets.html2sav");
                hostileHost.Headers.Add("Sec-Fetch-Site", "cross-site");
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(hostileHost)).StatusCode);
            }
            client.DefaultRequestHeaders.Add("Origin", "https://appassets.html2sav");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
            var workspaces = await client.GetAsync("/api/batch-operations/workspaces");
            Assert.Equal(HttpStatusCode.OK, workspaces.StatusCode);
            Assert.Equal("https://appassets.html2sav", Assert.Single(workspaces.Headers.GetValues("Access-Control-Allow-Origin")));
            client.DefaultRequestHeaders.Remove("X-DocumentIDE-Session-Token");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/batch-operations/workspaces")).StatusCode);
            client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-startup-secret");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/batch-operations/workspaces")).StatusCode);
            client.DefaultRequestHeaders.Remove("X-DocumentIDE-Session-Token");
            using (var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/batch-operations/preview"))
            {
                preflight.Headers.Add("Access-Control-Request-Method", "POST");
                preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-documentide-session-token");
                using var response = await client.SendAsync(preflight);
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.Equal("https://appassets.html2sav", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
                Assert.Contains("X-DocumentIDE-Session-Token", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Headers")));
                Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            }
            client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
            using var workspaceJson = JsonDocument.Parse(await workspaces.Content.ReadAsStringAsync());
            Assert.Equal("ws", Assert.Single(workspaceJson.RootElement.EnumerateArray()).GetProperty("id").GetString());
            var created = await client.PostAsJsonAsync("/api/batch-operations/preview", new { scope = new { workspaceId = "ws", type = 0, documentIds = new[] { "doc" }, includeSubfolders = true, excludedBranches = Array.Empty<string>() }, find = "target", replacement = "new" });
            Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
            Assert.Equal("https://appassets.html2sav", Assert.Single(created.Headers.GetValues("Access-Control-Allow-Origin")));
            using var creationJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var planId = creationJson.RootElement.GetProperty("planId").GetString();
            JsonElement preview = default;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var response = await client.GetAsync($"/api/batch-operations/{planId}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                preview = json.RootElement.Clone();
                if (preview.GetProperty("status").GetString() is not ("Pending" or "Running")) break;
                await Task.Delay(50);
            }
            Assert.Equal("Completed", preview.GetProperty("status").GetString());
            Assert.Equal(2, preview.GetProperty("occurrenceCount").GetInt32());
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/word/open", new { documentId = "doc" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
