using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Core.Search;
using Xunit;

namespace DocumentIDE.Core.Tests.Indexing;

public class IncrementalIndexCoordinatorTests : IDisposable
{
    private readonly FakeDocumentRepository _documentRepo = new();
    private readonly FakeDocumentMetadataRepository _metadataRepo = new();
    private readonly FakeFtsIndex _ftsIndex = new();
    private readonly FakeMarkdownParser _markdownParser = new();
    private readonly FakeDocxParser _docxParser = new();
    private readonly string _tempFolder;

    public IncrementalIndexCoordinatorTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "idx06_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task OneModifiedFile_ReindexesOnlyItself()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var doc1Path = Path.Combine(_tempFolder, "doc1.md");
        var doc2Path = Path.Combine(_tempFolder, "doc2.md");
        await File.WriteAllTextAsync(doc1Path, "# Doc 1\nContent 1");
        await File.WriteAllTextAsync(doc2Path, "# Doc 2\nContent 2");

        var doc1 = new Document { Id = "doc1", CurrentPath = doc1Path, Filename = "doc1.md", Extension = ".md", ContentFingerprint = "fp1" };
        var doc2 = new Document { Id = "doc2", CurrentPath = doc2Path, Filename = "doc2.md", Extension = ".md", ContentFingerprint = "fp2" };
        await _documentRepo.CreateAsync(doc1);
        await _documentRepo.CreateAsync(doc2);

        // Pre-index doc2
        _ftsIndex.Entries["doc2"] = new FtsIndexEntry("doc2", "doc2.md", "Content 2");

        // Act: modify doc1 only
        await File.WriteAllTextAsync(doc1Path, "# Doc 1 Modified\nContent 1 Updated");
        doc1.ContentFingerprint = "fp1_updated";
        await _documentRepo.UpdateAsync(doc1);

        var changeEvent = new FileChangeEvent
        {
            ChangeType = FileChangeType.Modified,
            DocumentId = "doc1",
            Path = doc1Path
        };

        await coordinator.ProcessFileChangeAsync(changeEvent);

