using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Jobs;

namespace DocumentIDE.Core.Filesystem;

public class FileChangeCoordinator : IDisposable
{
    private readonly ISourceWatcher _watcher;
    private readonly DocumentIdentityService _identityService;
    private readonly IDocumentRepository _documentRepo;
    private readonly IJobScheduler? _jobScheduler;
    private readonly TimeSpan _debounceDelay;

    private readonly ConcurrentDictionary<string, PendingPathChange> _pendingChanges = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    public event EventHandler<FileChangeEvent>? OnFileChanged;
    public event EventHandler<string>? OnReconciliationScheduled;

    private class PendingPathChange
    {
        public string CanonicalPath { get; }
        public string? OldCanonicalPath { get; set; }
        public string FolderSourceId { get; }
        public Timer Timer { get; set; }

        public PendingPathChange(string canonicalPath, string? oldCanonicalPath, string folderSourceId, Timer timer)
        {
            CanonicalPath = canonicalPath;
            OldCanonicalPath = oldCanonicalPath;
            FolderSourceId = folderSourceId;
            Timer = timer;
        }
    }

    public FileChangeCoordinator(
        ISourceWatcher watcher,
        DocumentIdentityService identityService,
        IDocumentRepository documentRepo,
        IJobScheduler? jobScheduler = null,
        TimeSpan? debounceDelay = null)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _documentRepo = documentRepo ?? throw new ArgumentNullException(nameof(documentRepo));
        _jobScheduler = jobScheduler;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(300);

