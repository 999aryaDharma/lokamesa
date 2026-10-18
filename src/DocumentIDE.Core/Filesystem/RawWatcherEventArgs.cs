using System;
using System.IO;

namespace DocumentIDE.Core.Filesystem;

public class RawWatcherEventArgs : EventArgs
{
    public WatcherChangeTypes ChangeType { get; }
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public string FolderSourceId { get; }

    public RawWatcherEventArgs(
        WatcherChangeTypes changeType,
        string fullPath,
        string? oldFullPath,
        string folderSourceId)
    {
        ChangeType = changeType;
        FullPath = fullPath;
        OldFullPath = oldFullPath;
        FolderSourceId = folderSourceId;
    }
}
