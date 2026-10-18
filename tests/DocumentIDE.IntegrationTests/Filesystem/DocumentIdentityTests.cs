using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class DocumentIdentityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _service;

    public DocumentIdentityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs02_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _documentRepo = new DocumentRepository(_appDb);
        _identityProvider = new WindowsFileIdentityProvider();
        _service = new DocumentIdentityService(_documentRepo, _identityProvider);
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
    public async Task Rename_Keeps_DocumentId()
    {
        // 1. Arrange: Create a temporary file
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs02_rename_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string originalPath = Path.Combine(tempDir, "original.md");
        File.WriteAllText(originalPath, "# Original Content");

        try
        {
            // 2. Act: Resolve original file
            var doc1 = await _service.ResolveDocumentAsync(originalPath);
            Assert.NotNull(doc1);
            string originalId = doc1.Id;

            // Rename the physical file
            string newPath = Path.Combine(tempDir, "renamed.md");
            File.Move(originalPath, newPath);

            // Resolve the renamed file
            var doc2 = await _service.ResolveDocumentAsync(newPath);

            // 3. Assert: DocumentId remains identical
            Assert.NotNull(doc2);
            Assert.Equal(originalId, doc2.Id);
            Assert.Equal(Path.GetFullPath(newPath), doc2.CurrentPath);
            Assert.Equal(Path.GetFullPath(originalPath), doc2.LastKnownPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Two_Distinct_Duplicate_Files_Receive_Distinct_DocumentIds()
    {
        // 1. Arrange: Create two files with identical content but different paths
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs02_duplicate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path1 = Path.Combine(tempDir, "file1.md");
        string path2 = Path.Combine(tempDir, "file2.md");
        
        string identicalContent = "# Identical Content";
        File.WriteAllText(path1, identicalContent);
        File.WriteAllText(path2, identicalContent);

        try
        {
            // 2. Act: Resolve both files
            var doc1 = await _service.ResolveDocumentAsync(path1);
            var doc2 = await _service.ResolveDocumentAsync(path2);

            // 3. Assert: They get distinct DocumentIds
            Assert.NotNull(doc1);
            Assert.NotNull(doc2);
            Assert.NotEqual(doc1.Id, doc2.Id);
            Assert.Equal(Path.GetFullPath(path1), doc1.CurrentPath);
            Assert.Equal(Path.GetFullPath(path2), doc2.CurrentPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Path_Casing_Change_Does_Not_Duplicate_Record()
    {
        // 1. Arrange: Create a temporary file
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs02_casing_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "casing_test.md");
        File.WriteAllText(path, "# Casing Content");

        try
        {
            // 2. Act: Resolve with original path casing
            var doc1 = await _service.ResolveDocumentAsync(path);
            Assert.NotNull(doc1);
            string originalId = doc1.Id;

            // Resolve with different casing (e.g. CASING_TEST.MD)
            string upperCasedPath = Path.Combine(tempDir, "CASING_TEST.MD");
            var doc2 = await _service.ResolveDocumentAsync(upperCasedPath);

            // 3. Assert: They map to the same record and ID is preserved
            Assert.NotNull(doc2);
            Assert.Equal(originalId, doc2.Id);
            Assert.Equal(Path.GetFullPath(upperCasedPath), doc2.CurrentPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Fallback_NonNTFS_Rename_Correlates_Correctly()
    {
        // Test fallback fingerprint-based correlation by mocking or simulating identity failure
        var mockProvider = new MockIdentityProvider();
        var fallbackService = new DocumentIdentityService(_documentRepo, mockProvider);

        string tempDir = Path.Combine(Path.GetTempPath(), $"fs02_fallback_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path1 = Path.Combine(tempDir, "original.md");
        File.WriteAllText(path1, "# Fallback Fingerprint Test Content");

        try
        {
            // Resolve original (non-NTFS)
            var doc1 = await fallbackService.ResolveDocumentAsync(path1);
            Assert.NotNull(doc1);
            string originalId = doc1.Id;
            Assert.Null(doc1.FilesystemVolumeId);

            // Now, simulate that the original file is missing
            doc1.AvailabilityStatus = "missing";
            doc1.LastKnownPath = doc1.CurrentPath;
            await _documentRepo.UpdateAsync(doc1);

            // Rename the physical file
            string path2 = Path.Combine(tempDir, "renamed.md");
            File.Move(path1, path2);

            // Resolve the renamed file
            var doc2 = await fallbackService.ResolveDocumentAsync(path2);

            // Assert: Correlated by fingerprint, returning the same DocumentId
            Assert.NotNull(doc2);
            Assert.Equal(originalId, doc2.Id);
            Assert.Equal(Path.GetFullPath(path2), doc2.CurrentPath);
            Assert.Equal("available", doc2.AvailabilityStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private class MockIdentityProvider : IFileIdentityProvider
    {
        public FileIdentity? GetFileIdentity(string path) => null;
    }
}
