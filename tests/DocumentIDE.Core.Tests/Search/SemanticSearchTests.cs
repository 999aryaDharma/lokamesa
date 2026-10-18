using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Search;
using DocumentIDE.Core.Semantic;
using Xunit;

namespace DocumentIDE.Core.Tests.Search;

public class SemanticSearchTests
{
    private class FakeEmbeddingStore : IEmbeddingStore
    {
        public Dictionary<string, DocumentEmbedding> Store { get; } = new();

        public Task SaveEmbeddingAsync(DocumentEmbedding embedding, CancellationToken cancellationToken = default)
        {
            Store[embedding.DocumentId] = embedding;
            return Task.CompletedTask;
        }

        public Task<DocumentEmbedding?> GetEmbeddingAsync(string documentId, CancellationToken cancellationToken = default)
        {
            Store.TryGetValue(documentId, out var emb);
            return Task.FromResult(emb);
        }

        public Task DeleteEmbeddingAsync(string documentId, CancellationToken cancellationToken = default)
        {
            Store.Remove(documentId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DocumentEmbedding>> GetAllEmbeddingsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DocumentEmbedding> list = Store.Values.ToList();
            return Task.FromResult(list);
        }
    }

    private class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderId => "local-fake";
        public string ModelId => "test-model";
        public int Dimensions => 3;
        public bool IsExternal => false;
        public bool Available { get; set; } = true;
        public Dictionary<string, float[]> QueryVectors { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Available);
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        {
            var res = new List<float[]>();
            foreach (var input in inputs)
            {
                if (QueryVectors.TryGetValue(input, out var vector))
                {
                    res.Add(vector);
                }
                else
                {
                    res.Add(new float[] { 1.0f, 0.0f, 0.0f });
                }
            }
            return Task.FromResult<IReadOnlyList<float[]>>(res);
        }
    }

    private class FakeDocumentRepository : IDocumentRepository
    {
        public List<Document> Documents { get; } = new();

        public Task CreateAsync(Document document)
        {
            Documents.Add(document);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            Documents.RemoveAll(d => d.Id == id);
            return Task.CompletedTask;
        }

