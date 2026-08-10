using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Service for attaching and managing local folder sources within a workspace.
/// FS-01: Folder Source Attachment.
///
/// CRITICAL SAFETY CONTRACT:
/// - Attaching a folder MUST NEVER write any file into the user's source folder.
/// - The source folder is read-only for this operation (Directory.Exists + enumeration only).
/// - No .documentide files, lockfiles, or metadata are ever written to the source folder.
/// </summary>
public interface IFolderSourceService
{
    /// <summary>
    /// Attaches a local folder to the given workspace.
    /// Canonicalizes the path, validates existence, enumerates supported files,
    /// and persists the FolderSource record to SQLite.
    /// </summary>
    /// <param name="workspaceId">The workspace to attach the folder to.</param>
    /// <param name="rawFolderPath">The raw user-provided path (will be canonicalized).</param>
    /// <returns>Attachment result with persisted entity and file count.</returns>
    /// <exception cref="FolderNotFoundException">Thrown if directory does not exist or is inaccessible.</exception>
    Task<AttachFolderResult> AttachFolderAsync(string workspaceId, string rawFolderPath);

    /// <summary>
    /// Returns all folder sources attached to the given workspace,
    /// refreshing their availability status against the current filesystem.
    /// </summary>
    Task<IReadOnlyList<FolderSource>> GetFolderSourcesAsync(string workspaceId);

    /// <summary>
    /// Counts the supported files (.docx, .md) in a folder recursively,
    /// ignoring hidden directories. Does NOT mutate the source folder.
    /// </summary>
    int CountSupportedFiles(string canonicalPath);
}
