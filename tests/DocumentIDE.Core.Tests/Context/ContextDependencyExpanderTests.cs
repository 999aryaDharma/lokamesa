using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Context;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using Xunit;

namespace DocumentIDE.Core.Tests.Context;

public class ContextDependencyExpanderTests
{
    private class FakeRelationRepository : IRelationRepository
    {
        public List<Relation> Relations { get; } = new();

        public Task AddAsync(Relation relation)
        {
            Relations.Add(relation);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Relation> relations)
        {
            Relations.AddRange(relations);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id) => Task.CompletedTask;
        public Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType) => Task.CompletedTask;
        public Task<Relation?> GetByIdAsync(string id) => Task.FromResult(Relations.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<Relation>> GetBySourceDocumentAsync(string workspaceId, string sourceDocumentId)
        {
            IReadOnlyList<Relation> res = Relations.Where(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId).ToList();
            return Task.FromResult(res);
        }

        public Task<IReadOnlyList<Relation>> GetByTargetDocumentAsync(string workspaceId, string targetDocumentId)
        {
            IReadOnlyList<Relation> res = Relations.Where(r => r.WorkspaceId == workspaceId && r.TargetDocumentId == targetDocumentId).ToList();
            return Task.FromResult(res);
        }

        public Task<IReadOnlyList<Relation>> GetBacklinksAsync(string workspaceId, string targetDocumentId)
        {
            IReadOnlyList<Relation> res = Relations.Where(r => r.WorkspaceId == workspaceId && r.TargetDocumentId == targetDocumentId && r.RelationOrigin != RelationOrigin.Inferred).ToList();
            return Task.FromResult(res);
        }

        public Task<IReadOnlyList<Relation>> GetRelationsAsync(string workspaceId, string? origin = null, string? type = null)
        {
            var query = Relations.Where(r => r.WorkspaceId == workspaceId);
            if (!string.IsNullOrWhiteSpace(origin)) query = query.Where(r => r.RelationOrigin == origin);
            if (!string.IsNullOrWhiteSpace(type)) query = query.Where(r => r.RelationType == type);
            IReadOnlyList<Relation> res = query.ToList();
            return Task.FromResult(res);
        }

        public Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations) => Task.CompletedTask;
    }

    [Fact]
    public async Task CycleTerminates()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";

        // Cycle: A -> B -> A
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docB", RelationOrigin = RelationOrigin.Explicit, RelationType = RelationType.MarkdownLink });
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docB", TargetDocumentId = "docA", RelationOrigin = RelationOrigin.Explicit, RelationType = RelationType.MarkdownLink });

        var expander = new ContextDependencyExpander(repo);
        var options = new ContextExpansionOptions { IncludeExplicitDependencies = true, MaxDepth = 5 };

        var result = await expander.ExpandAsync(ws, new[] { "docA" }, options);

        Assert.NotNull(result);
        Assert.Equal(new[] { "docA", "docB" }, result.ExpandedSourceIds);
        Assert.Single(result.AddedByDependency);
        Assert.Equal("docB", result.AddedByDependency[0].DocumentId);
    }

    [Fact]
    public async Task SemanticEdgeIsNotTraversedByDefault()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";

        // docA has explicit link to docB and inferred/semantic link to docC
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docB", RelationOrigin = RelationOrigin.Explicit, RelationType = RelationType.MarkdownLink });
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docC", RelationOrigin = RelationOrigin.Inferred, RelationType = RelationType.SemanticSuggestion });

        var expander = new ContextDependencyExpander(repo);
        var options = new ContextExpansionOptions { IncludeExplicitDependencies = true, MaxDepth = 1 };

        var result = await expander.ExpandAsync(ws, new[] { "docA" }, options);

        Assert.Equal(new[] { "docA", "docB" }, result.ExpandedSourceIds);
        Assert.DoesNotContain("docC", result.ExpandedSourceIds);
    }

    [Fact]
    public async Task SemanticEdgeTraversedWhenInferredEnabled()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";

        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docC", RelationOrigin = RelationOrigin.Inferred, RelationType = RelationType.SemanticSuggestion });

        var expander = new ContextDependencyExpander(repo);
        var options = new ContextExpansionOptions { IncludeExplicitDependencies = true, IncludeInferredDependencies = true, MaxDepth = 1 };

        var result = await expander.ExpandAsync(ws, new[] { "docA" }, options);

        Assert.Equal(new[] { "docA", "docC" }, result.ExpandedSourceIds);
        Assert.Single(result.AddedByDependency);
        Assert.Equal("docC", result.AddedByDependency[0].DocumentId);
    }

    [Fact]
    public async Task ExpandedSourceListIsReviewable()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";

        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docB", RelationOrigin = RelationOrigin.Explicit, RelationType = RelationType.Wikilink });

        var expander = new ContextDependencyExpander(repo);
        var result = await expander.ExpandAsync(ws, new[] { "docA" }, new ContextExpansionOptions { MaxDepth = 1 });

        Assert.Single(result.AddedByDependency);
        var item = result.AddedByDependency[0];
        Assert.Equal("docB", item.DocumentId);
        Assert.Equal("docA", item.AddedFromDocumentId);
        Assert.Equal(1, item.Depth);
        Assert.Equal(RelationOrigin.Explicit, item.RelationOrigin);
    }

    [Fact]
    public async Task RespectsMaxDepthLimit()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";

        // Chain: A -> B -> C -> D
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docB", RelationOrigin = RelationOrigin.Explicit });
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docB", TargetDocumentId = "docC", RelationOrigin = RelationOrigin.Explicit });
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docC", TargetDocumentId = "docD", RelationOrigin = RelationOrigin.Explicit });

        var expander = new ContextDependencyExpander(repo);

        // MaxDepth = 1 -> only B
        var res1 = await expander.ExpandAsync(ws, new[] { "docA" }, new ContextExpansionOptions { MaxDepth = 1 });
        Assert.Equal(new[] { "docA", "docB" }, res1.ExpandedSourceIds);

        // MaxDepth = 2 -> B and C
        var res2 = await expander.ExpandAsync(ws, new[] { "docA" }, new ContextExpansionOptions { MaxDepth = 2 });
        Assert.Equal(new[] { "docA", "docB", "docC" }, res2.ExpandedSourceIds);
    }

    [Fact]
    public async Task DisabledExpansionReturnsSeedSourcesOnly()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docB", RelationOrigin = RelationOrigin.Explicit });

        var expander = new ContextDependencyExpander(repo);
        var options = new ContextExpansionOptions { IncludeExplicitDependencies = false };

        var result = await expander.ExpandAsync(ws, new[] { "docA" }, options);

        Assert.Equal(new[] { "docA" }, result.ExpandedSourceIds);
        Assert.Empty(result.AddedByDependency);
    }

    [Fact]
    public async Task DeduplicatesAcrossMultipleSeeds()
    {
        var repo = new FakeRelationRepository();
        string ws = "ws-1";

        // docA -> docShared
        // docB -> docShared
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docA", TargetDocumentId = "docShared", RelationOrigin = RelationOrigin.Explicit });
        await repo.AddAsync(new Relation { WorkspaceId = ws, SourceDocumentId = "docB", TargetDocumentId = "docShared", RelationOrigin = RelationOrigin.Explicit });

        var expander = new ContextDependencyExpander(repo);
        var result = await expander.ExpandAsync(ws, new[] { "docA", "docB" }, new ContextExpansionOptions { MaxDepth = 1 });

        Assert.Equal(new[] { "docA", "docB", "docShared" }, result.ExpandedSourceIds);
        Assert.Single(result.AddedByDependency);
        Assert.Equal("docShared", result.AddedByDependency[0].DocumentId);
    }
}
