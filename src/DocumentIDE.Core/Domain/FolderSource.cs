using System;

namespace DocumentIDE.Core.Domain;

public class FolderSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkspaceId { get; set; } = string.Empty;
    public string CanonicalPath { get; set; } = string.Empty;
    public string? FilesystemVolumeId { get; set; }
    public string? FilesystemObjectId { get; set; }
    public string AvailabilityStatus { get; set; } = "available";
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
