using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Jobs;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Jobs;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.SoakTests;

/// <summary>
/// QA-02 soak infrastructure. Everything is wired against a real temporary filesystem,
/// a real SQLite <see cref="AppDb"/>, and the real product components
/// (<see cref="SourceWatcher"/>, <see cref="FileChangeCoordinator"/>,
/// <see cref="DocumentIdentityService"/>, <see cref="IncrementalIndexCoordinator"/>,
/// <see cref="StartupReconciler"/>, <see cref="LocalJobScheduler"/>, FTS index).
/// The only fake is <see cref="SoakWatcherSeam"/>, a narrow fault-injection seam around
/// the real watcher for the two things Windows cannot be made to do deterministically:
/// dropping events and raising buffer-overflow.
/// </summary>
internal static class SoakTimeouts
{
    public static readonly TimeSpan Persist = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(250);
}

internal static class SoakSupport
{
    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Simulates a Word-like save: write a same-directory temporary file (its '.tmp'
    /// suffix makes the watcher pipeline ignore it) and atomically rename it over the
    /// target, exactly like Word's Ctrl+S and the app's own AtomicFileReplacer do.
    /// Retries on transient sharing violations — indexer reads do not open with
    /// FileShare.Delete, so a concurrent read can block a single replace attempt just
    /// like it can block Word; real editors retry the same way.
    /// </summary>
    public static void AtomicReplaceFromBytes(string targetPath, byte[] bytes)
    {
        string tempPath = targetPath + ".qa02.tmp";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            File.WriteAllBytes(tempPath, bytes);
            try
            {
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: false);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await condition()) return true;
            }
            catch (IOException)
            {
                // Transient sharing violation while the writer finishes; retry.
            }
            catch (SqliteException)
            {
                // Transient SQLite lock under concurrent writers; retry.
            }
            await Task.Delay(30);
        }

        try
        {
            return await condition();
        }
        catch
        {
            return false;
        }
    }
}

internal readonly record struct JobSnapshot(
    string JobId,
    string JobType,
    string DocumentId,
    JobState State,
    string? Fingerprint);

/// <summary>
/// Narrow fault-injection seam: forwards every real event from the real
/// <see cref="SourceWatcher"/> and can (a) drop forwarded raw events to simulate the
/// Windows watcher-buffer overflow where events are lost, while (b) still being able to
/// raise the overflow notification Windows raises in that situation.
/// </summary>
internal sealed class SoakWatcherSeam : ISourceWatcher
{
    private readonly ISourceWatcher _inner;
    private int _droppedCount;

    public SoakWatcherSeam(ISourceWatcher inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _inner.OnRawEvent += (sender, e) =>
        {
            if (Volatile.Read(ref _dropRawEvents) != 0)
            {
                Interlocked.Increment(ref _droppedCount);
                OnDroppedRawEvent?.Invoke(sender, e);
                return;
            }
            OnRawEvent?.Invoke(sender, e);
        };
        _inner.OnWatcherOverflow += (sender, sourceId) => OnWatcherOverflow?.Invoke(sender, sourceId);
    }

    public event EventHandler<RawWatcherEventArgs>? OnRawEvent;
    public event EventHandler<string>? OnWatcherOverflow;

    /// <summary>Diagnostic hook for events deliberately dropped by the fault seam.</summary>
    public event EventHandler<RawWatcherEventArgs>? OnDroppedRawEvent;

    private int _dropRawEvents;

    public int DroppedEventCount => Volatile.Read(ref _droppedCount);

    public bool DropRawEvents
    {
        get => Volatile.Read(ref _dropRawEvents) != 0;
        set => Volatile.Write(ref _dropRawEvents, value ? 1 : 0);
    }

    public void StartWatching(FolderSource source) => _inner.StartWatching(source);
    public void StopWatching(string folderSourceId) => _inner.StopWatching(folderSourceId);
    public bool IsWatching(string folderSourceId) => _inner.IsWatching(folderSourceId);

    public void InjectOverflow(string folderSourceId) => OnWatcherOverflow?.Invoke(this, folderSourceId);

    public void Dispose() => _inner.Dispose();
}

