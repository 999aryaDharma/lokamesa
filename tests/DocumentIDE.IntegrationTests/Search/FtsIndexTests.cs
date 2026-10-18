using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Search;

public class FtsIndexTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly FtsIndex _ftsIndex;

    public FtsIndexTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_fts_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _ftsIndex = new FtsIndex(_appDb);
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
    public async Task ExactTokenLookup_ReturnsExpectedDocument()
    {
        // Required Test 1: Exact token lookup returns expected document.
        var docId = Guid.NewGuid().ToString();
        var entry = new FtsIndexEntry(
            DocumentId: docId,
            Filename: "nusa_dua_report.docx",
            Body: "The device at site NUSA DUA was inspected and operating properly.",
            Headings: "Site Inspection NUSA DUA",
            Tables: "Device status: Online",
            Headers: "NUSA DUA SITE REPORT",
            Footers: "Page 1 of 5",
            Frontmatter: "location: nusa_dua"
        );

        await _ftsIndex.IndexDocumentAsync(entry);

        var results = await _ftsIndex.SearchAsync("NUSA");

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.DocumentId == docId);
        var match = results.First(r => r.DocumentId == docId);
        Assert.Equal("nusa_dua_report.docx", match.Filename);
        Assert.NotNull(match.Snippet);
    }

    [Fact]
    public async Task DeletingAndRebuildingIndex_DoesNotTouchSourceFiles()
    {
        // Required Test 2: Deleting/rebuilding index does not touch source files.
        var tempFolder = Path.Combine(Path.GetTempPath(), $"docide_fts_src_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);

        try
        {
            var file1 = Path.Combine(tempFolder, "doc1.md");
            var file2 = Path.Combine(tempFolder, "doc2.docx");

            await File.WriteAllTextAsync(file1, "# Heading 1\nContent of document 1.");
            await File.WriteAllTextAsync(file2, "Content of document 2.");

            var file1Creation = File.GetLastWriteTimeUtc(file1);
            var file2Creation = File.GetLastWriteTimeUtc(file2);
            var file1ContentBefore = await File.ReadAllTextAsync(file1);
            var file2ContentBefore = await File.ReadAllTextAsync(file2);

            var docId1 = Guid.NewGuid().ToString();
            var docId2 = Guid.NewGuid().ToString();

            await _ftsIndex.IndexBatchAsync(new[]
            {
                new FtsIndexEntry(docId1, "doc1.md", file1ContentBefore, "Heading 1"),
                new FtsIndexEntry(docId2, "doc2.docx", file2ContentBefore)
            });

            // Verify search works
            var searchBefore = await _ftsIndex.SearchAsync("document");
            Assert.Equal(2, searchBefore.Count);

            // Rebuild index with new entries or clear index
            await _ftsIndex.RebuildIndexAsync(new[]
            {
                new FtsIndexEntry(docId1, "doc1.md", file1ContentBefore, "Heading 1")
            });

            var searchAfter = await _ftsIndex.SearchAsync("document");
            Assert.Single(searchAfter);
            Assert.Equal(docId1, searchAfter[0].DocumentId);

            // Verify source files were untouched
            Assert.True(File.Exists(file1));
            Assert.True(File.Exists(file2));
            Assert.Equal(file1Creation, File.GetLastWriteTimeUtc(file1));
            Assert.Equal(file2Creation, File.GetLastWriteTimeUtc(file2));
            Assert.Equal(file1ContentBefore, await File.ReadAllTextAsync(file1));
            Assert.Equal(file2ContentBefore, await File.ReadAllTextAsync(file2));
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                try { Directory.Delete(tempFolder, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ConcurrentSearchWhileUpdate_RemainsResponsive()
    {
        // Required Test 3: Concurrent search while update remains responsive.
        // Index initial batch
        var initialEntries = Enumerable.Range(1, 50).Select(i => new FtsIndexEntry(
            DocumentId: $"doc_{i}",
            Filename: $"file_{i}.md",
            Body: $"This is document number {i} containing searching keyword test_concurrent_token.",
            Headings: $"Section {i}"
        )).ToList();

        await _ftsIndex.IndexBatchAsync(initialEntries);

        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));

        var readerTask = Task.Run(async () =>
        {
            int searchCount = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                var results = await _ftsIndex.SearchAsync("test_concurrent_token");
                Assert.NotEmpty(results);
                searchCount++;
                await Task.Delay(5);
            }
            return searchCount;
        });

        var writerTask = Task.Run(async () =>
        {
            int writeCount = 0;
            for (int i = 51; i <= 100 && !cts.Token.IsCancellationRequested; i++)
            {
                await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(
                    DocumentId: $"doc_{i}",
                    Filename: $"file_{i}.md",
                    Body: $"Document {i} with test_concurrent_token.",
                    Headings: $"Heading {i}"
                ));
                writeCount++;
                await Task.Delay(10);
            }
            return writeCount;
        });

        await Task.WhenAll(readerTask, writerTask);

        var searchCount = await readerTask;
        var writeCount = await writerTask;

        Assert.True(searchCount > 0, "Concurrent search readers should execute successfully.");
        Assert.True(writeCount > 0, "Concurrent index writers should execute successfully.");
    }

    [Fact]
    public async Task MalformedInput_HandledGracefully()
    {
        var docId = Guid.NewGuid().ToString();
        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry(docId, "file.txt", "Some body"));

        // Search with malformed or syntax characters (e.g. quotes, brackets, stars)
        var res1 = await _ftsIndex.SearchAsync("\"\"\"");
        var res2 = await _ftsIndex.SearchAsync("AND OR NOT (");
        var res3 = await _ftsIndex.SearchAsync("");

        Assert.NotNull(res1);
        Assert.NotNull(res2);
        Assert.NotNull(res3);
        Assert.Empty(res3);
    }

    [Fact]
    public async Task Diagnostics_ReturnsIndexStatus()
    {
        var diagBefore = await _ftsIndex.GetDiagnosticsAsync();
        Assert.True(diagBefore.IsHealthy);
        Assert.Equal(0, diagBefore.TotalIndexedDocuments);

        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry("d1", "f1.md", "body"));
        await _ftsIndex.IndexDocumentAsync(new FtsIndexEntry("d2", "f2.md", "body"));

        var diagAfter = await _ftsIndex.GetDiagnosticsAsync();
        Assert.True(diagAfter.IsHealthy);
        Assert.Equal(2, diagAfter.TotalIndexedDocuments);
        Assert.False(string.IsNullOrEmpty(diagAfter.IndexVersion));
    }
}
