using System;

namespace DocumentIDE.Core.Domain;

public class MarkdownDocumentMetadata
{
    public string DocumentId { get; set; } = string.Empty;
    public string? FrontmatterJson { get; set; }
    public string? HeadingsJson { get; set; }
    public string? OutboundLinksJson { get; set; }
    public string? WikilinksJson { get; set; }
    public int? CodeBlockCount { get; set; }
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string ParserVersion { get; set; } = "1.0.0";
}
