using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Domain.Repositories;

/// <summary>
/// Repository for persisting and querying FolderSource records.
/// FS-01: Folder Source Attachment.
/// </summary>
public interface IFolderSourceRepository
{
    Task CreateAsync(FolderSource folderSource);
    Task<IReadOnlyList<FolderSource>> GetByWorkspaceAsync(string workspaceId);
    Task UpdateAvailabilityAsync(string id, string availabilityStatus);
    Task DeleteAsync(string id);
}
