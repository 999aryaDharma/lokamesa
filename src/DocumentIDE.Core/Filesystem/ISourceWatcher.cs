using System;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

public interface ISourceWatcher : IDisposable
{
    event EventHandler<RawWatcherEventArgs>? OnRawEvent;
    event EventHandler<string>? OnWatcherOverflow; // Sends FolderSourceId
    void StartWatching(FolderSource source);
    void StopWatching(string folderSourceId);
    bool IsWatching(string folderSourceId);
}