/// <summary>
/// Wires the real product components over a throwaway temp directory + SQLite database.
/// </summary>
internal sealed class SoakRig : IDisposable
{
    private const int DebounceMilliseconds = 60;

    private readonly string _workDir;
    private readonly List<string> _log = new();
    private readonly object _logGate = new();
    private readonly object _reconciliationGate = new();
    private int _reconciliationScheduledCount;

    public SoakRig(string testName, int seed)
    {
        TestName = testName;
        Seed = seed;
        _workDir = Path.Combine(Path.GetTempPath(), "docide_qa02", $"{testName}_{Guid.NewGuid():N}");
        RootPath = Path.Combine(_workDir, "root");
        Directory.CreateDirectory(RootPath);

        LogPath = Path.Combine(Path.GetTempPath(), "docide_qa02", "logs", $"{testName}.log");

        Db = new AppDb(Path.Combine(_workDir, "soak.db"));
        Db.InitializeAsync().GetAwaiter().GetResult();

        Documents = new DocumentRepository(Db);
        FolderSources = new FolderSourceRepository(Db);
        Metadata = new DocumentMetadataRepository(Db);
        Fts = new FtsIndex(Db);
        IdentityProvider = new WindowsFileIdentityProvider();
        Identity = new DocumentIdentityService(Documents, IdentityProvider);
        MoveCorrelation = new MoveCorrelationService(Documents, IdentityProvider, Identity, FolderSources);
        Jobs = new LocalJobScheduler(maxDegreeOfParallelism: 4, queueCapacity: 512);
        Watcher = new SoakWatcherSeam(new SourceWatcher());

        Coordinator = new FileChangeCoordinator(
            Watcher,
            Identity,
            Documents,
            Jobs,
            debounceDelay: TimeSpan.FromMilliseconds(DebounceMilliseconds),
            moveCorrelationService: MoveCorrelation);

        Indexer = new IncrementalIndexCoordinator(
            Documents,
            Metadata,
            Fts,
            new MarkdownStructureParser(),
            new DocxStructureParser(),
            Coordinator);

        Reconciler = new StartupReconciler(FolderSources, Documents, Identity, IdentityProvider);

        Coordinator.OnFileChanged += (_, e) =>
        {
            ChangeEvents.Enqueue(e);
            string postState = "";
            if (!string.IsNullOrEmpty(e.DocumentId))
            {
                try
                {
                    var doc = Documents.GetByIdAsync(e.DocumentId).GetAwaiter().GetResult();
                    if (doc != null)
                    {
                        postState = $" state={doc.AvailabilityStatus}/{doc.IndexStatus} current='{doc.CurrentPath}' " +
                                    $"identity={doc.FilesystemVolumeId}:{doc.FilesystemObjectId} fp={doc.ContentFingerprint}";
                    }
                }
                catch
                {
                    // Diagnostics only.
                }
            }
            Log($"change {e.ChangeType} path='{e.Path}' old='{e.OldPath}' doc={e.DocumentId}{postState}");
        };
        Watcher.OnRawEvent += (_, e) =>
        {
            Log($"raw {e.ChangeType} path='{System.IO.Path.GetFileName(e.FullPath)}' " +
                $"old='{(string.IsNullOrEmpty(e.OldFullPath) ? "" : System.IO.Path.GetFileName(e.OldFullPath))}'");
        };
        Watcher.OnDroppedRawEvent += (_, e) =>
        {
            Log($"raw DROPPED {e.ChangeType} path='{System.IO.Path.GetFileName(e.FullPath)}' " +
                $"old='{(string.IsNullOrEmpty(e.OldFullPath) ? "" : System.IO.Path.GetFileName(e.OldFullPath))}'");
        };
        Coordinator.OnReconciliationScheduled += (_, sourceId) =>
        {
            lock (_reconciliationGate)
            {
                _reconciliationScheduledCount++;
            }
            Log($"reconciliation scheduled for source {sourceId}");
        };
        Jobs.OnJobProgressChanged += (_, info) =>
        {
            JobEvents.Enqueue(new JobSnapshot(info.JobId, info.JobType, info.DocumentId, info.State, info.Fingerprint));
        };

        Source = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = WorkspaceId,
            CanonicalPath = RootPath,
            DisplayName = "QA-02 soak root",
            AvailabilityStatus = "available"
        };

