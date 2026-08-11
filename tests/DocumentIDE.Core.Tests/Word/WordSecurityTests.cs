using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Word;
using DocumentIDE.Infrastructure.Word;
using Xunit;

namespace DocumentIDE.Core.Tests.Word;

public class WordSecurityTests : IDisposable
{
    private readonly string _tempDir;

    public WordSecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WordSecurityTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private class MockRepo : IDocumentRepository
    {
        private readonly Dictionary<string, Document> _docs = new();

        public Task CreateAsync(Document doc)
        {
            _docs[doc.Id] = doc;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Document doc)
        {
            _docs[doc.Id] = doc;
            return Task.CompletedTask;
        }

        public Task<Document?> GetByIdAsync(string id)
        {
            if (id == null) return Task.FromResult<Document?>(null);
            _docs.TryGetValue(id, out var doc);
            return Task.FromResult<Document?>(doc);
        }

        public Task<Document?> GetByPathAsync(string currentPath) => Task.FromResult<Document?>(null);
        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) => Task.FromResult<Document?>(null);
        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) => Task.FromResult(new List<Document>());
        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) => Task.FromResult<IReadOnlyList<Document>>(new List<Document>());
        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId) => Task.FromResult<IReadOnlyList<Document>>(new List<Document>());
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    [Fact]
    public async Task ImmutabilityTest_SHA256AndFileBytesUnchangedAfterOpenInvocation()
    {
        var filePath = Path.Combine(_tempDir, "unmutated_source.docx");
        byte[] contentBytes = Encoding.UTF8.GetBytes("Word document binary stream content for immutability test.");
        File.WriteAllBytes(filePath, contentBytes);

        string originalSha256;
        using (var sha256 = SHA256.Create())
        {
            originalSha256 = Convert.ToHexString(sha256.ComputeHash(contentBytes));
        }

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-sha256-test",
            CurrentPath = filePath,
            Filename = "unmutated_source.docx",
            Extension = ".docx",
            DocumentType = "docx",
            ContentFingerprint = originalSha256
        };
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(repo, processLauncher: _ => true, availabilityChecker: () => true);

        var result = await launcher.OpenDocumentAsync("doc-sha256-test");

        Assert.True(result.Success);
        Assert.Equal(WordLaunchStatus.Success, result.Status);

        byte[] postLaunchBytes = File.ReadAllBytes(filePath);
        string postLaunchSha256;
        using (var sha256 = SHA256.Create())
        {
            postLaunchSha256 = Convert.ToHexString(sha256.ComputeHash(postLaunchBytes));
        }

        Assert.Equal(originalSha256, postLaunchSha256);
        Assert.Equal(contentBytes, postLaunchBytes);
    }

    [Fact]
    public async Task PathTraversalTest_RelativePathTraversal_ResolvesToCanonicalAbsolutePath()
    {
        var subDir = Path.Combine(_tempDir, "subfolder");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(_tempDir, "target.docx");
        File.WriteAllText(filePath, "DOCX file content");

        var relativeTraversalPath = Path.Combine(subDir, "..", "target.docx");

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-traversal",
            CurrentPath = relativeTraversalPath,
            Filename = "target.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        string? launchedPath = null;
        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: path =>
            {
                launchedPath = path;
                return true;
            },
            availabilityChecker: () => true
        );

        var result = await launcher.OpenDocumentAsync("doc-traversal");

        Assert.True(result.Success);
        var expectedCanonicalPath = Path.GetFullPath(filePath);
        Assert.Equal(expectedCanonicalPath, launchedPath);
        Assert.Equal(expectedCanonicalPath, result.FilePath);
    }

    [Fact]
    public async Task SecurityTest_ExtensionSpoofing_PhysicalFileIsExecutable_ExposesVulnerabilityOrBehaivor()
    {
        // Physical file on disk is an executable / batch script
        var exePath = Path.Combine(_tempDir, "payload.bat");
        File.WriteAllText(exePath, "@echo off\r\necho Hacked");

        // Document entity has Extension = ".docx", but CurrentPath = payload.bat
        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-spoofed-ext",
            CurrentPath = exePath,
            Filename = "payload.bat",
            Extension = ".docx", // Spoofed extension in DB model
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        string? launchedPath = null;
        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: path =>
            {
                launchedPath = path;
                return true;
            },
            availabilityChecker: () => true
        );

        var result = await launcher.OpenDocumentAsync("doc-spoofed-ext");

        // DocumentIDE should either check actual file extension on disk canonicalPath or allow if valid.
        // We log what happens empirically.
        Assert.NotNull(launchedPath);
        Assert.Equal(Path.GetFullPath(exePath), launchedPath);
    }

    [Fact]
    public async Task PathTraversalTest_NonExistentTraversalPath_ReturnsDocumentNotFound()
    {
        var nonExistentPath = Path.Combine(_tempDir, "nonexistent", "..", "missing.docx");

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-missing-traversal",
            CurrentPath = nonExistentPath,
            Filename = "missing.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(repo, processLauncher: _ => true, availabilityChecker: () => true);

        var result = await launcher.OpenDocumentAsync("doc-missing-traversal");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
    }
}
