using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Search;

public record SearchResult(
    string DocumentId,
    string Filename,
    double Score,
    IReadOnlyList<string> MatchSources,
    IReadOnlyList<string> Snippets,
    string? MetadataSummary = null,
    string? Path = null
);

public record DocumentSearchOptions(
    string? WorkspaceId = null,
    string? FolderPath = null,
    IReadOnlySet<string>? AllowedDocumentIds = null,
    int Limit = 50,
    bool IncludeMetadataSummary = false
);

public record SearchExecutionResult(
    IReadOnlyList<SearchResult> Results,
    int TotalCount,
    double ExecutionTimeMs,
    SearchQuery Query,
    IReadOnlyList<FilterChipInfo>? ActiveFilterChips = null
);

public interface IDocumentSearchService
{
    Task<SearchExecutionResult> SearchAsync(string rawQuery, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default);
    Task<SearchExecutionResult> SearchAsync(SearchQuery query, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default);
}

public class DocumentSearchService : IDocumentSearchService
{
    private readonly IFtsIndex _ftsIndex;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentMetadataRepository? _metadataRepository;
    private readonly IFuzzySearchService _fuzzySearchService;
    private readonly ISemanticSearchService? _semanticSearchService;

    public DocumentSearchService(
        IFtsIndex ftsIndex,
        IDocumentRepository documentRepository,
        IDocumentMetadataRepository? metadataRepository = null,
        IFuzzySearchService? fuzzySearchService = null,
        ISemanticSearchService? semanticSearchService = null)
    {
        _ftsIndex = ftsIndex ?? throw new ArgumentNullException(nameof(ftsIndex));
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _metadataRepository = metadataRepository;
        _fuzzySearchService = fuzzySearchService ?? new FuzzySearchService();
        _semanticSearchService = semanticSearchService;
    }

    public Task<SearchExecutionResult> SearchAsync(string rawQuery, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        var parsed = SearchQueryParser.Parse(rawQuery);
        return SearchAsync(parsed, options, cancellationToken);
    }