        // Assert: doc1 is reindexed, doc2 index remains untouched
        Assert.True(_ftsIndex.Entries.ContainsKey("doc1"));
        Assert.Equal("doc1.md", _ftsIndex.Entries["doc1"].Filename);
        Assert.Equal(1, _markdownParser.ParseCallCount);
        Assert.Equal("ready", coordinator.GetIndexState("doc1"));
    }

    [Fact]
    public async Task RepeatedEvent_SameFingerprint_DoesNotDuplicateWork()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var docPath = Path.Combine(_tempFolder, "doc1.md");
        await File.WriteAllTextAsync(docPath, "# Doc 1\nSame Content");

        var doc = new Document { Id = "doc1", CurrentPath = docPath, Filename = "doc1.md", Extension = ".md", ContentFingerprint = "same_fp" };
        await _documentRepo.CreateAsync(doc);

        var changeEvent = new FileChangeEvent
        {
            ChangeType = FileChangeType.Modified,
            DocumentId = "doc1",
            Path = docPath
        };

        // First index run
        await coordinator.ProcessFileChangeAsync(changeEvent);
        int initialParseCount = _markdownParser.ParseCallCount;
        Assert.Equal(1, initialParseCount);

        // Act: repeated event for same fingerprint
        await coordinator.ProcessFileChangeAsync(changeEvent);

        // Assert: parse count should NOT increase
        Assert.Equal(initialParseCount, _markdownParser.ParseCallCount);
    }

    [Fact]
    public async Task DeletedFile_IsRemovedFromActiveIndex_ButRetainedAsMissingMetadata()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var docPath = Path.Combine(_tempFolder, "doc1.md");
        await File.WriteAllTextAsync(docPath, "# Doc 1\nContent");

        var doc = new Document { Id = "doc1", CurrentPath = docPath, Filename = "doc1.md", Extension = ".md", ContentFingerprint = "fp1" };
        await _documentRepo.CreateAsync(doc);

        // First index it
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            DocumentId = "doc1",
            Path = docPath
        });

        Assert.True(_ftsIndex.Entries.ContainsKey("doc1"));
        Assert.NotNull(await _metadataRepo.GetMarkdownMetadataAsync("doc1"));

        // Delete physical file
        File.Delete(docPath);

        // Act: Deleted event
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Deleted,
            DocumentId = "doc1",
            Path = docPath
        });

        // Assert: Removed from FTS active index
        Assert.False(_ftsIndex.Entries.ContainsKey("doc1"));

        // Retained as missing metadata (metadata row still exists)
        var mdMeta = await _metadataRepo.GetMarkdownMetadataAsync("doc1");
        Assert.NotNull(mdMeta);
        Assert.Equal("missing", coordinator.GetIndexState("doc1"));
    }

    [Fact]
    public async Task ParserFailure_PublishesErrorState_DoesNotThrow()
    {
        // Arrange
        _markdownParser.ShouldFail = true;

        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var docPath = Path.Combine(_tempFolder, "bad.md");
        await File.WriteAllTextAsync(docPath, "Malformed Content");

        var doc = new Document { Id = "bad_doc", CurrentPath = docPath, Filename = "bad.md", Extension = ".md", ContentFingerprint = "bad_fp" };
        await _documentRepo.CreateAsync(doc);

        IndexStateChangedEventArgs? publishedEvent = null;
        coordinator.OnIndexStateChanged += (sender, args) =>
        {
            if (args.State == "error")
            {
                publishedEvent = args;
            }
        };

        // Act: Should not throw exception
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            DocumentId = "bad_doc",
            Path = docPath
        });

        // Assert: Error state published
        Assert.Equal("error", coordinator.GetIndexState("bad_doc"));
        Assert.NotNull(publishedEvent);
        Assert.Equal("bad_doc", publishedEvent.DocumentId);
        Assert.Equal("error", publishedEvent.State);
    }

    [Fact]
    public async Task DocxFile_ReindexesSuccessfully()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var docPath = Path.Combine(_tempFolder, "sample.docx");
        await File.WriteAllTextAsync(docPath, "dummy docx content");

        var doc = new Document { Id = "docx1", CurrentPath = docPath, Filename = "sample.docx", Extension = ".docx", ContentFingerprint = "docx_fp" };
        await _documentRepo.CreateAsync(doc);

        // Act
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            DocumentId = "docx1",
            Path = docPath
        });

        // Assert
        Assert.True(_ftsIndex.Entries.ContainsKey("docx1"));
        Assert.Equal(1, _docxParser.ParseCallCount);
        Assert.NotNull(await _metadataRepo.GetDocxMetadataAsync("docx1"));
        Assert.Equal("ready", coordinator.GetIndexState("docx1"));
    }

    [Fact]
    public async Task CacheInvalidationEvent_Fired_OnDocumentChangeOrDelete()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var docPath = Path.Combine(_tempFolder, "doc1.md");
        await File.WriteAllTextAsync(docPath, "# Content");

        var doc = new Document { Id = "doc1", CurrentPath = docPath, Filename = "doc1.md", Extension = ".md", ContentFingerprint = "fp1" };
        await _documentRepo.CreateAsync(doc);

        var invalidatedDocIds = new List<string>();
        coordinator.OnCacheInvalidated += (sender, args) =>
        {
            invalidatedDocIds.Add(args.DocumentId);
        };

        // Act: Process file change
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            DocumentId = "doc1",
            Path = docPath
        });

        // Delete file event
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Deleted,
            DocumentId = "doc1",
            Path = docPath
        });

        // Assert
        Assert.Equal(2, invalidatedDocIds.Count);
        Assert.All(invalidatedDocIds, id => Assert.Equal("doc1", id));
    }

    [Fact]
    public async Task ReindexWorkspaceAsync_ReindexesAllWorkspaceDocuments()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var doc1Path = Path.Combine(_tempFolder, "w1.md");
        var doc2Path = Path.Combine(_tempFolder, "w2.md");
        await File.WriteAllTextAsync(doc1Path, "# W1");
        await File.WriteAllTextAsync(doc2Path, "# W2");

        var doc1 = new Document { Id = "w1", CurrentPath = doc1Path, Filename = "w1.md", Extension = ".md", ContentFingerprint = "fp_w1" };
        var doc2 = new Document { Id = "w2", CurrentPath = doc2Path, Filename = "w2.md", Extension = ".md", ContentFingerprint = "fp_w2" };
        await _documentRepo.CreateAsync(doc1);
        await _documentRepo.CreateAsync(doc2);
        _documentRepo.SetWorkspaceDocuments("ws1", new[] { doc1, doc2 });

        // Act
        await coordinator.ReindexWorkspaceAsync("ws1");

        // Assert
        Assert.True(_ftsIndex.Entries.ContainsKey("w1"));
        Assert.True(_ftsIndex.Entries.ContainsKey("w2"));
        Assert.Equal(2, _markdownParser.ParseCallCount);
    }

    [Fact]
    public async Task NonDocumentExtension_IsIgnored()
    {
        // Arrange
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var docPath = Path.Combine(_tempFolder, "image.png");
        await File.WriteAllTextAsync(docPath, "fake image");

        var doc = new Document { Id = "img1", CurrentPath = docPath, Filename = "image.png", Extension = ".png", ContentFingerprint = "fp_img" };
        await _documentRepo.CreateAsync(doc);

        // Act
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            DocumentId = "img1",
            Path = docPath
        });

        // Assert
        Assert.False(_ftsIndex.Entries.ContainsKey("img1"));
        Assert.Equal(0, _markdownParser.ParseCallCount);
        Assert.Equal(0, _docxParser.ParseCallCount);
    }
}

