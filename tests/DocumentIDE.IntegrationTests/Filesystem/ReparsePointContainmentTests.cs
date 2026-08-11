using System;
using System.Collections.Generic;
using System.IO;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using NUnit.Framework;

namespace DocumentIDE.IntegrationTests.Filesystem
{
    [TestFixture]
    public class ReparsePointContainmentTests
    {
        private string _tempDir;
        private string _tempFile;
        private string _junctionPath;
        private FolderSource _folderSource;

        [SetUp]
        public void SetUp()
        {
            // Create a temporary directory for the test workspace
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);

            // Create a regular file inside the temp directory
            _tempFile = Path.Combine(_tempDir, "regular.txt");
            File.WriteAllText(_tempFile, "sample");

            // Create another directory that will be the target for a junction (reparse point)
            string targetDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(targetDir);
            string targetFile = Path.Combine(targetDir, "outside.txt");
            File.WriteAllText(targetFile, "outside");

            // Create a junction that points to the target directory (requires admin rights on Windows)
            _junctionPath = Path.Combine(_tempDir, "junction");
            var procInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C mklink /J \"{_junctionPath}\" \"{targetDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = System.Diagnostics.Process.Start(procInfo);
            proc?.WaitForExit();

            // Prepare a folder source representing the temp directory root
            _folderSource = new FolderSource
            {
                Id = Guid.NewGuid().ToString(),
                WorkspaceId = "test-workspace",
                CanonicalPath = _tempDir,
                DisplayName = "Temp Workspace",
                AvailabilityStatus = "available",
                AddedAt = DateTime.UtcNow.ToString("O"),
                CreatedAt = DateTime.UtcNow.ToString("O"),
                UpdatedAt = DateTime.UtcNow.ToString("O")
            };
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { /* ignore cleanup errors */ }
        }

        [Test]
        public void IsReparsePoint_Junction_ReturnsTrue()
        {
            bool isReparse = SourceContainmentPolicy.IsReparsePoint(_junctionPath);
            Assert.IsTrue(isReparse, "Junction should be detected as a reparse point");
        }

        [Test]
        public void IsContained_InsideRoot_ReturnsTrue()
        {
            var sources = new List<FolderSource> { _folderSource };
            bool contained = SourceContainmentPolicy.IsContained(_tempFile, sources);
            Assert.IsTrue(contained);
        }

        [Test]
        public void IsContained_EscapesRoot_ReturnsFalse()
        {
            var sources = new List<FolderSource> { _folderSource };
            string outsidePath = Path.GetTempPath();
            bool contained = SourceContainmentPolicy.IsContained(outsidePath, sources);
            Assert.IsFalse(contained);
        }

        [Test]
        public void ValidateContainment_EscapingPath_Throws()
        {
            var sources = new List<FolderSource> { _folderSource };
            string outsidePath = Path.GetTempPath();
            Assert.Throws<InvalidPathException>(() => SourceContainmentPolicy.ValidateContainment(outsidePath, sources));
        }
    }
}
