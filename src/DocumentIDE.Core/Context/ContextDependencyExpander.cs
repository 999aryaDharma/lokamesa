using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Context;

/// <summary>
/// Options controlling explicit dependency expansion for context packs.
/// </summary>
public class ContextExpansionOptions
{
    /// <summary>
    /// Gets or sets whether to expand explicit backlinks and dependencies. Default is true.
    /// </summary>
    public bool IncludeExplicitDependencies { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include inferred/semantic dependencies. Default is false (semantic edges are not traversed by default).
    /// </summary>
    public bool IncludeInferredDependencies { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum depth to traverse dependencies. Default is 1.
    /// </summary>
    public int MaxDepth { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether to traverse outbound references (documents linked by seed sources). Default is true.
    /// </summary>
    public bool TraverseOutbound { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to traverse inbound backlinks (documents that link to seed sources). Default is true.
    /// </summary>
    public bool TraverseInbound { get; set; } = true;

    /// <summary>
    /// Optional set of relation origins to traverse. If null or empty, defaults to Explicit and Manual (and Inferred if IncludeInferredDependencies is true).
    /// </summary>
    public HashSet<string>? AllowedOrigins { get; set; }
}

/// <summary>
/// Details of a document added to the context pack by dependency expansion.
/// </summary>
public class ContextExpansionItem
{
    /// <summary>
    /// Identifier of the document added by dependency.
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the parent document from which this dependency was discovered.
    /// </summary>
    public string AddedFromDocumentId { get; set; } = string.Empty;

    /// <summary>
    /// The hop distance/depth from the seed document at which this dependency was found.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// The origin of the relation (e.g., Explicit, Manual, Inferred).
    /// </summary>
    public string RelationOrigin { get; set; } = string.Empty;

    /// <summary>
    /// The type of relation (e.g., MarkdownLink, Wikilink, FrontmatterDependency).
    /// </summary>
    public string RelationType { get; set; } = string.Empty;
}

/// <summary>
/// Result of expanding dependencies for a set of seed source documents.
/// </summary>
public class ContextExpansionResult
{
    /// <summary>
    /// The initial user-selected seed source document identifiers.
    /// </summary>
    public IReadOnlyList<string> SeedSourceIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The complete ordered and deduplicated list of source identifiers (seeds + added dependencies).
    /// </summary>
    public IReadOnlyList<string> ExpandedSourceIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Detailed list of documents added specifically via dependency traversal, reviewable before export.
    /// </summary>
    public IReadOnlyList<ContextExpansionItem> AddedByDependency { get; set; } = Array.Empty<ContextExpansionItem>();
}

/// <summary>
/// Service responsible for bounded, deterministic expansion of context packs using explicit dependencies and backlinks.
/// Implements CTX-02 sprint specification.
/// </summary>
public class ContextDependencyExpander
{
    private readonly IRelationRepository? _relationRepository;

    public ContextDependencyExpander(IRelationRepository? relationRepository = null)
    {
        _relationRepository = relationRepository;
    }

    /// <summary>
    /// Expands seed source document IDs using relations in the graph while enforcing depth bounds, cycle elimination, and deduplication.
    /// </summary>
    public async Task<ContextExpansionResult> ExpandAsync(
        string workspaceId,
        IEnumerable<string> seedSourceIds,
        ContextExpansionOptions? options = null,
        Func<string, Task<IEnumerable<Relation>>>? customRelationProvider = null)
    {
        if (seedSourceIds == null) throw new ArgumentNullException(nameof(seedSourceIds));

        options ??= new ContextExpansionOptions();

        var seeds = seedSourceIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        var expandedIds = new List<string>(seeds);
        var addedItems = new List<ContextExpansionItem>();

        if (!options.IncludeExplicitDependencies || seeds.Count == 0 || options.MaxDepth <= 0)
        {
            return new ContextExpansionResult
            {
                SeedSourceIds = seeds,
                ExpandedSourceIds = expandedIds,
                AddedByDependency = addedItems
            };
        }

        var visited = new HashSet<string>(seeds);
        var queue = new Queue<(string DocumentId, int Depth)>();
        foreach (var seed in seeds)
        {
            queue.Enqueue((seed, 0));
        }

        while (queue.Count > 0)
        {
            var (currentDocId, currentDepth) = queue.Dequeue();

            if (currentDepth >= options.MaxDepth)
            {
                continue;
            }

            IEnumerable<Relation> relations;
            if (customRelationProvider != null)
            {
                relations = await customRelationProvider(currentDocId);
            }
            else if (_relationRepository != null)
            {
                var list = new List<Relation>();
                if (options.TraverseOutbound)
                {
                    var outbound = await _relationRepository.GetBySourceDocumentAsync(workspaceId, currentDocId);
                    if (outbound != null) list.AddRange(outbound);
                }
                if (options.TraverseInbound)
                {
                    var inbound = await _relationRepository.GetByTargetDocumentAsync(workspaceId, currentDocId);
                    if (inbound != null) list.AddRange(inbound);
                }
                relations = list;
            }
            else
            {
                relations = Array.Empty<Relation>();
            }

            foreach (var rel in relations)
            {
                if (rel == null) continue;

                bool isAllowedOrigin = options.AllowedOrigins != null && options.AllowedOrigins.Count > 0
                    ? options.AllowedOrigins.Contains(rel.RelationOrigin)
                    : IsAllowedOriginByDefault(rel, options.IncludeInferredDependencies);

                if (!isAllowedOrigin) continue;

                string neighborId = string.Equals(rel.SourceDocumentId, currentDocId, StringComparison.Ordinal)
                    ? rel.TargetDocumentId
                    : rel.SourceDocumentId;

                if (string.IsNullOrWhiteSpace(neighborId)) continue;

                if (!visited.Contains(neighborId))
                {
                    visited.Add(neighborId);
                    expandedIds.Add(neighborId);

                    var item = new ContextExpansionItem
                    {
                        DocumentId = neighborId,
                        AddedFromDocumentId = currentDocId,
                        Depth = currentDepth + 1,
                        RelationOrigin = rel.RelationOrigin,
                        RelationType = rel.RelationType
                    };
                    addedItems.Add(item);

                    queue.Enqueue((neighborId, currentDepth + 1));
                }
            }
        }

        return new ContextExpansionResult
        {
            SeedSourceIds = seeds,
            ExpandedSourceIds = expandedIds,
            AddedByDependency = addedItems
        };
    }

    private static bool IsAllowedOriginByDefault(Relation rel, bool includeInferred)
    {
        if (rel.RelationOrigin == RelationOrigin.Inferred || rel.RelationType == RelationType.SemanticSuggestion)
        {
            return includeInferred;
        }
        return true;
    }
}