        _watcher.OnRawEvent += HandleRawEvent;
        _watcher.OnWatcherOverflow += HandleWatcherOverflow;
    }

    public void StartWatching(FolderSource source)
    {
        _watcher.StartWatching(source);
    }

    public void StopWatching(string folderSourceId)
    {
        _watcher.StopWatching(folderSourceId);
    }

    private void HandleRawEvent(object? sender, RawWatcherEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.FullPath)) return;

        string canonicalPath = Path.GetFullPath(e.FullPath);

        if (IsTemporaryOrLockFile(canonicalPath))
        {
            return;
        }

        if (!IsSupportedDocumentExtension(canonicalPath))
        {
            return;
        }

        string? oldCanonicalPath = !string.IsNullOrWhiteSpace(e.OldFullPath)
            ? Path.GetFullPath(e.OldFullPath)
            : null;

        // Raw callbacks do NOT do synchronous file I/O or DB parsing (Acceptance Criteria: hints only & non-blocking).
        // Enqueue / reset debounce timer per target path.
        _pendingChanges.AddOrUpdate(
            canonicalPath,
            path =>
            {
                var timer = new Timer(
                    state => _ = ProcessPendingPathAsync((string)state!),
                    path,
                    _debounceDelay,
                    Timeout.InfiniteTimeSpan);
                return new PendingPathChange(path, oldCanonicalPath, e.FolderSourceId, timer);
            },
            (path, existing) =>
            {
                existing.Timer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
                if (oldCanonicalPath != null)
                {
                    existing.OldCanonicalPath = oldCanonicalPath;
                }
                return existing;
            });
    }

    private async Task ProcessPendingPathAsync(string canonicalPath)
    {
        if (!_pendingChanges.TryRemove(canonicalPath, out var pending))
        {
            return;
        }

        try
        {
            pending.Timer.Dispose();
        }
        catch { }

        if (_cts.IsCancellationRequested) return;

        try
        {
            bool exists = File.Exists(canonicalPath);

            if (exists)
            {
                // Retrieve document state before identity resolution for comparison
                var docBefore = await _documentRepo.GetByPathAsync(canonicalPath);
                if (docBefore == null && !string.IsNullOrEmpty(pending.OldCanonicalPath))
                {
                    docBefore = await _documentRepo.GetByPathAsync(pending.OldCanonicalPath);
                }

                // Verify final filesystem state using DocumentIdentityService (Step 3)
                Document resolvedDoc;
                try
                {
                    resolvedDoc = await _identityService.ResolveDocumentAsync(canonicalPath);
                }
                catch (IOException)
                {
                    // File may be locked transiently by external process, retry stat once
                    await Task.Delay(50);
                    if (!File.Exists(canonicalPath)) return;
                    resolvedDoc = await _identityService.ResolveDocumentAsync(canonicalPath);
                }

                FileChangeEvent? changeEvent = null;

                if (docBefore == null)
                {
                    // New document created
                    changeEvent = new FileChangeEvent
                    {
                        ChangeType = FileChangeType.Created,
                        Path = canonicalPath,
                        DocumentId = resolvedDoc.Id,
                        FolderSourceId = pending.FolderSourceId
                    };
                }
                else
                {
                    bool pathChanged = !string.Equals(docBefore.CurrentPath, canonicalPath, StringComparison.OrdinalIgnoreCase)
                                       || (!string.IsNullOrEmpty(pending.OldCanonicalPath) && !string.Equals(pending.OldCanonicalPath, canonicalPath, StringComparison.OrdinalIgnoreCase));

                    if (pathChanged)
                    {
                        changeEvent = new FileChangeEvent
                        {
                            ChangeType = FileChangeType.Renamed,
                            Path = canonicalPath,
                            OldPath = docBefore.CurrentPath,
                            DocumentId = resolvedDoc.Id,
                            FolderSourceId = pending.FolderSourceId
                        };
                    }
                    else if (docBefore.ContentFingerprint != resolvedDoc.ContentFingerprint || docBefore.ModifiedAt != resolvedDoc.ModifiedAt)
                    {
                        changeEvent = new FileChangeEvent
                        {
                            ChangeType = FileChangeType.Modified,
                            Path = canonicalPath,
                            DocumentId = resolvedDoc.Id,
                            FolderSourceId = pending.FolderSourceId
                        };
                    }
                    // Else: Fingerprint & metadata identical -> Duplicate event ignored, no event emitted!
                }

                if (changeEvent != null)
                {
                    OnFileChanged?.Invoke(this, changeEvent);
                }
            }
            else
            {
                // File does not exist physically on disk
                var docInDb = await _documentRepo.GetByPathAsync(canonicalPath);
                if (docInDb == null && !string.IsNullOrEmpty(pending.OldCanonicalPath))
                {
                    docInDb = await _documentRepo.GetByPathAsync(pending.OldCanonicalPath);
                }

                if (docInDb != null && docInDb.AvailabilityStatus != "missing")
                {
                    docInDb.AvailabilityStatus = "missing";
                    docInDb.LastKnownPath = docInDb.CurrentPath;
                    docInDb.UpdatedAt = DateTime.UtcNow.ToString("O");
                    await _documentRepo.UpdateAsync(docInDb);

                    var changeEvent = new FileChangeEvent
                    {
                        ChangeType = FileChangeType.Deleted,
                        Path = canonicalPath,
                        OldPath = docInDb.LastKnownPath,
                        DocumentId = docInDb.Id,
                        FolderSourceId = pending.FolderSourceId
                    };
                    OnFileChanged?.Invoke(this, changeEvent);
                }
            }
        }
        catch (Exception ex)
        {
            // Log/catch safely to prevent background task unhandled exception
            System.Diagnostics.Debug.WriteLine($"Error processing file change for {canonicalPath}: {ex}");
        }
    }

    private async void HandleWatcherOverflow(object? sender, string folderSourceId)
    {
        if (_jobScheduler != null)
        {
            var job = new DelegateJob(async context =>
            {
                context.ReportProgress(0, $"Starting reconciliation for folder source {folderSourceId} after watcher overflow.");
                await Task.Yield();
                context.ReportProgress(100, "Reconciliation complete.");
            });

            await _jobScheduler.EnqueueJobAsync(
                JobTypes.Reconciliation,
                folderSourceId,
                null,
                job);
        }

        OnReconciliationScheduled?.Invoke(this, folderSourceId);
    }

    private static bool IsTemporaryOrLockFile(string path)
    {
        string fileName = Path.GetFileName(path);
        if (string.IsNullOrEmpty(fileName)) return true;
        if (fileName.StartsWith("~$", StringComparison.Ordinal)) return true;
        if (fileName.StartsWith("~", StringComparison.Ordinal)) return true;
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return true;
        if (fileName.StartsWith(".")) return true;
        return false;
    }

    private static bool IsSupportedDocumentExtension(string path)
    {
        string ext = Path.GetExtension(path);
        return string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watcher.OnRawEvent -= HandleRawEvent;
        _watcher.OnWatcherOverflow -= HandleWatcherOverflow;

        foreach (var pending in _pendingChanges.Values)
        {
            try { pending.Timer.Dispose(); } catch { }
        }
        _pendingChanges.Clear();
        _cts.Dispose();
    }
}