        SeedWorkspaceRow();
        FolderSources.CreateAsync(Source).GetAwaiter().GetResult();

        Log($"rig ready root='{RootPath}' workspace={WorkspaceId} seed={seed}");
    }

    public string TestName { get; }
    public int Seed { get; }
    public string RootPath { get; }
    public string LogPath { get; }
    public string WorkspaceId { get; } = Guid.NewGuid().ToString();
    public FolderSource Source { get; }

    public AppDb Db { get; }
    public DocumentRepository Documents { get; }
    public FolderSourceRepository FolderSources { get; }
    public DocumentMetadataRepository Metadata { get; }
    public FtsIndex Fts { get; }
    public WindowsFileIdentityProvider IdentityProvider { get; }
    public DocumentIdentityService Identity { get; }
    public MoveCorrelationService MoveCorrelation { get; }
    public LocalJobScheduler Jobs { get; }
    public SoakWatcherSeam Watcher { get; }
    public FileChangeCoordinator Coordinator { get; }
    public IncrementalIndexCoordinator Indexer { get; }
    public StartupReconciler Reconciler { get; }

    public ConcurrentQueue<FileChangeEvent> ChangeEvents { get; } = new();
    public ConcurrentQueue<JobSnapshot> JobEvents { get; } = new();

    public int ReconciliationScheduledCount
    {
        get { lock (_reconciliationGate) return _reconciliationScheduledCount; }
    }

    /// <summary>Distinct job ids observed by the scheduler; duplicates would inflate this.</summary>
    public int DistinctJobCount => JobEvents.Select(j => j.JobId).Distinct(StringComparer.Ordinal).Count();

    public void StartWatching() => Coordinator.StartWatching(Source);

    public Task<Document?> GetByPathAsync(string path) => Documents.GetByPathAsync(Path.GetFullPath(path));

    public IReadOnlyDictionary<string, string> GetIndexStates() => Indexer.GetAllIndexStates();

    /// <summary>Waits for the event pipeline and the incremental indexer to go quiet.</summary>
    public async Task WaitForQuietAsync(TimeSpan? minQuiet = null, TimeSpan? timeout = null)
    {
        TimeSpan quiet = minQuiet ?? SoakTimeouts.QuietWindow;
        var deadline = DateTime.UtcNow + (timeout ?? SoakTimeouts.DefaultWait);
        int lastActivity = ActivityStamp();
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            int activity = ActivityStamp();
            if (activity != lastActivity)
            {
                lastActivity = activity;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange >= quiet)
            {
                return;
            }
            await Task.Delay(30);
        }
    }

    private int ActivityStamp()
    {
        int indexing = 0;
        foreach (var state in Indexer.GetAllIndexStates().Values)
        {
            if (state == "indexing") indexing++;
        }
        return ChangeEvents.Count * 31 + indexing;
    }

    public void Log(string message)
    {
        lock (_logGate)
        {
            _log.Add($"{DateTime.UtcNow:HH:mm:ss.fff} {message}");
        }
    }

    public void FlushLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            List<string> snapshot;
            lock (_logGate)
            {
                snapshot = new List<string>(_log);
            }
            File.WriteAllLines(LogPath, snapshot);
        }
        catch
        {
            // Logging must never mask the real test failure.
        }
    }

    private void SeedWorkspaceRow()
    {
        using var conn = Db.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspaces (id, name, active_view, created_at, updated_at)
            VALUES (@id, 'QA-02 Soak Workspace', 'canvas', @now, @now);";
        cmd.Parameters.AddWithValue("@id", WorkspaceId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // Stop everything first so late callbacks stop appending, then persist the log.
        try { Coordinator.Dispose(); } catch { }
        try { Indexer.Dispose(); } catch { }
        try { Watcher.Dispose(); } catch { }
        try { Jobs.Dispose(); } catch { }
        FlushLog();

        try
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_workDir, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup; the log path is outside this directory.
        }
    }
}

