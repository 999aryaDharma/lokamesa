using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Graph;

public record RelationInspectorItem
{
    public string RelationId { get; init; } = string.Empty;
    public string SourceDocumentId { get; init; } = string.Empty;
    public string? SourceDocumentName { get; init; }
    public string? SourceDocumentPath { get; init; }
    public string TargetDocumentId { get; init; } = string.Empty;
    public string? TargetDocumentName { get; init; }
    public string? TargetDocumentPath { get; init; }
    public string RelationType { get; init; } = string.Empty;
    public string RelationOrigin { get; init; } = string.Empty;
    public bool IsResolved { get; init; } = true;
    public bool IsAmbiguous { get; init; } = false;
    public double Confidence { get; init; } = 1.0;
    public string? MetadataJson { get; init; }
    public string? RawTarget { get; init; }
}

public record DocumentInspectorRelations
{
    public string DocumentId { get; init; } = string.Empty;
    public IReadOnlyList<RelationInspectorItem> References { get; init; } = Array.Empty<RelationInspectorItem>();
    public IReadOnlyList<RelationInspectorItem> Backlinks { get; init; } = Array.Empty<RelationInspectorItem>();
    public IReadOnlyList<RelationInspectorItem> InferredRelations { get; init; } = Array.Empty<RelationInspectorItem>();
    public IReadOnlyList<RelationInspectorItem> UnresolvedLinks { get; init; } = Array.Empty<RelationInspectorItem>();
}

public interface IBacklinkService
{
    Task<DocumentInspectorRelations> GetInspectorRelationsAsync(string workspaceId, string documentId);
}

public class BacklinkService : IBacklinkService
{
    private readonly IRelationRepository _relationRepository;
    private readonly IDocumentRepository? _documentRepository;

    public BacklinkService(
        IRelationRepository relationRepository,
        IDocumentRepository? documentRepository = null)
    {
        _relationRepository = relationRepository ?? throw new ArgumentNullException(nameof(relationRepository));
        _documentRepository = documentRepository;
    }

