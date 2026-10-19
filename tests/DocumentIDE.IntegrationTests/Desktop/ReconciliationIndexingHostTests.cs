using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// Regression pin for "search finds nothing after attaching a folder".
///
/// Attaching a folder source only ever inserted the <c>folder_sources</c> row;
/// nothing scanned the folder into <c>documents</c>, and nothing indexed those
/// documents into <c>search_content</c>. The frontend never called
/// <c>POST /api/workspaces/{id}/reconcile</c> either, so a freshly attached
/// folder was invisible everywhere: the document list, the canvas, and search.
/// </summary>
[Collection("HostFixtures")]
public sealed class ReconciliationIndexingHostTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "reconcile-index-" + Guid.NewGuid().ToString("N"));

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
    public async Task AttachThenReconcile_MakesTheDocumentSearchable()
    {
        Directory.CreateDirectory(_root);
        string docsDir = Path.Combine(_root, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(Path.Combine(docsDir, "notes.md"), "# Notes\n\nThe quokka is a marsupial.");

        var db = new AppDb(Path.Combine(_root, "app.db"));
        await using var host = await WebAppHost.StartAsync(database: db);

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", "https://appassets.html2sav");

        using var attach = await client.PostAsJsonAsync(
            $"{host.ApiUrl}/api/workspaces/{WorkspaceBootstrap.DefaultWorkspaceId}/folder-sources",
            new { folderPath = docsDir });
        Assert.Equal(HttpStatusCode.OK, attach.StatusCode);

        // Before reconciliation: no cached documents, and the search index has
        // nothing to find - this is the state the user saw.
        using var beforeDocs = await client.GetAsync(
            $"{host.ApiUrl}/api/workspaces/{WorkspaceBootstrap.DefaultWorkspaceId}/documents");
        using var beforeJson = JsonDocument.Parse(await beforeDocs.Content.ReadAsStringAsync());
        Assert.Empty(beforeJson.RootElement.EnumerateArray());

        using var reconcile = await client.PostAsync(
            $"{host.ApiUrl}/api/workspaces/{WorkspaceBootstrap.DefaultWorkspaceId}/reconcile", content: null);
        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);

        using var afterDocs = await client.GetAsync(
            $"{host.ApiUrl}/api/workspaces/{WorkspaceBootstrap.DefaultWorkspaceId}/documents");
        using var afterJson = JsonDocument.Parse(await afterDocs.Content.ReadAsStringAsync());
        Assert.Single(afterJson.RootElement.EnumerateArray());

        using var search = await client.GetAsync(
            $"{host.ApiUrl}/api/search?q=quokka&workspaceId={WorkspaceBootstrap.DefaultWorkspaceId}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchJson = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        Assert.True(
            searchJson.RootElement.GetProperty("results").GetArrayLength() > 0,
            "Expected the reconciled document to be searchable after attach+reconcile.");
    }
}
