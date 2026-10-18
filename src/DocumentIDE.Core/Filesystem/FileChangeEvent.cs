using System;

namespace DocumentIDE.Core.Filesystem;

public class FileChangeEvent
{
    public FileChangeType ChangeType { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? OldPath { get; set; }
    public string? DocumentId { get; set; }
    public string? FolderSourceId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
