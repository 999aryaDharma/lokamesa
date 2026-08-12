using System;

namespace DocumentIDE.Core.Domain;

public class DocumentMetadata
{
    public string DocumentId { get; set; } = string.Empty;
    public int? PageCount { get; set; }
    public int? SectionCount { get; set; }
    public int? TableCount { get; set; }
    public int? ImageCount { get; set; }
    public string? PaperSummary { get; set; }
    public string? OrientationSummary { get; set; }
    public string? MarginSummary { get; set; }
    public string? PrimaryFont { get; set; }
    public double? PrimaryFontSize { get; set; }
    public bool? HeaderPresent { get; set; }
    public bool? FooterPresent { get; set; }
    public string? HeadingsJson { get; set; }
    public string ExtractedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string ParserVersion { get; set; } = "1.0.0";
}