    public async Task<SearchExecutionResult> SearchAsync(SearchQuery query, DocumentSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        if (query.Mode == SearchMode.Smart && _semanticSearchService != null)
        {
            return await _semanticSearchService.SearchAsync(query, options, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        options ??= new DocumentSearchOptions();

        int limit = options.Limit > 0 ? options.Limit : 50;

        // Extract filters from SearchQuery
        string? folderFilter = options.FolderPath ?? GetFilterValue(query, "folder");
        string? workspaceFilter = options.WorkspaceId ?? GetFilterValue(query, "workspace");
        string? typeFilter = GetFilterValue(query, "type");
        string? nameFilter = GetFilterValue(query, "name");

        var filterPredicate = MetadataFilterCompiler.Compile(query);

        // 1. Gather scope candidates if workspace or folder filter is specified
        Dictionary<string, Document>? candidateDocsMap = null;

        if (!string.IsNullOrWhiteSpace(workspaceFilter))
        {
            var wsDocs = await _documentRepository.GetByWorkspaceAsync(workspaceFilter);
            candidateDocsMap = wsDocs.ToDictionary(d => d.Id, d => d);
        }

        if (!string.IsNullOrWhiteSpace(folderFilter))
        {
            var folderDocs = await _documentRepository.GetByFolderSourcePathAsync(folderFilter);
            var folderMap = folderDocs.ToDictionary(d => d.Id, d => d);

            if (candidateDocsMap == null)
            {
                candidateDocsMap = folderMap;
            }
            else
            {
                candidateDocsMap = candidateDocsMap
                    .Where(kvp => folderMap.ContainsKey(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }

        // 2. Perform FTS Search if search terms exist
        string ftsQuery = SearchQueryParser.ToFts5Query(query);
        IReadOnlyList<FtsSearchResult> ftsResults = Array.Empty<FtsSearchResult>();

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            ftsResults = await _ftsIndex.SearchAsync(ftsQuery, limit * 4, cancellationToken);
        }

        var searchTerms = query.Terms.Concat(query.ExactPhrases)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.ToLowerInvariant())
            .ToList();

        var finalResults = new List<SearchResult>();
        var evaluatedTuples = new List<(Document Doc, DocumentMetadata? Docx, MarkdownDocumentMetadata? Md)>();

        if (!string.IsNullOrWhiteSpace(ftsQuery))
        {
            // FTS Search Path
            foreach (var ftsItem in ftsResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (options.AllowedDocumentIds != null && !options.AllowedDocumentIds.Contains(ftsItem.DocumentId))
                {
                    continue;
                }

                Document? doc = null;
                if (candidateDocsMap != null)
                {
                    if (!candidateDocsMap.TryGetValue(ftsItem.DocumentId, out doc))
                    {
                        continue;
                    }
                }
                else
                {
                    doc = await _documentRepository.GetByIdAsync(ftsItem.DocumentId);
                }

                if (doc == null) continue;

                if (!string.IsNullOrWhiteSpace(folderFilter) && !IsPathInFolder(doc.CurrentPath, folderFilter))
                {
                    continue;
                }

                DocumentMetadata? docxMeta = null;
                MarkdownDocumentMetadata? mdMeta = null;
                if (_metadataRepository != null)
                {
                    docxMeta = await _metadataRepository.GetDocxMetadataAsync(doc.Id);
                    mdMeta = await _metadataRepository.GetMarkdownMetadataAsync(doc.Id);
                }

                if (!filterPredicate(doc, docxMeta, mdMeta))
                {
                    continue;
                }

                evaluatedTuples.Add((doc, docxMeta, mdMeta));

                double rankScore = CalculateRankScore(doc.Filename, searchTerms, ftsItem);

                var snippets = new List<string>();
                if (!string.IsNullOrWhiteSpace(ftsItem.Snippet))
                {
                    snippets.Add(ftsItem.Snippet);
                }

                finalResults.Add(new SearchResult(
                    DocumentId: doc.Id,
                    Filename: doc.Filename,
                    Score: rankScore,
                    MatchSources: ftsItem.MatchSources ?? Array.Empty<string>(),
                    Snippets: snippets,
                    MetadataSummary: options.IncludeMetadataSummary ? BuildMetadataSummary(doc) : null,
                    Path: doc.CurrentPath
                ));
            }
        }
        else
        {
            // Pure Metadata / Filter Search Path (no text query terms)
            IEnumerable<Document> candidateDocs;
            if (candidateDocsMap != null)
            {
                candidateDocs = candidateDocsMap.Values;
            }
            else
            {
                candidateDocs = await _documentRepository.GetByWorkspaceAsync("");
            }

            foreach (var doc in candidateDocs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (options.AllowedDocumentIds != null && !options.AllowedDocumentIds.Contains(doc.Id))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(folderFilter) && !IsPathInFolder(doc.CurrentPath, folderFilter))
                {
                    continue;
                }

                DocumentMetadata? docxMeta = null;
                MarkdownDocumentMetadata? mdMeta = null;
                if (_metadataRepository != null)
                {
                    docxMeta = await _metadataRepository.GetDocxMetadataAsync(doc.Id);
                    mdMeta = await _metadataRepository.GetMarkdownMetadataAsync(doc.Id);
                }

                if (!filterPredicate(doc, docxMeta, mdMeta))
                {
                    continue;
                }

                evaluatedTuples.Add((doc, docxMeta, mdMeta));

                double rankScore = 1000.0;
                finalResults.Add(new SearchResult(
                    DocumentId: doc.Id,
                    Filename: doc.Filename,
                    Score: rankScore,
                    MatchSources: new[] { "metadata" },
                    Snippets: Array.Empty<string>(),
                    MetadataSummary: options.IncludeMetadataSummary ? BuildMetadataSummary(doc) : null,
                    Path: doc.CurrentPath
                ));
            }
        }

        if (query.Mode == SearchMode.Fuzzy && _fuzzySearchService != null)
        {
            var existingDocIds = finalResults.Select(r => r.DocumentId).ToHashSet();
            IEnumerable<Document> allCandidateDocs;

            if (candidateDocsMap != null)
            {
                allCandidateDocs = candidateDocsMap.Values;
            }
            else
            {
                allCandidateDocs = await _documentRepository.GetByWorkspaceAsync("");
            }

            var fuzzyCandidates = new List<FuzzySearchCandidate>();
            foreach (var doc in allCandidateDocs)
            {
                if (options.AllowedDocumentIds != null && !options.AllowedDocumentIds.Contains(doc.Id))
                    continue;
                if (!string.IsNullOrWhiteSpace(folderFilter) && !IsPathInFolder(doc.CurrentPath, folderFilter))
                    continue;

                fuzzyCandidates.Add(new FuzzySearchCandidate(
                    DocumentId: doc.Id,
                    Filename: doc.Filename,
                    Body: doc.Filename,
                    Headings: null,
                    CurrentPath: doc.CurrentPath
                ));
            }

            var fuzzyResults = _fuzzySearchService.PerformFuzzySearch(query, fuzzyCandidates);

            foreach (var fRes in fuzzyResults)
            {
                if (!existingDocIds.Contains(fRes.DocumentId))
                {
                    var matchedDoc = allCandidateDocs.FirstOrDefault(d => d.Id == fRes.DocumentId);
                    if (matchedDoc != null)
                    {
                        DocumentMetadata? docxMeta = null;
                        MarkdownDocumentMetadata? mdMeta = null;
                        if (_metadataRepository != null)
                        {
                            docxMeta = await _metadataRepository.GetDocxMetadataAsync(matchedDoc.Id);
                            mdMeta = await _metadataRepository.GetMarkdownMetadataAsync(matchedDoc.Id);
                        }

                        if (filterPredicate(matchedDoc, docxMeta, mdMeta))
                        {
                            evaluatedTuples.Add((matchedDoc, docxMeta, mdMeta));
                            finalResults.Add(new SearchResult(
                                DocumentId: fRes.DocumentId,
                                Filename: fRes.Filename,
                                Score: fRes.Score,
                                MatchSources: fRes.MatchSources,
                                Snippets: fRes.Snippets,
                                MetadataSummary: options.IncludeMetadataSummary ? BuildMetadataSummary(matchedDoc) : null,
                                Path: fRes.Path
                            ));
                            existingDocIds.Add(fRes.DocumentId);
                        }
                    }
                }
            }
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

    private static double CalculateRankScore(string filename, List<string> searchTerms, FtsSearchResult ftsResult)
    {
        double tierScore = 0.0;
        string fileNameStem = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
        string fullFileName = filename.ToLowerInvariant();

        bool isExactFilename = searchTerms.Any(t =>
            string.Equals(t, fileNameStem, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t, fullFileName, StringComparison.OrdinalIgnoreCase));

        bool isPartialFilename = !isExactFilename && (
            searchTerms.Any(t => fullFileName.Contains(t, StringComparison.OrdinalIgnoreCase)) ||
            (ftsResult.MatchSources != null && ftsResult.MatchSources.Contains("filename", StringComparer.OrdinalIgnoreCase))
        );

        bool isHeading = ftsResult.MatchSources != null && ftsResult.MatchSources.Contains("heading", StringComparer.OrdinalIgnoreCase);
        bool isBody = ftsResult.MatchSources != null && ftsResult.MatchSources.Contains("body", StringComparer.OrdinalIgnoreCase);
        bool isTable = ftsResult.MatchSources != null && ftsResult.MatchSources.Contains("table", StringComparer.OrdinalIgnoreCase);
        bool isHeaderOrFooter = ftsResult.MatchSources != null && (
            ftsResult.MatchSources.Contains("header", StringComparer.OrdinalIgnoreCase) ||
            ftsResult.MatchSources.Contains("footer", StringComparer.OrdinalIgnoreCase)
        );
        bool isFrontmatter = ftsResult.MatchSources != null && ftsResult.MatchSources.Contains("frontmatter", StringComparer.OrdinalIgnoreCase);

        if (isExactFilename) tierScore += 10000.0;
        else if (isPartialFilename) tierScore += 5000.0;

        if (isHeading) tierScore += 2500.0;
        if (isBody) tierScore += 1000.0;
        if (isTable) tierScore += 500.0;
        if (isHeaderOrFooter) tierScore += 250.0;
        if (isFrontmatter) tierScore += 100.0;

        double secondaryScore = 1.0 / (1.0 + Math.Max(0.0, ftsResult.Score));
        return tierScore + secondaryScore;
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
