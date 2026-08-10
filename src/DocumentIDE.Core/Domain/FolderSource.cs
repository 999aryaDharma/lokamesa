using System;

namespace DocumentIDE.Core.Domain;

/// <summary>
/// A local folder attached to a workspace as a document source.
/// The workspace never owns or imports the physical files.
/// Canvas drag never physically moves files in the source folder.
/// FS-01: Folder Source Attachment.
/// </summary>
public class FolderSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkspaceId { get; set; } = string.Empty;
    public string CanonicalPath { get; set; } = string.Empty;
    public string? FilesystemVolumeId { get; set; }
    public string? FilesystemObjectId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = "available";
    public string AddedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string? LastReconciledAt { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
