extern alias ShareHostApp;

using System.Net;
using System.Text.Json;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// SHR-06 — Share-Scoped Search &amp; Graph: the public viewer may search and
/// browse the graph only over the explicit share membership. Scope is enforced
/// server-side against the frozen manifest — never "whole workspace then
/// UI-filtered". Nonmember content is unreachable, edges touching a private
/// node are omitted, and payloads carry no local paths.
/// </summary>
public sealed class ShareScopeTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokens = new();
    private readonly List<string> _createdShareRoots = new();
    private string? _token;

    public ShareScopeTests(WebApplicationFactory<ShareHostApp::Program> factory)
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

    // RED requirement: search cannot find nonmember content.

    [Fact]
    public async Task Search_CannotFindNonmemberContent()
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        WriteManifest(shareId, root, new[]
        {
            ("doc-1", "member-notes.md"),
        });
        WritePreview(root, "doc-1", "<h1>Member heading</h1><p>SunshineMember alpha body</p>");

        // Nonmember content that exists on disk (stray preview bytes for an ID
        // that was never an explicit share member) must stay invisible.
        WritePreview(root, "doc-stranger", "<h1>Stranger</h1><p>ZebraSecret hidden body</p>");

        using var client = _factory.CreateClient().WithShareToken(_token!);

        var stranger = await client.GetAsync($"/share/{shareId}/search?q=ZebraSecret");
        Assert.Equal(HttpStatusCode.OK, stranger.StatusCode);
        var strangerBody = await stranger.Content.ReadAsStringAsync();
        Assert.DoesNotContain("doc-stranger", strangerBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ZebraSecret", strangerBody, StringComparison.Ordinal);
        Assert.Empty(SearchResultIds(strangerBody));

        // Member content remains findable: scope narrows, it does not blind.
        var member = await client.GetAsync($"/share/{shareId}/search?q=SunshineMember");
        Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        Assert.Equal(new[] { "doc-1" }, SearchResultIds(await member.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Search_UnknownShare_ReturnsUnauthorized_WithoutLeakingPaths()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/share/shr06-unknown-{Guid.NewGuid():N}/search?q=hello");

        // B2 fail-closed: an unknown share has no access policy, so the gate
        // denies before the route runs — this also avoids leaking existence.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("LocalApplicationData", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmptyResults()
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        WriteManifest(shareId, root, new[] { ("doc-1", "member-notes.md") });
        WritePreview(root, "doc-1", "<p>SunshineMember alpha body</p>");

        using var client = _factory.CreateClient().WithShareToken(_token!);
        foreach (var url in new[]
                 {
                     $"/share/{shareId}/search",
                     $"/share/{shareId}/search?q=",
                     $"/share/{shareId}/search?q=%20%20",
                 })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(SearchResultIds(await response.Content.ReadAsStringAsync()));
        }
    }

    [Fact]
    public async Task Search_ResultsSupportViewerNavigation()
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        WriteManifest(shareId, root, new[] { ("doc-1", "member-notes.md") });
        WritePreview(root, "doc-1", "<p>SunshineMember alpha body</p>");

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var search = await client.GetAsync($"/share/{shareId}/search?q=SunshineMember");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        using var document = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var result = Assert.Single(document.RootElement.GetProperty("results").EnumerateArray());
        Assert.Equal("doc-1", result.GetProperty("documentId").GetString());
        var previewUrl = result.GetProperty("previewUrl").GetString();
        Assert.Equal($"/share/{shareId}/documents/doc-1/preview", previewUrl);

        // The advertised viewer navigation target actually serves the member.
        var preview = await client.GetAsync(previewUrl);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("SunshineMember", await preview.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Graph_OmitsEdgeToPrivateNode()
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        WriteManifest(shareId, root, new[]
        {
            ("doc-a", "alpha.md"),
            ("doc-b", "beta.md"),
        });
        File.WriteAllText(Path.Combine(root, "graph.json"), JsonSerializer.Serialize(new[]
        {
            new { sourceDocumentId = "doc-a", targetDocumentId = "doc-b", relationType = "MarkdownLink", relationOrigin = "Explicit" },
            new { sourceDocumentId = "doc-a", targetDocumentId = "doc-private", relationType = "MarkdownLink", relationOrigin = "Explicit" },
            new { sourceDocumentId = "doc-private", targetDocumentId = "doc-b", relationType = "Wikilink", relationOrigin = "Explicit" },
        }));

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var response = await client.GetAsync($"/share/{shareId}/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("doc-private", body, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(body);
        var nodeIds = document.RootElement.GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("documentId").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new HashSet<string>(new[] { "doc-a", "doc-b" }, StringComparer.Ordinal), nodeIds);

        var edges = document.RootElement.GetProperty("edges").EnumerateArray().ToList();
        var edge = Assert.Single(edges);
        Assert.Equal("doc-a", edge.GetProperty("sourceDocumentId").GetString());
        Assert.Equal("doc-b", edge.GetProperty("targetDocumentId").GetString());
    }

    [Fact]
    public async Task Graph_WithoutGraphArtifact_ReturnsMembersWithNoEdges()
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        WriteManifest(shareId, root, new[] { ("doc-a", "alpha.md") });

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var response = await client.GetAsync($"/share/{shareId}/graph");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var node = Assert.Single(document.RootElement.GetProperty("nodes").EnumerateArray());
        Assert.Equal("doc-a", node.GetProperty("documentId").GetString());
        Assert.Empty(document.RootElement.GetProperty("edges").EnumerateArray());
    }

    [Fact]
    public async Task Graph_UnknownShare_ReturnsUnauthorized_WithoutLeakingPaths()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/share/shr06-unknown-{Guid.NewGuid():N}/graph");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("LocalApplicationData", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Payload_ContainsNoLocalPath()
    {
        var shareId = NewShareId();
        var root = EnsureShareRoot(shareId);
        WriteManifest(shareId, root, new[]
        {
            ("doc-a", "alpha.md"),
            ("doc-b", "beta.md"),
        });
        WritePreview(root, "doc-a", "<p>SunshineMember shared body</p>");
        WritePreview(root, "doc-b", "<p>Another shared body</p>");
        File.WriteAllText(Path.Combine(root, "graph.json"), JsonSerializer.Serialize(new[]
        {
            new { sourceDocumentId = "doc-a", targetDocumentId = "doc-b", relationType = "MarkdownLink", relationOrigin = "Explicit" },
        }));

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var bodies = new[]
        {
            await (await client.GetAsync($"/share/{shareId}/manifest")).Content.ReadAsStringAsync(),
            await (await client.GetAsync($"/share/{shareId}/search?q=shared")).Content.ReadAsStringAsync(),
            await (await client.GetAsync($"/share/{shareId}/graph")).Content.ReadAsStringAsync(),
        };

        foreach (var body in bodies)
        {
            Assert.DoesNotContain(":\\", body, StringComparison.Ordinal);
            Assert.DoesNotContain("/home/", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LocalApplicationData", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private string NewShareId() => $"shr06-{Guid.NewGuid():N}";

    private string EnsureShareRoot(string shareId)
    {
        var root = _tokens.EnsureShareRootExists(shareId);
        _createdShareRoots.Add(root);
        return root;
    }

    private void WriteManifest(string shareId, string root, (string id, string name)[] members)
    {
        var manifest = new ShareManifest
        {
            ShareId = shareId,
            Mode = "Snapshot",
            AllowOriginalDownload = false,
            AllowPdfDownload = false,
            Documents = members.Select(m => new SanitizedDocumentMetadata
            {
                DocumentId = m.id,
                DisplayName = m.name,
                Extension = Path.GetExtension(m.name),
                SizeBytes = 10,
            }).ToList(),
        };
        File.WriteAllText(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(manifest));
        // B2: register the access policy so the ShareHost can authorize the viewer.
        _token = ShareHostTestSupport.IssueToken(_tokens, shareId);
    }

    private static void WritePreview(string root, string documentId, string html)
    {
        Directory.CreateDirectory(Path.Combine(root, "previews"));
        File.WriteAllText(Path.Combine(root, "previews", documentId + ".html"), html);
    }

    private static string[] SearchResultIds(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("documentId").GetString() ?? string.Empty)
            .ToArray();
    }
}