/// <summary>A churn-tracked source file and the document identity it must keep.</summary>
internal sealed class TrackedFile
{
    public TrackedFile(string path, string documentId, string lastToken)
    {
        Path = path;
        DocumentId = documentId;
        LastToken = lastToken;
    }

    public string Path { get; set; }
    public string DocumentId { get; }
    public string LastToken { get; set; }
}

/// <summary>
/// Automated churn harness: Word-like saves, creates, renames, moves and deletes over
/// the real watcher pipeline, keeping a deterministic model of every tracked file and
/// its expected DocumentId/content token. All operations use poll-with-timeout waits.
/// </summary>
internal sealed class ChurnHarness
{
    private readonly SoakRig _rig;
    private readonly Random _random;
    private readonly List<TrackedFile> _live = new();
    private readonly List<TrackedFile> _deleted = new();
    private readonly string _subDir;

    public ChurnHarness(SoakRig rig, int seed)
    {
        _rig = rig;
        _random = new Random(seed);
        _subDir = System.IO.Path.Combine(rig.RootPath, "sub");
        Directory.CreateDirectory(_subDir);
    }

    public IReadOnlyList<TrackedFile> LiveFiles => _live;
    public IReadOnlyList<TrackedFile> DeletedFiles => _deleted;

    /// <summary>Number of physical files this harness has created so far.</summary>
    public int FilesEverCreated { get; private set; }

    public TrackedFile PickLive() => _live[_random.Next(_live.Count)];

    public static byte[] ContentBytes(string token) =>
        Encoding.UTF8.GetBytes($"# Dokumen QA-02\nToken {token}\n");

    /// <summary>
    /// Creates a physical file and tracks it. When <paramref name="waitForConvergence"/> is
    /// false (lost-event window), the file is only on disk — the pipeline has not seen it —
    /// so its DocumentId is unknown until reconciliation. Returns null when the requested
    /// convergence never happened (only possible when waiting).
    /// </summary>
    public async Task<TrackedFile?> CreateAsync(string name, string token, bool waitForConvergence = true)
    {
        string path = System.IO.Path.Combine(_rig.RootPath, name);
        byte[] bytes = ContentBytes(token);
        await File.WriteAllBytesAsync(path, bytes);
        FilesEverCreated++;

        Document? created = null;
        bool converged = false;
        if (waitForConvergence)
        {
            string fingerprint = SoakSupport.Sha256Hex(bytes);
            converged = await SoakSupport.WaitUntilAsync(async () =>
            {
                var doc = await _rig.Documents.GetByPathAsync(path);
                return doc != null && doc.ContentFingerprint == fingerprint && doc.IndexStatus == "ready";
            }, SoakTimeouts.Persist);
            created = await _rig.Documents.GetByPathAsync(path);
        }

        _rig.Log($"create {(waitForConvergence ? (converged ? "OK" : "TIMEOUT") : "DROPPED")} " +
                 $"{name} token={token} doc={(created?.Id ?? "<unknown>")}");

        if (waitForConvergence && created == null) return null;

        var tracked = new TrackedFile(path, created?.Id ?? string.Empty, token);
        _live.Add(tracked);
        return tracked;
    }

    public async Task<bool> SaveAsync(TrackedFile file, string token, bool waitForConvergence = true)
    {
        byte[] bytes = ContentBytes(token);
        SoakSupport.AtomicReplaceFromBytes(file.Path, bytes);

        bool converged = false;
        if (waitForConvergence)
        {
            string fingerprint = SoakSupport.Sha256Hex(bytes);
            converged = await SoakSupport.WaitUntilAsync(async () =>
            {
                var doc = await _rig.Documents.GetByPathAsync(file.Path);
                return doc != null
                       && doc.Id == file.DocumentId
                       && doc.ContentFingerprint == fingerprint
                       && doc.IndexStatus == "ready";
            }, SoakTimeouts.Persist);
        }
        else
        {
            file.LastToken = token; // disk truth; the pipeline will only learn it on reconciliation
        }

        if (converged) file.LastToken = token;
        _rig.Log($"save {(waitForConvergence ? (converged ? "OK" : "TIMEOUT") : "DROPPED")} " +
                 $"{System.IO.Path.GetFileName(file.Path)} token={token} id={file.DocumentId}");
        return waitForConvergence ? converged : true;
    }

