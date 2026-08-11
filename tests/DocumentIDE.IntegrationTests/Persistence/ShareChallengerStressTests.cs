using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Persistence;

public class ShareChallengerStressTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly ShareRepository _shareRepo;
    private readonly DocumentRepository _docRepo;
    private readonly ShareTokenService _tokenService;

    public ShareChallengerStressTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_share_stress_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _shareRepo = new ShareRepository(_appDb);
        _docRepo = new DocumentRepository(_appDb);
        _tokenService = new ShareTokenService();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
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
    public async Task Schema_Integrity_AllTablesAndIndexesExist()
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('shares', 'share_members', 'share_snapshot_artifacts');";
        var tableCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(3, tableCount);

        using var idxCmd = conn.CreateCommand();
        idxCmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='index' AND name IN ('idx_shares_token_hash', 'idx_share_members_share_id', 'idx_share_members_document_id', 'idx_share_snapshot_artifacts_share_id');";
        var indexCount = Convert.ToInt32(await idxCmd.ExecuteScalarAsync());
        Assert.Equal(4, indexCount);
    }

    [Fact]
    public async Task Foreign_Key_Constraint_ShareMember_NonExistent_Document_Fails()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            TokenHash = "validhash123"
        };
        await _shareRepo.CreateAsync(share);

        var member = new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = "non-existent-doc-id",
            DisplayPath = "test.md",
            AddedAt = DateTime.UtcNow.ToString("O")
        };

        await Assert.ThrowsAsync<SqliteException>(async () => await _shareRepo.AddMemberAsync(member));
    }

    [Fact]
    public async Task Cascade_Delete_Document_Removes_Share_Members()
    {
        var doc = new Document { Id = Guid.NewGuid().ToString(), CurrentPath = @"D:\doc.md", Filename = "doc.md" };
        await _docRepo.CreateAsync(doc);

        var share = new Share { Id = Guid.NewGuid().ToString(), TokenHash = "hash-cascade-doc" };
        var member = new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = doc.Id,
            DisplayPath = "doc.md"
        };
        share.Members.Add(member);
        await _shareRepo.CreateAsync(share);

        var membersBefore = await _shareRepo.GetMembersAsync(share.Id);
        Assert.Single(membersBefore);

        // Delete document from documents table
        await _docRepo.DeleteAsync(doc.Id);

        var membersAfter = await _shareRepo.GetMembersAsync(share.Id);
        Assert.Empty(membersAfter);
    }

    [Fact]
    public async Task Concurrent_Share_Creation_And_Queries_StressTest()
    {
        const int concurrentWorkers = 25;
        const int itemsPerWorker = 4;

        // Pre-create document for share members
        var doc = new Document { Id = Guid.NewGuid().ToString(), CurrentPath = @"D:\concurrent.md", Filename = "concurrent.md" };
        await _docRepo.CreateAsync(doc);

        var tasks = Enumerable.Range(0, concurrentWorkers).Select(async workerId =>
        {
            for (int i = 0; i < itemsPerWorker; i++)
            {
                var token = _tokenService.GenerateToken();
                var tokenHash = _tokenService.ComputeHash(token);
                var shareId = $"share-{workerId}-{i}-{Guid.NewGuid():N}";

                var share = new Share
                {
                    Id = shareId,
                    Mode = i % 2 == 0 ? ShareMode.Snapshot : ShareMode.Live,
                    AccessMode = AccessMode.SecretToken,
                    TokenHash = tokenHash,
                    AllowOriginalDownload = true,
                    AllowPdfDownload = false,
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                    UpdatedAt = DateTime.UtcNow.ToString("O")
                };

                share.Members.Add(new ShareMember
                {
                    Id = Guid.NewGuid().ToString(),
                    ShareId = shareId,
                    DocumentId = doc.Id,
                    DisplayPath = $"concurrent_{workerId}_{i}.md",
                    AddedAt = DateTime.UtcNow.ToString("O")
                });

                share.SnapshotArtifacts.Add(new ShareSnapshotArtifact
                {
                    Id = Guid.NewGuid().ToString(),
                    ShareId = shareId,
                    DocumentId = doc.Id,
                    ArtifactType = "html",
                    RelativePath = $"artifacts/{workerId}_{i}.html",
                    ContentHash = "hash_" + workerId + "_" + i,
                    SizeBytes = 1024,
                    CreatedAt = DateTime.UtcNow.ToString("O")
                });

                await _shareRepo.CreateAsync(share);

                // Immediate concurrent read checks
                var fetchedById = await _shareRepo.GetByIdAsync(shareId);
                Assert.NotNull(fetchedById);
                Assert.Equal(shareId, fetchedById!.Id);
                Assert.Single(fetchedById.Members);
                Assert.Single(fetchedById.SnapshotArtifacts);

                var fetchedByHash = await _shareRepo.GetByTokenHashAsync(tokenHash);
                Assert.NotNull(fetchedByHash);
                Assert.Equal(shareId, fetchedByHash!.Id);
            }
        });

        await Task.WhenAll(tasks);

        var allShares = await _shareRepo.GetAllAsync();
        Assert.Equal(concurrentWorkers * itemsPerWorker, allShares.Count);
    }

    [Fact]
    public async Task Bulk_Snapshot_Artifact_Storage_And_Retrieval()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            TokenHash = "bulk-artifact-token-hash"
        };
        await _shareRepo.CreateAsync(share);

        const int artifactCount = 50;
        for (int i = 0; i < artifactCount; i++)
        {
            var artifact = new ShareSnapshotArtifact
            {
                Id = Guid.NewGuid().ToString(),
                ShareId = share.Id,
                DocumentId = $"doc-{i}",
                ArtifactType = i % 2 == 0 ? "html" : "pdf",
                RelativePath = $"subfolder_{i}/artifact_{i}.dat",
                ContentHash = $"sha256_hash_value_{i:D4}",
                SizeBytes = i * 1000000L,
                CreatedAt = DateTime.UtcNow.ToString("O")
            };
            await _shareRepo.AddSnapshotArtifactAsync(artifact);
        }

        var artifacts = await _shareRepo.GetSnapshotArtifactsAsync(share.Id);
        Assert.Equal(artifactCount, artifacts.Count);
        Assert.Contains(artifacts, a => a.SizeBytes == 49000000L);
    }

    [Fact]
    public async Task Transaction_Rollback_On_CreateAsync_Failure()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            TokenHash = "rollback-test-hash"
        };

        // Add a member with non-existent documentId to trigger FK violation during CreateAsync transaction
        share.Members.Add(new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = "invalid-fk-doc-id",
            DisplayPath = "bad.md"
        });

        await Assert.ThrowsAsync<SqliteException>(async () => await _shareRepo.CreateAsync(share));

        // Verify share was NOT inserted due to transaction rollback
        var fetched = await _shareRepo.GetByIdAsync(share.Id);
        Assert.Null(fetched);
    }
}