#region Fakes

internal class FakeDocumentRepository : IDocumentRepository
{
    private readonly Dictionary<string, Document> _docs = new();
    private readonly Dictionary<string, List<Document>> _workspaceDocs = new();

    public void SetWorkspaceDocuments(string workspaceId, IEnumerable<Document> docs)
    {
        _workspaceDocs[workspaceId] = docs.ToList();
    }

    public Task<Document?> GetByIdAsync(string id)
    {
        _docs.TryGetValue(id, out var doc);
        return Task.FromResult(doc);
    }

    public Task<Document?> GetByPathAsync(string currentPath)
    {
        var doc = _docs.Values.FirstOrDefault(d => string.Equals(d.CurrentPath, currentPath, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(doc);
    }

    public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) => Task.FromResult<Document?>(null);
    public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) => Task.FromResult(new List<Document>());
    public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) => Task.FromResult<IReadOnlyList<Document>>(new List<Document>());
    public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId)
    {
        if (_workspaceDocs.TryGetValue(workspaceId, out var docs))
        {
            return Task.FromResult<IReadOnlyList<Document>>(docs);
        }
        return Task.FromResult<IReadOnlyList<Document>>(_docs.Values.ToList());
    }

    public Task CreateAsync(Document document)
    {
        _docs[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Document document)
    {
        _docs[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id)
    {
        _docs.Remove(id);
        return Task.CompletedTask;
    }
}

internal class FakeDocumentMetadataRepository : IDocumentMetadataRepository
{
    private readonly Dictionary<string, DocumentMetadata> _docxMeta = new();
    private readonly Dictionary<string, MarkdownDocumentMetadata> _mdMeta = new();
    private readonly Dictionary<string, (string Status, string? Error)> _statuses = new();

    public Task<DocumentMetadata?> GetDocxMetadataAsync(string documentId)
    {
        _docxMeta.TryGetValue(documentId, out var meta);
        return Task.FromResult(meta);
    }

    public Task SaveDocxMetadataAsync(DocumentMetadata metadata)
    {
        // Metadata saves are durable metadata only: "ready" is a post-FTS transition
        // owned by IncrementalIndexCoordinator.MarkStatusAsync (QA-07 hard-kill fix).
        _docxMeta[metadata.DocumentId] = metadata;
        return Task.CompletedTask;
    }

    public Task<MarkdownDocumentMetadata?> GetMarkdownMetadataAsync(string documentId)
    {
        _mdMeta.TryGetValue(documentId, out var meta);
        return Task.FromResult(meta);
    }

    public Task SaveMarkdownMetadataAsync(MarkdownDocumentMetadata metadata)
    {
        // Metadata saves are durable metadata only: "ready" is a post-FTS transition
        // owned by IncrementalIndexCoordinator.MarkStatusAsync (QA-07 hard-kill fix).
        _mdMeta[metadata.DocumentId] = metadata;
        return Task.CompletedTask;
    }

    public Task DeleteMetadataAsync(string documentId)
    {
        _docxMeta.Remove(documentId);
        _mdMeta.Remove(documentId);
        _statuses[documentId] = ("stale", null);
        return Task.CompletedTask;
    }

    public Task DeleteAllMetadataAsync()
    {
        _docxMeta.Clear();
        _mdMeta.Clear();
        _statuses.Clear();
        return Task.CompletedTask;
    }

    public Task MarkStatusAsync(string documentId, string status, string? lastError = null)
    {
        _statuses[documentId] = (status, lastError);
        return Task.CompletedTask;
    }

    public Task<DocumentMetadataDto?> GetMetadataDtoAsync(string documentId, string currentParserVersion = "1.0.0")
    {
        _docxMeta.TryGetValue(documentId, out var docx);
        _mdMeta.TryGetValue(documentId, out var md);
        _statuses.TryGetValue(documentId, out var statusInfo);

        return Task.FromResult<DocumentMetadataDto?>(new DocumentMetadataDto
        {
            DocumentId = documentId,
            DocumentType = docx != null ? "docx" : "markdown",
            IndexStatus = statusInfo.Status ?? "pending",
            DocxMetadata = docx,
            MarkdownMetadata = md
        });
    }
}

internal class FakeFtsIndex : IFtsIndex
{
    public Dictionary<string, FtsIndexEntry> Entries { get; } = new();

    public Task IndexDocumentAsync(FtsIndexEntry entry, CancellationToken cancellationToken = default)
    {
        Entries[entry.DocumentId] = entry;
        return Task.CompletedTask;
    }

    public Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries) Entries[entry.DocumentId] = entry;
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        Entries.Remove(documentId);
        return Task.CompletedTask;
    }

    public Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        Entries.Clear();
        return Task.CompletedTask;
    }

    public Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default)
    {
        Entries.Clear();
        foreach (var entry in entries) Entries[entry.DocumentId] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        var results = Entries.Values
            .Where(e => (e.Body != null && e.Body.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (e.Filename != null && e.Filename.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Select(e => new FtsSearchResult(e.DocumentId, e.Filename, 1.0, e.Body, new[] { "body" }))
            .ToList();
        return Task.FromResult<IReadOnlyList<FtsSearchResult>>(results);
    }

    public Task<FtsIndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FtsIndexDiagnostics(Entries.Count, true, "1.0.0"));
    }
}

