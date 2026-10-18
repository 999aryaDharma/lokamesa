using System;

namespace DocumentIDE.Core.Domain.Sharing;

public class ShareMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ShareId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public string AddedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
