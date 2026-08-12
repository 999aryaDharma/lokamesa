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

public class DocumentSearchServiceTests
{
    private class FakeFtsIndex : IFtsIndex
    {
        public List<FtsSearchResult> SearchResults { get; set; } = new();

        public Task IndexDocumentAsync(FtsIndexEntry entry, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearIndexAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FtsSearchResult>>(SearchResults);

        public Task<FtsIndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new FtsIndexDiagnostics(0, true, "1.0.0", null));
    }

    private class FakeDocumentRepository : IDocumentRepository
    {
        public Dictionary<string, Document> Documents { get; } = new();

        public Task<Document?> GetByIdAsync(string id) =>
            Task.FromResult(Documents.TryGetValue(id, out var doc) ? doc : null);

        public Task<Document?> GetByPathAsync(string currentPath) =>
            Task.FromResult(Documents.Values.FirstOrDefault(d => d.CurrentPath == currentPath));

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) =>
            Task.FromResult(Documents.Values.FirstOrDefault(d => d.FilesystemVolumeId == volumeId && d.FilesystemObjectId == objectId));

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) =>
            Task.FromResult(Documents.Values.Where(d => d.ContentFingerprint == fingerprint).ToList());

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) =>
            Task.FromResult<IReadOnlyList<Document>>(Documents.Values.Where(d => d.CurrentPath.StartsWith(folderPath)).ToList());

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId) =>
            Task.FromResult<IReadOnlyList<Document>>(Documents.Values.ToList());

        public Task CreateAsync(Document document) { Documents[document.Id] = document; return Task.CompletedTask; }
        public Task UpdateAsync(Document document) { Documents[document.Id] = document; return Task.CompletedTask; }
        public Task DeleteAsync(string id) { Documents.Remove(id); return Task.CompletedTask; }
    }

    private readonly FakeFtsIndex _fakeFtsIndex;
    private readonly FakeDocumentRepository _fakeDocumentRepo;
    private readonly DocumentSearchService _service;

    public DocumentSearchServiceTests()
    {
        _fakeFtsIndex = new FakeFtsIndex();
        _fakeDocumentRepo = new FakeDocumentRepository();
        _service = new DocumentSearchService(_fakeFtsIndex, _fakeDocumentRepo);
    }

    [Fact]
    public async Task SearchAsync_NullQuery_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SearchAsync((SearchQuery)null!));
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyResults()
    {
        var query = SearchQueryParser.Parse("");
        var result = await _service.SearchAsync(query);

        Assert.NotNull(result);
        Assert.Empty(result.Results);
        Assert.Equal(0, result.TotalCount);
        Assert.True(result.ExecutionTimeMs >= 0.0);
    }

    [Fact]
    public async Task SearchAsync_FiltersByDocumentType()
    {
        const string docId1 = "doc1";
        const string docId2 = "doc2";

        var doc1 = new Document { Id = docId1, Filename = "notes.md", Extension = "md", DocumentType = "markdown", CurrentPath = "/workspace/notes.md" };
        var doc2 = new Document { Id = docId2, Filename = "notes.docx", Extension = "docx", DocumentType = "docx", CurrentPath = "/workspace/notes.docx" };

        _fakeFtsIndex.SearchResults = new List<FtsSearchResult>
        {
            new FtsSearchResult(docId1, "notes.md", 1.0, "match", new[] { "body" }),
            new FtsSearchResult(docId2, "notes.docx", 1.0, "match", new[] { "body" })
        };

        _fakeDocumentRepo.Documents[docId1] = doc1;
        _fakeDocumentRepo.Documents[docId2] = doc2;

        var query = SearchQueryParser.Parse("notes type:md");
        var result = await _service.SearchAsync(query);

        Assert.Single(result.Results);
        Assert.Equal(docId1, result.Results[0].DocumentId);
    }

    [Fact]
    public async Task SearchAsync_FiltersByAllowedDocumentIds()
    {
        const string docId1 = "doc1";
        const string docId2 = "doc2";

        var doc1 = new Document { Id = docId1, Filename = "doc1.md", Extension = "md", DocumentType = "markdown", CurrentPath = "/workspace/doc1.md" };
        var doc2 = new Document { Id = docId2, Filename = "doc2.md", Extension = "md", DocumentType = "markdown", CurrentPath = "/workspace/doc2.md" };

        _fakeFtsIndex.SearchResults = new List<FtsSearchResult>
        {
            new FtsSearchResult(docId1, "doc1.md", 1.0, "test", new[] { "body" }),
            new FtsSearchResult(docId2, "doc2.md", 1.0, "test", new[] { "body" })
        };

        _fakeDocumentRepo.Documents[docId1] = doc1;
        _fakeDocumentRepo.Documents[docId2] = doc2;

        var options = new DocumentSearchOptions(AllowedDocumentIds: new HashSet<string> { docId2 });
        var result = await _service.SearchAsync("test", options);

        Assert.Single(result.Results);
        Assert.Equal(docId2, result.Results[0].DocumentId);
    }
}
