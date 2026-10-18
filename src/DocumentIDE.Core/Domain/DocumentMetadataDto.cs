namespace DocumentIDE.Core.Domain;

public class DocumentMetadataDto
{
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string IndexStatus { get; set; } = "pending";
    public DocumentMetadata? DocxMetadata { get; set; }
    public MarkdownDocumentMetadata? MarkdownMetadata { get; set; }
    public bool IsRebuildable { get; set; }
    public string? LastError { get; set; }
}
