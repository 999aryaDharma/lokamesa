using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Persistence;

public class ShareRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly ShareRepository _shareRepo;
    private readonly DocumentRepository _docRepo;

    public ShareRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_share_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _shareRepo = new ShareRepository(_appDb);
        _docRepo = new DocumentRepository(_appDb);
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
    public async Task CreateAndGetShare_PersistsShareAndMembersAndArtifacts()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\test.md",
            Filename = "test.md",
            Extension = ".md"
        };
        await _docRepo.CreateAsync(doc);

        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken,
            TokenHash = "sha256tokenhash123",
            AllowOriginalDownload = true,
            AllowPdfDownload = false,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };

        var member = new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = doc.Id,
            DisplayPath = "test.md",
            AddedAt = DateTime.UtcNow.ToString("O")
        };
        share.Members.Add(member);

        var artifact = new ShareSnapshotArtifact
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = doc.Id,
            ArtifactType = "html",
            RelativePath = "test.html",
            ContentHash = "hash12345",
            SizeBytes = 2048,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        share.SnapshotArtifacts.Add(artifact);

        await _shareRepo.CreateAsync(share);

        var fetched = await _shareRepo.GetByIdAsync(share.Id);
        Assert.NotNull(fetched);
        Assert.Equal(share.Id, fetched!.Id);
        Assert.Equal(ShareMode.Snapshot, fetched.Mode);
        Assert.Equal(AccessMode.SecretToken, fetched.AccessMode);
        Assert.Equal("sha256tokenhash123", fetched.TokenHash);
        Assert.True(fetched.AllowOriginalDownload);
        Assert.False(fetched.AllowPdfDownload);

        Assert.Single(fetched.Members);
        Assert.Equal(doc.Id, fetched.Members[0].DocumentId);
        Assert.Equal("test.md", fetched.Members[0].DisplayPath);

        Assert.Single(fetched.SnapshotArtifacts);
        Assert.Equal("test.html", fetched.SnapshotArtifacts[0].RelativePath);
    }

    [Fact]
    public async Task GetByTokenHash_ReturnsCorrectShare()
    {
        var tokenHash = "tokenhash_unique_5678";
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Live,
            AccessMode = AccessMode.Public,
            TokenHash = tokenHash
        };

        await _shareRepo.CreateAsync(share);

        var fetched = await _shareRepo.GetByTokenHashAsync(tokenHash);
        Assert.NotNull(fetched);
        Assert.Equal(share.Id, fetched!.Id);
    }

    [Fact]
    public async Task AddMember_And_RemoveMember_UpdatesShareMembers()
    {
        var doc1 = new Document { Id = Guid.NewGuid().ToString(), CurrentPath = @"D:\d1.md", Filename = "d1.md" };
        var doc2 = new Document { Id = Guid.NewGuid().ToString(), CurrentPath = @"D:\d2.md", Filename = "d2.md" };
        await _docRepo.CreateAsync(doc1);
        await _docRepo.CreateAsync(doc2);

        var share = new Share { Id = Guid.NewGuid().ToString(), TokenHash = "hash999" };
        await _shareRepo.CreateAsync(share);

        var member1 = new ShareMember { Id = Guid.NewGuid().ToString(), ShareId = share.Id, DocumentId = doc1.Id, DisplayPath = "d1.md" };
        var member2 = new ShareMember { Id = Guid.NewGuid().ToString(), ShareId = share.Id, DocumentId = doc2.Id, DisplayPath = "d2.md" };

        await _shareRepo.AddMemberAsync(member1);
        await _shareRepo.AddMemberAsync(member2);

        var members = await _shareRepo.GetMembersAsync(share.Id);
        Assert.Equal(2, members.Count);

        await _shareRepo.RemoveMemberAsync(share.Id, doc1.Id);

        var membersAfterRemove = await _shareRepo.GetMembersAsync(share.Id);
        Assert.Single(membersAfterRemove);
        Assert.Equal(doc2.Id, membersAfterRemove[0].DocumentId);
    }

    [Fact]
    public async Task DeleteShare_CascadesToMembersAndArtifacts()
    {
        var doc = new Document { Id = Guid.NewGuid().ToString(), CurrentPath = @"D:\d.md", Filename = "d.md" };
        await _docRepo.CreateAsync(doc);

        var share = new Share { Id = Guid.NewGuid().ToString(), TokenHash = "hashtodelete" };
        var member = new ShareMember { Id = Guid.NewGuid().ToString(), ShareId = share.Id, DocumentId = doc.Id, DisplayPath = "d.md" };
        share.Members.Add(member);
        await _shareRepo.CreateAsync(share);

        var fetchedBefore = await _shareRepo.GetByIdAsync(share.Id);
        Assert.NotNull(fetchedBefore);

        await _shareRepo.DeleteAsync(share.Id);

        var fetchedAfter = await _shareRepo.GetByIdAsync(share.Id);
        Assert.Null(fetchedAfter);

        var membersAfter = await _shareRepo.GetMembersAsync(share.Id);
        Assert.Empty(membersAfter);
    }
}
