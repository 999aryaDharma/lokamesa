extern alias ShareHostApp;

using System.Net;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// B5 — the public share link must not 404.
///
/// The "Copy link" URL the owner shows is <c>/{base}/share/{shareId}?token=…</c>,
/// but ShareHost only ever served <c>/share/{shareId}/manifest</c> and the other
/// JSON/artifact sub-routes: a viewer opening the copied link against ShareHost
/// got a bare 404 because no viewer page route existed. This lane pins the
/// read-only HTML index that makes the link usable, still fail-closed on the
/// token and still free of any physical path.
/// </summary>
public sealed class ShareViewerRouteTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokens = new();
    private readonly List<string> _createdShareRoots = new();
    private string _token = string.Empty;

    public ShareViewerRouteTests(WebApplicationFactory<ShareHostApp::Program> factory)
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

    [Fact]
    public async Task ViewerIndex_WithValidToken_ReturnsReadOnlyHtmlListingMembers()
    {
        var result = PublishMember("doc-1", "notes.md", "# Hello world");

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/share/{result.ShareId}?token={_token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        // The member is discoverable by its virtual display name.
        Assert.Contains("notes.md", body);
        // The page links to the member's frozen preview.
        Assert.Contains($"/share/{result.ShareId}/documents/doc-1/preview", body);
        // Read-only viewer page: no active content.
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewerIndex_WithoutToken_ReturnsUnauthorized()
    {
        var result = PublishMember("doc-1", "notes.md", "# Hello world");

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/share/{result.ShareId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ViewerIndex_NeverLeaksPhysicalPaths()
    {
        var result = PublishMember("doc-1", "notes.md", "# Hello world");

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/share/{result.ShareId}?token={_token}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalApplicationData", body, StringComparison.OrdinalIgnoreCase);
    }

    private SnapshotPublishResult PublishMember(string documentId, string displayName, string markdown)
    {
        var share = new Share
        {
            Id = $"shr05-{Guid.NewGuid():N}",
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken,
            AllowOriginalDownload = false,
            AllowPdfDownload = false,
        };
        _token = ShareHostTestSupport.IssueTokenFor(_tokens, share);

        var publisher = new SnapshotSharePublisher(_tokens, new FakeMarkdownRenderer());
        var result = publisher.Publish(share, new[]
        {
            new SnapshotSourceDocument(documentId, displayName, ".md", MarkdownSource: markdown),
        });
        _createdShareRoots.Add(result.ShareRoot);
        return result;
    }

    private sealed class FakeMarkdownRenderer : ISnapshotMarkdownRenderer
    {
        public string RenderAndSanitize(string markdown) =>
            "<div>" + WebUtility.HtmlEncode(markdown ?? string.Empty) + "</div>";
    }
}
