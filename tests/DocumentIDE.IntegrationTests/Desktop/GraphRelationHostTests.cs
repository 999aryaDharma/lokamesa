using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// Regression pin for "the graph always shows nodes with no edges".
///
/// Nothing in production ever wrote the <c>relations</c> table: the Markdown link
/// indexer and <c>ReplaceDerivedRelationsAsync</c> had no caller, so
/// <c>GET /api/workspaces/{id}/graph</c> always returned <c>edges: []</c> and
/// adding <c>[[wikilinks]]</c>/links never created an edge. Reconciliation now
/// derives explicit relations from Markdown source as part of the same pipeline
/// that makes an attached folder searchable.
/// </summary>
[Collection("HostFixtures")]
public sealed class GraphRelationHostTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "graph-relation-" + Guid.NewGuid().ToString("N"));

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
    public async Task AttachThenReconcile_DerivesGraphEdgesFromMarkdownLinks()
    {
        Directory.CreateDirectory(_root);
        string docsDir = Path.Combine(_root, "docs");
        Directory.CreateDirectory(docsDir);
        File.WriteAllText(
            Path.Combine(docsDir, "alpha.md"),
            "# Alpha\n\nSee [Beta](beta.md) and also [[beta]].");
        File.WriteAllText(Path.Combine(docsDir, "beta.md"), "# Beta\n\nBody.");

        var db = new AppDb(Path.Combine(_root, "app.db"));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);
        var workspaceId = WorkspaceBootstrap.DefaultWorkspaceId;

        using (var attach = await client.PostAsJsonAsync(
            $"/api/workspaces/{workspaceId}/folder-sources",
            new { folderPath = docsDir }))
        {
            Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        }

        using (var reconcile = await client.PostAsync(
            $"/api/workspaces/{workspaceId}/reconcile", content: null))
        {
            Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);
        }

        using var graph = await client.GetAsync($"/api/workspaces/{workspaceId}/graph");
        Assert.Equal(HttpStatusCode.OK, graph.StatusCode);
        using var json = JsonDocument.Parse(await graph.Content.ReadAsStringAsync());

        var nodes = json.RootElement.GetProperty("nodes").EnumerateArray().ToList();
        var alphaId = nodes.Single(n => n.GetProperty("displayName").GetString() == "alpha.md")
            .GetProperty("documentId").GetString();
        var betaId = nodes.Single(n => n.GetProperty("displayName").GetString() == "beta.md")
            .GetProperty("documentId").GetString();

        var edges = json.RootElement.GetProperty("edges").EnumerateArray().ToList();
        Assert.Contains(edges, e =>
            e.GetProperty("sourceDocumentId").GetString() == alphaId &&
            e.GetProperty("targetDocumentId").GetString() == betaId &&
            e.GetProperty("relationOrigin").GetString() == "Explicit");
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", "https://appassets.html2sav");
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }
}