    public async Task<bool> RenameAsync(TrackedFile file, string suffix, bool waitForConvergence = true)
    {
        string directory = System.IO.Path.GetDirectoryName(file.Path)!;
        string stem = System.IO.Path.GetFileNameWithoutExtension(file.Path);
        string extension = System.IO.Path.GetExtension(file.Path);
        string newPath = System.IO.Path.Combine(directory, $"{stem}-{suffix}{extension}");
        if (File.Exists(newPath)) newPath = System.IO.Path.Combine(directory, $"{stem}-{suffix}-{Guid.NewGuid():N}{extension}");

        File.Move(file.Path, newPath);
        file.Path = newPath; // disk truth

        bool converged = false;
        if (waitForConvergence)
        {
            converged = await SoakSupport.WaitUntilAsync(async () =>
            {
                var doc = await _rig.Documents.GetByPathAsync(newPath);
                return doc != null && doc.Id == file.DocumentId && doc.AvailabilityStatus == "available";
            }, SoakTimeouts.Persist);
        }

        _rig.Log($"rename {(waitForConvergence ? (converged ? "OK" : "TIMEOUT") : "DROPPED")} " +
                 $"-> {System.IO.Path.GetFileName(newPath)} id={file.DocumentId}");
        return waitForConvergence ? converged : true;
    }

    public async Task<bool> MoveToSubfolderAsync(TrackedFile file, string suffix, bool waitForConvergence = true)
    {
        string target = System.IO.Path.Combine(_subDir, System.IO.Path.GetFileName(file.Path));
        if (File.Exists(target))
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(file.Path);
            string extension = System.IO.Path.GetExtension(file.Path);
            target = System.IO.Path.Combine(_subDir, $"{stem}-{suffix}{extension}");
        }

        File.Move(file.Path, target);
        file.Path = target; // disk truth

        bool converged = false;
        if (waitForConvergence)
        {
            converged = await SoakSupport.WaitUntilAsync(async () =>
            {
                var doc = await _rig.Documents.GetByPathAsync(target);
                return doc != null && doc.Id == file.DocumentId && doc.AvailabilityStatus == "available";
            }, SoakTimeouts.Persist);
        }

        _rig.Log($"move {(waitForConvergence ? (converged ? "OK" : "TIMEOUT") : "DROPPED")} " +
                 $"-> sub\\{System.IO.Path.GetFileName(target)} id={file.DocumentId}");
        return waitForConvergence ? converged : true;
    }

    public async Task<bool> DeleteAsync(TrackedFile file, bool waitForConvergence = true)
    {
        File.Delete(file.Path);

        bool converged = false;
        if (waitForConvergence)
        {
            converged = await SoakSupport.WaitUntilAsync(async () =>
            {
                var doc = await _rig.Documents.GetByPathAsync(file.Path);
                return doc != null && doc.AvailabilityStatus == "missing";
            }, SoakTimeouts.Persist);
        }

        _live.Remove(file);
        _deleted.Add(file);
        _rig.Log($"delete {(waitForConvergence ? (converged ? "OK" : "TIMEOUT") : "DROPPED")} " +
                 $"{System.IO.Path.GetFileName(file.Path)} id={file.DocumentId}");
        return waitForConvergence ? converged : true;
    }

    /// <summary>
    /// Raises the overflow notification and waits until the coordinator has scheduled
    /// the reconciliation event (the scheduler job is enqueued inside that handler).
    /// </summary>
    public async Task<bool> InjectOverflowAsync()
    {
        int before = _rig.ReconciliationScheduledCount;
        _rig.Watcher.InjectOverflow(_rig.Source.Id);
        bool scheduled = await SoakSupport.WaitUntilAsync(
            () => Task.FromResult(_rig.ReconciliationScheduledCount > before),
            SoakTimeouts.DefaultWait);
        _rig.Log($"overflow injected (scheduled={scheduled}, dropped so far={_rig.Watcher.DroppedEventCount})");
        return scheduled;
    }
}
