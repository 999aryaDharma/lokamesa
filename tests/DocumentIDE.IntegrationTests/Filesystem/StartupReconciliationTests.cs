using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class StartupReconciliationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly FolderSourceRepository _folderRepo;
    private readonly DocumentRepository _documentRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly StartupReconciler _reconciler;
    private readonly string _testWorkspaceId;

    public StartupReconciliationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs04_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _folderRepo = new FolderSourceRepository(_appDb);
        _documentRepo = new DocumentRepository(_appDb);
        _identityProvider = new WindowsFileIdentityProvider();
        _identityService = new DocumentIdentityService(_documentRepo, _identityProvider);
        _reconciler = new StartupReconciler(_folderRepo, _documentRepo, _identityService, _identityProvider);
        _testWorkspaceId = Guid.NewGuid().ToString();

        // Seed a workspace row so FK constraints pass
        using var conn = _appDb.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspaces (id, name, active_view, created_at, updated_at)
            VALUES (@id, 'Test Workspace', 'canvas', @now, @now);";
        cmd.Parameters.AddWithValue("@id", _testWorkspaceId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
    public async Task Changes_Made_While_App_Closed_Are_Detected()
    {
        // 1. Arrange: Setup folder source & 3 initial files
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs04_downtime_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string fileUnchanged = Path.Combine(tempDir, "unchanged.md");
        string fileModified = Path.Combine(tempDir, "modified.md");
        string fileDeleted = Path.Combine(tempDir, "deleted.md");

        File.WriteAllText(fileUnchanged, "# Unchanged Content");
        File.WriteAllText(fileModified, "# Old Modified Content");
        File.WriteAllText(fileDeleted, "# Deleted Content");

        try
        {
            var folderSource = new FolderSource
            {
                Id = Guid.NewGuid().ToString(),
                WorkspaceId = _testWorkspaceId,
                CanonicalPath = tempDir,
                DisplayName = "TestFolder",
                AvailabilityStatus = "available"
            };
            await _folderRepo.CreateAsync(folderSource);

            // Populate DB with initial document state
            var docUnchanged = await _identityService.ResolveDocumentAsync(fileUnchanged);
            var docModified = await _identityService.ResolveDocumentAsync(fileModified);
            var docDeleted = await _identityService.ResolveDocumentAsync(fileDeleted);

            // Simulate app closed downtime changes:
            // a) Modify `fileModified` content and timestamp
            await Task.Delay(50);
            File.WriteAllText(fileModified, "# NEW Modified Content on Disk!");

            // b) Delete `fileDeleted` from disk
            File.Delete(fileDeleted);

            // c) Add a new file `fileCreated` on disk
            string fileCreated = Path.Combine(tempDir, "newly_created.md");
            File.WriteAllText(fileCreated, "# Brand New File Content");

            // 2. Act: Perform startup reconciliation
            var result = await _reconciler.ReconcileWorkspaceAsync(_testWorkspaceId);

            // 3. Assert: Verify difference classification
            Assert.NotNull(result);
            Assert.Equal(_testWorkspaceId, result.WorkspaceId);
            Assert.Equal(1, result.UnchangedCount);
            Assert.Equal(1, result.ModifiedCount);
            Assert.Equal(1, result.NewCount);
            Assert.Equal(1, result.MissingCount);

            // Check modified document record
            var updatedModifiedDoc = await _documentRepo.GetByIdAsync(docModified.Id);
            Assert.NotNull(updatedModifiedDoc);
            Assert.Equal("pending", updatedModifiedDoc.IndexStatus);
            Assert.Equal(new FileInfo(fileModified).Length, updatedModifiedDoc.SizeBytes);

            // Check missing document record
            var updatedDeletedDoc = await _documentRepo.GetByIdAsync(docDeleted.Id);
            Assert.NotNull(updatedDeletedDoc);
            Assert.Equal("missing", updatedDeletedDoc.AvailabilityStatus);

            // Check newly created document record
            var createdDoc = await _documentRepo.GetByPathAsync(fileCreated);
            Assert.NotNull(createdDoc);
            Assert.Equal("available", createdDoc.AvailabilityStatus);
            Assert.Equal("pending", createdDoc.IndexStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Unchanged_Files_Are_Not_Reparsed()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs04_unchanged_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string file1 = Path.Combine(tempDir, "doc1.md");
        string file2 = Path.Combine(tempDir, "doc2.docx");

        File.WriteAllText(file1, "# Doc 1");
        File.WriteAllText(file2, "Fake docx content");

        try
        {
            var folderSource = new FolderSource
            {
                Id = Guid.NewGuid().ToString(),
                WorkspaceId = _testWorkspaceId,
                CanonicalPath = tempDir,
                DisplayName = "TestUnchanged",
                AvailabilityStatus = "available"
            };
            await _folderRepo.CreateAsync(folderSource);

            await _identityService.ResolveDocumentAsync(file1);
            await _identityService.ResolveDocumentAsync(file2);

            // Act: Reconcile untouched workspace
            var result = await _reconciler.ReconcileWorkspaceAsync(_testWorkspaceId);

            // Assert
            Assert.Equal(2, result.TotalFiles);
            Assert.Equal(2, result.UnchangedCount);
            Assert.Equal(0, result.ModifiedCount);
            Assert.Equal(0, result.NewCount);
            Assert.Equal(0, result.MissingCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Cached_Canvas_Appears_Before_Reconciliation_Completes()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs04_cached_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string file1 = Path.Combine(tempDir, "doc1.md");
        File.WriteAllText(file1, "# Cached Doc");

        try
        {
            var folderSource = new FolderSource
            {
                Id = Guid.NewGuid().ToString(),
                WorkspaceId = _testWorkspaceId,
                CanonicalPath = tempDir,
                DisplayName = "CachedFolder"
            };
            await _folderRepo.CreateAsync(folderSource);
            var doc = await _identityService.ResolveDocumentAsync(file1);

            // Act: Instantly retrieve cached documents without running ReconcileWorkspaceAsync
            var cachedDocs = await _reconciler.GetCachedWorkspaceDocumentsAsync(_testWorkspaceId);

            // Assert: Fast return of cached items
            Assert.NotNull(cachedDocs);
            Assert.Single(cachedDocs);
            Assert.Equal(doc.Id, cachedDocs[0].Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Filesystem_Wins_Over_Stale_Cache()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs04_stale_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string file1 = Path.Combine(tempDir, "stale.md");
        File.WriteAllText(file1, "# New Disk Content Here");

        try
        {
            var folderSource = new FolderSource
            {
                Id = Guid.NewGuid().ToString(),
                WorkspaceId = _testWorkspaceId,
                CanonicalPath = tempDir,
                DisplayName = "StaleFolder"
            };
            await _folderRepo.CreateAsync(folderSource);

            var doc = await _identityService.ResolveDocumentAsync(file1);
            // Manually corrupt DB size and modified_at to simulate stale cache
            doc.SizeBytes = 999999;
            doc.ModifiedAt = DateTime.UtcNow.AddDays(-10).ToString("O");
            doc.IndexStatus = "indexed";
            await _documentRepo.UpdateAsync(doc);

            // Act: Reconcile against physical filesystem
            var result = await _reconciler.ReconcileWorkspaceAsync(_testWorkspaceId);

            // Assert: DB state updated to match disk
            var updatedDoc = await _documentRepo.GetByIdAsync(doc.Id);
            Assert.NotNull(updatedDoc);
            Assert.Equal(1, result.ModifiedCount);
            Assert.Equal(new FileInfo(file1).Length, updatedDoc.SizeBytes);
            Assert.Equal("pending", updatedDoc.IndexStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Reconciliation_Never_Rewrites_Source_Files()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs04_readonly_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string file1 = Path.Combine(tempDir, "doc1.md");
        string file2 = Path.Combine(tempDir, "doc2.docx");

        File.WriteAllText(file1, "# Important User File Content 1");
        File.WriteAllBytes(file2, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 });

        string hash1Initial = ComputeSha256(file1);
        string hash2Initial = ComputeSha256(file2);
        DateTime mtime1Initial = File.GetLastWriteTimeUtc(file1);
        DateTime mtime2Initial = File.GetLastWriteTimeUtc(file2);

        try
        {
            var folderSource = new FolderSource
            {
                Id = Guid.NewGuid().ToString(),
                WorkspaceId = _testWorkspaceId,
                CanonicalPath = tempDir,
                DisplayName = "ReadOnlyFolder"
            };
            await _folderRepo.CreateAsync(folderSource);

            // Act
            await _reconciler.ReconcileWorkspaceAsync(_testWorkspaceId);

            // Assert: 100% byte for byte identical and timestamps untouched
            Assert.Equal(hash1Initial, ComputeSha256(file1));
            Assert.Equal(hash2Initial, ComputeSha256(file2));
            Assert.Equal(mtime1Initial, File.GetLastWriteTimeUtc(file1));
            Assert.Equal(mtime2Initial, File.GetLastWriteTimeUtc(file2));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Unavailable_Folder_Source_Does_Not_Erase_Workspace_State()
    {
        // Arrange
        string missingPath = Path.Combine(Path.GetTempPath(), $"fs04_missing_root_{Guid.NewGuid():N}");
        var folderSource = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = _testWorkspaceId,
            CanonicalPath = missingPath,
            DisplayName = "MissingDrive",
            AvailabilityStatus = "available"
        };
        await _folderRepo.CreateAsync(folderSource);

        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = Path.Combine(missingPath, "doc_on_missing_drive.md"),
            Filename = "doc_on_missing_drive.md",
            Extension = ".md",
            DocumentType = "markdown",
            SizeBytes = 120,
            AvailabilityStatus = "available"
        };
        await _documentRepo.CreateAsync(doc);

        // Act
        var result = await _reconciler.ReconcileWorkspaceAsync(_testWorkspaceId);

        // Assert: Folder source marked unavailable, but document record is NOT deleted or lost
        var updatedSources = await _folderRepo.GetByWorkspaceAsync(_testWorkspaceId);
        Assert.Equal("missing", updatedSources[0].AvailabilityStatus);

        var preservedDoc = await _documentRepo.GetByIdAsync(doc.Id);
        Assert.NotNull(preservedDoc);
        Assert.Equal("missing", preservedDoc.AvailabilityStatus);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