    public async Task<DocumentInspectorRelations> GetInspectorRelationsAsync(string workspaceId, string documentId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));

        var outbound = await _relationRepository.GetBySourceDocumentAsync(workspaceId, documentId);
        var inbound = await _relationRepository.GetByTargetDocumentAsync(workspaceId, documentId);

        var references = new List<RelationInspectorItem>();
        var backlinks = new List<RelationInspectorItem>();
        var inferred = new List<RelationInspectorItem>();
        var unresolved = new List<RelationInspectorItem>();

        var docCache = new Dictionary<string, Document?>();

        async Task<Document?> GetDocAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || _documentRepository == null)
                return null;

            if (docCache.TryGetValue(id, out var doc))
                return doc;

            try
            {
                doc = await _documentRepository.GetByIdAsync(id);
            }
            catch
            {
                doc = null;
            }

            docCache[id] = doc;
            return doc;
        }

        // Process Outbound Relations (References & Outbound Inferred)
        foreach (var rel in outbound)
        {
            var sourceDoc = await GetDocAsync(rel.SourceDocumentId);
            var targetDoc = await GetDocAsync(rel.TargetDocumentId);

            string? rawTarget = ExtractRawTarget(rel);
            bool isAmbiguous = CheckIsAmbiguous(rel);
            bool isTargetResolved = targetDoc != null;

            string targetName = isTargetResolved
                ? targetDoc!.Filename
                : (!string.IsNullOrWhiteSpace(rawTarget) ? rawTarget! : rel.TargetDocumentId);

            var item = new RelationInspectorItem
            {
                RelationId = rel.Id,
                SourceDocumentId = rel.SourceDocumentId,
                SourceDocumentName = sourceDoc?.Filename ?? rel.SourceDocumentId,
                SourceDocumentPath = sourceDoc?.CurrentPath,
                TargetDocumentId = rel.TargetDocumentId,
                TargetDocumentName = targetName,
                TargetDocumentPath = targetDoc?.CurrentPath,
                RelationType = rel.RelationType,
                RelationOrigin = rel.RelationOrigin,
                IsResolved = isTargetResolved && !isAmbiguous,
                IsAmbiguous = isAmbiguous,
                Confidence = rel.Confidence,
                MetadataJson = rel.MetadataJson,
                RawTarget = rawTarget
            };

            if (rel.RelationOrigin == RelationOrigin.Inferred || rel.RelationType == RelationType.SemanticSuggestion)
            {
                inferred.Add(item);
            }
            else
            {
                references.Add(item);
                if (!item.IsResolved || item.IsAmbiguous)
                {
                    unresolved.Add(item);
                }
            }
        }

        // Process Inbound Relations (Backlinks & Inbound Inferred)
        foreach (var rel in inbound)
        {
            if (rel.RelationOrigin == RelationOrigin.Inferred || rel.RelationType == RelationType.SemanticSuggestion)
            {
                if (!inferred.Any(x => x.RelationId == rel.Id))
                {
                    var sourceDoc = await GetDocAsync(rel.SourceDocumentId);
                    var targetDoc = await GetDocAsync(rel.TargetDocumentId);
                    string? rawTarget = ExtractRawTarget(rel);
                    bool isAmbiguous = CheckIsAmbiguous(rel);

                    var item = new RelationInspectorItem
                    {
                        RelationId = rel.Id,
                        SourceDocumentId = rel.SourceDocumentId,
                        SourceDocumentName = sourceDoc?.Filename ?? rel.SourceDocumentId,
                        SourceDocumentPath = sourceDoc?.CurrentPath,
                        TargetDocumentId = rel.TargetDocumentId,
                        TargetDocumentName = targetDoc?.Filename ?? rel.TargetDocumentId,
                        TargetDocumentPath = targetDoc?.CurrentPath,
                        RelationType = rel.RelationType,
                        RelationOrigin = rel.RelationOrigin,
                        IsResolved = sourceDoc != null && targetDoc != null && !isAmbiguous,
                        IsAmbiguous = isAmbiguous,
                        Confidence = rel.Confidence,
                        MetadataJson = rel.MetadataJson,
                        RawTarget = rawTarget
                    };
                    inferred.Add(item);
                }
                continue;
            }

            // Inbound Explicit/Manual relation -> Backlink (Referenced By)
            var srcDoc = await GetDocAsync(rel.SourceDocumentId);
            var tgtDoc = await GetDocAsync(rel.TargetDocumentId);
            string? rawTgt = ExtractRawTarget(rel);
            bool isAmb = CheckIsAmbiguous(rel);
            bool isSrcResolved = srcDoc != null;

            var backlinkItem = new RelationInspectorItem
            {
                RelationId = rel.Id,
                SourceDocumentId = rel.SourceDocumentId,
                SourceDocumentName = isSrcResolved ? srcDoc!.Filename : rel.SourceDocumentId,
                SourceDocumentPath = srcDoc?.CurrentPath,
                TargetDocumentId = rel.TargetDocumentId,
                TargetDocumentName = tgtDoc?.Filename ?? rel.TargetDocumentId,
                TargetDocumentPath = tgtDoc?.CurrentPath,
                RelationType = rel.RelationType,
                RelationOrigin = rel.RelationOrigin,
                IsResolved = isSrcResolved && !isAmb,
                IsAmbiguous = isAmb,
                Confidence = rel.Confidence,
                MetadataJson = rel.MetadataJson,
                RawTarget = rawTgt
            };

            backlinks.Add(backlinkItem);
            if (!backlinkItem.IsResolved || backlinkItem.IsAmbiguous)
            {
                unresolved.Add(backlinkItem);
            }
        }

        // Order deterministically
        var sortedReferences = references
            .OrderBy(r => r.TargetDocumentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.TargetDocumentId)
            .ThenBy(r => r.RelationType)
            .ThenBy(r => r.RelationId)
            .ToList();

        var sortedBacklinks = backlinks
            .OrderBy(r => r.SourceDocumentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SourceDocumentId)
            .ThenBy(r => r.RelationType)
            .ThenBy(r => r.RelationId)
            .ToList();

        var sortedInferred = inferred
            .OrderByDescending(r => r.Confidence)
            .ThenBy(r => r.SourceDocumentId == documentId ? r.TargetDocumentName : r.SourceDocumentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RelationId)
            .ToList();

        var sortedUnresolved = unresolved
            .OrderBy(r => r.TargetDocumentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RawTarget, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RelationId)
            .ToList();

        return new DocumentInspectorRelations
        {
            DocumentId = documentId,
            References = sortedReferences,
            Backlinks = sortedBacklinks,
            InferredRelations = sortedInferred,
            UnresolvedLinks = sortedUnresolved
        };
    }

    private static string? ExtractRawTarget(Relation rel)
    {
        if (string.IsNullOrWhiteSpace(rel.MetadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rel.MetadataJson);
            if (doc.RootElement.TryGetProperty("rawTarget", out var rawTargetElem))
            {
                return rawTargetElem.GetString();
            }
            if (doc.RootElement.TryGetProperty("raw_target", out var rawTargetElem2))
            {
                return rawTargetElem2.GetString();
            }
        }
        catch
        {
            // Ignore JSON parse errors
        }

        return null;
    }

    private static bool CheckIsAmbiguous(Relation rel)
    {
        if (string.IsNullOrWhiteSpace(rel.MetadataJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rel.MetadataJson);
            if (doc.RootElement.TryGetProperty("isAmbiguous", out var ambElem))
            {
                return ambElem.GetBoolean();
            }
            if (doc.RootElement.TryGetProperty("is_ambiguous", out var ambElem2))
            {
                return ambElem2.GetBoolean();
            }
        }
        catch
        {
            // Ignore JSON parse errors
        }

        return false;
    }
}
