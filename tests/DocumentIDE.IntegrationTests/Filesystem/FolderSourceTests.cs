using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;

using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

/// <summary>
/// FS-01 integration tests — Folder Source Attachment.
/// TDD: tests are written before implementation to define behavior.
///
/// CRITICAL SAFETY: Test 4 asserts that attaching a folder NEVER mutates the source directory.
/// </summary>
public class FolderSourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly FolderSourceRepository _folderSourceRepo;
    private readonly FolderSourceService _service;
    private readonly string _testWorkspaceId;

    public FolderSourceTests()
    {
        // Use isolated temp DB per test class instance
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs01_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _folderSourceRepo = new FolderSourceRepository(_appDb);
        _service = new FolderSourceService(_folderSourceRepo);
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

    // -------------------------------------------------------------------------
    // Test 1 (FIRST FAILING): Attach_Valid_Folder_Succeeds_And_Persists
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Attach_Valid_Folder_Succeeds_And_Persists()
    {
        // Arrange: create a temp source directory with some files
        string sourceDir = Path.Combine(Path.GetTempPath(), $"fs01_source_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "report.docx"), "fake docx");
        File.WriteAllText(Path.Combine(sourceDir, "notes.md"), "# Notes");
        File.WriteAllText(Path.Combine(sourceDir, "image.png"), "fake image");  // not supported

        try
        {
            // Act
            var result = await _service.AttachFolderAsync(_testWorkspaceId, sourceDir);

            // Assert: returned result is correct
            Assert.NotNull(result);
            Assert.NotNull(result.FolderSource);
            Assert.Equal(_testWorkspaceId, result.FolderSource.WorkspaceId);
            Assert.Equal("available", result.FolderSource.AvailabilityStatus);
            Assert.Equal(2, result.SupportedFileCount); // only .docx and .md

            // Assert: persisted in DB
            var persisted = await _service.GetFolderSourcesAsync(_testWorkspaceId);
            Assert.Single(persisted);
            Assert.Equal(Path.GetFullPath(sourceDir), persisted[0].CanonicalPath);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Reject_Nonexistent_Or_Inaccessible_Path_With_Typed_Error
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Reject_Nonexistent_Or_Inaccessible_Path_With_Typed_Error()
    {
        string nonexistentPath = @"C:\ThisFolderDoesNotExist_DocumentIDE_Test_" + Guid.NewGuid().ToString("N");

        var ex = await Assert.ThrowsAsync<FolderNotFoundException>(
            () => _service.AttachFolderAsync(_testWorkspaceId, nonexistentPath));

        Assert.Contains(nonexistentPath, ex.Message);
        Assert.Contains(nonexistentPath, ex.FolderPath);

        // Nothing should have been persisted
        var sources = await _service.GetFolderSourcesAsync(_testWorkspaceId);
        Assert.Empty(sources);
    }

    // -------------------------------------------------------------------------
    // Test 3: Reopening_Workspace_Restores_Source_Reference
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Reopening_Workspace_Restores_Source_Reference()
    {
        string sourceDir = Path.Combine(Path.GetTempPath(), $"fs01_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "doc.md"), "content");

        try
        {
            // Attach folder (simulates first session)
            await _service.AttachFolderAsync(_testWorkspaceId, sourceDir);

            // Re-create the service using the same DB (simulates app restart / new session)
            var newRepo = new FolderSourceRepository(_appDb);
            var newService = new FolderSourceService(newRepo);

            // Query restored folder sources
            var restored = await newService.GetFolderSourcesAsync(_testWorkspaceId);

            Assert.Single(restored);
            Assert.Equal(Path.GetFullPath(sourceDir), restored[0].CanonicalPath);
            Assert.Equal("available", restored[0].AvailabilityStatus);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Test 4: Attaching_Folder_Does_Not_Mutate_Source_Directory
    // CRITICAL SAFETY: FS-01 non-negotiable — source folder MUST remain 100% untouched
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Attaching_Folder_Does_Not_Mutate_Source_Directory()
    {
        string sourceDir = Path.Combine(Path.GetTempPath(), $"fs01_nomutate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "existing.md"), "existing content");

        try
        {
            // Capture snapshot of directory before attachment
            var filesBefore = Directory.GetFileSystemEntries(sourceDir, "*", SearchOption.AllDirectories)
                .OrderBy(p => p).ToArray();

            // Attach the folder
            await _service.AttachFolderAsync(_testWorkspaceId, sourceDir);

            // Capture snapshot of directory after attachment
            var filesAfter = Directory.GetFileSystemEntries(sourceDir, "*", SearchOption.AllDirectories)
                .OrderBy(p => p).ToArray();

            // Assert: directory contents are 100% identical — no files/dirs created
            Assert.Equal(filesBefore.Length, filesAfter.Length);
            for (int i = 0; i < filesBefore.Length; i++)
            {
                Assert.Equal(filesBefore[i], filesAfter[i]);
            }

            // Extra check: no .documentide marker was created anywhere
            var documentideFiles = Directory.GetFiles(sourceDir, "*.documentide", SearchOption.AllDirectories);
            Assert.Empty(documentideFiles);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Test 5: Hidden_Directories_Are_Excluded_From_File_Count
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Hidden_Directories_Are_Excluded_From_File_Count()
    {
        string sourceDir = Path.Combine(Path.GetTempPath(), $"fs01_hidden_{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "visible.md"), "content");

        // Create hidden .git directory with a .md file inside
        var gitDir = Path.Combine(sourceDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "COMMIT_EDITMSG.md"), "commit message");

        try
        {
            var result = await _service.AttachFolderAsync(_testWorkspaceId, sourceDir);

            // Only visible.md should be counted — not the file in .git
            Assert.Equal(1, result.SupportedFileCount);
        }
        finally
        {
            Directory.Delete(sourceDir, recursive: true);
        }
    }
}
