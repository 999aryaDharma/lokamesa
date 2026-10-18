using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using Xunit;

namespace DocumentIDE.Core.Tests.Filesystem;

public class TestRecycleBin : IRecycleBin
{
    public bool WasCalled { get; private set; }
    public string? LastPath { get; private set; }
    public bool ReturnSuccess { get; set; } = true;
    public string? SimulatedError { get; set; }

    public bool SendToRecycleBin(string path, out string? error)
    {
        WasCalled = true;
        LastPath = path;
        error = SimulatedError;
        return ReturnSuccess;
    }
}

public class TestMoveCorrelationService : IMoveCorrelationService
{
    public bool RecordMoveIntentCalled { get; private set; }
    public string? LastRecordedSource { get; private set; }
    public string? LastRecordedDestination { get; private set; }

    public void RecordMoveIntent(string documentId, string sourcePath, string destinationPath, TimeSpan? ttl = null)
    {
        RecordMoveIntentCalled = true;
        LastRecordedSource = sourcePath;
        LastRecordedDestination = destinationPath;
    }

    public PhysicalMoveIntent? FindActiveIntentBySource(string sourcePath) => null;
    public PhysicalMoveIntent? FindActiveIntentByDestination(string destinationPath) => null;
    public void CompleteIntent(string intentId) { }
    public Task<DocumentIDE.Core.Domain.Document?> CorrelateSameVolumeMoveAsync(string newPath, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<DocumentIDE.Core.Domain.Document?>(null);
    public Task<RebindCandidateListDto> GetRebindCandidatesAsync(string documentId, string? searchDirectory = null, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(new RebindCandidateListDto(documentId, "", false, new()));
    public Task<RebindResultDto> RebindDocumentAsync(string documentId, string targetPath, bool forceRebind = false, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(new RebindResultDto(documentId, "", targetPath, "available", true));
}

public class PhysicalFileOperationsTests
{
    [Theory]
    [InlineData("sub/file.txt")]
    [InlineData("sub\\file.txt")]
    [InlineData("../file.txt")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:\\file.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("con.txt")]
    [InlineData("NUL.md")]
    [InlineData("aux.docx")]
    [InlineData("file.txt:stream")]
    [InlineData("doc.docx:$DATA")]
    [InlineData("file.txt.")]
    [InlineData("file.txt ")]
    [InlineData("file<1>.txt")]
    [InlineData("file|2.txt")]
    [InlineData("file?3.txt")]
    [InlineData("file*4.txt")]
    [InlineData("file\"5.txt")]
    [InlineData("")]
    [InlineData("   ")]
    public void PathSafetyValidator_RejectsInvalidBasenames(string invalidBasename)
    {
        Assert.False(PathSafetyValidator.IsValidBasename(invalidBasename, out string? errorMessage));
        Assert.NotNull(errorMessage);
        Assert.Throws<InvalidPathException>(() => PathSafetyValidator.ValidateBasename(invalidBasename));
    }

    [Theory]
    [InlineData("valid_file.txt")]
    [InlineData("My Document (1).docx")]
    [InlineData("notes_2026.md")]
    [InlineData("README")]
    public void PathSafetyValidator_AcceptsValidBasenames(string validBasename)
    {
        Assert.True(PathSafetyValidator.IsValidBasename(validBasename, out string? errorMessage));
        Assert.Null(errorMessage);
        PathSafetyValidator.ValidateBasename(validBasename);
    }

    [Fact]
    public async Task Rename_ValidBasename_SuccessfullyRenamesFileAndJournalsIntent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "oldname.md");
            await File.WriteAllTextAsync(sourceFile, "# Content");

            var correlationService = new TestMoveCorrelationService();
            var operations = new PhysicalFileOperations(moveCorrelationService: correlationService);

            var result = await operations.RenameItemAsync(sourceFile, "newname.md");

            Assert.True(result.Success);
            Assert.False(File.Exists(sourceFile));
            Assert.True(File.Exists(result.TargetPath));
            Assert.True(correlationService.RecordMoveIntentCalled);
            Assert.Equal(Path.GetFullPath(sourceFile), correlationService.LastRecordedSource);
            Assert.Equal(Path.GetFullPath(result.TargetPath), correlationService.LastRecordedDestination);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Rename_DestinationCollision_FailsExplicitly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "file1.md");
            string targetFile = Path.Combine(tempDir, "file2.md");
            await File.WriteAllTextAsync(sourceFile, "Content 1");
            await File.WriteAllTextAsync(targetFile, "Content 2");

            var operations = new PhysicalFileOperations();

            await Assert.ThrowsAsync<PathCollisionException>(() => operations.RenameItemAsync(sourceFile, "file2.md"));

            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Move_ValidDestination_MovesFileAndJournalsIntent()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        string subDir = Path.Combine(tempDir, "subfolder");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(subDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "document.md");
            await File.WriteAllTextAsync(sourceFile, "# Hello");

            var correlationService = new TestMoveCorrelationService();
            var operations = new PhysicalFileOperations(moveCorrelationService: correlationService);

            var result = await operations.MoveItemAsync(sourceFile, subDir);

            Assert.True(result.Success);
            Assert.False(File.Exists(sourceFile));
            Assert.True(File.Exists(Path.Combine(subDir, "document.md")));
            Assert.True(correlationService.RecordMoveIntentCalled);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Duplicate_GeneratesNonCollidingCopyName()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "report.md");
            await File.WriteAllTextAsync(sourceFile, "# Report");

            var operations = new PhysicalFileOperations();