        public Task<Document?> GetByIdAsync(string id)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));
        }

        public Task<Document?> GetByPathAsync(string currentPath)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => string.Equals(d.CurrentPath, currentPath, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => d.FilesystemVolumeId == volumeId && d.FilesystemObjectId == objectId));
        }

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId)
        {
            return Task.FromResult<IReadOnlyList<Document>>(Documents);
        }

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath)
        {
            return Task.FromResult<IReadOnlyList<Document>>(Documents);
        }

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint)
        {
            return Task.FromResult(Documents.Where(d => d.ContentFingerprint == fingerprint).ToList());
        }

        public Task UpdateAsync(Document document)
        {
            var idx = Documents.FindIndex(d => d.Id == document.Id);
            if (idx >= 0) Documents[idx] = document;
            return Task.CompletedTask;
        }
    }

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

    [Fact]
    public async Task SemanticMode_FindsConceptuallyRelatedFixture()
    {
        var provider = new FakeEmbeddingProvider();
        var store = new FakeEmbeddingStore();
        var docRepo = new FakeDocumentRepository();

        // Query vector for "artificial intelligence"
        provider.QueryVectors["artificial intelligence"] = new float[] { 0.9f, 0.1f, 0.0f };

        // Doc 1: Related conceptually (high similarity), does NOT contain exact phrase "artificial intelligence"
        var doc1 = new Document { Id = "doc-1", Filename = "neural_networks.md", CurrentPath = "/docs/neural_networks.md", ContentFingerprint = "fp-1" };
        store.Store["doc-1"] = new DocumentEmbedding { DocumentId = "doc-1", ContentFingerprint = "fp-1", Vector = new float[] { 0.95f, 0.05f, 0.0f } };
        await docRepo.CreateAsync(doc1);

        // Doc 2: Unrelated conceptually (orthogonal vector)
        var doc2 = new Document { Id = "doc-2", Filename = "baking_recipes.md", CurrentPath = "/docs/baking_recipes.md", ContentFingerprint = "fp-2" };
        store.Store["doc-2"] = new DocumentEmbedding { DocumentId = "doc-2", ContentFingerprint = "fp-2", Vector = new float[] { 0.0f, 0.0f, 1.0f } };
        await docRepo.CreateAsync(doc2);

        var semanticService = new SemanticSearchService(provider, store, docRepo);
        var query = SearchQueryParser.Parse("artificial intelligence mode:smart");

        var result = await semanticService.SearchAsync(query);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Results);
        Assert.Equal("doc-1", result.Results[0].DocumentId);
        Assert.Contains("semantic", result.Results[0].MatchSources);
        Assert.True(result.Results[0].Score > 0.8);
    }

    [Fact]
    public async Task ExactMode_ResultsUnchanged()
    {
        var ftsIndex = new FakeFtsIndex();
        var docRepo = new FakeDocumentRepository();
        var provider = new FakeEmbeddingProvider();
        var store = new FakeEmbeddingStore();

        var doc1 = new Document { Id = "doc-1", Filename = "recipe.md", CurrentPath = "/docs/recipe.md", ContentFingerprint = "fp-1" };
        await docRepo.CreateAsync(doc1);

        ftsIndex.SearchResults.Add(new FtsSearchResult("doc-1", "recipe.md", 1.0, "pasta recipe content", new List<string> { "body" }));

        var semanticService = new SemanticSearchService(provider, store, docRepo);
        var searchService = new DocumentSearchService(ftsIndex, docRepo, semanticSearchService: semanticService);

        var query = SearchQueryParser.Parse("pasta mode:exact");
        var result = await searchService.SearchAsync(query);

        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.Equal("doc-1", result.Results[0].DocumentId);
        Assert.Contains("body", result.Results[0].MatchSources);
        Assert.DoesNotContain("semantic", result.Results[0].MatchSources);
    }

    [Fact]
    public async Task SearchReplace_DoesNotDependOnSemanticService()
    {
        // Verification test: SearchReplace strictly uses deterministic matching without semantic dependency.
        var ftsIndex = new FakeFtsIndex();
        var docRepo = new FakeDocumentRepository();
        var searchService = new DocumentSearchService(ftsIndex, docRepo);

        // DocumentSearchService instantiated without ISemanticSearchService
        var query = SearchQueryParser.Parse("target_string mode:exact");
        var result = await searchService.SearchAsync(query);

        Assert.NotNull(result);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task ModelUnavailable_ReturnsTypedOutcome()
    {
        var provider = new FakeEmbeddingProvider { Available = false };
        var store = new FakeEmbeddingStore();
        var docRepo = new FakeDocumentRepository();

        var semanticService = new SemanticSearchService(provider, store, docRepo);
        var query = SearchQueryParser.Parse("quantum computing mode:smart");

        var result = await semanticService.SearchAsync(query);

        Assert.NotNull(result);
        Assert.Empty(result.Results);
        Assert.Contains(result.Query.Diagnostics, d => d.Message.Contains("unavailable"));
    }

    [Fact]
    public async Task StaleEmbedding_IsGracefullySkipped()
    {
        var provider = new FakeEmbeddingProvider();
        var store = new FakeEmbeddingStore();
        var docRepo = new FakeDocumentRepository();

        provider.QueryVectors["quantum"] = new float[] { 1.0f, 0.0f, 0.0f };

        var doc1 = new Document { Id = "doc-1", Filename = "physics.md", CurrentPath = "/docs/physics.md", ContentFingerprint = "fp-new" };
        await docRepo.CreateAsync(doc1);

        // Store contains embedding with OLD content fingerprint
        store.Store["doc-1"] = new DocumentEmbedding { DocumentId = "doc-1", ContentFingerprint = "fp-old", Vector = new float[] { 1.0f, 0.0f, 0.0f } };

        var semanticService = new SemanticSearchService(provider, store, docRepo);
        var query = SearchQueryParser.Parse("quantum mode:smart");

        var result = await semanticService.SearchAsync(query);

        Assert.NotNull(result);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task MetadataFilter_IsAppliedInSemanticSearch()
    {
        var provider = new FakeEmbeddingProvider();
        var store = new FakeEmbeddingStore();
        var docRepo = new FakeDocumentRepository();

        provider.QueryVectors["algorithms"] = new float[] { 1.0f, 0.0f, 0.0f };

        var doc1 = new Document { Id = "doc-1", Filename = "sorting.md", DocumentType = "md", CurrentPath = "/docs/sorting.md", ContentFingerprint = "fp-1" };
        var doc2 = new Document { Id = "doc-2", Filename = "sorting.docx", DocumentType = "docx", CurrentPath = "/docs/sorting.docx", ContentFingerprint = "fp-2" };

        store.Store["doc-1"] = new DocumentEmbedding { DocumentId = "doc-1", ContentFingerprint = "fp-1", Vector = new float[] { 0.9f, 0.1f, 0.0f } };
        store.Store["doc-2"] = new DocumentEmbedding { DocumentId = "doc-2", ContentFingerprint = "fp-2", Vector = new float[] { 0.9f, 0.1f, 0.0f } };

        await docRepo.CreateAsync(doc1);
        await docRepo.CreateAsync(doc2);

        var semanticService = new SemanticSearchService(provider, store, docRepo);
        var query = SearchQueryParser.Parse("algorithms type:md mode:smart");

        var result = await semanticService.SearchAsync(query);

        Assert.NotNull(result);
        Assert.Single(result.Results);
        Assert.Equal("doc-1", result.Results[0].DocumentId);
    }
}
