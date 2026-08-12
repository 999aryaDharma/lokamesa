using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Semantic;

namespace DocumentIDE.Core.Graph;

public class SemanticSuggestionOptions
{
    public int TopK { get; set; } = 5;
    public double MinConfidence { get; set; } = 0.5;
    public bool EnableInferredEdges { get; set; } = true;
    public bool ExcludeStaleEmbeddings { get; set; } = true;
}

public class SemanticEdgeDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string SourceDocumentId { get; set; } = string.Empty;
    public string TargetDocumentId { get; set; } = string.Empty;
    public string RelationType { get; set; } = Domain.RelationType.SemanticSuggestion;
    public string RelationOrigin { get; set; } = Domain.RelationOrigin.Semantic;
    public double Confidence { get; set; }
    public bool IsDashed => RelationOrigin == Domain.RelationOrigin.Semantic || RelationOrigin == Domain.RelationOrigin.Inferred;
    public string? MetadataJson { get; set; }
}

public class SemanticRelationResult
{
    public bool Success { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<Relation> Relations { get; set; } = Array.Empty<Relation>();
    public IReadOnlyList<SemanticEdgeDto> Edges { get; set; } = Array.Empty<SemanticEdgeDto>();

    public static SemanticRelationResult Ok(IReadOnlyList<Relation> relations, IReadOnlyList<SemanticEdgeDto> edges) =>
        new() { Success = true, IsAvailable = true, Relations = relations, Edges = edges };

    public static SemanticRelationResult Unavailable(string message = "Embedding provider or model unavailable.") =>
        new() { Success = false, IsAvailable = false, ErrorCode = "ProviderUnavailable", ErrorMessage = message };

    public static SemanticRelationResult Failure(string errorCode, string message) =>
        new() { Success = false, IsAvailable = true, ErrorCode = errorCode, ErrorMessage = message };
}

public interface ISemanticRelationService
{
    Task<SemanticRelationResult> ComputeSuggestionsForDocumentAsync(
        string workspaceId,
        string documentId,
        SemanticSuggestionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<SemanticRelationResult> ComputeWorkspaceSuggestionsAsync(
        string workspaceId,
        SemanticSuggestionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<SemanticRelationResult> GenerateAndPersistSuggestionsAsync(
        string workspaceId,
        string documentId,
        SemanticSuggestionOptions? options = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<SemanticEdgeDto> FilterEdges(
        IEnumerable<SemanticEdgeDto> edges,
        SemanticSuggestionOptions? options = null);
}

public class SemanticRelationService : ISemanticRelationService
{
    private readonly IEmbeddingStore _embeddingStore;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IRelationRepository? _relationRepository;
    private readonly IDocumentRepository? _documentRepository;

    public SemanticRelationService(
        IEmbeddingStore embeddingStore,
        IEmbeddingProvider? embeddingProvider = null,
        IRelationRepository? relationRepository = null,
        IDocumentRepository? documentRepository = null)
    {
        _embeddingStore = embeddingStore ?? throw new ArgumentNullException(nameof(embeddingStore));
        _embeddingProvider = embeddingProvider;
        _relationRepository = relationRepository;
        _documentRepository = documentRepository;
    }

    public async Task<SemanticRelationResult> ComputeSuggestionsForDocumentAsync(
        string workspaceId,
        string documentId,
        SemanticSuggestionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));

        if (_embeddingProvider != null)
        {
            bool available = await _embeddingProvider.IsAvailableAsync(cancellationToken);
            if (!available)
            {
                return SemanticRelationResult.Unavailable("Embedding provider or model unavailable.");
            }
        }

        options ??= new SemanticSuggestionOptions();
        if (!options.EnableInferredEdges)
        {
            return SemanticRelationResult.Ok(Array.Empty<Relation>(), Array.Empty<SemanticEdgeDto>());
        }

        var sourceEmb = await _embeddingStore.GetEmbeddingAsync(documentId, cancellationToken);
        if (sourceEmb == null || sourceEmb.Vector == null || sourceEmb.Vector.Length == 0)
        {
            return SemanticRelationResult.Ok(Array.Empty<Relation>(), Array.Empty<SemanticEdgeDto>());
        }

        if (options.ExcludeStaleEmbeddings && _documentRepository != null)
        {
            var sourceDoc = await _documentRepository.GetByIdAsync(documentId);
            if (sourceDoc != null && !string.Equals(sourceDoc.ContentFingerprint, sourceEmb.ContentFingerprint, StringComparison.Ordinal))
            {
                return SemanticRelationResult.Ok(Array.Empty<Relation>(), Array.Empty<SemanticEdgeDto>());
            }
        }

        var allEmbeddings = await _embeddingStore.GetAllEmbeddingsAsync(cancellationToken);
        var candidates = new List<(string CandidateId, double Confidence)>();

        foreach (var emb in allEmbeddings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (emb == null || string.Equals(emb.DocumentId, documentId, StringComparison.Ordinal))
                continue;
            if (emb.Vector == null || emb.Vector.Length == 0)
                continue;

            if (options.ExcludeStaleEmbeddings && _documentRepository != null)
            {
                var candidateDoc = await _documentRepository.GetByIdAsync(emb.DocumentId);
                if (candidateDoc != null && !string.Equals(candidateDoc.ContentFingerprint, emb.ContentFingerprint, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            double confidence = ComputeCosineSimilarity(sourceEmb.Vector, emb.Vector);
            if (confidence < options.MinConfidence)
                continue;

            candidates.Add((emb.DocumentId, confidence));
        }

        var topCandidates = candidates
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.CandidateId, StringComparer.OrdinalIgnoreCase)
            .Take(options.TopK)
            .ToList();

        var relations = new List<Relation>();
        var edges = new List<SemanticEdgeDto>();

        foreach (var cand in topCandidates)
        {
            string relId = Guid.NewGuid().ToString();
            string metadataJson = "{\"isDashed\":true}";

            var relation = new Relation
            {
                Id = relId,
                WorkspaceId = workspaceId,
                SourceDocumentId = documentId,
                TargetDocumentId = cand.CandidateId,
                RelationType = RelationType.SemanticSuggestion,
                RelationOrigin = RelationOrigin.Semantic,
                Confidence = cand.Confidence,
                MetadataJson = metadataJson,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                UpdatedAt = DateTime.UtcNow.ToString("O")
            };

            var edge = new SemanticEdgeDto
            {
                Id = relId,
                WorkspaceId = workspaceId,
                SourceDocumentId = documentId,
                TargetDocumentId = cand.CandidateId,
                RelationType = RelationType.SemanticSuggestion,
                RelationOrigin = RelationOrigin.Semantic,
                Confidence = cand.Confidence,
                MetadataJson = metadataJson
            };

            relations.Add(relation);
            edges.Add(edge);
        }

        return SemanticRelationResult.Ok(relations, edges);
    }

    public async Task<SemanticRelationResult> ComputeWorkspaceSuggestionsAsync(
        string workspaceId,
        SemanticSuggestionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));

        if (_embeddingProvider != null)
        {
            bool available = await _embeddingProvider.IsAvailableAsync(cancellationToken);
            if (!available)
            {
                return SemanticRelationResult.Unavailable("Embedding provider or model unavailable.");
            }
        }

        options ??= new SemanticSuggestionOptions();
        if (!options.EnableInferredEdges)
        {
            return SemanticRelationResult.Ok(Array.Empty<Relation>(), Array.Empty<SemanticEdgeDto>());
        }

        var allEmbeddings = await _embeddingStore.GetAllEmbeddingsAsync(cancellationToken);
        if (allEmbeddings == null || allEmbeddings.Count == 0)
        {
            return SemanticRelationResult.Ok(Array.Empty<Relation>(), Array.Empty<SemanticEdgeDto>());
        }

        HashSet<string>? validWorkspaceDocIds = null;
        if (_documentRepository != null)
        {
            var wsDocs = await _documentRepository.GetByWorkspaceAsync(workspaceId);
            validWorkspaceDocIds = new HashSet<string>(wsDocs.Select(d => d.Id));
        }

        var candidateEmbs = allEmbeddings
            .Where(e => e != null && !string.IsNullOrEmpty(e.DocumentId))
            .Where(e => validWorkspaceDocIds == null || validWorkspaceDocIds.Contains(e.DocumentId))
            .ToList();

        var allRelations = new List<Relation>();
        var allEdges = new List<SemanticEdgeDto>();

        foreach (var sourceEmb in candidateEmbs)
        {
            var res = await ComputeSuggestionsForDocumentAsync(workspaceId, sourceEmb.DocumentId, options, cancellationToken);
            if (!res.Success)
            {
                return res;
            }

            allRelations.AddRange(res.Relations);
            allEdges.AddRange(res.Edges);
        }

        return SemanticRelationResult.Ok(allRelations, allEdges);
    }

    public async Task<SemanticRelationResult> GenerateAndPersistSuggestionsAsync(
        string workspaceId,
        string documentId,
        SemanticSuggestionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await ComputeSuggestionsForDocumentAsync(workspaceId, documentId, options, cancellationToken);
        if (!result.Success)
        {
            return result;
        }

        if (_relationRepository != null && result.Relations.Count > 0)
        {
            await _relationRepository.AddRangeAsync(result.Relations);
        }

        return result;
    }

    public IReadOnlyList<SemanticEdgeDto> FilterEdges(
        IEnumerable<SemanticEdgeDto> edges,
        SemanticSuggestionOptions? options = null)
    {
        if (edges == null) return Array.Empty<SemanticEdgeDto>();

        options ??= new SemanticSuggestionOptions();
        if (!options.EnableInferredEdges)
        {
            return Array.Empty<SemanticEdgeDto>();
        }

        return edges
            .Where(e => e != null && e.Confidence >= options.MinConfidence)
            .GroupBy(e => e.SourceDocumentId)
            .SelectMany(g => g.OrderByDescending(e => e.Confidence).ThenBy(e => e.TargetDocumentId).Take(options.TopK))
            .ToList();
    }

    public static double ComputeCosineSimilarity(float[]? vectorA, float[]? vectorB)
    {
        if (vectorA == null || vectorB == null || vectorA.Length == 0 || vectorB.Length == 0 || vectorA.Length != vectorB.Length)
            return 0.0;

        double dotProduct = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < vectorA.Length; i++)
        {
            double valA = vectorA[i];
            double valB = vectorB[i];
            dotProduct += valA * valB;
            normA += valA * valA;
            normB += valB * valB;
        }

        if (normA <= 0.0 || normB <= 0.0)
            return 0.0;

        double similarity = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return Math.Max(0.0, Math.Min(1.0, similarity));
    }
}
