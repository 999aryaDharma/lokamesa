using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using Xunit;

namespace DocumentIDE.Core.Tests.Filesystem;

public class PhysicalFileOperationsAdversarialTests
{
    [Theory]
    // 1. Boundary / Null / Empty / Whitespace
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    // 2. Directory Separators & Absolute Paths
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    [InlineData("/file.txt")]
    [InlineData("\\file.txt")]
    [InlineData("C:\\file.txt")]
    [InlineData("D:/file.txt")]
    // 3. Navigation / Relative Traversal
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../file.txt")]
    [InlineData("..\\file.txt")]
    [InlineData("./file.txt")]
    // 4. Reserved Windows Device Names (Case-insensitive, single extensions)
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("CON.txt")]
    [InlineData("nul.md")]
    [InlineData("aux.docx")]
    // 5. Alternate Data Stream (ADS) colon syntax
    [InlineData("file.txt:stream")]
    [InlineData("file.txt:$DATA")]
    [InlineData("file.txt:1")]
    [InlineData(":stream")]
    // 6. Trailing Dots and Spaces
    [InlineData("file.txt.")]
    [InlineData("file.txt..")]
    [InlineData("file.txt ")]
    [InlineData("file.txt   ")]
    // 7. Invalid Windows Characters / ASCII Control Chars
    [InlineData("file<name>.txt")]
    [InlineData("file>name.txt")]
    [InlineData("file|name.txt")]
    [InlineData("file?name.txt")]
    [InlineData("file*name.txt")]
    [InlineData("file\"name.txt")]
    [InlineData("file\0.txt")]
    [InlineData("file\n.txt")]
    [InlineData("file\r.txt")]
    public void StressTest_PathSafetyValidator_RejectsAllBoundaryAttacks(string attackInput)
    {
        bool isValid = PathSafetyValidator.IsValidBasename(attackInput, out string? errorMessage);
        Assert.False(isValid, $"PathSafetyValidator accepted invalid basename: '{attackInput}'");
        Assert.NotNull(errorMessage);

        Assert.Throws<InvalidPathException>(() => PathSafetyValidator.ValidateBasename(attackInput));
    }

    [Theory]
    [InlineData("valid_file.txt")]
    [InlineData("My Document (1).docx")]
    [InlineData("notes_2026.md")]
    [InlineData("README")]
    [InlineData(".gitignore")]
    [InlineData(".env")]
    public void StressTest_PathSafetyValidator_AcceptsLegitimateBasenames(string validBasename)
    {
        bool isValid = PathSafetyValidator.IsValidBasename(validBasename, out string? errorMessage);
        Assert.True(isValid, $"PathSafetyValidator rejected valid basename: '{validBasename}' with error: '{errorMessage}'");
        Assert.Null(errorMessage);

        PathSafetyValidator.ValidateBasename(validBasename);
    }

    [Fact]
    public async Task StressTest_NonExistentSource_ThrowsFileNotFoundException()
    {
        var operations = new PhysicalFileOperations();
        string fakeSource = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N") + ".txt");
        string tempDir = Path.GetTempPath();

        await Assert.ThrowsAsync<FileNotFoundException>(() => operations.RenameItemAsync(fakeSource, "newname.txt"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => operations.MoveItemAsync(fakeSource, tempDir));
        await Assert.ThrowsAsync<FileNotFoundException>(() => operations.DuplicateItemAsync(fakeSource));
    }

    [Fact]
    public async Task StressTest_NonExistentDestinationDirectory_ThrowsDirectoryNotFoundException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Stress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "file.txt");
            await File.WriteAllTextAsync(sourceFile, "test content");

            var operations = new PhysicalFileOperations();
            string fakeDestDir = Path.Combine(tempDir, "NonExistentDir");

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => operations.MoveItemAsync(sourceFile, fakeDestDir));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StressTest_LockedFile_HandlesSharingViolationGracefully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Stress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string sourceFile = Path.Combine(tempDir, "locked_file.txt");
            await File.WriteAllTextAsync(sourceFile, "Locked content");

            // Open file with exclusive lock (FileShare.None)
            using (var stream = new FileStream(sourceFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var correlationService = new TestMoveCorrelationService();
                var operations = new PhysicalFileOperations(moveCorrelationService: correlationService);

                // 1. Rename on locked file
                var renameResult = await operations.RenameItemAsync(sourceFile, "renamed_locked.txt");
                Assert.False(renameResult.Success);
                Assert.NotNull(renameResult.ErrorMessage);
                Assert.True(File.Exists(sourceFile));

                // 2. Move on locked file
                string subDir = Path.Combine(tempDir, "sub");
                Directory.CreateDirectory(subDir);
                var moveResult = await operations.MoveItemAsync(sourceFile, subDir);
                Assert.False(moveResult.Success);
                Assert.NotNull(moveResult.ErrorMessage);
                Assert.True(File.Exists(sourceFile));

                // 3. Duplicate on locked file
                var dupResult = await operations.DuplicateItemAsync(sourceFile);
                Assert.False(dupResult.Success);
                Assert.NotNull(dupResult.ErrorMessage);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StressTest_ConcurrentMoves_FailsGracefullyWithoutDataLoss()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Stress_" + Guid.NewGuid().ToString("N"));
        string dirA = Path.Combine(tempDir, "DirA");
        string dirB = Path.Combine(tempDir, "DirB");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        try
        {
            string sourceFile = Path.Combine(tempDir, "shared_file.txt");
            await File.WriteAllTextAsync(sourceFile, "Concurrent move test content");

            var operations = new PhysicalFileOperations();

            // Run two concurrent move tasks for the same source file
            var task1 = Task.Run(() => operations.MoveItemAsync(sourceFile, dirA));
            var task2 = Task.Run(() => operations.MoveItemAsync(sourceFile, dirB));

            // Await both
            PhysicalOperationResultDto? res1 = null;
            PhysicalOperationResultDto? res2 = null;

            try { res1 = await task1; } catch { }
            try { res2 = await task2; } catch { }

            int successCount = (res1?.Success == true ? 1 : 0) + (res2?.Success == true ? 1 : 0);
            Assert.True(successCount <= 1, "Only one concurrent move operation should succeed.");

            // Confirm file exists in exactly one place (either dirA, dirB, or source)
            bool existsInSource = File.Exists(sourceFile);
            bool existsInA = File.Exists(Path.Combine(dirA, "shared_file.txt"));
            bool existsInB = File.Exists(Path.Combine(dirB, "shared_file.txt"));

            int locationCount = (existsInSource ? 1 : 0) + (existsInA ? 1 : 0) + (existsInB ? 1 : 0);
            Assert.Equal(1, locationCount);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StressTest_CreateFile_CaseSensitivityCollisionOnWindows()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Stress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string existingFile = Path.Combine(tempDir, "document.md");
            await File.WriteAllTextAsync(existingFile, "Existing content");

            var operations = new PhysicalFileOperations();

            // Attempt to create "DOCUMENT.MD" which differs only in case
            await Assert.ThrowsAsync<PathCollisionException>(() => operations.CreateMarkdownFileAsync(tempDir, "DOCUMENT.MD", "# New Content"));

            // Original content preserved
            Assert.Equal("Existing content", await File.ReadAllTextAsync(existingFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task StressTest_WindowsOS_PhysicalFileCreationForMultiExtensionReservedNames()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "FS07_Stress_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string targetPath = Path.Combine(tempDir, "CON.spec.md");
            // Windows allows CON.spec.md because the stem before first dot or full stem is CON.spec, not CON
            await File.WriteAllTextAsync(targetPath, "Test multi ext");
            Assert.True(File.Exists(targetPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
