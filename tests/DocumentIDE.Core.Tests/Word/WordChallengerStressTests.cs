using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Word;
using DocumentIDE.Infrastructure.Word;
using Xunit;

namespace DocumentIDE.Core.Tests.Word;

public class WordChallengerStressTests : IDisposable
{
    private readonly string _tempDir;

    public WordChallengerStressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WordChallengerTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task StressTest_Concurrency_50ParallelRequests_StrictlySerializesToMaxOne()
    {
        var filePath = Path.Combine(_tempDir, "stress_concurrency.docx");
        File.WriteAllText(filePath, "Stress Test OpenXML DOCX Content");

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-stress-conc",
            CurrentPath = filePath,
            Filename = "stress_concurrency.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        int currentConcurrency = 0;
        int maxConcurrencyRecorded = 0;
        object lockObj = new object();

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: path =>
            {
                lock (lockObj)
                {
                    currentConcurrency++;
                    if (currentConcurrency > maxConcurrencyRecorded)
                    {
                        maxConcurrencyRecorded = currentConcurrency;
                    }
                }

                // Simulate brief Word COM activation latency
                Task.Delay(15).Wait();

                lock (lockObj)
                {
                    currentConcurrency--;
                }
                return true;
            },
            availabilityChecker: () => true
        );

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => launcher.OpenDocumentAsync("doc-stress-conc"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(50, results.Length);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.All(results, r => Assert.Equal(WordLaunchStatus.Success, r.Status));
        Assert.Equal(1, maxConcurrencyRecorded);
    }

    [Fact]
    public async Task StressTest_SemaphoreReleaseOnException_DoesNotDeadlockSubsequentCalls()
    {
        var filePath = Path.Combine(_tempDir, "exception_recovery.docx");
        File.WriteAllText(filePath, "Exception Recovery DOCX");

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-ex-rec",
            CurrentPath = filePath,
            Filename = "exception_recovery.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        bool shouldThrow = true;

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: path =>
            {
                if (shouldThrow)
                {
                    shouldThrow = false;
                    throw new InvalidOperationException("Simulated COM Launch Crash!");
                }
                return true;
            },
            availabilityChecker: () => true
        );

        // First call triggers exception
        var result1 = await launcher.OpenDocumentAsync("doc-ex-rec");
        Assert.False(result1.Success);
        Assert.Equal(WordLaunchStatus.ComError, result1.Status);
        Assert.Contains("Simulated COM Launch Crash!", result1.ErrorMessage);

        // Second call should succeed because semaphore was released in finally block
        var result2 = await launcher.OpenDocumentAsync("doc-ex-rec");
        Assert.True(result2.Success);
        Assert.Equal(WordLaunchStatus.Success, result2.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ErrorTest_MissingDocumentId_ReturnsNotFoundStatus(string? invalidDocId)
    {
        var repo = new MockRepo();
        var launcher = new WordDesktopLauncher(repo, _ => true, () => true);

        var result = await launcher.OpenDocumentAsync(invalidDocId!);

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
        Assert.Equal("Document ID is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task ErrorTest_NonExistentFilePath_ReturnsDocumentNotFoundStatus()
    {
        var missingPath = Path.Combine(_tempDir, "does_not_exist_file_999.docx");
        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-nonexistent-file",
            CurrentPath = missingPath,
            Filename = "does_not_exist_file_999.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(repo, _ => true, () => true);

        var result = await launcher.OpenDocumentAsync("doc-nonexistent-file");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.DocumentNotFound, result.Status);
        Assert.Contains("does not exist on disk", result.ErrorMessage);
    }

    [Theory]
    [InlineData(".md", "markdown")]
    [InlineData(".txt", "text")]
    [InlineData(".pdf", "pdf")]
    [InlineData(".png", "image")]
    [InlineData("", "unknown")]
    public async Task ErrorTest_NonDocxFileType_ReturnsInvalidFileTypeStatus(string extension, string docType)
    {
        var filePath = Path.Combine(_tempDir, "sample" + (string.IsNullOrEmpty(extension) ? ".bin" : extension));
        File.WriteAllText(filePath, "Some non-docx file content");

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-invalid-type-" + docType,
            CurrentPath = filePath,
            Filename = Path.GetFileName(filePath),
            Extension = extension,
            DocumentType = docType
        };
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(repo, _ => true, () => true);

        var result = await launcher.OpenDocumentAsync(doc.Id);

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.InvalidFileType, result.Status);
        Assert.Contains("is not supported for opening in Microsoft Word", result.ErrorMessage);
    }

    [Fact]
    public async Task ErrorTest_MissingWordInstallationState_ReturnsWordNotInstalledStatus()
    {
        var filePath = Path.Combine(_tempDir, "test.docx");
        File.WriteAllText(filePath, "DOCX content");

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-word-missing",
            CurrentPath = filePath,
            Filename = "test.docx",
            Extension = ".docx",
            DocumentType = "docx"
        };
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(
            repo,
            processLauncher: _ => true,
            availabilityChecker: () => false
        );

        var result = await launcher.OpenDocumentAsync("doc-word-missing");

        Assert.False(result.Success);
        Assert.Equal(WordLaunchStatus.WordNotInstalled, result.Status);
        Assert.Equal("Microsoft Word is required for this operation.", result.ErrorMessage);
    }

    [Fact]
    public async Task FileSafetyTest_SourceFileUnmutated_HashIdenticalAfterOpen()
    {
        var filePath = Path.Combine(_tempDir, "source_safety.docx");
        byte[] originalContent = Encoding.UTF8.GetBytes("Binary openxml content verification for safety");
        File.WriteAllBytes(filePath, originalContent);

        string originalHash;
        using (var sha256 = SHA256.Create())
        {
            originalHash = Convert.ToHexString(sha256.ComputeHash(originalContent));
        }

        var repo = new MockRepo();
        var doc = new Document
        {
            Id = "doc-safety",
            CurrentPath = filePath,
            Filename = "source_safety.docx",
            Extension = ".docx",
            DocumentType = "docx",
            ContentFingerprint = originalHash
        };
        await repo.CreateAsync(doc);

        var launcher = new WordDesktopLauncher(repo, _ => true, () => true);
        var result = await launcher.OpenDocumentAsync("doc-safety");

        Assert.True(result.Success);

        byte[] postLaunchContent = File.ReadAllBytes(filePath);
        string postLaunchHash;
        using (var sha256 = SHA256.Create())
        {
            postLaunchHash = Convert.ToHexString(sha256.ComputeHash(postLaunchContent));
        }

        Assert.Equal(originalHash, postLaunchHash);
        Assert.Equal(originalContent, postLaunchContent);
    }
}
