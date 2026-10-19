using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Markdown;

namespace DocumentIDE.Core.Graph;

/// <summary>
/// GR-01/GR-03 — derives a workspace's explicit relations from Markdown source.
///
/// This is the production writer the <c>relations</c> table was missing: it turns
/// the outbound Markdown links, wikilinks and frontmatter dependencies of each
/// Markdown document (extracted by <see cref="MarkdownLinkIndexer"/>) into
/// <c>Explicit</c> relation rows via
/// <see cref="IRelationRepository.ReplaceDerivedRelationsAsync"/>, so the graph
/// view returns real edges instead of <c>edges: []</c>.
///
/// Resolution is deterministic and fail-closed: a target is linked only when it
/// resolves to exactly one known workspace document; external URLs, anchors and
/// unresolved/ambiguous targets produce no edge rather than a guessed one.
/// Re-indexing a document replaces its derived relations wholesale, which removes
/// stale edges after an edit while leaving manual/inferred relations untouched.
/// </summary>
public sealed class MarkdownRelationIndexer(
    IDocumentRepository documents,
    IRelationRepository relations,
    IMarkdownStructureParser markdownParser)
{
    private static readonly HashSet<string> MarkdownExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

    /// <summary>
    /// Derives relations for every document in the workspace. Documents that are
    /// not readable Markdown have their derived relations cleared so the store
    /// never keeps edges for content that no longer produces them.
    /// </summary>
    public async Task<int> ReindexWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return 0;
        }

        var workspaceDocuments = await documents.GetByWorkspaceAsync(workspaceId);
        var pathToDocumentId = BuildPathIndex(workspaceDocuments);

        var total = 0;
        foreach (var document in workspaceDocuments)
        {
            total += await IndexDocumentCoreAsync(workspaceId, document, pathToDocumentId, cancellationToken);
        }
        return total;
    }

    /// <summary>
    /// Derives relations for a single document (MD-03 "update graph after save").
    /// </summary>
    public async Task<int> IndexDocumentAsync(string workspaceId, string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || string.IsNullOrWhiteSpace(documentId))
        {
            return 0;
        }

        var document = await documents.GetByIdAsync(documentId);
        if (document == null)
        {
            return 0;
        }

        var workspaceDocuments = await documents.GetByWorkspaceAsync(workspaceId);
        var pathToDocumentId = BuildPathIndex(workspaceDocuments);
        return await IndexDocumentCoreAsync(workspaceId, document, pathToDocumentId, cancellationToken);
    }

    private async Task<int> IndexDocumentCoreAsync(
        string workspaceId,
        Document document,
        IReadOnlyDictionary<string, string> pathToDocumentId,
        CancellationToken cancellationToken)
    {
        if (!IsReadableMarkdown(document))
        {
            await relations.ReplaceDerivedRelationsAsync(workspaceId, document.Id, Array.Empty<Relation>());
            return 0;
        }

        string source;
        try
        {
            source = await File.ReadAllTextAsync(document.CurrentPath, cancellationToken);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        var parseResult = markdownParser.Parse(source);
        var candidates = MarkdownLinkIndexer.GetExplicitRelationCandidates(document.CurrentPath, parseResult);
        var workspaceFiles = pathToDocumentId.Keys.ToList();

        var derived = new List<Relation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (!IsResolvableTarget(candidate.RawTarget))
            {
                continue;
            }

            var resolution = MarkdownLinkIndexer.ResolveLinkTarget(
                document.CurrentPath, candidate.RawTarget, candidate.RelationType, workspaceFiles);
            if (!resolution.IsResolved || resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                continue;
            }

            if (!pathToDocumentId.TryGetValue(Path.GetFullPath(resolution.ResolvedPath), out var targetDocumentId))
            {
                continue;
            }

            // A self-reference is not a graph edge.
            if (string.Equals(targetDocumentId, document.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var relationType = MapRelationType(candidate.RelationType);
            var relationId = $"{document.Id}|{targetDocumentId}|{relationType}";
            if (!seen.Add(relationId))
            {
                continue;
            }

            derived.Add(new Relation
            {
                Id = relationId,
                TargetDocumentId = targetDocumentId,
                RelationType = relationType,
                RelationOrigin = RelationOrigin.Explicit,
                Confidence = 1.0,
            });
        }

        await relations.ReplaceDerivedRelationsAsync(workspaceId, document.Id, derived);
        return derived.Count;
    }

    private static Dictionary<string, string> BuildPathIndex(IReadOnlyList<Document> workspaceDocuments)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in workspaceDocuments)
        {
            if (string.IsNullOrWhiteSpace(document.CurrentPath))
            {
                continue;
            }

            try
            {
                index[Path.GetFullPath(document.CurrentPath)] = document.Id;
            }
            catch (Exception)
            {
                // A malformed path simply does not participate in resolution.
            }
        }
        return index;
    }

    private static bool IsReadableMarkdown(Document document)
    {
        if (document == null || string.IsNullOrWhiteSpace(document.CurrentPath) || !File.Exists(document.CurrentPath))
        {
            return false;
        }

        var extension = (document.Extension ?? Path.GetExtension(document.CurrentPath) ?? string.Empty).Trim();
        if (MarkdownExtensions.Contains(extension))
        {
            return true;
        }

        return string.Equals(document.DocumentType, "markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsResolvableTarget(string? rawTarget)
    {
        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            return false;
        }

        var target = rawTarget.Trim();
        if (target.StartsWith('#'))
        {
            return false;
        }
        if (target.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }
        if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string MapRelationType(MarkdownLinkType type) => type switch
    {
        MarkdownLinkType.Wikilink => RelationType.Wikilink,
        MarkdownLinkType.FrontmatterDependency => RelationType.FrontmatterDependency,
        _ => RelationType.MarkdownLink,
    };
}
