using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class MoveCorrelationIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly FolderSourceRepository _folderRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly MoveCorrelationService _correlationService;

    public MoveCorrelationIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs05_test_{Guid.NewGuid():N}.db");
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
    public async Task SameVolumeMove_PreservesDocumentId()
    {
        // 1. Arrange: Create initial physical file and resolve identity
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_samevol_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string originalPath = Path.Combine(tempDir, "original_file.md");
        File.WriteAllText(originalPath, "# Same Volume Move Test Content");

        try
        {
            var initialDoc = await _identityService.ResolveDocumentAsync(originalPath);
            Assert.NotNull(initialDoc);
            string expectedDocumentId = initialDoc.Id;

            // Physical same-volume move/rename
            string targetPath = Path.Combine(tempDir, "renamed_file.md");
            File.Move(originalPath, targetPath);

            // 2. Act: Correlate file change via MoveCorrelationService
            var correlatedDoc = await _correlationService.CorrelateSameVolumeMoveAsync(targetPath);

            // 3. Assert: DocumentId remains identical, path history updated
            Assert.NotNull(correlatedDoc);
            Assert.Equal(expectedDocumentId, correlatedDoc.Id);
            Assert.Equal(Path.GetFullPath(targetPath), correlatedDoc.CurrentPath);
            Assert.Equal(Path.GetFullPath(originalPath), correlatedDoc.LastKnownPath);
            Assert.Equal("available", correlatedDoc.AvailabilityStatus);
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
    public async Task TwoIdenticalCandidateCopies_RequireConfirmation()
    {
        // 1. Arrange: Create original document record in DB marked missing
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_ambig_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalPath = Path.Combine(tempDir, "missing_original.md");
        string content = "# Identical Content Copy Test";
        File.WriteAllText(originalPath, content);

        var originalDoc = await _identityService.ResolveDocumentAsync(originalPath);
        Assert.NotNull(originalDoc);

        // Delete physical original and mark missing in DB
        File.Delete(originalPath);
        originalDoc.AvailabilityStatus = "missing";
        originalDoc.LastKnownPath = originalPath;
        await _documentRepo.UpdateAsync(originalDoc);

        // Create two new separate physical files with identical content
        string copy1Path = Path.Combine(tempDir, "copy1.md");
        string copy2Path = Path.Combine(tempDir, "copy2.md");
        File.WriteAllText(copy1Path, content);
        File.WriteAllText(copy2Path, content);

        try
        {
            // 2. Act: Get rebind candidates for missing document
            var result = await _correlationService.GetRebindCandidatesAsync(originalDoc.Id, tempDir);

            // 3. Assert: System identifies ambiguity and returns 2 candidates
            Assert.NotNull(result);
            Assert.True(result.IsAmbiguous);
            Assert.True(result.Candidates.Count >= 2);

            // Verify auto-rebind without force flag throws exception or fails
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _correlationService.RebindDocumentAsync(originalDoc.Id, copy1Path, forceRebind: false);
            });

            // Verify explicit rebind WITH force flag succeeds
            var rebindResult = await _correlationService.RebindDocumentAsync(originalDoc.Id, copy1Path, forceRebind: true);
            Assert.True(rebindResult.Success);
            Assert.Equal(copy1Path, rebindResult.NewPath);
            Assert.Equal("available", rebindResult.AvailabilityStatus);
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
    public async Task AppOriginatedMove_CorrelatesWatcherDeleteCreateSequence()
    {
        // 1. Arrange: Create original document
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_journal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePath = Path.Combine(tempDir, "journal_source.md");
        string destPath = Path.Combine(tempDir, "journal_dest.md");

        File.WriteAllText(sourcePath, "# Journal Intent Move Content");
        var doc = await _identityService.ResolveDocumentAsync(sourcePath);
        Assert.NotNull(doc);

        var mockWatcher = new MockSourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            mockWatcher,
            _identityService,
            _documentRepo,
            jobScheduler: null,
            debounceDelay: TimeSpan.FromMilliseconds(50),
            moveCorrelationService: _correlationService);

        var emittedEvents = new List<FileChangeEvent>();
        coordinator.OnFileChanged += (sender, e) =>
        {
            lock (emittedEvents)
            {
                emittedEvents.Add(e);
            }
        };

        var source = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = Guid.NewGuid().ToString(),
            CanonicalPath = tempDir
        };
        coordinator.StartWatching(source);

        // 2. Act: Register intent, perform physical move, raise raw watcher Deleted + Created events
        _correlationService.RecordMoveIntent(doc.Id, sourcePath, destPath, TimeSpan.FromSeconds(10));
        File.Move(sourcePath, destPath);

        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Deleted, sourcePath, null, source.Id);
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Created, destPath, null, source.Id);

        // Wait for the debounced logical event with a generous timeout: a fixed sleep
        // races the 50 ms debounce timer under parallel test load (same class of flake
        // as SourceWatcherTests), so poll for the event and then let the debounce
        // window settle before asserting that the delete+create pair correlated into
        // exactly one rename.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (emittedEvents)
            {
                if (emittedEvents.Count > 0) break;
            }
            await Task.Delay(25);
        }
        await Task.Delay(200);

        try
        {
            // 3. Assert: Single Renamed event emitted, preserving DocumentId
            lock (emittedEvents)
            {
                Assert.Single(emittedEvents);
                Assert.Equal(FileChangeType.Renamed, emittedEvents[0].ChangeType);
                Assert.Equal(doc.Id, emittedEvents[0].DocumentId);
                Assert.Equal(Path.GetFullPath(destPath), emittedEvents[0].Path);
            }

            var updatedDoc = await _documentRepo.GetByIdAsync(doc.Id);
            Assert.NotNull(updatedDoc);
            Assert.Equal(Path.GetFullPath(destPath), updatedDoc.CurrentPath);
            Assert.Equal("available", updatedDoc.AvailabilityStatus);
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
    public async Task SourceFileStillExists_FlagsAmbiguous_And_RefusesAutoRebind()
    {
        // 1. Arrange: Create original document record, but KEEP original physical file on disk
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_srcexist_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalPath = Path.Combine(tempDir, "source_existing.md");
        string content = "# Content for Copy vs Move Test";
        File.WriteAllText(originalPath, content);

        var originalDoc = await _identityService.ResolveDocumentAsync(originalPath);
        Assert.NotNull(originalDoc);

        // Create a copy of the file elsewhere, source file still exists!
        string copiedPath = Path.Combine(tempDir, "source_copied.md");
        File.WriteAllText(copiedPath, content);

        try
        {
            // 2. Act: Query candidates for original document
            var result = await _correlationService.GetRebindCandidatesAsync(originalDoc.Id, tempDir);

            // 3. Assert: Source file existing forces overallIsAmbiguous == true
            Assert.NotNull(result);
            Assert.True(result.IsAmbiguous);
            Assert.NotEmpty(result.Candidates);
            Assert.True(result.Candidates[0].IsAmbiguous);

            // Auto-rebind without forceRebind should be rejected
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _correlationService.RebindDocumentAsync(originalDoc.Id, copiedPath, forceRebind: false);
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
    public async Task ExpiredMoveIntent_DoesNotCorrelateWatcherEvents()
    {
        // 1. Arrange: Register an intent with a very short negative/zero TTL or expired timestamp
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs05_expired_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string sourcePath = Path.Combine(tempDir, "expired_source.md");
        string destPath = Path.Combine(tempDir, "expired_dest.md");

        File.WriteAllText(sourcePath, "# Expired Intent Content");
        var doc = await _identityService.ResolveDocumentAsync(sourcePath);
        Assert.NotNull(doc);

        // Register intent with negative TTL so it is immediately expired
        _correlationService.RecordMoveIntent(doc.Id, sourcePath, destPath, TimeSpan.FromMilliseconds(-100));

        // Act & Assert: Intent lookup by source or destination returns null
        Assert.Null(_correlationService.FindActiveIntentBySource(sourcePath));
        Assert.Null(_correlationService.FindActiveIntentByDestination(destPath));

        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private class MockSourceWatcher : ISourceWatcher

    {
        private readonly HashSet<string> _watchedFolderIds = new();
        public event EventHandler<RawWatcherEventArgs>? OnRawEvent;
        public event EventHandler<string>? OnWatcherOverflow;

        public void StartWatching(FolderSource source) => _watchedFolderIds.Add(source.Id);
        public void StopWatching(string folderSourceId) => _watchedFolderIds.Remove(folderSourceId);
        public bool IsWatching(string folderSourceId) => _watchedFolderIds.Contains(folderSourceId);

        public void RaiseRawEvent(WatcherChangeTypes changeType, string fullPath, string? oldFullPath, string folderSourceId)
        {
            OnRawEvent?.Invoke(this, new RawWatcherEventArgs(changeType, fullPath, oldFullPath, folderSourceId));
        }

        public void RaiseOverflow(string folderSourceId) => OnWatcherOverflow?.Invoke(this, folderSourceId);
        public void Dispose() => _watchedFolderIds.Clear();
    }
}
