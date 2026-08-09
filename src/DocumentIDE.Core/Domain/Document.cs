using System;

namespace DocumentIDE.Core.Domain;

public class Document
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? FilesystemVolumeId { get; set; }
    public string? FilesystemObjectId { get; set; }
    public string CurrentPath { get; set; } = string.Empty;
    public string? LastKnownPath { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "markdown";
    public string? ContentFingerprint { get; set; }
    public string AvailabilityStatus { get; set; } = "available";
    public string IndexStatus { get; set; } = "pending";
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
