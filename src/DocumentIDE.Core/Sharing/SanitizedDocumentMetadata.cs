namespace DocumentIDE.Core.Sharing;

public class SanitizedDocumentMetadata
{
    public string DocumentId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "markdown";
    public long SizeBytes { get; set; }
    public string? ModifiedAt { get; set; }
    public string? ContentFingerprint { get; set; }
}
