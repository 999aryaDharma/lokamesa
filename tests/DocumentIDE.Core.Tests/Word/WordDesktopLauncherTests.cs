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

public class WordDesktopLauncherTests : IDisposable
{
    private readonly string _tempDirectory;

    public WordDesktopLauncherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DocIDE_WordLauncher_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in temp dir
            }
        }
    }

    private class InMemoryDocumentRepository : IDocumentRepository
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
    public async Task OpenDocumentAsync_WhenWordNotInstalled_ReturnsTypedErrorStatusAndMessage()
    {
        var repo = new InMemoryDocumentRepository();
        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: _ => true,
            availabilityChecker: () => false
        );

        var result = await launcher.OpenDocumentAsync("doc-123");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.WordNotInstalled, result.Status);
        Assert.Equal("Microsoft Word is required for this operation.", result.ErrorMessage);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenDocumentIdNotFound_ReturnsDocumentNotFoundStatus()
    {
        var repo = new InMemoryDocumentRepository();
        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: _ => true,
            availabilityChecker: () => true
        );

        var result = await launcher.OpenDocumentAsync("non-existent-id");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
        Assert.Contains("non-existent-id", result.ErrorMessage);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenFileNotDocx_ReturnsInvalidFileTypeStatus()
    {
        var filePath = Path.Combine(_tempDirectory, "sample.md");
        File.WriteAllText(filePath, "# Hello Markdown");

        var doc = new Document
        {
            Id = "doc-md",
            CurrentPath = filePath,
            Filename = "sample.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        var repo = new InMemoryDocumentRepository();
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: _ => true,
            availabilityChecker: () => true
        );

        var result = await launcher.OpenDocumentAsync("doc-md");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.InvalidFileType, result.Status);
        Assert.Contains(".md", result.ErrorMessage);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenPhysicalFileMissing_ReturnsDocumentNotFoundStatus()
    {
        var filePath = Path.Combine(_tempDirectory, "missing.docx");

        var doc = new Document
        {
            Id = "doc-missing",
            CurrentPath = filePath,
            Filename = "missing.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        var repo = new InMemoryDocumentRepository();
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: _ => true,
            availabilityChecker: () => true
        );

        var result = await launcher.OpenDocumentAsync("doc-missing");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
        Assert.Contains("does not exist", result.ErrorMessage);
    }

    [Fact]
    public async Task OpenDocumentAsync_WhenValidDocx_LaunchesExternalProcessOnCanonicalPath()
    {
        var filePath = Path.Combine(_tempDirectory, "contract.docx");
        File.WriteAllText(filePath, "Dummy OpenXML DOCX Content");

        var doc = new Document
        {
            Id = "doc-docx",
            CurrentPath = filePath,
            Filename = "contract.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        var repo = new InMemoryDocumentRepository();
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

        var result = await launcher.OpenDocumentAsync("doc-docx");

        Assert.True(result.Success);
        Assert.Equal(WordLaunchStatus.Success, result.Status);
        Assert.Equal("doc-docx", result.DocumentId);
        Assert.Equal(Path.GetFullPath(filePath), result.FilePath);
        Assert.Equal(Path.GetFullPath(filePath), launchedPath);
    }

    [Fact]
    public async Task OpenDocumentAsync_DoesNotMutateSourceFileOrFingerprint()
    {
        var filePath = Path.Combine(_tempDirectory, "immutable.docx");
        byte[] originalBytes = Encoding.UTF8.GetBytes("Pre-launch content for fingerprint check");
        File.WriteAllBytes(filePath, originalBytes);

        string initialHash;
        using (var sha256 = SHA256.Create())
        {
            initialHash = Convert.ToHexString(sha256.ComputeHash(originalBytes));
        }

        var doc = new Document
        {
            Id = "doc-immutable",
            CurrentPath = filePath,
            Filename = "immutable.docx",
            Extension = ".docx",
            DocumentType = "docx",
            ContentFingerprint = initialHash
        };
        var repo = new InMemoryDocumentRepository();
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: _ => true,
            availabilityChecker: () => true
        );

        var result = await launcher.OpenDocumentAsync("doc-immutable");

        Assert.True(result.Success);

        byte[] postLaunchBytes = File.ReadAllBytes(filePath);
        string postLaunchHash;
        using (var sha256 = SHA256.Create())
        {
            postLaunchHash = Convert.ToHexString(sha256.ComputeHash(postLaunchBytes));
        }

        Assert.Equal(initialHash, postLaunchHash);
        Assert.Equal(originalBytes, postLaunchBytes);
    }

    [Fact]
    public async Task OpenDocumentAsync_SerializesConcurrentLaunchRequests_EnforcingMaxConcurrencyOne()
    {
        var filePath = Path.Combine(_tempDirectory, "concurrent.docx");
        File.WriteAllText(filePath, "Concurrency Test DOCX Content");

        var doc = new Document
        {
            Id = "doc-concurrent",
            CurrentPath = filePath,
            Filename = "concurrent.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        var repo = new InMemoryDocumentRepository();
        await repo.CreateAsync(doc);

        int activeLaunchers = 0;
        int maxSimultaneous = 0;
        object syncRoot = new object();

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: path =>
            {
                lock (syncRoot)
                {
                    activeLaunchers++;
                    if (activeLaunchers > maxSimultaneous)
                    {
                        maxSimultaneous = activeLaunchers;
                    }
                }

                Task.Delay(50).Wait();

                lock (syncRoot)
                {
                    activeLaunchers--;
                }
                return true;
            },
            availabilityChecker: () => true
        );

        var task1 = launcher.OpenDocumentAsync("doc-concurrent");
        var task2 = launcher.OpenDocumentAsync("doc-concurrent");
        var task3 = launcher.OpenDocumentAsync("doc-concurrent");

        var results = await Task.WhenAll(task1, task2, task3);

        Assert.True(results[0].Success);
        Assert.True(results[1].Success);
        Assert.True(results[2].Success);
        Assert.Equal(1, maxSimultaneous);
    }
}
