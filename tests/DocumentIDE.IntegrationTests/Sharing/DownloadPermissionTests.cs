extern alias ShareHostApp;

using System.Net;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ShareHostSecurity = ShareHostApp::DocumentIDE.ShareHost.Security;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// SHR-07 — Share Download Permissions: original/PDF download permissions
/// default OFF and are enforced server-side. UI hiding is not the boundary:
/// direct-URL and direct-ID access must be denied by the ShareHost itself.
/// </summary>
public sealed class DownloadPermissionTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokens = new();
    private readonly List<string> _createdShareRoots = new();
    private readonly FakeMarkdownRenderer _markdown = new();
    private string? _token;

    public DownloadPermissionTests(WebApplicationFactory<ShareHostApp::Program> factory)
    {
        // SHR-07 probes permission gates, not rate budgets: raise only the
        // per-IP download window (same override mechanism as QA-05) so the
        // class's sequential probes never trip 429. Rate-limit enforcement
        // itself remains covered by PublicShareAttackSurfaceTests.
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ShareHostSecurity.ShareRateLimitOptions>();
                services.AddSingleton(new ShareHostSecurity.ShareRateLimitOptions
                {
                    DownloadsPerMinutePerIp = 1000
                });
            }));
    }

    public void Dispose()
    {
        _factory.Dispose();
        foreach (var root in _createdShareRoots)
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    // Sprint-mandated first behavior: OFF by default, denied server-side.

    [Fact]
    public async Task OriginalDownload_Off_CannotBeDownloaded()
    {
        var share = NewShare(allowOriginal: false, allowPdf: false);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Frozen")
        });

        Assert.False(result.Manifest.AllowOriginalDownload);
        Assert.False(Directory.Exists(Path.Combine(result.ShareRoot, "originals")));

        using var client = _factory.CreateClient().WithShareToken(_token!);

        // Direct URL: UI hiding is not the boundary, the route itself denies.
        var download = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-original");
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);

        var artifactBypass = await client.GetAsync($"/share/{result.ShareId}/artifacts/originals/notes.md");
        Assert.True(
            artifactBypass.StatusCode == HttpStatusCode.Forbidden ||
            artifactBypass.StatusCode == HttpStatusCode.NotFound,
            $"Download-OFF original must not be served via artifact route, got {artifactBypass.StatusCode}");
    }

    [Fact]
    public async Task PdfDownload_Off_CannotBeDownloaded()
    {
        var share = NewShare(allowOriginal: false, allowPdf: false);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Frozen")
        });

        Assert.False(result.Manifest.AllowPdfDownload);
        Assert.False(Directory.Exists(Path.Combine(result.ShareRoot, "pdfs")));

        using var client = _factory.CreateClient().WithShareToken(_token!);

        var download = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-pdf");
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
    }

    [Fact]
    public void OriginalPermission_On_IsPreservedInManifest()
    {
        var share = NewShare(allowOriginal: true, allowPdf: false);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md",
                MarkdownSource: "# Frozen", OriginalBytes: Encoding.UTF8.GetBytes("original-bytes"))
        });

        var manifest = JsonSerializer.Deserialize<ShareManifest>(
            File.ReadAllText(Path.Combine(result.ShareRoot, "manifest.json")));
        Assert.NotNull(manifest);
        Assert.True(manifest!.AllowOriginalDownload);
    }

    [Fact]
    public void PdfPermission_On_IsPreservedInManifest()
    {
        var share = NewShare(allowOriginal: false, allowPdf: true);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md",
                MarkdownSource: "# Frozen", PdfBytes: Encoding.UTF8.GetBytes("%PDF-1.7"))
        });

        var manifest = JsonSerializer.Deserialize<ShareManifest>(
            File.ReadAllText(Path.Combine(result.ShareRoot, "manifest.json")));
        Assert.NotNull(manifest);
        Assert.True(manifest!.AllowPdfDownload);
    }

    [Fact]
    public async Task ChangingPermission_UpdatesRouteBehavior()
    {
        var share = NewShare(allowOriginal: false, allowPdf: false);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Frozen")
        });

        using var client = _factory.CreateClient().WithShareToken(_token!);

        // OFF: denied even though the document is a member.
        var denied = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-original");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Owner flips the permission ON (no artifact prepared): the route must
        // re-read the manifest per request instead of serving a cached decision.
        RewriteManifestFlags(result.ShareRoot, allowOriginal: true, allowPdf: false);
        var noArtifact = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-original");
        Assert.Equal(HttpStatusCode.NotFound, noArtifact.StatusCode);

        // Owner flips the permission back OFF: denied again.
        RewriteManifestFlags(result.ShareRoot, allowOriginal: false, allowPdf: false);
        var deniedAgain = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-original");
        Assert.Equal(HttpStatusCode.Forbidden, deniedAgain.StatusCode);
    }

    [Fact]
    public async Task OriginalDownload_On_ServesPreparedBytes()
    {
        var share = NewShare(allowOriginal: true, allowPdf: false);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var originalBytes = Encoding.UTF8.GetBytes("# Frozen original source");
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md",
                MarkdownSource: "# Frozen", OriginalBytes: originalBytes)
        });

        Assert.True(result.Manifest.AllowOriginalDownload);
        Assert.Equal(originalBytes,
            File.ReadAllBytes(_tokens.GetArtifactPath(result.ShareId, "originals/notes.md")));

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var download = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-original");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(originalBytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PdfDownload_On_ServesPreparedBytes()
    {
        var share = NewShare(allowOriginal: false, allowPdf: true);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.7 frozen");
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md",
                MarkdownSource: "# Frozen", PdfBytes: pdfBytes)
        });

        Assert.True(result.Manifest.AllowPdfDownload);

        using var client = _factory.CreateClient().WithShareToken(_token!);
        var download = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-pdf");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdfBytes, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task NonmemberDirectId_WithPermissionOn_ReturnsNotFound()
    {
        var share = NewShare(allowOriginal: true, allowPdf: true);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-member", "member.md", ".md",
                MarkdownSource: "# Member",
                OriginalBytes: Encoding.UTF8.GetBytes("member-original"),
                PdfBytes: Encoding.UTF8.GetBytes("%PDF member"))
        });

        using var client = _factory.CreateClient().WithShareToken(_token!);

        // Permission is ON but the ID is not a share member: 404, never bytes.
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/share/{result.ShareId}/documents/doc-nonmember/download-original")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/share/{result.ShareId}/documents/doc-nonmember/download-pdf")).StatusCode);

        // The member itself still downloads.
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/share/{result.ShareId}/documents/doc-member/download-original")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"/share/{result.ShareId}/documents/doc-member/download-pdf")).StatusCode);
    }

    [Fact]
    public void DownloadArtifacts_PreparedOnlyAsPolicyRequires()
    {
        // OFF: even supplied bytes must never land under the share root.
        var offShare = NewShare(allowOriginal: false, allowPdf: false);
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var offResult = PublishTracked(publisher, offShare, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md",
                MarkdownSource: "# Frozen",
                OriginalBytes: Encoding.UTF8.GetBytes("must-not-persist"),
                PdfBytes: Encoding.UTF8.GetBytes("must-not-persist"))
        });
        Assert.False(Directory.Exists(Path.Combine(offResult.ShareRoot, "originals")));
        Assert.False(Directory.Exists(Path.Combine(offResult.ShareRoot, "pdfs")));

        // ON without owner-side bytes: typed failure, never a half-published share.
        var onShare = NewShare(allowOriginal: true, allowPdf: true);
        var missingEx = Assert.Throws<InvalidOperationException>(() =>
            PublishTracked(publisher, onShare, new[]
            {
                new SnapshotSourceDocument("doc-2", "other.md", ".md", MarkdownSource: "# Other")
            }));
        Assert.Contains("original", missingEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    private Share NewShare(bool allowOriginal, bool allowPdf)
    {
        var share = new Share
        {
            Id = $"shr07-{Guid.NewGuid():N}",
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken,
            AllowOriginalDownload = allowOriginal,
            AllowPdfDownload = allowPdf
        };
        // B2: owner-issued token; the publisher writes the matching access policy.
        _token = ShareHostTestSupport.IssueTokenFor(_tokens, share);
        return share;
    }

    private SnapshotPublishResult PublishTracked(
        SnapshotSharePublisher publisher, Share share, IEnumerable<SnapshotSourceDocument> sources)
    {
        var result = publisher.Publish(share, sources);
        _createdShareRoots.Add(result.ShareRoot);
        return result;
    }

    private static void RewriteManifestFlags(string shareRoot, bool allowOriginal, bool allowPdf)
    {
        var manifestPath = Path.Combine(shareRoot, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ShareManifest>(File.ReadAllText(manifestPath));
        Assert.NotNull(manifest);
        manifest!.AllowOriginalDownload = allowOriginal;
        manifest.AllowPdfDownload = allowPdf;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
    }

    private sealed class FakeMarkdownRenderer : ISnapshotMarkdownRenderer
    {
        public string RenderAndSanitize(string markdown)
        {
            return "<div>" + System.Net.WebUtility.HtmlEncode(markdown ?? string.Empty) + "</div>";
        }
    }
}
