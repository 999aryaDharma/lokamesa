using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// Regression pin for the shipped-app failure "attach folder -> Failed to fetch".
///
/// The shell boots the "default" workspace id. On a fresh database no such row
/// existed, so the folder-source insert violated its foreign key, the API
/// returned 500, and the exception handler cleared the CORS headers - the UI
/// only saw "Failed to fetch". The host must seed the workspace before serving.
/// </summary>
[Collection("HostFixtures")]
public sealed class WebAppHostBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "host-bootstrap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task FreshDatabase_SeedsTheDefaultWorkspace_AndAcceptsAFolderAttachment()
    {
        Directory.CreateDirectory(_root);
        string docs = Path.Combine(_root, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "a.md"), "# a");
        File.WriteAllText(Path.Combine(docs, "b.md"), "# b");

        var db = new AppDb(Path.Combine(_root, "app.db"));
        await using var host = await WebAppHost.StartAsync(database: db);

        Assert.NotNull(await new WorkspaceRepository(db).GetByIdAsync(WorkspaceBootstrap.DefaultWorkspaceId));

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", "https://appassets.html2sav");

        using var response = await client.PostAsJsonAsync(
            $"{host.ApiUrl}/api/workspaces/{WorkspaceBootstrap.DefaultWorkspaceId}/folder-sources",
            new { folderPath = docs });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "https://appassets.html2sav",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("supportedFileCount").GetInt32());
        Assert.Equal(
            docs,
            json.RootElement.GetProperty("folderSource").GetProperty("canonicalPath").GetString());
    }
}
