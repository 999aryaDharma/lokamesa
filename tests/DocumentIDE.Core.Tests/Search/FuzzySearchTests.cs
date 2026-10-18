using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Search;
using Xunit;

namespace DocumentIDE.Core.Tests.Search;

public class FuzzySearchTests
{
    private class FakeFtsIndex : IFtsIndex
    {
        public List<FtsSearchResult> SearchResults { get; set; } = new();

        public Task IndexDocumentAsync(FtsIndexEntry entry, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteDocumentAsync(string documentId, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearIndexAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FtsSearchResult>>(SearchResults);
        public Task<FtsIndexDiagnostics> GetDiagnosticsAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(new FtsIndexDiagnostics(0, true, "1.0.0", null));
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

    [Fact]
    public void CalculateDistance_SmallTypo_FindsMatchAndCorrectDistance()
    {
        var service = new FuzzySearchService();
        var result = service.CalculateDistance("documnet", "document");

        Assert.True(result.IsMatch);
        Assert.Equal(2, result.EditDistance);
        Assert.True(result.SimilarityScore > 0.5);
    }

    [Fact]
    public void CalculateDistance_ExactMatch_ReturnsDistanceZeroAndSimilarityOne()
    {
        var service = new FuzzySearchService();
        var result = service.CalculateDistance("document", "document");

        Assert.True(result.IsMatch);
        Assert.Equal(0, result.EditDistance);
        Assert.Equal(1.0, result.SimilarityScore);
    }

    [Fact]
    public void CalculateDistance_ShortWord_RequiresExactMatch()
    {
        var service = new FuzzySearchService();
        var result = service.CalculateDistance("cat", "car");

        Assert.False(result.IsMatch, "Short terms <= 3 characters should not fuzzy match across 1-letter edits.");
    }

    [Fact]
    public void PerformFuzzySearch_SmallTypo_FindsExpectedDocument()
    {
        var service = new FuzzySearchService();
        var query = SearchQueryParser.Parse("archtecture");

        var candidates = new List<FuzzySearchCandidate>
        {
            new("doc1", "architecture_guide.md", Body: "This document describes software architecture in detail."),
            new("doc2", "unrelated.md", Body: "Random unrelated content here.")
        };

        var results = service.PerformFuzzySearch(query, candidates);

        Assert.Single(results);
        Assert.Equal("doc1", results[0].DocumentId);
        Assert.Contains("fuzzy", results[0].MatchSources);
    }

    [Fact]
    public void PerformFuzzySearch_DisabledOptions_ReturnsEmpty()
    {
        var service = new FuzzySearchService();
        var query = SearchQueryParser.Parse("archtecture");

        var candidates = new List<FuzzySearchCandidate>
        {
            new("doc1", "architecture.md", Body: "software architecture")
        };

        var options = new FuzzySearchOptions(Enabled: false);
        var results = service.PerformFuzzySearch(query, candidates, options);

        Assert.Empty(results);
    }

    [Fact]
    public void PerformFuzzySearch_CapsCandidateSetAtMaxCandidates()
    {
        var service = new FuzzySearchService();
        var query = SearchQueryParser.Parse("test");

        var candidates = Enumerable.Range(1, 100)
            .Select(i => new FuzzySearchCandidate($"doc{i}", $"file_{i}.md", Body: "test string"))
            .ToList();

        var options = new FuzzySearchOptions(MaxCandidates: 10);
        var results = service.PerformFuzzySearch(query, candidates, options);

        Assert.True(results.Count <= 10);
    }

    [Fact]
    public async Task DocumentSearchService_FuzzyMode_ExactOutranksFuzzyResult()
    {
        var fakeFts = new FakeFtsIndex();
        var fakeRepo = new FakeDocumentRepository();
        var fuzzyService = new FuzzySearchService();

        var exactDoc = new Document { Id = "doc1", Filename = "architecture.md", CurrentPath = "/workspace/architecture.md" };
        var fuzzyDoc = new Document { Id = "doc2", Filename = "archtecture.md", CurrentPath = "/workspace/archtecture.md" };

        fakeRepo.Documents["doc1"] = exactDoc;
        fakeRepo.Documents["doc2"] = fuzzyDoc;

        fakeFts.SearchResults = new List<FtsSearchResult>
        {
            new FtsSearchResult("doc1", "architecture.md", 1.0, "exact snippet", new[] { "filename", "body" })
        };

        var searchService = new DocumentSearchService(fakeFts, fakeRepo, fuzzySearchService: fuzzyService);
        var query = new SearchQuery(
            Terms: new[] { "architecture" },
            ExactPhrases: Array.Empty<string>(),
            ExcludedTerms: Array.Empty<string>(),
            Filters: Array.Empty<QueryFilter>(),
            Mode: SearchMode.Fuzzy
        );

        var executionResult = await searchService.SearchAsync(query);

        Assert.NotEmpty(executionResult.Results);
        var firstResult = executionResult.Results[0];
        Assert.Equal("doc1", firstResult.DocumentId);

        if (executionResult.Results.Count > 1)
        {
            Assert.True(firstResult.Score > executionResult.Results[1].Score, "Exact match score should be strictly greater than fuzzy match score.");
        }
    }

    [Fact]
    public async Task DocumentSearchService_ExactMode_DoesNotReturnFuzzyMatches()
    {
        var fakeFts = new FakeFtsIndex();
        var fakeRepo = new FakeDocumentRepository();
        var fuzzyService = new FuzzySearchService();

        var fuzzyDoc = new Document { Id = "doc2", Filename = "archtecture.md", CurrentPath = "/workspace/archtecture.md" };
        fakeRepo.Documents["doc2"] = fuzzyDoc;
        fakeFts.SearchResults = new List<FtsSearchResult>();

        var searchService = new DocumentSearchService(fakeFts, fakeRepo, fuzzySearchService: fuzzyService);
        var query = new SearchQuery(
            Terms: new[] { "architecture" },
            ExactPhrases: Array.Empty<string>(),
            ExcludedTerms: Array.Empty<string>(),
            Filters: Array.Empty<QueryFilter>(),
            Mode: SearchMode.Exact
        );

        var executionResult = await searchService.SearchAsync(query);

        Assert.Empty(executionResult.Results);
    }

    [Fact]
    public void BatchModule_HasNoDependencyOnFuzzyMatcher()
    {
        var batchAssembly = typeof(OperationPlan).Assembly;
        var batchTypes = batchAssembly.GetTypes().Where(t => t.Namespace != null && t.Namespace.StartsWith("DocumentIDE.Core.Batch")).ToList();

        foreach (var type in batchTypes)
        {
            var fieldsAndProps = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var member in fieldsAndProps)
            {
                if (member is FieldInfo field)
                {
                    Assert.DoesNotContain("Fuzzy", field.FieldType.Name, StringComparison.OrdinalIgnoreCase);
                }
                else if (member is PropertyInfo prop)
                {
                    Assert.DoesNotContain("Fuzzy", prop.PropertyType.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