            var dup1 = await operations.DuplicateItemAsync(sourceFile);
            Assert.True(dup1.Success);
            Assert.Equal(Path.Combine(tempDir, "report - Copy.md"), dup1.TargetPath);

            var dup2 = await operations.DuplicateItemAsync(sourceFile);
            Assert.True(dup2.Success);
            Assert.Equal(Path.Combine(tempDir, "report - Copy (2).md"), dup2.TargetPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CreateMarkdownFile_AppendsMdExtensionAndCreatesFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var operations = new PhysicalFileOperations();

            var result = await operations.CreateMarkdownFileAsync(tempDir, "my_notes", "# Header");

            Assert.True(result.Success);
            Assert.Equal(Path.Combine(tempDir, "my_notes.md"), result.TargetPath);
            Assert.Equal("# Header", await File.ReadAllTextAsync(result.TargetPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CreateDocxFile_GeneratesValidOpenXmlZipPackage()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var operations = new PhysicalFileOperations();

            var result = await operations.CreateDocxFileAsync(tempDir, "contract");

            Assert.True(result.Success);
            Assert.Equal(Path.Combine(tempDir, "contract.docx"), result.TargetPath);
            Assert.True(File.Exists(result.TargetPath));

            byte[] bytes = await File.ReadAllBytesAsync(result.TargetPath);
            Assert.True(bytes.Length > 0);
            // Verify PK ZIP magic bytes
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task DeleteItem_InvokesRecycleBin()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string fileToDelete = Path.Combine(tempDir, "trash.txt");
            await File.WriteAllTextAsync(fileToDelete, "garbage");

            var recycleBin = new TestRecycleBin();
            var operations = new PhysicalFileOperations(recycleBin: recycleBin);

            var result = await operations.DeleteItemAsync(fileToDelete);

            Assert.True(result.Success);
            Assert.True(recycleBin.WasCalled);
            Assert.Equal(Path.GetFullPath(fileToDelete), recycleBin.LastPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CanvasDrag_DecouplingContract_CanvasClassesDoNotDependOnIPhysicalFileOperations()
    {
        // Architecture contract check:
        // Canvas domain or controllers must not depend on IPhysicalFileOperations.
        var coreAssembly = typeof(IPhysicalFileOperations).Assembly;
        var canvasTypes = coreAssembly.GetTypes().Where(t => t.Name.Contains("Canvas", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var type in canvasTypes)
        {
            var constructors = type.GetConstructors();
            foreach (var ctor in constructors)
            {
                var paramsInfo = ctor.GetParameters();
                Assert.DoesNotContain(paramsInfo, p => p.ParameterType == typeof(IPhysicalFileOperations));
            }
        }
    }

    [Fact]
    public void MinimalDocxBuilder_GeneratedBytes_PassDocxPackagePreflightValidation()
    {
        byte[] docxBytes = MinimalDocxBuilder.CreateMinimalDocxBytes();
        using var stream = new MemoryStream(docxBytes);

        var preflight = new DocumentIDE.Infrastructure.Docx.DocxPackagePreflight();
        var result = preflight.ValidatePackage(stream);

        Assert.True(result.IsValid, $"DocxPackagePreflight failed: {result.ErrorMessage} ({result.ErrorCode})");
        Assert.NotNull(result.Metrics);
        Assert.True(result.Metrics.EntryCount > 0);
    }

    [Fact]
    public async Task Rename_DestinationCollision_DoesNotCorruptDestinationFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "file1.md");
            string targetFile = Path.Combine(tempDir, "file2.md");
            string targetOriginalContent = "Original Target Content Do Not Overwrite";
            string sourceOriginalContent = "Original Source Content";

            await File.WriteAllTextAsync(sourceFile, sourceOriginalContent);
            await File.WriteAllTextAsync(targetFile, targetOriginalContent);

            var operations = new PhysicalFileOperations();

            await Assert.ThrowsAsync<PathCollisionException>(() => operations.RenameItemAsync(sourceFile, "file2.md"));

            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(targetFile));
            Assert.Equal(sourceOriginalContent, await File.ReadAllTextAsync(sourceFile));
            Assert.Equal(targetOriginalContent, await File.ReadAllTextAsync(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Move_DestinationCollision_DoesNotCorruptDestinationFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        string subDir = Path.Combine(tempDir, "subfolder");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(subDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "doc.md");
            string targetFile = Path.Combine(subDir, "doc.md");
            string sourceOriginalContent = "Source File Content";
            string targetOriginalContent = "Subfolder Existing Content";

            await File.WriteAllTextAsync(sourceFile, sourceOriginalContent);
            await File.WriteAllTextAsync(targetFile, targetOriginalContent);

            var operations = new PhysicalFileOperations();

            await Assert.ThrowsAsync<PathCollisionException>(() => operations.MoveItemAsync(sourceFile, subDir));

            Assert.True(File.Exists(sourceFile));
            Assert.True(File.Exists(targetFile));
            Assert.Equal(sourceOriginalContent, await File.ReadAllTextAsync(sourceFile));
            Assert.Equal(targetOriginalContent, await File.ReadAllTextAsync(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CreateFile_DestinationCollision_DoesNotCorruptDestinationFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string targetFile = Path.Combine(tempDir, "notes.md");
            string existingContent = "Pre-existing Notes Content";
            await File.WriteAllTextAsync(targetFile, existingContent);

            var operations = new PhysicalFileOperations();

            await Assert.ThrowsAsync<PathCollisionException>(() => operations.CreateMarkdownFileAsync(tempDir, "notes.md", "# Overwrite Attempt"));

            Assert.True(File.Exists(targetFile));
            Assert.Equal(existingContent, await File.ReadAllTextAsync(targetFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}

