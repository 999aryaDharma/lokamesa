extern alias ShareHostApp;

using System.Net;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Sharing;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// SHR-05 — Membership-Safe Live Share: content refreshes for existing members
/// only; membership never expands automatically. New files in a shared folder
/// and later collection changes stay non-public until an explicit owner
/// AddMember operation.
/// </summary>
public sealed class LiveShareMembershipTests
    : IClassFixture<WebApplicationFactory<ShareHostApp::Program>>, IDisposable
{
    private readonly WebApplicationFactory<ShareHostApp::Program> _factory;
    private readonly ShareTokenService _tokens = new();
    private readonly List<string> _createdShareRoots = new();
    private readonly FakeMarkdownRenderer _markdown = new();

    public LiveShareMembershipTests(WebApplicationFactory<ShareHostApp::Program> factory)
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

    // RED requirement: new file in shared folder is not public.

    [Fact]
    public void NewFileInSharedFolder_IsNotPublic()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });

        // A brand-new file is saved in the shared folder (FS-03 Created event).
        // The owner-side resolver can provide its content, but the publisher
        // must refuse to publish it: live content != live membership.
        var created = new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            Path = Path.Combine(Path.GetTempPath(), "shared-folder", "new-file.md"),
            DocumentId = "doc-new",
        };
        var outcome = publisher.HandleFileChange(share, created, id =>
            new LiveSourceDocument("doc-new", "new-file.md", ".md", MarkdownSource: "# New file"));

        Assert.Equal(LiveRefreshOutcome.IgnoredNonMember, outcome.Outcome);
        Assert.DoesNotContain(share.Members, m => m.DocumentId == "doc-new");
        Assert.False(File.Exists(
            Path.Combine(publisher.ShareRootFor(share.Id), "previews", "doc-new.html")));
        var manifest = ReadManifest(share.Id);
        Assert.Single(manifest.Documents);
        Assert.Equal("doc-1", manifest.Documents[0].DocumentId);
    }

    [Fact]
    public void CollectionMembershipChange_DoesNotAutoPublish()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });
        var revisionBefore = result.Manifest.LiveRevision;

        // The underlying collection later gains doc-2. Reporting the current
        // collection must not add members or artifacts.
        var currentCollection = new List<Document>
        {
            new() { Id = "doc-1", Filename = "notes.md", Extension = ".md" },
            new() { Id = "doc-2", Filename = "added-later.md", Extension = ".md" },
        };
        var memberCount = publisher.NoteCollectionChanged(share, currentCollection);

        Assert.Equal(1, memberCount);
        Assert.Single(share.Members);
        Assert.False(File.Exists(_tokens.GetArtifactPath(share.Id, "previews/doc-2.html")));
        Assert.Equal(revisionBefore, ReadManifest(share.Id).LiveRevision);
    }

    [Fact]
    public async Task ExistingMemberUpdate_AppearsAfterRefresh()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });

        var revision = publisher.RefreshMember(share,
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v2 CHANGED"));

        Assert.Equal(2, revision);
        var stored = File.ReadAllText(_tokens.GetArtifactPath(share.Id, "previews/doc-1.html"));
        Assert.Contains("Member v2 CHANGED", stored);

        // The public ShareHost serves the refreshed member content live.
        using var client = _factory.CreateClient();
        var preview = await client.GetAsync($"/share/{share.Id}/documents/doc-1/preview");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("Member v2 CHANGED", await preview.Content.ReadAsStringAsync());
    }

    [Fact]
    public void NonmemberRefresh_ThrowsTypedError()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            publisher.RefreshMember(share,
                new LiveSourceDocument("doc-stranger", "other.md", ".md", MarkdownSource: "# Stranger")));
        Assert.Contains("not a share member", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddAndRemoveMember_AreExplicitOwnerOperations()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });

        // Explicit owner add: new member becomes public with its artifact.
        var afterAdd = publisher.AddMember(share,
            new LiveSourceDocument("doc-2", "added.md", ".md", MarkdownSource: "# Added explicitly"));
        Assert.Equal(2, afterAdd);
        Assert.Equal(2, ReadManifest(share.Id).Documents.Count);

        using var client = _factory.CreateClient();
        var addedPreview = await client.GetAsync($"/share/{share.Id}/documents/doc-2/preview");
        Assert.Equal(HttpStatusCode.OK, addedPreview.StatusCode);

        // Explicit owner remove: member and artifact disappear; viewers get 404.
        var afterRemove = publisher.RemoveMember(share, "doc-2");
        Assert.Equal(3, afterRemove);
        Assert.DoesNotContain(share.Members, m => m.DocumentId == "doc-2");
        Assert.False(File.Exists(_tokens.GetArtifactPath(share.Id, "previews/doc-2.html")));
        Assert.Single(ReadManifest(share.Id).Documents);

        var removedPreview = await client.GetAsync($"/share/{share.Id}/documents/doc-2/preview");
        Assert.Equal(HttpStatusCode.NotFound, removedPreview.StatusCode);
    }

    [Fact]
    public void ManifestAndArtifacts_AreVersionedAtomically()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        var result = PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });

        Assert.Equal(1, result.Manifest.LiveRevision);
        Assert.Equal(1, ReadManifest(share.Id).LiveRevision);

        publisher.RefreshMember(share,
            new LiveSourceDocument("doc-1", "notes.md", ".md", MarkdownSource: "# Member v2"));
        Assert.Equal(2, ReadManifest(share.Id).LiveRevision);

        // Atomic writes leave no temp debris and a parseable manifest.
        var leftovers = Directory.GetFiles(result.ShareRoot, "*.tmp-*", SearchOption.AllDirectories);
        Assert.Empty(leftovers);
        var manifestJson = File.ReadAllText(Path.Combine(result.ShareRoot, "manifest.json"));
        var manifest = JsonSerializer.Deserialize<ShareManifest>(manifestJson);
        Assert.NotNull(manifest);
        Assert.Equal("Live", manifest!.Mode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, manifest.LiveRevision);
        Assert.Single(manifest.Documents);
    }

    [Fact]
    public async Task WatcherSubscription_RefreshesMemberOnly()
    {
        var share = NewLiveShare();
        var publisher = new LiveSharePublisher(_tokens, _markdown);
        PublishTracked(publisher, share, new[]
        {
            new LiveSourceDocument("doc-live-1", "notes.md", ".md", MarkdownSource: "# Member v1")
        });

        // Real FS-03 coordinator with a mock transport: the owner subscribes
        // the live share to verified file-change events.
        var tempDir = Path.Combine(Path.GetTempPath(), $"shr05_watch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var memberPath = Path.Combine(tempDir, "notes.md");
            File.WriteAllText(memberPath, "# Member v1");
            var newcomerPath = Path.Combine(tempDir, "newcomer.md");
            File.WriteAllText(newcomerPath, "# Newcomer");

            var repo = new InMemoryDocumentRepo();
            var memberDoc = new Document
            {
                Id = "doc-live-1",
                Filename = "notes.md",
                Extension = ".md",
                DocumentType = "markdown",
                CurrentPath = Path.GetFullPath(memberPath),
                AvailabilityStatus = "available",
            };
            await repo.CreateAsync(memberDoc);

            var mockWatcher = new MockSourceWatcher();
            var identity = new DocumentIdentityService(repo, new NullIdentityProvider());
            using var coordinator = new FileChangeCoordinator(
                mockWatcher, identity, repo, jobScheduler: null,
                debounceDelay: TimeSpan.FromMilliseconds(50));

            var handled = new List<LiveWatcherResult>();
            using var subscription = publisher.Subscribe(share, coordinator, id =>
                string.Equals(id, "doc-live-1", StringComparison.Ordinal)
                    ? new LiveSourceDocument("doc-live-1", "notes.md", ".md",
                        MarkdownSource: File.ReadAllText(memberPath))
                    : null,
                onHandled: r => { lock (handled) { handled.Add(r); } });

            // Member save → refreshed with latest saved content.
            File.WriteAllText(memberPath, "# Member v2 SAVED");
            mockWatcher.RaiseRawEvent(
                System.IO.WatcherChangeTypes.Changed, memberPath, null, "folder-1");
            Assert.True(WaitFor(() =>
                File.ReadAllText(_tokens.GetArtifactPath(share.Id, "previews/doc-live-1.html"))
                    .Contains("Member v2 SAVED", StringComparison.Ordinal)),
                "member artifact was not refreshed from the watcher event");

            // Newcomer file creation → verified by the coordinator but never public.
            mockWatcher.RaiseRawEvent(
                System.IO.WatcherChangeTypes.Created, newcomerPath, null, "folder-1");
            await Task.Delay(500);
            Assert.DoesNotContain(share.Members, m => m.DocumentId != "doc-live-1");
            Assert.False(File.Exists(_tokens.GetArtifactPath(share.Id, "previews/newcomer.html")));
            Assert.Single(ReadManifest(share.Id).Documents);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private Share NewLiveShare()
    {
        return new Share
        {
            Id = $"shr05-{Guid.NewGuid():N}",
            Mode = ShareMode.Live,
            AccessMode = AccessMode.SecretToken,
            AllowOriginalDownload = false,
            AllowPdfDownload = false
        };
    }

    private LivePublishResult PublishTracked(
        LiveSharePublisher publisher, Share share, IEnumerable<LiveSourceDocument> sources)
    {
        var result = publisher.Publish(share, sources);
        _createdShareRoots.Add(result.ShareRoot);
        return result;
    }

    private ShareManifest ReadManifest(string shareId)
    {
        var json = File.ReadAllText(Path.Combine(_tokens.GetShareRootPath(shareId), "manifest.json"));
        return JsonSerializer.Deserialize<ShareManifest>(json)!;
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try { if (condition()) return true; } catch { }
            Thread.Sleep(50);
        }
        try { return condition(); } catch { return false; }
    }

    private sealed class FakeMarkdownRenderer : ISnapshotMarkdownRenderer
    {
        public string RenderAndSanitize(string markdown)
        {
            var text = (markdown ?? string.Empty)
                .Replace("<script>", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("</script>", string.Empty, StringComparison.OrdinalIgnoreCase);
            return "<div>" + System.Net.WebUtility.HtmlEncode(text) + "</div>";
        }
    }

    private sealed class MockSourceWatcher : ISourceWatcher
    {
        public event EventHandler<RawWatcherEventArgs>? OnRawEvent;
        public event EventHandler<string>? OnWatcherOverflow;

        public void StartWatching(FolderSource source) { }
        public void StopWatching(string folderSourceId) { }
        public bool IsWatching(string folderSourceId) => true;
        public void Dispose() { }

        public void RaiseRawEvent(
            System.IO.WatcherChangeTypes changeType, string fullPath, string? oldFullPath, string folderSourceId)
        {
            OnRawEvent?.Invoke(this, new RawWatcherEventArgs(changeType, fullPath, oldFullPath, folderSourceId));
        }
    }

    private sealed class NullIdentityProvider : IFileIdentityProvider
    {
        public FileIdentity? GetFileIdentity(string path) => null;
    }

    private sealed class InMemoryDocumentRepo : IDocumentRepository
    {
        private readonly Dictionary<string, Document> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Document> _byPath = new(StringComparer.OrdinalIgnoreCase);

        public Task CreateAsync(Document document)
        {
            _byId[document.Id] = document;
            if (!string.IsNullOrWhiteSpace(document.CurrentPath))
                _byPath[Path.GetFullPath(document.CurrentPath)] = document;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            if (_byId.Remove(id, out var doc) && !string.IsNullOrWhiteSpace(doc.CurrentPath))
                _byPath.Remove(Path.GetFullPath(doc.CurrentPath));
            return Task.CompletedTask;
        }

        public Task<Document?> GetByIdAsync(string id)
        {
            _byId.TryGetValue(id, out var doc);
            return Task.FromResult(Clone(doc));
        }

        public Task<Document?> GetByPathAsync(string currentPath)
        {
            try
            {
                _byPath.TryGetValue(Path.GetFullPath(currentPath), out var doc);
                // Return a copy: the FS-03 coordinator compares before/after
                // snapshots, which collapses if reads alias one instance.
                return Task.FromResult(Clone(doc));
            }
            catch
            {
                return Task.FromResult<Document?>(null);
            }
        }

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId)
            => Task.FromResult<Document?>(null);

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint)
            => Task.FromResult(new List<Document>());

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath)
            => Task.FromResult<IReadOnlyList<Document>>(new List<Document>());

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId)
            => Task.FromResult<IReadOnlyList<Document>>(new List<Document>());

        public Task UpdateAsync(Document document)
        {
            _byId[document.Id] = document;
            if (!string.IsNullOrWhiteSpace(document.CurrentPath))
                _byPath[Path.GetFullPath(document.CurrentPath)] = document;
            return Task.CompletedTask;
        }

        private static Document? Clone(Document? doc) => doc == null ? null : new Document
        {
            Id = doc.Id,
            FilesystemVolumeId = doc.FilesystemVolumeId,
            FilesystemObjectId = doc.FilesystemObjectId,
            CurrentPath = doc.CurrentPath,
            LastKnownPath = doc.LastKnownPath,
            Filename = doc.Filename,
            Extension = doc.Extension,
            DocumentType = doc.DocumentType,
            SizeBytes = doc.SizeBytes,
            ModifiedAt = doc.ModifiedAt,
            ContentFingerprint = doc.ContentFingerprint,
            AvailabilityStatus = doc.AvailabilityStatus,
            IndexStatus = doc.IndexStatus,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt,
        };
    }
}
