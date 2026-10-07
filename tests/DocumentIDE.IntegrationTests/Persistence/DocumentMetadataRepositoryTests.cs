using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;
using DocumentIDE.IntegrationTests.Docx;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Persistence;

public class DocumentMetadataRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _tempDir;
    private AppDb _appDb;
    private DocumentMetadataRepository _metadataRepo;
    private DocumentRepository _docRepo;

    public DocumentMetadataRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_metadata_test_{Guid.NewGuid():N}.db");
        _tempDir = Path.Combine(Path.GetTempPath(), $"docide_metadata_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _metadataRepo = new DocumentMetadataRepository(_appDb);
        _docRepo = new DocumentRepository(_appDb);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
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
    public async Task MetadataSurvivesRestart()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\report.docx",
            Filename = "report.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await _docRepo.CreateAsync(doc);

        var docxMeta = new DocumentMetadata
        {
            DocumentId = doc.Id,
            PageCount = 12,
            SectionCount = 2,
            TableCount = 3,
            ImageCount = 5,
            PaperSummary = "A4",
            OrientationSummary = "Portrait",
            MarginSummary = "Normal",
            PrimaryFont = "Calibri",
            PrimaryFontSize = 11.0,
            HeaderPresent = true,
            FooterPresent = true,
            HeadingsJson = "[{\"level\":1,\"text\":\"Introduction\"}]",
            ExtractedAt = DateTime.UtcNow.ToString("O"),
            ParserVersion = "1.0.0"
        };
        await _metadataRepo.SaveDocxMetadataAsync(docxMeta);

        // Simulate app restart by clearing pool and initializing new AppDb instance on same file path
        SqliteConnection.ClearAllPools();
        var restartDb = new AppDb(_dbPath);
        await restartDb.InitializeAsync();
        var restartRepo = new DocumentMetadataRepository(restartDb);

        var fetched = await restartRepo.GetDocxMetadataAsync(doc.Id);
        Assert.NotNull(fetched);
        Assert.Equal(doc.Id, fetched!.DocumentId);
        Assert.Equal(12, fetched.PageCount);
        Assert.Equal(2, fetched.SectionCount);
        Assert.Equal(3, fetched.TableCount);
        Assert.Equal(5, fetched.ImageCount);
        Assert.Equal("A4", fetched.PaperSummary);
        Assert.Equal("Portrait", fetched.OrientationSummary);
        Assert.Equal("Normal", fetched.MarginSummary);
        Assert.Equal("Calibri", fetched.PrimaryFont);
        Assert.Equal(11.0, fetched.PrimaryFontSize);
        Assert.True(fetched.HeaderPresent);
        Assert.True(fetched.FooterPresent);
        Assert.Equal("[{\"level\":1,\"text\":\"Introduction\"}]", fetched.HeadingsJson);
        Assert.Equal("1.0.0", fetched.ParserVersion);
    }

    [Fact]
    public async Task ParserVersionMismatch_MarksDerivedDataRebuildable()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\article.md",
            Filename = "article.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        await _docRepo.CreateAsync(doc);

        var mdMeta = new MarkdownDocumentMetadata
        {
            DocumentId = doc.Id,
            FrontmatterJson = "{\"title\":\"Test\"}",
            HeadingsJson = "[{\"level\":1,\"text\":\"Header\"}]",
            CodeBlockCount = 2,
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            ParserVersion = "1.0.0"
        };
        await _metadataRepo.SaveMarkdownMetadataAsync(mdMeta);

        // Query DTO with higher/mismatched parser version
        var dto = await _metadataRepo.GetMetadataDtoAsync(doc.Id, currentParserVersion: "1.1.0");
        Assert.NotNull(dto);
        Assert.True(dto!.IsRebuildable);
        Assert.Equal("stale", dto.IndexStatus);

        // Verify document in DB index status is now marked stale
        var updatedDoc = await _docRepo.GetByIdAsync(doc.Id);
        Assert.NotNull(updatedDoc);
        Assert.Equal("stale", updatedDoc!.IndexStatus);
    }

    [Fact]
    public async Task MixedMarginSummary_IsDeterministic()
    {
        var inputOrder1 = new[] { "Normal", "Narrow", "Wide" };
        var inputOrder2 = new[] { "Wide", "Normal", "Narrow" };
        var inputOrder3 = new[] { "Narrow", "Wide", "Normal", "Narrow" };

        var summary1 = MetadataNormalizer.NormalizeMarginSummary(inputOrder1);
        var summary2 = MetadataNormalizer.NormalizeMarginSummary(inputOrder2);
        var summary3 = MetadataNormalizer.NormalizeMarginSummary(inputOrder3);

        Assert.Equal("Mixed (Narrow, Normal, Wide)", summary1);
        Assert.Equal(summary1, summary2);
        Assert.Equal(summary1, summary3);
    }

    [Fact]
    public async Task DerivedMetadata_CanBeDeletedAndRebuilt()
    {
        // A real package on disk: derived data is only rebuilt completely after a full
        // indexing pass (metadata + FTS), never by a metadata-only save.
        var docPath = Path.Combine(_tempDir, "sample.docx");
        await File.WriteAllBytesAsync(docPath, DocxTestFixtureBuilder.CreateValidDocxPackage());

        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = docPath,
            Filename = "sample.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await _docRepo.CreateAsync(doc);

        var initialMeta = new DocumentMetadata
        {
            DocumentId = doc.Id,
            PageCount = 5,
            PaperSummary = "Letter",
            ParserVersion = "1.0.0"
        };
        await _metadataRepo.SaveDocxMetadataAsync(initialMeta);

        var beforeDelete = await _metadataRepo.GetDocxMetadataAsync(doc.Id);
        Assert.NotNull(beforeDelete);

        // Delete metadata
        await _metadataRepo.DeleteMetadataAsync(doc.Id);

        var afterDelete = await _metadataRepo.GetDocxMetadataAsync(doc.Id);
        Assert.Null(afterDelete);

        var docAfterDelete = await _docRepo.GetByIdAsync(doc.Id);
        Assert.NotNull(docAfterDelete);
        Assert.Equal("stale", docAfterDelete!.IndexStatus);

        // Rebuild metadata
        var rebuiltMeta = new DocumentMetadata
        {
            DocumentId = doc.Id,
            PageCount = 6,
            PaperSummary = "Letter",
            ParserVersion = "1.0.0"
        };
        await _metadataRepo.SaveDocxMetadataAsync(rebuiltMeta);

        var afterRebuild = await _metadataRepo.GetDocxMetadataAsync(doc.Id);
        Assert.NotNull(afterRebuild);
        Assert.Equal(6, afterRebuild!.PageCount);

        // Hard boundary: metadata is durable, but the document must not claim "ready"
        // until the searchable index write has succeeded. A process kill here must
        // never be recoverable as a false "ready".
        var docAfterRebuild = await _docRepo.GetByIdAsync(doc.Id);
        Assert.Equal("stale", docAfterRebuild!.IndexStatus);

        // Only a complete indexing pass marks ready — after the FTS write succeeded.
        var coordinator = new IncrementalIndexCoordinator(
            _docRepo,
            _metadataRepo,
            new FtsIndex(_appDb),
            new MarkdownStructureParser(),
            new DocxStructureParser());
        await coordinator.ReindexDocumentAsync(doc.Id);

        Assert.Equal("ready", (await _docRepo.GetByIdAsync(doc.Id))!.IndexStatus);
        // The docx parser's searchable body is currently the file name (placeholder
        // until deep DOCX parsing lands); the file name is a durable FTS column, so a
        // hit proves the search row exists.
        Assert.Contains(
            await new FtsIndex(_appDb).SearchAsync("sample"),
            r => r.DocumentId == doc.Id);
    }

    [Fact]
    public async Task MetadataSaveWithoutFts_DoesNotMarkReady_HardKillBoundary()
    {
        var docPath = Path.Combine(_tempDir, "boundary.md");
        await File.WriteAllTextAsync(docPath, "# Boundary\n\nSome searchable text");
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = docPath,
            Filename = "boundary.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        await _docRepo.CreateAsync(doc);
        Assert.Equal("pending", (await _docRepo.GetByIdAsync(doc.Id))!.IndexStatus);

        // The coordinator persists derived metadata before writing the FTS row. If the
        // process is killed exactly in between, no search rows exist for the document,
        // so it must not be readable as "ready".
        await _metadataRepo.SaveMarkdownMetadataAsync(new MarkdownDocumentMetadata
        {
            DocumentId = doc.Id,
            HeadingsJson = "[]",
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            ParserVersion = "1.0.0"
        });

        var afterMetadataSave = await _docRepo.GetByIdAsync(doc.Id);
        Assert.Equal("pending", afterMetadataSave!.IndexStatus);
        Assert.NotEqual("ready", afterMetadataSave.IndexStatus);
        Assert.NotNull(await _metadataRepo.GetMarkdownMetadataAsync(doc.Id));

        // A complete indexing run is the only transition to ready, and it is backed by
        // a real search row.
        var coordinator = new IncrementalIndexCoordinator(
            _docRepo,
            _metadataRepo,
            new FtsIndex(_appDb),
            new MarkdownStructureParser());
        await coordinator.ReindexDocumentAsync(doc.Id);

        Assert.Equal("ready", (await _docRepo.GetByIdAsync(doc.Id))!.IndexStatus);
        Assert.Contains(
            await new FtsIndex(_appDb).SearchAsync("Boundary"),
            r => r.DocumentId == doc.Id);
    }

    [Fact]
    public async Task NullMeansNotApplicable_NotFabricatedDefaults()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\notes.md",
            Filename = "notes.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        await _docRepo.CreateAsync(doc);

        var mdMeta = new MarkdownDocumentMetadata
        {
            DocumentId = doc.Id,
            FrontmatterJson = null,
            HeadingsJson = "[]",
            OutboundLinksJson = null,
            WikilinksJson = null,
            CodeBlockCount = null,
            UpdatedAt = DateTime.UtcNow.ToString("O"),
            ParserVersion = "1.0.0"
        };
        await _metadataRepo.SaveMarkdownMetadataAsync(mdMeta);

        var fetched = await _metadataRepo.GetMarkdownMetadataAsync(doc.Id);
        Assert.NotNull(fetched);
        Assert.Null(fetched!.FrontmatterJson);
        Assert.Null(fetched.OutboundLinksJson);
        Assert.Null(fetched.WikilinksJson);
        Assert.Null(fetched.CodeBlockCount);

        // Check DOCX metadata with explicit nulls
        var docxDoc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\minimal.docx",
            Filename = "minimal.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await _docRepo.CreateAsync(docxDoc);

        var docxMeta = new DocumentMetadata
        {
            DocumentId = docxDoc.Id,
            PageCount = null,
            SectionCount = null,
            TableCount = null,
            ImageCount = null,
            PaperSummary = null,
            OrientationSummary = null,
            MarginSummary = null,
            PrimaryFont = null,
            PrimaryFontSize = null,
            HeaderPresent = null,
            FooterPresent = null,
            HeadingsJson = null,
            ExtractedAt = DateTime.UtcNow.ToString("O"),
            ParserVersion = "1.0.0"
        };
        await _metadataRepo.SaveDocxMetadataAsync(docxMeta);

        var fetchedDocx = await _metadataRepo.GetDocxMetadataAsync(docxDoc.Id);
        Assert.NotNull(fetchedDocx);
        Assert.Null(fetchedDocx!.PageCount);
        Assert.Null(fetchedDocx.SectionCount);
        Assert.Null(fetchedDocx.TableCount);
        Assert.Null(fetchedDocx.ImageCount);
        Assert.Null(fetchedDocx.PaperSummary);
        Assert.Null(fetchedDocx.OrientationSummary);
        Assert.Null(fetchedDocx.MarginSummary);
        Assert.Null(fetchedDocx.PrimaryFont);
        Assert.Null(fetchedDocx.PrimaryFontSize);
        Assert.Null(fetchedDocx.HeaderPresent);
        Assert.Null(fetchedDocx.FooterPresent);
        Assert.Null(fetchedDocx.HeadingsJson);
    }
}
