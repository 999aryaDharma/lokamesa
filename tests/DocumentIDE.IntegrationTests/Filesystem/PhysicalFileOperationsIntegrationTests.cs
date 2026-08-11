using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class PhysicalFileOperationsIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly FolderSourceRepository _folderRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly MoveCorrelationService _correlationService;
    private readonly WindowsRecycleBin _recycleBin;
    private readonly PhysicalFileOperations _operations;
    private readonly string _tempDirectory;

    public PhysicalFileOperationsIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs07_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _documentRepo = new DocumentRepository(_appDb);
        _folderRepo = new FolderSourceRepository(_appDb);
        _identityProvider = new WindowsFileIdentityProvider();
        _identityService = new DocumentIdentityService(_documentRepo, _identityProvider);
        _correlationService = new MoveCorrelationService(_documentRepo, _identityProvider, _identityService, _folderRepo);
        _recycleBin = new WindowsRecycleBin();
        _operations = new PhysicalFileOperations(_correlationService, _recycleBin, _documentRepo);

        _tempDirectory = Path.Combine(Path.GetTempPath(), $"fs07_integration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch { }

        try
        {
            if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
        }
        catch { }
    }

    [Fact]
    public async Task PhysicalOperations_FullLifecycle_CreateRenameMoveDuplicateDelete()
    {
        // 1. Create Folder
        var createDirResult = await _operations.CreateDirectoryAsync(_tempDirectory, "ProjectFiles");
        Assert.True(createDirResult.Success);
        string projectDir = createDirResult.TargetPath;
        Assert.True(Directory.Exists(projectDir));

        // 2. Create Markdown File
        var createMdResult = await _operations.CreateMarkdownFileAsync(projectDir, "notes", "# Initial Notes");
        Assert.True(createMdResult.Success);
        string mdPath = createMdResult.TargetPath;
        Assert.True(File.Exists(mdPath));

        // 3. Create DOCX File
        var createDocxResult = await _operations.CreateDocxFileAsync(projectDir, "spec");
        Assert.True(createDocxResult.Success);
        string docxPath = createDocxResult.TargetPath;
        Assert.True(File.Exists(docxPath));

        // Register document in repository for correlation testing
        string docId = Guid.NewGuid().ToString();
        var doc = new Document
        {
            Id = docId,
            CurrentPath = mdPath,
            Filename = "notes.md",
            Extension = ".md",
            DocumentType = "markdown",
            SizeBytes = new FileInfo(mdPath).Length,
            ContentFingerprint = "test-fp",
            AvailabilityStatus = "available",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        await _documentRepo.CreateAsync(doc);

        // 4. Rename Markdown File
        var renameResult = await _operations.RenameItemAsync(mdPath, "notes_v2.md");
        Assert.True(renameResult.Success);
        string renamedMdPath = renameResult.TargetPath;
        Assert.False(File.Exists(mdPath));
        Assert.True(File.Exists(renamedMdPath));

        // Verify active move intent was recorded
        var activeIntent = _correlationService.FindActiveIntentByDestination(renamedMdPath);
        Assert.NotNull(activeIntent);
        Assert.Equal(docId, activeIntent.DocumentId);

        // 5. Move File to Subdirectory
        string subDir = Path.Combine(projectDir, "Archive");
        await _operations.CreateDirectoryAsync(projectDir, "Archive");
        var moveResult = await _operations.MoveItemAsync(renamedMdPath, subDir);
        Assert.True(moveResult.Success);
        string movedMdPath = moveResult.TargetPath;
        Assert.False(File.Exists(renamedMdPath));
        Assert.True(File.Exists(movedMdPath));

        // 6. Duplicate File
        var dupResult = await _operations.DuplicateItemAsync(movedMdPath, "notes_v2_copy.md");
        Assert.True(dupResult.Success);
        Assert.True(File.Exists(dupResult.TargetPath));

        // 7. Windows Recycle Bin Delete (if running on Windows)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var deleteResult = await _operations.DeleteItemAsync(dupResult.TargetPath);
            Assert.True(deleteResult.Success);
            Assert.False(File.Exists(dupResult.TargetPath));
        }
    }

    [Fact]
    public async Task PhysicalOperations_DestinationCollision_RejectsOperation()
    {
        string file1 = Path.Combine(_tempDirectory, "file1.md");
        string file2 = Path.Combine(_tempDirectory, "file2.md");
        await File.WriteAllTextAsync(file1, "File 1");
        await File.WriteAllTextAsync(file2, "File 2");

        await Assert.ThrowsAsync<PathCollisionException>(() => _operations.RenameItemAsync(file1, "file2.md"));
        await Assert.ThrowsAsync<PathCollisionException>(() => _operations.CreateMarkdownFileAsync(_tempDirectory, "file1.md"));
        await Assert.ThrowsAsync<PathCollisionException>(() => _operations.DuplicateItemAsync(file1, "file2.md"));
    }
}
