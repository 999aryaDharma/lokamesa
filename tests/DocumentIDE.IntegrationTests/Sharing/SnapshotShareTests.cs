extern alias ShareHostApp;

using System.Net;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// SHR-04 — Snapshot Share: a frozen read-only share package containing only
/// explicit member artifacts as they existed at publication time.
/// </summary>
public sealed class SnapshotShareTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokens = new();
    private readonly List<string> _createdShareRoots = new();
    private readonly FakeMarkdownRenderer _markdown = new();
    private string? _token;

    public SnapshotShareTests(WebApplicationFactory<ShareHostApp::Program> factory)
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

    // RED requirement: later local edit does not change the snapshot.

    [Fact]
    public void LaterLocalEdit_DoesNotChangeSnapshot()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);

        var v1 = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Title v1")
        });
        var frozenPreview = File.ReadAllText(
            _tokens.GetArtifactPath(v1.ShareId, "previews/doc-1.html"));
        var frozenManifest = File.ReadAllText(
            Path.Combine(v1.ShareRoot, "manifest.json"));

        // Owner edits the local source after publication and attempts to
        // publish again: the already-published package stays frozen.
        var frozenEx = Assert.Throws<InvalidOperationException>(() =>
            PublishTracked(publisher, share, new[]
            {
                new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Title v2 CHANGED")
            }));
        Assert.Contains("frozen", frozenEx.Message, StringComparison.OrdinalIgnoreCase);

        var afterPreview = File.ReadAllText(
            _tokens.GetArtifactPath(v1.ShareId, "previews/doc-1.html"));

        // Snapshot semantics: the already-published package is frozen.
        // A later local edit must not rewrite the frozen artifacts; the owner
        // must publish a new snapshot explicitly (SHR-05 live-share is out of scope).
        Assert.Equal(frozenPreview, afterPreview);
        Assert.Contains("Title v1", afterPreview);
        Assert.DoesNotContain("CHANGED", afterPreview);
        Assert.Equal(frozenManifest, File.ReadAllText(Path.Combine(v1.ShareRoot, "manifest.json")));
    }

    [Fact]
    public void DocxPreviewArtifact_ComesFromOwnerSideCache_Verbatim()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var ownerCacheBytes = Encoding.UTF8.GetBytes("<h1>owner-rendered docx preview</h1>");

        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-word", "report.docx", ".docx", DocxPreviewBytes: ownerCacheBytes)
        });

        var stored = File.ReadAllBytes(_tokens.GetArtifactPath(result.ShareId, "previews/doc-word.html"));
        Assert.Equal(ownerCacheBytes, stored);
    }

    [Fact]
    public void MarkdownArtifact_IsSanitizedAtPublishTime()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);

        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-md", "notes.md", ".md",
                MarkdownSource: "<script>evil()</script># Hello")
        });

        var stored = File.ReadAllText(_tokens.GetArtifactPath(result.ShareId, "previews/doc-md.html"));
        Assert.DoesNotContain("<script", stored, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", stored);
    }

    [Fact]
    public void OnlyAllowedPublicMetadata_IsCopied_NoPhysicalPaths()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);

        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Hi")
        });

        var manifestJson = File.ReadAllText(Path.Combine(result.ShareRoot, "manifest.json"));
        Assert.DoesNotContain("C:\\", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D:\\", manifestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalApplicationData", manifestJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishedManifest_IsFrozenVersioned()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);

        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Hi")
        });

        var manifest = JsonSerializer.Deserialize<ShareManifest>(
            File.ReadAllText(Path.Combine(result.ShareRoot, "manifest.json")));
        Assert.NotNull(manifest);
        Assert.Equal("Snapshot", manifest!.Mode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SnapshotSharePublisher.SnapshotFormatVersion, manifest.SnapshotVersion);
        Assert.Single(manifest.Documents);
        Assert.False(manifest.AllowOriginalDownload);
    }

    [Fact]
    public async Task NonmemberDocument_ReturnsNotFound_OnViewerEndpoints()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-member", "member.md", ".md", MarkdownSource: "# Member")
        });

        using var client = _factory.CreateClient().WithShareToken(_token!);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/share/{result.ShareId}/documents/doc-nonmember")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/share/{result.ShareId}/documents/doc-nonmember/preview")).StatusCode);
    }

    [Fact]
    public async Task MemberPreview_IsServed_OriginalDownload_AbsentUnlessEnabled()
    {
        var share = NewSnapshotShare();
        var publisher = new SnapshotSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new SnapshotSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Frozen")
        });

        using var client = _factory.CreateClient().WithShareToken(_token!);

        var preview = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewBody = await preview.Content.ReadAsStringAsync();
        Assert.Contains("Frozen", previewBody);
        Assert.DoesNotContain("<script", previewBody, StringComparison.OrdinalIgnoreCase);

        var snapshot = await client.GetAsync($"/share/{result.ShareId}/snapshot");
        Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);

        // Original download is OFF by default: the route must deny, and no
        // original bytes may exist under the share root.
        var download = await client.GetAsync($"/share/{result.ShareId}/documents/doc-1/download-original");
        Assert.Equal(HttpStatusCode.Forbidden, download.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(result.ShareRoot, "originals")));
    }

    [Fact]
    public void ShareHost_DoesNotInvokeWord()
    {
        // ShareHost must serve only frozen owner-side artifacts: no Word/COM
        // automation, no Infrastructure (WORD-03 queue), no mutation services.
        var assembly = typeof(ShareHostApp::Program).Assembly;
        var referenced = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, n => n.Contains("Word", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("Interop", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("DocumentIDE.Infrastructure", referenced, StringComparer.OrdinalIgnoreCase);

        var endpointType = typeof(ShareHostApp::DocumentIDE.ShareHost.Endpoints.SnapshotEndpoints);
        foreach (var ctor in endpointType.GetConstructors())
        {
            foreach (var param in ctor.GetParameters())
            {
                var fullName = param.ParameterType.FullName ?? string.Empty;
                Assert.DoesNotContain("Word", fullName, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Interop", fullName, StringComparison.OrdinalIgnoreCase);
            }
        }

        var sourcePath = FindRepoFile("src", "DocumentIDE.ShareHost", "Endpoints", "SnapshotEndpoints.cs");
        var source = File.ReadAllText(sourcePath);
        // Code-level boundary (identifiers/usings), not prose: doc comments may
        // describe the Word-free guarantee without creating a dependency.
        var codeLines = source.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal));
        var code = string.Join('\n', codeLines);
        Assert.DoesNotContain("using DocumentIDE.Infrastructure", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WordAutomation", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WordQueue", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Interop", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COM automation", code, StringComparison.OrdinalIgnoreCase);
    }

    private Share NewSnapshotShare()
    {
        var share = new Share
        {
            Id = $"shr04-{Guid.NewGuid():N}",
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken,
            AllowOriginalDownload = false,
            AllowPdfDownload = false
        };
        // B2: the owner issues a token at create time; the publisher writes the
        // matching access policy so the ShareHost can authorize the viewer.
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

    private static string FindRepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repository file: " + string.Join("/", parts));
    }

    private sealed class FakeMarkdownRenderer : ISnapshotMarkdownRenderer
    {
        public string RenderAndSanitize(string markdown)
        {
            // Owner-side MD-02 stand-in: render as HTML paragraph and strip
            // active content the same way the maintained sanitizer would.
            var text = (markdown ?? string.Empty)
                .Replace("<script>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("</script>", string.Empty, StringComparison.OrdinalIgnoreCase);
            var start = text.IndexOf("evil()", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                text = text.Remove(start, "evil()".Length);
            }
            return "<div>" + System.Net.WebUtility.HtmlEncode(text) + "</div>";
        }
    }
}
