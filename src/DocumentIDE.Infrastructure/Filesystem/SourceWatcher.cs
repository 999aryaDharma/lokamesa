using System;
using System.Collections.Concurrent;
using System.IO;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Infrastructure.Filesystem;

public class SourceWatcher : ISourceWatcher
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();

    public event EventHandler<RawWatcherEventArgs>? OnRawEvent;
    public event EventHandler<string>? OnWatcherOverflow;

    public void StartWatching(FolderSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (string.IsNullOrWhiteSpace(source.CanonicalPath)) return;

        StopWatching(source.Id);

        if (!Directory.Exists(source.CanonicalPath))
        {
            return;
        }

        try
        {
            var fsw = new FileSystemWatcher(source.CanonicalPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.CreationTime,
                Filter = "*.*"
            };

            fsw.Created += (s, e) => RaiseRawEvent(WatcherChangeTypes.Created, e.FullPath, null, source.Id);
            fsw.Changed += (s, e) => RaiseRawEvent(WatcherChangeTypes.Changed, e.FullPath, null, source.Id);
            fsw.Deleted += (s, e) => RaiseRawEvent(WatcherChangeTypes.Deleted, e.FullPath, null, source.Id);
            fsw.Renamed += (s, e) => RaiseRawEvent(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath, source.Id);
            fsw.Error += (s, e) => RaiseOverflow(source.Id);

            fsw.EnableRaisingEvents = true;
            _watchers[source.Id] = fsw;
        }
        catch (Exception)
        {
            // If starting watcher fails due to permissions/drive unmount, ignore safely
        }
    }

    public void StopWatching(string folderSourceId)
    {
        if (string.IsNullOrWhiteSpace(folderSourceId)) return;

        if (_watchers.TryRemove(folderSourceId, out var fsw))
        {
            try
            {
                fsw.EnableRaisingEvents = false;
                fsw.Dispose();
            }
            catch { }
        }
    }

    public bool IsWatching(string folderSourceId)
    {
        return !string.IsNullOrWhiteSpace(folderSourceId) && _watchers.ContainsKey(folderSourceId);
    }

    private void RaiseRawEvent(WatcherChangeTypes changeType, string fullPath, string? oldFullPath, string folderSourceId)
    {
        OnRawEvent?.Invoke(this, new RawWatcherEventArgs(changeType, fullPath, oldFullPath, folderSourceId));
    }

    private void RaiseOverflow(string folderSourceId)
    {
        OnWatcherOverflow?.Invoke(this, folderSourceId);
    }

    public void Dispose()
    {
        foreach (var id in _watchers.Keys.ToList())
        {
            StopWatching(id);
        }
    }
}
