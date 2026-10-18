using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class MoveCorrelationAdversarialChallengerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly FolderSourceRepository _folderRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly MoveCorrelationService _correlationService;

    public MoveCorrelationAdversarialChallengerTests()
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

    [Fact]
    public async Task NonMutationGuardrail_CorrelationAndRebind_NeverMutateDiskFiles()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_nonmut_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string filePath = Path.Combine(tempDir, "sample_document.md");
        string originalContent = "# Non-Mutation Guardrail Test Content\n\nOriginal file data.";
        File.WriteAllText(filePath, originalContent);

        var fileInfoBefore = new FileInfo(filePath);
        DateTime lastWriteBefore = fileInfoBefore.LastWriteTimeUtc;
        long sizeBefore = fileInfoBefore.Length;
        string hashBefore = ComputeSha256(filePath);

        try
        {
            // Act 1: Resolve document
            var doc = await _identityService.ResolveDocumentAsync(filePath);
            Assert.NotNull(doc);

            // Act 2: Move file and correlate
            string targetPath = Path.Combine(tempDir, "sample_document_moved.md");
            File.Move(filePath, targetPath);

            var correlated = await _correlationService.CorrelateSameVolumeMoveAsync(targetPath);
            Assert.NotNull(correlated);

            // Act 3: Query candidates
            var candidates = await _correlationService.GetRebindCandidatesAsync(doc.Id, tempDir);
            Assert.NotNull(candidates);

            // Act 4: Perform explicit rebind
            var rebindResult = await _correlationService.RebindDocumentAsync(doc.Id, targetPath, forceRebind: true);
            Assert.True(rebindResult.Success);

            // Assert: Verify physical file on disk was NEVER modified in bytes, size, or SHA-256 hash
            var fileInfoAfter = new FileInfo(targetPath);
            Assert.Equal(sizeBefore, fileInfoAfter.Length);
            Assert.Equal(originalContent, File.ReadAllText(targetPath));
            Assert.Equal(hashBefore, ComputeSha256(targetPath));
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
    public async Task AmbiguityGuardrail_IdenticalContentMultipleCandidates_RefusesAutoRebindWithoutForce()
    {
        // Arrange: Create missing document record and 3 identical physical candidate files
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_ambig_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string missingPath = Path.Combine(tempDir, "deleted_source.md");
        string sharedContent = "Identical text content across multiple candidate files.";
        File.WriteAllText(missingPath, sharedContent);

        var doc = await _identityService.ResolveDocumentAsync(missingPath);
        Assert.NotNull(doc);

        File.Delete(missingPath);
        doc.AvailabilityStatus = "missing";
        doc.LastKnownPath = missingPath;
        await _documentRepo.UpdateAsync(doc);

        string cand1 = Path.Combine(tempDir, "candidate_a.md");
        string cand2 = Path.Combine(tempDir, "candidate_b.md");
        string cand3 = Path.Combine(tempDir, "candidate_c.md");
        File.WriteAllText(cand1, sharedContent);
        File.WriteAllText(cand2, sharedContent);
        File.WriteAllText(cand3, sharedContent);

        try
        {
            // Act: Query candidates
            var candidateList = await _correlationService.GetRebindCandidatesAsync(doc.Id, tempDir);

            // Assert: IsAmbiguous is true, candidates count == 3
            Assert.True(candidateList.IsAmbiguous);
            Assert.Equal(3, candidateList.Candidates.Count);

            // Auto-rebind attempt MUST fail
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _correlationService.RebindDocumentAsync(doc.Id, cand1, forceRebind: false);
            });

            Assert.Contains("Rebind refused", ex.Message);

            // Document in DB must still be missing and pointing to original last known path
            var docInDb = await _documentRepo.GetByIdAsync(doc.Id);
            Assert.NotNull(docInDb);
            Assert.Equal("missing", docInDb.AvailabilityStatus);
            Assert.Equal(missingPath, docInDb.LastKnownPath);
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
    public async Task AmbiguityGuardrail_SourceFileStillExists_TreatsAsCopyAndRefusesAutoRebind()
    {
        // Arrange: Original document physical file stays on disk
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_source_copy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePath = Path.Combine(tempDir, "original_source.md");
        string content = "Source file content that gets copied elsewhere.";
        File.WriteAllText(sourcePath, content);

        var doc = await _identityService.ResolveDocumentAsync(sourcePath);
        Assert.NotNull(doc);

        // Copy file (source remains!)
        string copyPath = Path.Combine(tempDir, "copied_destination.md");
        File.WriteAllBytes(copyPath, File.ReadAllBytes(sourcePath));

        try
        {
            // Act
            var candidateList = await _correlationService.GetRebindCandidatesAsync(doc.Id, tempDir);

            // Assert: Source file existing forces overallIsAmbiguous == true
            Assert.True(candidateList.IsAmbiguous);
            Assert.True(candidateList.Candidates.Count > 0);
            Assert.True(candidateList.Candidates[0].IsAmbiguous);

            // Attempt auto-rebind to copyPath without forceRebind MUST throw
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _correlationService.RebindDocumentAsync(doc.Id, copyPath, forceRebind: false);
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

    [Fact]
    public void CandidateScorer_LowScore_IsMarkedAmbiguous()
    {
        var doc = new Document
        {
            Id = "doc-low",
            Filename = "notes.md",
            SizeBytes = 5000,
            ContentFingerprint = "1111222233334444",
            ModifiedAt = DateTime.UtcNow.ToString("O")
        };

        // Score a candidate that only has stem match (+10 pts) and approximate modified time (+5 pts) => Total 15 pts < 75 pts
        var score = MoveCorrelationScorer.ScoreCandidate(
            doc,
            candidateFilename: "notes.docx",
            candidateSizeBytes: 10000, // size differs greatly
            candidateFingerprint: "9999888877776666", // hash differs
            candidateModifiedAt: DateTime.UtcNow.AddSeconds(-30));

        Assert.True(score.IsAmbiguous);
        Assert.True(score.MatchScore < 50);
        Assert.Equal(CandidateMatchConfidence.Ambiguous, score.Confidence);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
