namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Thrown when a folder path does not exist or is inaccessible during attachment.
/// FS-01: Folder Source Attachment.
/// </summary>
public sealed class FolderNotFoundException : Exception
{
    public string FolderPath { get; }

    public FolderNotFoundException(string folderPath)
        : base($"The folder '{folderPath}' does not exist or cannot be accessed.")
    {
        FolderPath = folderPath;
    }

    public FolderNotFoundException(string folderPath, Exception innerException)
        : base($"The folder '{folderPath}' does not exist or cannot be accessed.", innerException)
    {
        FolderPath = folderPath;
    }
}
