using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Search;

public class ExactSearchTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly FtsIndex _ftsIndex;
    private readonly DocumentRepository _documentRepository;
    private readonly DocumentSearchService _searchService;

    public ExactSearchTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_exact_search_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _ftsIndex = new FtsIndex(_appDb);
        _documentRepository = new DocumentRepository(_appDb);
        _searchService = new DocumentSearchService(_ftsIndex, _documentRepository);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
        {
            try { File.Delete(walPath); } catch { }
        }
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
        {
            try { File.Delete(shmPath); } catch { }
        }
    }

    [Fact]
    public async Task FilenameExact_OutranksBodyMatch()
    {
        // Required Test 1: Filename exact outranks body match
        var docIdA = Guid.NewGuid().ToString();
        var docIdB = Guid.NewGuid().ToString();

        var now = DateTime.UtcNow.ToString("O");
        var docA = new Document
        {
            Id = docIdA,
            Filename = "report.docx",
            Extension = "docx",
            DocumentType = "docx",
            CurrentPath = "/workspace/report.docx",
            CreatedAt = now,
            UpdatedAt = now
        };

        var docB = new Document
        {
            Id = docIdB,
            Filename = "notes.docx",
            Extension = "docx",
            DocumentType = "docx",
            CurrentPath = "/workspace/notes.docx",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _documentRepository.CreateAsync(docA);
        await _documentRepository.CreateAsync(docB);

        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(
            DocumentId: docIdA,
            Filename: "report.docx",
            Body: "General status update."
        ));

        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(
            DocumentId: docIdB,
            Filename: "notes.docx",
            Body: "report report report report report report"
        ));

        var result = await _searchService.SearchAsync("report");

        Assert.NotNull(result);
        Assert.True(result.Results.Count >= 2, "Expected at least 2 results");
        Assert.Equal(docIdA, result.Results[0].DocumentId);
        Assert.Equal(docIdB, result.Results[1].DocumentId);
    }

    [Fact]
    public async Task TableAndHeaderSource_IsIdentified()
    {
        // Required Test 2: Table/header source is identified
        var docId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");
        var doc = new Document
        {
            Id = docId,
            Filename = "budget.docx",
            Extension = "docx",
            DocumentType = "docx",
            CurrentPath = "/workspace/budget.docx",
            CreatedAt = now,
            UpdatedAt = now
        };
        await _documentRepository.CreateAsync(doc);

        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(
            DocumentId: docId,
            Filename: "budget.docx",
            Body: "Annual report overview.",
            Headers: "Quarterly Financial Overview",
            Tables: "sales_figure 500000"
        ));

        var headerSearchResult = await _searchService.SearchAsync("Quarterly");
        Assert.NotEmpty(headerSearchResult.Results);
        var headerMatchDoc = headerSearchResult.Results.First(r => r.DocumentId == docId);
        Assert.Contains("header", headerMatchDoc.MatchSources);

        var tableSearchResult = await _searchService.SearchAsync("sales_figure");
        Assert.NotEmpty(tableSearchResult.Results);
        var tableMatchDoc = tableSearchResult.Results.First(r => r.DocumentId == docId);
        Assert.Contains("table", tableMatchDoc.MatchSources);
    }

    [Fact]
    public async Task Search_OnlyReturnsDocumentsInRequestedScope()
    {
        // Required Test 3: Search only returns documents in requested scope
        var docId1 = Guid.NewGuid().ToString();
        var docId2 = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");

        var doc1 = new Document
        {
            Id = docId1,
            Filename = "spec1.md",
            Extension = "md",
            DocumentType = "markdown",
            CurrentPath = "D:/project/folderA/spec1.md",
            CreatedAt = now,
            UpdatedAt = now
        };

        var doc2 = new Document
        {
            Id = docId2,
            Filename = "spec2.md",
            Extension = "md",
            DocumentType = "markdown",
            CurrentPath = "D:/project/folderB/spec2.md",
            CreatedAt = now,
            UpdatedAt = now
        };

        await _documentRepository.CreateAsync(doc1);
        await _documentRepository.CreateAsync(doc2);

        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(docId1, "spec1.md", "architecture specification term_scope_test"));
        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(docId2, "spec2.md", "architecture specification term_scope_test"));

        // Search with folderA scope option
        var options = new DocumentSearchOptions(FolderPath: "D:/project/folderA");
        var result = await _searchService.SearchAsync("term_scope_test", options);

        Assert.Single(result.Results);
        Assert.Equal(docId1, result.Results[0].DocumentId);
    }

    [Fact]
    public async Task WarmQuery_MeasuresLatency()
    {
        var docId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow.ToString("O");
        await _documentRepository.CreateAsync(new Document
        {
            Id = docId,
            Filename = "latency.md",
            Extension = "md",
            DocumentType = "markdown",
            CurrentPath = "/workspace/latency.md",
            CreatedAt = now,
            UpdatedAt = now
        });
        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(docId, "latency.md", "performance benchmarking search query"));

        var result = await _searchService.SearchAsync("performance");
        Assert.True(result.ExecutionTimeMs >= 0.0);
    }
}
