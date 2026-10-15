using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.ShareHost.Graph;

/// <summary>
/// SHR-06 — a share-scoped graph node. Carries only the member document id,
/// virtual display name, and virtual preview URL for viewer navigation.
/// Never carries a local filesystem path.
/// </summary>
public sealed record ShareGraphNode(
    string DocumentId,
    string DisplayName,
    string PreviewUrl);

/// <summary>
/// SHR-06 — a share-scoped graph edge. Both endpoints are guaranteed share
/// members; edges touching a private node are omitted before serving.
/// Carries relation kind/origin labels only — never source paths.
/// </summary>
public sealed record ShareGraphEdge(
    string SourceDocumentId,
    string TargetDocumentId,
    string RelationType,
    string RelationOrigin);

/// <summary>Share-scoped viewer graph payload: member nodes + member-only edges.</summary>
public sealed record ShareGraphPayload(
    IReadOnlyList<ShareGraphNode> Nodes,
    IReadOnlyList<ShareGraphEdge> Edges);

/// <summary>
/// SHR-06 — share-scoped graph over explicit share membership only.
///
/// Nodes are exactly the frozen manifest members. Edges come from the
/// prepared <c>graph.json</c> share artifact when present (otherwise the
/// graph has member nodes and no edges) and are filtered server-side to
/// relations where both endpoints are share members. An edge to a private
/// node is omitted entirely: neither the edge nor the private node appears
/// in the payload, so viewers cannot probe nonmember existence through the
/// graph.
/// </summary>
public sealed class ShareGraphService
{
    /// <summary>Prepared graph artifact bytes read at most.</summary>
    public const long MaxGraphArtifactBytes = 1_000_000;

    private readonly ShareTokenService _tokens;

    public ShareGraphService(ShareTokenService tokens)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <summary>
    /// Builds the viewer graph for <paramref name="manifest"/>: member nodes
    /// plus both-endpoints-member edges. Malformed graph artifacts degrade to
    /// member nodes with no edges instead of throwing.
    /// </summary>
    public ShareGraphPayload BuildGraph(ShareManifest manifest)
    {
        if (manifest is null)
        {
            return new ShareGraphPayload(Array.Empty<ShareGraphNode>(), Array.Empty<ShareGraphEdge>());
        }

        var members = manifest.Documents ?? new List<SanitizedDocumentMetadata>();
        var memberIds = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<ShareGraphNode>(members.Count);
        foreach (var doc in members)
        {
            if (doc is null || string.IsNullOrWhiteSpace(doc.DocumentId))
            {
                continue;
            }

            if (!memberIds.Add(doc.DocumentId))
            {
                continue;
            }

            nodes.Add(new ShareGraphNode(
                doc.DocumentId,
                doc.DisplayName ?? string.Empty,
                $"/share/{manifest.ShareId}/documents/{doc.DocumentId}/preview"));
        }

        var edges = new List<ShareGraphEdge>();
        foreach (var record in ReadPreparedEdges(manifest.ShareId))
        {
            if (record is null ||
                string.IsNullOrWhiteSpace(record.SourceDocumentId) ||
                string.IsNullOrWhiteSpace(record.TargetDocumentId))
            {
                continue;
            }

            // Both-endpoints-member filter: the core SHR-06 graph guarantee.
            if (!memberIds.Contains(record.SourceDocumentId) ||
                !memberIds.Contains(record.TargetDocumentId))
            {
                continue;
            }

            edges.Add(new ShareGraphEdge(
                record.SourceDocumentId,
                record.TargetDocumentId,
                string.IsNullOrWhiteSpace(record.RelationType) ? "Related" : record.RelationType,
                string.IsNullOrWhiteSpace(record.RelationOrigin) ? "Explicit" : record.RelationOrigin));
        }

        return new ShareGraphPayload(nodes, edges);
    }

    private IReadOnlyList<ShareGraphEdgeRecord> ReadPreparedEdges(string shareId)
    {
        string artifactPath;
        try
        {
            artifactPath = _tokens.GetArtifactPath(shareId, "graph.json");
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<ShareGraphEdgeRecord>();
        }
        catch (ArgumentException)
        {
            return Array.Empty<ShareGraphEdgeRecord>();
        }

        try
        {
            if (!File.Exists(artifactPath))
            {
                return Array.Empty<ShareGraphEdgeRecord>();
            }

            if (new FileInfo(artifactPath).Length > MaxGraphArtifactBytes)
            {
                return Array.Empty<ShareGraphEdgeRecord>();
            }

            var json = File.ReadAllText(artifactPath);
            var records = JsonSerializer.Deserialize<List<ShareGraphEdgeRecord>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return records ?? (IReadOnlyList<ShareGraphEdgeRecord>)Array.Empty<ShareGraphEdgeRecord>();
        }
        catch (IOException)
        {
            return Array.Empty<ShareGraphEdgeRecord>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<ShareGraphEdgeRecord>();
        }
        catch (JsonException)
        {
            return Array.Empty<ShareGraphEdgeRecord>();
        }
        catch (NotSupportedException)
        {
            return Array.Empty<ShareGraphEdgeRecord>();
        }
    }

    /// <summary>
    /// Prepared edge record as written to the share-scoped <c>graph.json</c>
    /// artifact. Only endpoint ids and relation labels are read; any other
    /// fields (including path-like metadata, if present) are ignored and
    /// never propagated to the viewer payload.
    /// </summary>
    private sealed class ShareGraphEdgeRecord
    {
        public string? SourceDocumentId { get; set; }
        public string? TargetDocumentId { get; set; }
        public string? RelationType { get; set; }
        public string? RelationOrigin { get; set; }
    }
}
