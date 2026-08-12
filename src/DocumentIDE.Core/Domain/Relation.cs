using System;

namespace DocumentIDE.Core.Domain;

public static class RelationOrigin
{
    public const string Explicit = "Explicit";
    public const string Inferred = "Inferred";
    public const string Manual = "Manual";
    public const string Semantic = "semantic";
}

public static class RelationType
{
    public const string MarkdownLink = "MarkdownLink";
    public const string Wikilink = "Wikilink";
    public const string FrontmatterDependency = "FrontmatterDependency";
    public const string ManualRelation = "ManualRelation";
    public const string SemanticSuggestion = "SemanticSuggestion";
}

public class Relation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkspaceId { get; set; } = string.Empty;
    public string SourceDocumentId { get; set; } = string.Empty;
    public string TargetDocumentId { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string RelationOrigin { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
    public string? MetadataJson { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
