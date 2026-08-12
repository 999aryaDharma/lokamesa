using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Jobs;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Jobs;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

public class SourceWatcherTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly DocumentRepository _documentRepo;
    private readonly FolderSourceRepository _folderRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly LocalJobScheduler _jobScheduler;

    public SourceWatcherTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs03_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _documentRepo = new DocumentRepository(_appDb);
        _folderRepo = new FolderSourceRepository(_appDb);
        _identityProvider = new WindowsFileIdentityProvider();
        _identityService = new DocumentIdentityService(_documentRepo, _identityProvider);
        _jobScheduler = new LocalJobScheduler(maxDegreeOfParallelism: 2, queueCapacity: 100);
    }

    public void Dispose()
    {
        _jobScheduler.Dispose();
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
    public async Task One_Word_Like_Burst_Becomes_One_Logical_Modification()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs03_wordburst_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string docPath = Path.Combine(tempDir, "document.docx");
        File.WriteAllText(docPath, "Initial Content");

        var doc = await _identityService.ResolveDocumentAsync(docPath);
        Assert.NotNull(doc);

        var mockWatcher = new MockSourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            mockWatcher,
            _identityService,
            _documentRepo,
            _jobScheduler,
            debounceDelay: TimeSpan.FromMilliseconds(50));

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
            CanonicalPath = tempDir,
            DisplayName = "Test Folder"
        };
        coordinator.StartWatching(source);

        // Simulate Word save burst events in rapid succession (within 50ms)
        string tempLockPath = Path.Combine(tempDir, "~$document.docx");
        string tempSavePath = Path.Combine(tempDir, "~WRD0001.tmp");

        // 1. Word creates lock file
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Created, tempLockPath, null, source.Id);
        // 2. Word creates temp save file
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Created, tempSavePath, null, source.Id);
        // 3. Word writes to temp file
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Changed, tempSavePath, null, source.Id);
        // 4. Update docPath physical content (simulating atomic replace by Word)
        File.WriteAllText(docPath, "Updated Content After Word Save");
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Changed, docPath, null, source.Id);
        // 5. Word deletes temp save file
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Deleted, tempSavePath, null, source.Id);
        // 6. Word deletes lock file
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Deleted, tempLockPath, null, source.Id);
        // 7. Final change event on main file
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Changed, docPath, null, source.Id);

        // Wait for debounce window (50ms + margin)
        await Task.Delay(250);

        try
        {
            // Assert: Exactly ONE logical modification event emitted for docPath
            lock (emittedEvents)
            {
                Assert.Single(emittedEvents);
                Assert.Equal(FileChangeType.Modified, emittedEvents[0].ChangeType);
                Assert.Equal(Path.GetFullPath(docPath), emittedEvents[0].Path);
            }

            // Verify document in repo is updated with new content fingerprint
            var updatedDoc = await _documentRepo.GetByIdAsync(doc.Id);
            Assert.NotNull(updatedDoc);
            Assert.Equal("available", updatedDoc.AvailabilityStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Duplicate_Events_Do_Not_Duplicate_Document_Records()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs03_dup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string docPath = Path.Combine(tempDir, "dup_test.md");
        File.WriteAllText(docPath, "Static Content");

        var doc = await _identityService.ResolveDocumentAsync(docPath);
        Assert.NotNull(doc);

        var mockWatcher = new MockSourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            mockWatcher,
            _identityService,
            _documentRepo,
            _jobScheduler,
            debounceDelay: TimeSpan.FromMilliseconds(50));

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

        // Fire 5 duplicate Changed events without altering physical content
        for (int i = 0; i < 5; i++)
        {
            mockWatcher.RaiseRawEvent(WatcherChangeTypes.Changed, docPath, null, source.Id);
        }

        await Task.Delay(250);

        try
        {
            // Assert: No new logical events fired because fingerprint & metadata didn't change
            lock (emittedEvents)
            {
                Assert.Empty(emittedEvents);
            }

            // Verify DB still has exactly 1 record
            var fetched = await _documentRepo.GetByPathAsync(Path.GetFullPath(docPath));
            Assert.NotNull(fetched);
            Assert.Equal(doc.Id, fetched.Id);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Overflow_Schedules_Source_Reconciliation()
    {
        // Arrange
        var mockWatcher = new MockSourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            mockWatcher,
            _identityService,
            _documentRepo,
            _jobScheduler,
            debounceDelay: TimeSpan.FromMilliseconds(50));

        string folderSourceId = Guid.NewGuid().ToString();
        string? scheduledSourceId = null;
        var tcs = new TaskCompletionSource<bool>();

        var capturedJobs = new List<JobInfo>();
        _jobScheduler.OnJobProgressChanged += (sender, info) =>
        {
            lock (capturedJobs)
            {
                capturedJobs.Add(info);
            }
        };

        coordinator.OnReconciliationScheduled += (sender, id) =>
        {
            scheduledSourceId = id;
            tcs.TrySetResult(true);
        };

        // Act: Trigger buffer overflow on watcher
        mockWatcher.RaiseOverflow(folderSourceId);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Equal(tcs.Task, completed);
        Assert.Equal(folderSourceId, scheduledSourceId);

        // Verify job in LocalJobScheduler
        lock (capturedJobs)
        {
            Assert.Contains(capturedJobs, j => j.JobType == JobTypes.Reconciliation && j.DocumentId == folderSourceId);
        }
    }

    [Fact]
    public async Task Physical_SourceWatcher_Detects_Real_File_Creation()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs03_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        using var realWatcher = new SourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            realWatcher,
            _identityService,
            _documentRepo,
            _jobScheduler,
            debounceDelay: TimeSpan.FromMilliseconds(100));

        var emittedEvents = new List<FileChangeEvent>();
        var eventReceivedTcs = new TaskCompletionSource<FileChangeEvent>();

        coordinator.OnFileChanged += (sender, e) =>
        {
            lock (emittedEvents)
            {
                emittedEvents.Add(e);
            }
            eventReceivedTcs.TrySetResult(e);
        };

        var source = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = Guid.NewGuid().ToString(),
            CanonicalPath = tempDir,
            DisplayName = "Real Temp Dir"
        };

        coordinator.StartWatching(source);

        try
        {
            // Act: Physically create a document on disk
            string createdPath = Path.Combine(tempDir, "real_document.md");
            File.WriteAllText(createdPath, "# Physical File Test");

            // Wait for watcher and coordinator to process
            var completedTask = await Task.WhenAny(eventReceivedTcs.Task, Task.Delay(3000));
            Assert.Equal(eventReceivedTcs.Task, completedTask);

            var eventPayload = await eventReceivedTcs.Task;
            Assert.Equal(FileChangeType.Created, eventPayload.ChangeType);
            Assert.Equal(Path.GetFullPath(createdPath), eventPayload.Path);
            Assert.NotNull(eventPayload.DocumentId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Deleted_File_Publishes_Deleted_Event_And_Marks_Missing()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs03_del_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string docPath = Path.Combine(tempDir, "to_delete.md");
        File.WriteAllText(docPath, "Content To Delete");

        var doc = await _identityService.ResolveDocumentAsync(docPath);
        Assert.NotNull(doc);
        Assert.Equal("available", doc.AvailabilityStatus);

        var mockWatcher = new MockSourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            mockWatcher,
            _identityService,
            _documentRepo,
            _jobScheduler,
            debounceDelay: TimeSpan.FromMilliseconds(50));

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

        // Delete physical file & raise raw Deleted event
        File.Delete(docPath);
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Deleted, docPath, null, source.Id);

        await Task.Delay(500);

        try
        {
            // Assert: Deleted event emitted and document marked missing in DB
            lock (emittedEvents)
            {
                Assert.Single(emittedEvents);
                Assert.Equal(FileChangeType.Deleted, emittedEvents[0].ChangeType);
                Assert.Equal(doc.Id, emittedEvents[0].DocumentId);
            }

            var updatedDoc = await _documentRepo.GetByIdAsync(doc.Id);
            Assert.NotNull(updatedDoc);
            Assert.Equal("missing", updatedDoc.AvailabilityStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Ignored_Files_Do_Not_Publish_Events()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), $"fs03_ignore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var mockWatcher = new MockSourceWatcher();
        using var coordinator = new FileChangeCoordinator(
            mockWatcher,
            _identityService,
            _documentRepo,
            _jobScheduler,
            debounceDelay: TimeSpan.FromMilliseconds(50));

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

        // Raise raw events for ignored file types (.txt, ~$lock.docx, ~WRD.tmp)
        string lockPath = Path.Combine(tempDir, "~$lock.docx");
        string txtPath = Path.Combine(tempDir, "notes.txt");
        string tmpPath = Path.Combine(tempDir, "~temp.tmp");

        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Created, lockPath, null, source.Id);
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Created, txtPath, null, source.Id);
        mockWatcher.RaiseRawEvent(WatcherChangeTypes.Created, tmpPath, null, source.Id);

        await Task.Delay(250);

        try
        {
            // Assert: No logical events emitted for ignored files
            lock (emittedEvents)
            {
                Assert.Empty(emittedEvents);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private class MockSourceWatcher : ISourceWatcher
    {
        private readonly HashSet<string> _watchedFolderIds = new();

        public event EventHandler<RawWatcherEventArgs>? OnRawEvent;
        public event EventHandler<string>? OnWatcherOverflow;

        public void StartWatching(FolderSource source)
        {
            _watchedFolderIds.Add(source.Id);
        }

        public void StopWatching(string folderSourceId)
        {
            _watchedFolderIds.Remove(folderSourceId);
        }

        public bool IsWatching(string folderSourceId)
        {
            return _watchedFolderIds.Contains(folderSourceId);
        }

        public void RaiseRawEvent(WatcherChangeTypes changeType, string fullPath, string? oldFullPath, string folderSourceId)
        {
            OnRawEvent?.Invoke(this, new RawWatcherEventArgs(changeType, fullPath, oldFullPath, folderSourceId));
        }

        public void RaiseOverflow(string folderSourceId)
        {
            OnWatcherOverflow?.Invoke(this, folderSourceId);
        }

        public void Dispose()
        {
            _watchedFolderIds.Clear();
        }
    }
}
