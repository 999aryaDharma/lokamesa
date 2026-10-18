using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class MoveCorrelationChallengerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly FolderSourceRepository _folderRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly MoveCorrelationService _correlationService;

    public MoveCorrelationChallengerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs05_challenger_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _documentRepo = new DocumentRepository(_appDb);
        _folderRepo = new FolderSourceRepository(_appDb);
        _identityProvider = new WindowsFileIdentityProvider();
        _identityService = new DocumentIdentityService(_documentRepo, _identityProvider);
        _correlationService = new MoveCorrelationService(_documentRepo, _identityProvider, _identityService, _folderRepo);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath)) { try { File.Delete(walPath); } catch { } }
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath)) { try { File.Delete(shmPath); } catch { } }
    }

    #region 1. Missing Files Edge Cases

    [Fact]
    public async Task RebindDocumentAsync_WithNonExistentTargetFile_ReturnsTypedMissingResult()
    {
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.md");

        var result = await _correlationService.RebindDocumentAsync("doc-123", nonExistentPath);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("missing", result.AvailabilityStatus);
        Assert.Contains("File not found", result.ErrorMessage);
    }

    [Fact]
    public async Task RebindDocumentAsync_WithNonExistentDocumentId_ReturnsTypedMissingResult()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"existing_{Guid.NewGuid():N}.md");
        File.WriteAllText(tempFile, "Hello World");

        try
        {
            var result = await _correlationService.RebindDocumentAsync("non-existent-doc-id", tempFile);

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Equal("missing", result.AvailabilityStatus);
            Assert.Contains("Document not found", result.ErrorMessage);
        }
        finally
        {
            if (File.Exists(tempFile)) { try { File.Delete(tempFile); } catch { } }
        }
    }

    [Fact]
    public async Task CorrelateSameVolumeMoveAsync_WithNonExistentPath_ReturnsNull()
    {
        string nonExistentPath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.md");

        var result = await _correlationService.CorrelateSameVolumeMoveAsync(nonExistentPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRebindCandidatesAsync_WithNonExistentDocumentId_ReturnsEmptyCandidateList()
    {
        var result = await _correlationService.GetRebindCandidatesAsync("unknown-doc-id");

        Assert.NotNull(result);
        Assert.Equal("unknown-doc-id", result.DocumentId);
        Assert.False(result.IsAmbiguous);
        Assert.Empty(result.Candidates);
    }

    #endregion

    #region 2. Locked Files Edge Cases

    [Fact]
    public async Task GetRebindCandidatesAsync_WhenCandidateIsLockedExclusively_GracefullySkipsLockedFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_lock_cand_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalPath = Path.Combine(tempDir, "original.md");
        string content = "# Locked Candidate Test Content";
        File.WriteAllText(originalPath, content);

        var doc = await _identityService.ResolveDocumentAsync(originalPath);
        Assert.NotNull(doc);

        File.Delete(originalPath);
        doc.AvailabilityStatus = "missing";
        doc.LastKnownPath = originalPath;
        await _documentRepo.UpdateAsync(doc);

        string readableCandidate = Path.Combine(tempDir, "readable_cand.md");
        string lockedCandidate = Path.Combine(tempDir, "locked_cand.md");
        File.WriteAllText(readableCandidate, content);
        File.WriteAllText(lockedCandidate, content);

        // Exclusively lock lockedCandidate so FileStream open fails
        using var lockStream = new FileStream(lockedCandidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        try
        {
            var result = await _correlationService.GetRebindCandidatesAsync(doc.Id, tempDir);

            Assert.NotNull(result);
            // The readable candidate should still be returned, while locked candidate is safely skipped
            Assert.Contains(result.Candidates, c => c.TargetPath.Equals(Path.GetFullPath(readableCandidate), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Candidates, c => c.TargetPath.Equals(Path.GetFullPath(lockedCandidate), StringComparison.OrdinalIgnoreCase));
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
    public async Task CorrelateSameVolumeMoveAsync_WhenFileIsLockedShared_ReadsFingerprintSuccessfully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_lock_shared_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalPath = Path.Combine(tempDir, "original_shared.md");
        File.WriteAllText(originalPath, "# Shared Lock Content");

        var doc = await _identityService.ResolveDocumentAsync(originalPath);
        Assert.NotNull(doc);

        string targetPath = Path.Combine(tempDir, "renamed_shared.md");
        File.Move(originalPath, targetPath);

        // Open with FileShare.ReadWrite so ComputeFingerprint (which requests FileShare.ReadWrite | FileShare.Delete) can open it
        using var openStream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        try
        {
            var result = await _correlationService.CorrelateSameVolumeMoveAsync(targetPath);

            Assert.NotNull(result);
            Assert.Equal(doc.Id, result.Id);
            Assert.Equal(Path.GetFullPath(targetPath), result.CurrentPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    #endregion

    #region 3. Duplicate File Contents on Different Paths

    [Fact]
    public async Task DuplicateContents_ThreeIdenticalCopies_MarkedAmbiguous_AutoRebindRefused()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_triple_dup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalPath = Path.Combine(tempDir, "original_missing.md");
        string identicalContent = "# Triple Duplicate Content Test";
        File.WriteAllText(originalPath, identicalContent);

        var doc = await _identityService.ResolveDocumentAsync(originalPath);
        Assert.NotNull(doc);

        File.Delete(originalPath);
        doc.AvailabilityStatus = "missing";
        doc.LastKnownPath = originalPath;
        await _documentRepo.UpdateAsync(doc);

        string copyA = Path.Combine(tempDir, "copyA.md");
        string copyB = Path.Combine(tempDir, "copyB.md");
        string copyC = Path.Combine(tempDir, "copyC.md");
        File.WriteAllText(copyA, identicalContent);
        File.WriteAllText(copyB, identicalContent);
        File.WriteAllText(copyC, identicalContent);

        try
        {
            var candidateList = await _correlationService.GetRebindCandidatesAsync(doc.Id, tempDir);

            Assert.NotNull(candidateList);
            Assert.True(candidateList.IsAmbiguous, "Multiple exact match candidates must mark search ambiguous.");
            Assert.Equal(3, candidateList.Candidates.Count);

            // Attempting auto-rebind (forceRebind = false) MUST throw InvalidOperationException
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _correlationService.RebindDocumentAsync(doc.Id, copyB, forceRebind: false);
            });
            Assert.Contains("Rebind refused", ex.Message);
            Assert.Contains("ambiguous", ex.Message);

            // Manual confirmation with forceRebind = true MUST succeed
            var rebindResult = await _correlationService.RebindDocumentAsync(doc.Id, copyB, forceRebind: true);
            Assert.True(rebindResult.Success);
            Assert.Equal(Path.GetFullPath(copyB), rebindResult.NewPath);
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
    public async Task DuplicateContents_OriginalSourceStillExists_FlagsAmbiguous()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_source_still_there_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalPath = Path.Combine(tempDir, "active_original.md");
        string content = "# Active Original Content";
        File.WriteAllText(originalPath, content);

        var doc = await _identityService.ResolveDocumentAsync(originalPath);
        Assert.NotNull(doc);

        // Keep active_original.md on disk, but create duplicate_file.md elsewhere
        string duplicatePath = Path.Combine(tempDir, "duplicate_file.md");
        File.WriteAllText(duplicatePath, content);

        try
        {
            var candidateList = await _correlationService.GetRebindCandidatesAsync(doc.Id, tempDir);

            Assert.NotNull(candidateList);
            Assert.True(candidateList.IsAmbiguous, "Presence of original source file must trigger ambiguity flag.");

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _correlationService.RebindDocumentAsync(doc.Id, duplicatePath, forceRebind: false);
            });
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    #endregion

    #region 4. Simultaneous Rename/Move Events (Concurrency & Race Conditions)

    [Fact]
    public async Task SimultaneousMoveIntents_ConcurrentOperations_ThreadSafeAndNoCorruptedState()
    {
        int concurrentCount = 50;
        var tasks = new List<Task>();

        for (int i = 0; i < concurrentCount; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                string docId = $"doc-concurrent-{index}";
                string src = $@"C:\temp\source_{index}.md";
                string dst = $@"C:\temp\dest_{index}.md";

                _correlationService.RecordMoveIntent(docId, src, dst, TimeSpan.FromSeconds(30));

                var foundSrc = _correlationService.FindActiveIntentBySource(src);
                Assert.NotNull(foundSrc);
                Assert.Equal(docId, foundSrc.DocumentId);

                var foundDst = _correlationService.FindActiveIntentByDestination(dst);
                Assert.NotNull(foundDst);
                Assert.Equal(docId, foundDst.DocumentId);

                _correlationService.CompleteIntent(foundSrc.IntentId);

                Assert.Null(_correlationService.FindActiveIntentBySource(src));
                Assert.Null(_correlationService.FindActiveIntentByDestination(dst));
            }));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task SimultaneousRebindRequests_ConcurrentExecution_DoesNotCorruptDatabase()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_concur_rebind_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        int count = 10;
        var docIds = new List<string>();
        var targetPaths = new List<string>();

        for (int i = 0; i < count; i++)
        {
            string file = Path.Combine(tempDir, $"concur_file_{i}.md");
            File.WriteAllText(file, $"# Concur Content {i}");
            var doc = await _identityService.ResolveDocumentAsync(file);
            Assert.NotNull(doc);
            docIds.Add(doc.Id);

            string movedFile = Path.Combine(tempDir, $"concur_moved_{i}.md");
            File.WriteAllText(movedFile, $"# Concur Content {i}");
            targetPaths.Add(movedFile);
        }

        var rebindTasks = new List<Task<RebindResultDto>>();
        for (int i = 0; i < count; i++)
        {
            int idx = i;
            rebindTasks.Add(Task.Run(async () =>
            {
                return await _correlationService.RebindDocumentAsync(docIds[idx], targetPaths[idx], forceRebind: true);
            }));
        }

        var results = await Task.WhenAll(rebindTasks);

        Assert.All(results, r => Assert.True(r.Success));

        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region 5. Typed Exceptions & Input Validation

    [Fact]
    public void RecordMoveIntent_NullOrWhitespaceInputs_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _correlationService.RecordMoveIntent("", @"C:\src.md", @"C:\dst.md"));
        Assert.Throws<ArgumentException>(() => _correlationService.RecordMoveIntent("doc-1", "", @"C:\dst.md"));
        Assert.Throws<ArgumentException>(() => _correlationService.RecordMoveIntent("doc-1", @"C:\src.md", ""));
    }

    [Fact]
    public async Task RebindDocumentAsync_NullOrWhitespaceInputs_ReturnsTypedErrorResult()
    {
        var res1 = await _correlationService.RebindDocumentAsync("", @"C:\dst.md");
        Assert.False(res1.Success);
        Assert.Equal("error", res1.AvailabilityStatus);

        var res2 = await _correlationService.RebindDocumentAsync("doc-1", "");
        Assert.False(res2.Success);
        Assert.Equal("error", res2.AvailabilityStatus);
    }

    #endregion
}
