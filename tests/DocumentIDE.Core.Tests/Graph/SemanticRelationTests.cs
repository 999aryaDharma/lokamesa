using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Context;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Graph;
using DocumentIDE.Core.Semantic;
using Xunit;

namespace DocumentIDE.Core.Tests.Graph;

public class SemanticRelationTests
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
        public string ModelId => "mini-lm";
        public int Dimensions => 3;
        public bool IsExternal => false;
        public bool Available { get; set; } = true;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Available);
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        {
            var res = inputs.Select(_ => new float[] { 1.0f, 0.0f, 0.0f }).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(res);
        }
    }

    private class FakeRelationRepository : IRelationRepository
    {
        public List<Relation> Relations { get; } = new();

        public Task AddAsync(Relation relation)
        {
            Relations.Add(relation);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Relation> relations)
        {
            Relations.AddRange(relations);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            Relations.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType)
        {
            Relations.RemoveAll(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId && r.TargetDocumentId == targetDocumentId && r.RelationType == relationType && r.RelationOrigin == RelationOrigin.Manual);
            return Task.CompletedTask;
        }

        public Task<Relation?> GetByIdAsync(string id)
        {
            return Task.FromResult(Relations.FirstOrDefault(r => r.Id == id));
        }

        public Task<IReadOnlyList<Relation>> GetBySourceDocumentAsync(string workspaceId, string sourceDocumentId)
        {
            IReadOnlyList<Relation> list = Relations.Where(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<Relation>> GetByTargetDocumentAsync(string workspaceId, string targetDocumentId)
        {
            IReadOnlyList<Relation> list = Relations.Where(r => r.WorkspaceId == workspaceId && r.TargetDocumentId == targetDocumentId).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<Relation>> GetBacklinksAsync(string workspaceId, string targetDocumentId)
        {
            IReadOnlyList<Relation> list = Relations.Where(r => r.WorkspaceId == workspaceId && r.TargetDocumentId == targetDocumentId && (r.RelationOrigin == RelationOrigin.Explicit || r.RelationOrigin == RelationOrigin.Manual)).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<Relation>> GetRelationsAsync(string workspaceId, string? origin = null, string? type = null)
        {
            var q = Relations.Where(r => r.WorkspaceId == workspaceId);
            if (!string.IsNullOrEmpty(origin)) q = q.Where(r => r.RelationOrigin == origin);
            if (!string.IsNullOrEmpty(type)) q = q.Where(r => r.RelationType == type);
            IReadOnlyList<Relation> list = q.ToList();
            return Task.FromResult(list);
        }

        public Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations)
        {
            Relations.RemoveAll(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId && r.RelationOrigin == RelationOrigin.Explicit);
            Relations.AddRange(newDerivedRelations);
            return Task.CompletedTask;
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

    [Fact]
    public async Task NoDocumentGetsMoreThanKDefaultSemanticEdges()
    {
        // Step 1: K=5 default limit per document
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider();
        var service = new SemanticRelationService(store, provider);

        string wsId = "ws-1";
        string targetDocId = "doc-target";

        // Seed 10 candidates with varying similarity
        store.Store[targetDocId] = new DocumentEmbedding
        {
            DocumentId = targetDocId,
            ContentFingerprint = "fp-target",
            Vector = new float[] { 1.0f, 0.0f, 0.0f }
        };

        for (int i = 1; i <= 10; i++)
        {
            string candidateId = $"doc-{i}";
            // Slightly different vector for each candidate so similarities are distinct
            float offset = i * 0.05f;
            store.Store[candidateId] = new DocumentEmbedding
            {
                DocumentId = candidateId,
                ContentFingerprint = $"fp-{i}",
                Vector = new float[] { 1.0f - offset, offset, 0.0f }
            };
        }

        var options = new SemanticSuggestionOptions { TopK = 5, MinConfidence = 0.0 };
        var result = await service.ComputeSuggestionsForDocumentAsync(wsId, targetDocId, options);

        Assert.True(result.Success);
        Assert.True(result.Edges.Count <= 5);
        Assert.Equal(5, result.Edges.Count);
    }

    [Fact]
    public async Task InferredEdgeIsDashed()
    {
        // Step 4: Render dashed edges
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider();
        var service = new SemanticRelationService(store, provider);

        string wsId = "ws-1";
        string docA = "doc-a";
        string docB = "doc-b";

        store.Store[docA] = new DocumentEmbedding { DocumentId = docA, ContentFingerprint = "fp-a", Vector = new float[] { 1.0f, 0.0f, 0.0f } };
        store.Store[docB] = new DocumentEmbedding { DocumentId = docB, ContentFingerprint = "fp-b", Vector = new float[] { 0.9f, 0.1f, 0.0f } };

        var result = await service.ComputeSuggestionsForDocumentAsync(wsId, docA, new SemanticSuggestionOptions { MinConfidence = 0.5 });

        Assert.True(result.Success);
        Assert.Single(result.Edges);
        var edge = result.Edges[0];
        Assert.True(edge.IsDashed);
        Assert.Equal(RelationOrigin.Semantic, edge.RelationOrigin);
        Assert.Equal(RelationType.SemanticSuggestion, edge.RelationType);
    }

    [Fact]
    public async Task LowConfidenceEdgeIsOmitted()
    {
        // Step 2: Apply minimum configurable confidence threshold
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider();
        var service = new SemanticRelationService(store, provider);

        string wsId = "ws-1";
        string docA = "doc-a";
        string docB = "doc-b";

        // Orthogonal vectors -> cosine similarity = 0.0
        store.Store[docA] = new DocumentEmbedding { DocumentId = docA, ContentFingerprint = "fp-a", Vector = new float[] { 1.0f, 0.0f, 0.0f } };
        store.Store[docB] = new DocumentEmbedding { DocumentId = docB, ContentFingerprint = "fp-b", Vector = new float[] { 0.0f, 1.0f, 0.0f } };

        var result = await service.ComputeSuggestionsForDocumentAsync(wsId, docA, new SemanticSuggestionOptions { MinConfidence = 0.5 });

        Assert.True(result.Success);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task ModelUnavailable_ProducesTypedOutcome()
    {
        // Failure / Edge Cases: Model/provider unavailable
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider { Available = false };
        var service = new SemanticRelationService(store, provider);

        var result = await service.ComputeSuggestionsForDocumentAsync("ws-1", "doc-a");

        Assert.False(result.Success);
        Assert.False(result.IsAvailable);
        Assert.Equal("ProviderUnavailable", result.ErrorCode);
    }

    [Fact]
    public async Task StaleEmbedding_IsOmittedWhenFingerprintMismatch()
    {
        // Failure / Edge Cases: Embedding is stale relative to content
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider();
        var docRepo = new FakeDocumentRepository();
        var service = new SemanticRelationService(store, provider, documentRepository: docRepo);

        string wsId = "ws-1";
        string docA = "doc-a";
        string docB = "doc-b";

        await docRepo.CreateAsync(new Document { Id = docA, ContentFingerprint = "fp-a-current" });
        await docRepo.CreateAsync(new Document { Id = docB, ContentFingerprint = "fp-b-current" });

        // docB in store has stale fingerprint
        store.Store[docA] = new DocumentEmbedding { DocumentId = docA, ContentFingerprint = "fp-a-current", Vector = new float[] { 1.0f, 0.0f, 0.0f } };
        store.Store[docB] = new DocumentEmbedding { DocumentId = docB, ContentFingerprint = "fp-b-stale", Vector = new float[] { 1.0f, 0.0f, 0.0f } };

        var result = await service.ComputeSuggestionsForDocumentAsync(wsId, docA, new SemanticSuggestionOptions { MinConfidence = 0.5, ExcludeStaleEmbeddings = true });

        Assert.True(result.Success);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task InferredEdgeFilterOffMode_OmitsInferredEdges()
    {
        // Step 5: Allow inferred-edge filter/off mode
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider();
        var service = new SemanticRelationService(store, provider);

        string wsId = "ws-1";
        string docA = "doc-a";
        string docB = "doc-b";

        store.Store[docA] = new DocumentEmbedding { DocumentId = docA, ContentFingerprint = "fp-a", Vector = new float[] { 1.0f, 0.0f, 0.0f } };
        store.Store[docB] = new DocumentEmbedding { DocumentId = docB, ContentFingerprint = "fp-b", Vector = new float[] { 1.0f, 0.0f, 0.0f } };

        var options = new SemanticSuggestionOptions { EnableInferredEdges = false };
        var result = await service.ComputeSuggestionsForDocumentAsync(wsId, docA, options);

        Assert.True(result.Success);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task SemanticEdge_NeverTreatedAsExplicitDependency()
    {
        // Acceptance criteria: Semantic edge is never treated as explicit dependency.
        var relRepo = new FakeRelationRepository();
        var store = new FakeEmbeddingStore();
        var provider = new FakeEmbeddingProvider();
        var service = new SemanticRelationService(store, provider, relRepo);

        string wsId = "ws-1";
        string docA = "doc-a";
        string docB = "doc-b";

        store.Store[docA] = new DocumentEmbedding { DocumentId = docA, ContentFingerprint = "fp-a", Vector = new float[] { 1.0f, 0.0f, 0.0f } };
        store.Store[docB] = new DocumentEmbedding { DocumentId = docB, ContentFingerprint = "fp-b", Vector = new float[] { 1.0f, 0.0f, 0.0f } };

        var persistResult = await service.GenerateAndPersistSuggestionsAsync(wsId, docA, new SemanticSuggestionOptions { MinConfidence = 0.5 });
        Assert.True(persistResult.Success);
        Assert.Single(relRepo.Relations);
        var storedRel = relRepo.Relations[0];
        Assert.Equal(RelationOrigin.Semantic, storedRel.RelationOrigin);

        // Verify with ContextDependencyExpander: default explicit expansion must ignore semantic edge
        var expander = new ContextDependencyExpander(relRepo);
        var expansionResult = await expander.ExpandAsync(wsId, new[] { docA }, new ContextExpansionOptions { IncludeExplicitDependencies = true, IncludeInferredDependencies = false });

        Assert.Single(expansionResult.ExpandedSourceIds);
        Assert.Equal(docA, expansionResult.ExpandedSourceIds[0]);
        Assert.Empty(expansionResult.AddedByDependency);
    }
}
