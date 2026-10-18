using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Graph;
using DocumentIDE.Core.Semantic;

namespace DocumentIDE.Core.Search;

public interface ISemanticSearchService
{
    Task<SearchExecutionResult> SearchAsync(string rawQuery, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<SearchExecutionResult> SearchAsync(SearchQuery query, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default);
}

public class SemanticSearchService : ISemanticSearchService
{
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IEmbeddingStore _embeddingStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentMetadataRepository? _metadataRepository;

    public SemanticSearchService(
        IEmbeddingProvider? embeddingProvider,
        IEmbeddingStore embeddingStore,
        IDocumentRepository documentRepository,
        IDocumentMetadataRepository? metadataRepository = null)
    {
        _embeddingProvider = embeddingProvider;
        _embeddingStore = embeddingStore ?? throw new ArgumentNullException(nameof(embeddingStore));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _metadataRepository = metadataRepository;
    }

    public Task<SearchExecutionResult> SearchAsync(string rawQuery, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        var parsed = SearchQueryParser.Parse(rawQuery);
        return SearchAsync(parsed, options, cancellationToken);
    }

    public async Task<SearchExecutionResult> SearchAsync(SearchQuery query, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        var stopwatch = Stopwatch.StartNew();
        options ??= new DocumentSearchOptions();
        int limit = options.Limit > 0 ? options.Limit : 50;

        if (_embeddingProvider == null)
        {
            stopwatch.Stop();
            var diagList = new List<QueryDiagnostic>(query.Diagnostics)
            {
                new QueryDiagnostic(DiagnosticSeverity.Warning, "Embedding provider is not configured.")
            };
            return new SearchExecutionResult(
                Results: Array.Empty<SearchResult>(),
                TotalCount: 0,
                ExecutionTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                Query: query with { Diagnostics = diagList },
                ActiveFilterChips: Array.Empty<FilterChipInfo>()
            );
        }

        bool available = await _embeddingProvider.IsAvailableAsync(cancellationToken);
        if (!available)
        {
            stopwatch.Stop();
            var diagList = new List<QueryDiagnostic>(query.Diagnostics)
            {
                new QueryDiagnostic(DiagnosticSeverity.Warning, "Embedding provider or model is unavailable.")
            };
            return new SearchExecutionResult(
                Results: Array.Empty<SearchResult>(),
                TotalCount: 0,
                ExecutionTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                Query: query with { Diagnostics = diagList },
                ActiveFilterChips: Array.Empty<FilterChipInfo>()
            );
        }

        string queryText = string.Join(" ", query.Terms.Concat(query.ExactPhrases)).Trim();
        if (string.IsNullOrWhiteSpace(queryText))
        {
            stopwatch.Stop();
            return new SearchExecutionResult(
                Results: Array.Empty<SearchResult>(),
                TotalCount: 0,
                ExecutionTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                Query: query,
                ActiveFilterChips: Array.Empty<FilterChipInfo>()
            );
        }

        string normalizedQuery = EmbeddingCoordinator.NormalizeInput(queryText);
        var queryVectors = await _embeddingProvider.GenerateEmbeddingsAsync(new[] { normalizedQuery }, cancellationToken);
        if (queryVectors == null || queryVectors.Count == 0 || queryVectors[0] == null)
        {
            stopwatch.Stop();
            return new SearchExecutionResult(
                Results: Array.Empty<SearchResult>(),
                TotalCount: 0,
                ExecutionTimeMs: stopwatch.Elapsed.TotalMilliseconds,
                Query: query,
                ActiveFilterChips: Array.Empty<FilterChipInfo>()
            );
        }

        float[] queryVector = queryVectors[0];

        string? folderFilter = options.FolderPath ?? GetFilterValue(query, "folder");
        string? workspaceFilter = options.WorkspaceId ?? GetFilterValue(query, "workspace");

        var filterPredicate = MetadataFilterCompiler.Compile(query);

        IEnumerable<Document> candidateDocs;
        if (!string.IsNullOrWhiteSpace(workspaceFilter))
        {
            candidateDocs = await _documentRepository.GetByWorkspaceAsync(workspaceFilter);
        }
        else if (!string.IsNullOrWhiteSpace(folderFilter))
        {
            candidateDocs = await _documentRepository.GetByFolderSourcePathAsync(folderFilter);
        }
        else
        {
            candidateDocs = await _documentRepository.GetByWorkspaceAsync("");
        }

        var allEmbeddings = await _embeddingStore.GetAllEmbeddingsAsync(cancellationToken);
        var embeddingMap = allEmbeddings
            .Where(e => e != null && !string.IsNullOrEmpty(e.DocumentId))
            .ToDictionary(e => e.DocumentId, e => e);

        var finalResults = new List<SearchResult>();
        var evaluatedTuples = new List<(Document Doc, DocumentMetadata? Docx, MarkdownDocumentMetadata? Md)>();

        foreach (var doc in candidateDocs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.AllowedDocumentIds != null && !options.AllowedDocumentIds.Contains(doc.Id))
                continue;

            if (!string.IsNullOrWhiteSpace(folderFilter) && !IsPathInFolder(doc.CurrentPath, folderFilter))
                continue;

            if (!embeddingMap.TryGetValue(doc.Id, out var storedEmb) || storedEmb.Vector == null || storedEmb.Vector.Length == 0)
                continue;

            // Handle missing/stale embeddings: skip if content fingerprint mismatch
            if (!string.Equals(doc.ContentFingerprint, storedEmb.ContentFingerprint, StringComparison.Ordinal))
                continue;

            DocumentMetadata? docxMeta = null;
            MarkdownDocumentMetadata? mdMeta = null;
            if (_metadataRepository != null)
            {
                docxMeta = await _metadataRepository.GetDocxMetadataAsync(doc.Id);
                mdMeta = await _metadataRepository.GetMarkdownMetadataAsync(doc.Id);
            }

            if (!filterPredicate(doc, docxMeta, mdMeta))
                continue;

            evaluatedTuples.Add((doc, docxMeta, mdMeta));

            double similarity = SemanticRelationService.ComputeCosineSimilarity(queryVector, storedEmb.Vector);

            if (similarity <= 0.0)
                continue;

            finalResults.Add(new SearchResult(
                DocumentId: doc.Id,
                Filename: doc.Filename,
                Score: similarity,
                MatchSources: new[] { "semantic" },
                Snippets: new[] { $"Semantic match ({similarity:P0})" },
                MetadataSummary: options.IncludeMetadataSummary ? BuildMetadataSummary(doc) : null,
                Path: doc.CurrentPath
            ));
        }

        var rankedResults = finalResults
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Filename, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.DocumentId)
            .Take(limit)
            .ToList();

        var activeChips = MetadataFilterCompiler.ExtractActiveFilterChips(query, evaluatedTuples);

        stopwatch.Stop();

        return new SearchExecutionResult(
            Results: rankedResults,
            TotalCount: rankedResults.Count,
            ExecutionTimeMs: stopwatch.Elapsed.TotalMilliseconds,
            Query: query,
            ActiveFilterChips: activeChips
        );
    }

    private static bool IsPathInFolder(string path, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folderPath)) return false;

        string normPath = path.Replace('\\', '/').TrimEnd('/');
        string normFolder = folderPath.Replace('\\', '/').TrimEnd('/');

        return normPath.StartsWith(normFolder + "/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normPath, normFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetFilterValue(SearchQuery query, string fieldName)
    {
        return query.Filters
            .FirstOrDefault(f => string.Equals(f.Field, fieldName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string BuildMetadataSummary(Document doc)
    {
        return $"{doc.DocumentType.ToUpperInvariant()} | {doc.CurrentPath}";
    }
}
