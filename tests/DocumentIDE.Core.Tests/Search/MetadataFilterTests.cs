using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Search;
using Xunit;

namespace DocumentIDE.Core.Tests.Search;

public class MetadataFilterTests
{
    private class FakeFtsIndex : IFtsIndex
    {
        public List<FtsSearchResult> SearchResults { get; set; } = new();

        public Task IndexDocumentAsync(FtsIndexEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearIndexAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FtsSearchResult>>(SearchResults);
        public Task<FtsIndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new FtsIndexDiagnostics(0, true, "1.0.0", null));
    }

    private class FakeDocumentRepository : IDocumentRepository
    {
        public Dictionary<string, Document> Documents { get; } = new();

        public Task<Document?> GetByIdAsync(string id) => Task.FromResult(Documents.TryGetValue(id, out var doc) ? doc : null);
        public Task<Document?> GetByPathAsync(string currentPath) => Task.FromResult(Documents.Values.FirstOrDefault(d => d.CurrentPath == currentPath));
        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) => Task.FromResult(Documents.Values.FirstOrDefault(d => d.FilesystemVolumeId == volumeId && d.FilesystemObjectId == objectId));
        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) => Task.FromResult(Documents.Values.Where(d => d.ContentFingerprint == fingerprint).ToList());
        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) => Task.FromResult<IReadOnlyList<Document>>(Documents.Values.Where(d => d.CurrentPath.StartsWith(folderPath)).ToList());
        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId) => Task.FromResult<IReadOnlyList<Document>>(Documents.Values.ToList());
        public Task CreateAsync(Document document) { Documents[document.Id] = document; return Task.CompletedTask; }
        public Task UpdateAsync(Document document) { Documents[document.Id] = document; return Task.CompletedTask; }
        public Task DeleteAsync(string id) { Documents.Remove(id); return Task.CompletedTask; }
    }

    private class FakeDocumentMetadataRepository : IDocumentMetadataRepository
    {
        public Dictionary<string, DocumentMetadata> DocxMetadata { get; } = new();
        public Dictionary<string, MarkdownDocumentMetadata> MarkdownMetadata { get; } = new();

        public Task<DocumentMetadata?> GetDocxMetadataAsync(string documentId) =>
            Task.FromResult(DocxMetadata.TryGetValue(documentId, out var meta) ? meta : null);

        public Task SaveDocxMetadataAsync(DocumentMetadata metadata) { DocxMetadata[metadata.DocumentId] = metadata; return Task.CompletedTask; }

        public Task<MarkdownDocumentMetadata?> GetMarkdownMetadataAsync(string documentId) =>
            Task.FromResult(MarkdownMetadata.TryGetValue(documentId, out var meta) ? meta : null);

        public Task SaveMarkdownMetadataAsync(MarkdownDocumentMetadata metadata) { MarkdownMetadata[metadata.DocumentId] = metadata; return Task.CompletedTask; }

        public Task DeleteMetadataAsync(string documentId) { DocxMetadata.Remove(documentId); MarkdownMetadata.Remove(documentId); return Task.CompletedTask; }
        public Task DeleteAllMetadataAsync() { DocxMetadata.Clear(); MarkdownMetadata.Clear(); return Task.CompletedTask; }
        public Task MarkStatusAsync(string documentId, string status, string? lastError = null) => Task.CompletedTask;
        public Task<DocumentMetadataDto?> GetMetadataDtoAsync(string documentId, string currentParserVersion = "1.0.0") =>
            Task.FromResult<DocumentMetadataDto?>(new DocumentMetadataDto
            {
                DocumentId = documentId,
                DocxMetadata = DocxMetadata.GetValueOrDefault(documentId),
                MarkdownMetadata = MarkdownMetadata.GetValueOrDefault(documentId)
            });
    }

    private readonly FakeFtsIndex _fakeFtsIndex;
    private readonly FakeDocumentRepository _fakeDocRepo;
    private readonly FakeDocumentMetadataRepository _fakeMetaRepo;
    private readonly DocumentSearchService _searchService;

    public MetadataFilterTests()
    {
        _fakeFtsIndex = new FakeFtsIndex();
        _fakeDocRepo = new FakeDocumentRepository();
        _fakeMetaRepo = new FakeDocumentMetadataRepository();
        _searchService = new DocumentSearchService(_fakeFtsIndex, _fakeDocRepo, _fakeMetaRepo);

        // Setup test documents
        var doc1 = new Document
        {
            Id = "doc1",
            Filename = "report-arial.docx",
            Extension = "docx",
            DocumentType = "docx",
            CurrentPath = "/docs/report-arial.docx",
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        var meta1 = new DocumentMetadata
        {
            DocumentId = "doc1",
            PrimaryFont = "Arial",
            PaperSummary = "Letter",
            MarginSummary = "normal",
            PrimaryFontSize = 12.0
        };

        var doc2 = new Document
        {
            Id = "doc2",
            Filename = "paper-a4.docx",
            Extension = "docx",
            DocumentType = "docx",
            CurrentPath = "/docs/paper-a4.docx",
            UpdatedAt = DateTime.UtcNow.AddDays(-10).ToString("O")
        };
        var meta2 = new DocumentMetadata
        {
            DocumentId = "doc2",
            PrimaryFont = "Calibri",
            PaperSummary = "A4",
            MarginSummary = "mixed",
            PrimaryFontSize = 11.0
        };

        var doc3 = new Document
        {
            Id = "doc3",
            Filename = "notes.md",
            Extension = "md",
            DocumentType = "markdown",
            CurrentPath = "/notes/notes.md",
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        var meta3 = new MarkdownDocumentMetadata
        {
            DocumentId = "doc3",
            HeadingsJson = "[\"Introduction\", \"Summary\"]"
        };

        _fakeDocRepo.Documents["doc1"] = doc1;
        _fakeDocRepo.Documents["doc2"] = doc2;
        _fakeDocRepo.Documents["doc3"] = doc3;

        _fakeMetaRepo.DocxMetadata["doc1"] = meta1;
        _fakeMetaRepo.DocxMetadata["doc2"] = meta2;
        _fakeMetaRepo.MarkdownMetadata["doc3"] = meta3;
    }

    [Fact]
    public async Task SearchAsync_FontFilter_ReturnsOnlyMatchingDocuments()
    {
        // Act: search with font:"Arial"
        var result = await _searchService.SearchAsync("font:Arial");

        // Assert: only doc1 (report-arial.docx) should be returned
        Assert.Single(result.Results);
        Assert.Equal("doc1", result.Results[0].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_PaperFilter_ReturnsOnlyMatchingDocuments()
    {
        // Act: search with paper:Letter
        var result = await _searchService.SearchAsync("paper:Letter");

        // Assert: only doc1 should be returned
        Assert.Single(result.Results);
        Assert.Equal("doc1", result.Results[0].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_MarginFilter_ReturnsOnlyMatchingDocuments()
    {
        var result = await _searchService.SearchAsync("margin:mixed");

        Assert.Single(result.Results);
        Assert.Equal("doc2", result.Results[0].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_CombinedTextAndMetadataFilter_ReturnsMatchingDocuments()
    {
        _fakeFtsIndex.SearchResults = new List<FtsSearchResult>
        {
            new FtsSearchResult("doc1", "report-arial.docx", 1.0, "match", new[] { "body" }),
            new FtsSearchResult("doc2", "paper-a4.docx", 1.0, "match", new[] { "body" })
        };

        // Act: search for text "match" with font:"Arial"
        var result = await _searchService.SearchAsync("match font:Arial");

        // Assert: only doc1 matches both text and font filter
        Assert.Single(result.Results);
        Assert.Equal("doc1", result.Results[0].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_MixedMetadataFilters_ReturnsMatchingDocuments()
    {
        // Act: search font:Arial paper:Letter
        var result = await _searchService.SearchAsync("font:Arial paper:Letter");

        Assert.Single(result.Results);
        Assert.Equal("doc1", result.Results[0].DocumentId);
    }

    [Fact]
    public void InspectorClick_ProducesEquivalentFilterQuery()
    {
        string baseQuery = "nusa dua";
        string updated = MetadataFilterCompiler.ApplyInspectorFilter(baseQuery, "font", "Arial");

        Assert.Equal("nusa dua font:Arial", updated);

        // Clicking font:Arial again should toggle/remove it
        string toggled = MetadataFilterCompiler.ApplyInspectorFilter(updated, "font", "Arial");
        Assert.Equal("nusa dua", toggled);
    }

    [Fact]
    public void InspectorClick_ReplacesDifferentValueOfSameFilter()
    {
        string baseQuery = "nusa dua font:Calibri";
        string updated = MetadataFilterCompiler.ApplyInspectorFilter(baseQuery, "font", "Arial");

        Assert.Equal("nusa dua font:Arial", updated);
    }

    [Fact]
    public async Task MetadataFilter_NeverMutatesDocuments()
    {
        var initialDoc1 = _fakeDocRepo.Documents["doc1"];
        string initialPath = initialDoc1.CurrentPath;
        string initialName = initialDoc1.Filename;

        await _searchService.SearchAsync("font:Arial paper:Letter");

        Assert.Equal(initialPath, _fakeDocRepo.Documents["doc1"].CurrentPath);
        Assert.Equal(initialName, _fakeDocRepo.Documents["doc1"].Filename);
    }
}
