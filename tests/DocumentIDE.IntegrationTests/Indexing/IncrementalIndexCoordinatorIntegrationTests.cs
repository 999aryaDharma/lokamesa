using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Indexing;

public class IncrementalIndexCoordinatorIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly DocumentMetadataRepository _metadataRepo;
    private readonly FtsIndex _ftsIndex;
    private readonly MarkdownStructureParser _markdownParser;
    private readonly DocxStructureParser _docxParser;
    private readonly string _tempFolder;

    public IncrementalIndexCoordinatorIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_idx06_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _documentRepo = new DocumentRepository(_appDb);
        _metadataRepo = new DocumentMetadataRepository(_appDb);
        _ftsIndex = new FtsIndex(_appDb);
        _markdownParser = new MarkdownStructureParser();
        _docxParser = new DocxStructureParser();

        _tempFolder = Path.Combine(Path.GetTempPath(), $"docide_idx06_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
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

        if (Directory.Exists(_tempFolder))
        {
            try { Directory.Delete(_tempFolder, true); } catch { }
        }
    }

    [Fact]
    public async Task EndToEnd_MarkdownFile_CreatedModifiedDeleted_Lifecycle()
    {
        var coordinator = new IncrementalIndexCoordinator(
            _documentRepo,
            _metadataRepo,
            _ftsIndex,
            _markdownParser,
            _docxParser);

        var filePath = Path.Combine(_tempFolder, "report.md");
        await File.WriteAllTextAsync(filePath, "# Report Heading\nThis report mentions NUSA_DUA_SITE in detail.");

        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = filePath,
            Filename = "report.md",
            Extension = ".md",
            DocumentType = "markdown",
            ContentFingerprint = "fp_v1"
        };
        await _documentRepo.CreateAsync(doc);

        // 1. Process Created event
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Created,
            DocumentId = doc.Id,
            Path = filePath
        });

        // Verify searchable via FTS
        var searchResults = await _ftsIndex.SearchAsync("NUSA_DUA_SITE");
        Assert.Single(searchResults);
        Assert.Equal(doc.Id, searchResults[0].DocumentId);

        // Verify metadata saved in SQLite
        var mdMetadata = await _metadataRepo.GetMarkdownMetadataAsync(doc.Id);
        Assert.NotNull(mdMetadata);
        Assert.Equal("1.0.0", mdMetadata.ParserVersion);

        // 2. Process Modified event with new content
        await File.WriteAllTextAsync(filePath, "# Report Heading Updated\nUpdated text with BALI_PROJECT token.");
        doc.ContentFingerprint = "fp_v2";
        await _documentRepo.UpdateAsync(doc);

        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Modified,
            DocumentId = doc.Id,
            Path = filePath
        });

        // Old token gone, new token searchable
        var oldSearchResults = await _ftsIndex.SearchAsync("NUSA_DUA_SITE");
        Assert.Empty(oldSearchResults);

        var newSearchResults = await _ftsIndex.SearchAsync("BALI_PROJECT");
        Assert.Single(newSearchResults);
        Assert.Equal(doc.Id, newSearchResults[0].DocumentId);

        // 3. Process Deleted event
        File.Delete(filePath);
        await coordinator.ProcessFileChangeAsync(new FileChangeEvent
        {
            ChangeType = FileChangeType.Deleted,
            DocumentId = doc.Id,
            Path = filePath
        });

        // FTS entry removed
        var afterDeleteSearchResults = await _ftsIndex.SearchAsync("BALI_PROJECT");
        Assert.Empty(afterDeleteSearchResults);

        // SQLite metadata row retained, status marked missing
        var retainedMetadata = await _metadataRepo.GetMarkdownMetadataAsync(doc.Id);
        Assert.NotNull(retainedMetadata);

        var updatedDoc = await _documentRepo.GetByIdAsync(doc.Id);
        Assert.NotNull(updatedDoc);
        Assert.Equal("missing", updatedDoc.IndexStatus);
    }
}
