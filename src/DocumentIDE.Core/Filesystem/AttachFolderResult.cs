using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Result returned when a folder source is successfully attached.
/// Includes the persisted entity and the enumerated supported file count.
/// FS-01: Folder Source Attachment.
/// </summary>
public sealed class AttachFolderResult
{
    public FolderSource FolderSource { get; init; } = null!;

    /// <summary>Count of supported .docx and .md files found recursively.</summary>
    public int SupportedFileCount { get; init; }

    /// <summary>Display name derived from the folder's directory name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
