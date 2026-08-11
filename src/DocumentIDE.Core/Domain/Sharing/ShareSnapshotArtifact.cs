using System;

namespace DocumentIDE.Core.Domain.Sharing;

public class ShareSnapshotArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ShareId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = "html";
    public string RelativePath { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