internal class FakeMarkdownParser : IMarkdownStructureParser
{
    public int ParseCallCount { get; private set; }
    public bool ShouldFail { get; set; }

    public MarkdownParseResult Parse(string markdownContent)
    {
        ParseCallCount++;
        if (ShouldFail)
        {
            throw new InvalidOperationException("Parser error: Invalid markdown structure.");
        }

        var frontmatter = new MarkdownFrontmatterResult(null, new Dictionary<string, object?>(), new List<string>(), new List<string>(), false, null);
        var metadata = new MarkdownMetadata(frontmatter, new List<string>(), new List<string>(), 0, 1, 0);

        return new MarkdownParseResult(
            markdownContent,
            markdownContent,
            frontmatter,
            new List<MarkdownHeadingInfo>(),
            new List<MarkdownOutlineNode>(),
            new List<MarkdownLinkInfo>(),
            metadata,
            0);
    }

    public MarkdownParseResult Parse(TextReader reader)
    {
        return Parse(reader.ReadToEnd());
    }
}

internal class FakeDocxParser : IDocxStructureParser
{
    public int ParseCallCount { get; private set; }

    public DocxParseResult Parse(string filePath)
    {
        ParseCallCount++;
        var meta = new DocumentMetadata
        {
            ExtractedAt = DateTime.UtcNow.ToString("O"),
            ParserVersion = "1.0.0"
        };
        return new DocxParseResult(meta, SearchableBody: "DOCX body text");
    }
}

#endregion
