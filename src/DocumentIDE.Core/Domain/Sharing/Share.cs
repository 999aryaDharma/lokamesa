using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.Domain.Sharing;

public class Share
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ShareMode Mode { get; set; } = ShareMode.Snapshot;
    public AccessMode AccessMode { get; set; } = AccessMode.SecretToken;
    public string TokenHash { get; set; } = string.Empty;
    public bool AllowOriginalDownload { get; set; } = false;
    public bool AllowPdfDownload { get; set; } = false;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string? ExpiresAt { get; set; }

    public List<ShareMember> Members { get; set; } = new List<ShareMember>();
    public List<ShareSnapshotArtifact> SnapshotArtifacts { get; set; } = new List<ShareSnapshotArtifact>();
}
