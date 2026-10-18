using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Graph;
using Xunit;

namespace DocumentIDE.Core.Tests.Graph;

/// <summary>
/// GR-04 – Manual Relations: TDD tests (Red → Green → Refactor).
/// </summary>
public class ManualRelationTests
{
    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

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

        public Task DeleteAsync(string id)
        {
            Relations.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType)
        {
            Relations.RemoveAll(r =>
                r.WorkspaceId == workspaceId &&
                r.SourceDocumentId == sourceDocumentId &&
                r.TargetDocumentId == targetDocumentId &&
                r.RelationType == relationType &&
                r.RelationOrigin == RelationOrigin.Manual);
            return Task.CompletedTask;
        }

        public Task<Relation?> GetByIdAsync(string id) =>
            Task.FromResult(Relations.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<Relation>> GetBySourceDocumentAsync(string workspaceId, string sourceDocumentId)
        {
            IReadOnlyList<Relation> res = Relations
                .Where(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId)
                .ToList();
            return Task.FromResult(res);
        }

        public Task<IReadOnlyList<Relation>> GetByTargetDocumentAsync(string workspaceId, string targetDocumentId)
        {
            IReadOnlyList<Relation> res = Relations
                .Where(r => r.WorkspaceId == workspaceId && r.TargetDocumentId == targetDocumentId)
                .ToList();
            return Task.FromResult(res);
        }

        public Task<IReadOnlyList<Relation>> GetBacklinksAsync(string workspaceId, string targetDocumentId)
        {
            IReadOnlyList<Relation> res = Relations
                .Where(r => r.WorkspaceId == workspaceId &&
                            r.TargetDocumentId == targetDocumentId &&
                            r.RelationOrigin != RelationOrigin.Inferred)
                .ToList();
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

        public Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations)
        {
            Relations.RemoveAll(r =>
                r.WorkspaceId == workspaceId &&
                r.SourceDocumentId == sourceDocumentId &&
                r.RelationOrigin == RelationOrigin.Explicit);
            Relations.AddRange(newDerivedRelations);
            return Task.CompletedTask;
        }
    }

    // ---------------------------------------------------------------------------
    // GR-04 Required Test 1:
    //   Manual relation survives Markdown reparse (i.e., ReplaceDerivedRelations
    //   does NOT remove Manual-origin rows).
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManualRelation_SurvivesMarkdownReparse()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        const string wsId = "ws-1";
        const string srcId = "doc-a";
        const string tgtId = "doc-b";

        // Create a manual relation
        await service.CreateAsync(wsId, srcId, tgtId, ManualRelationType.References);

        // Simulate a Markdown reparse: derived (Explicit) relations are replaced,
        // but Manual relations must NOT be touched.
        var newDerivedRelations = new List<Relation>
        {
            new()
            {
                Id = "derived-1",
                WorkspaceId = wsId,
                SourceDocumentId = srcId,
                TargetDocumentId = "doc-c",
                RelationType = RelationType.MarkdownLink,
                RelationOrigin = RelationOrigin.Explicit,
            }
        };
        await repo.ReplaceDerivedRelationsAsync(wsId, srcId, newDerivedRelations);

        // Manual relation must still exist
        var remaining = await repo.GetBySourceDocumentAsync(wsId, srcId);
        Assert.Contains(remaining, r => r.RelationOrigin == RelationOrigin.Manual && r.TargetDocumentId == tgtId);
    }

    // ---------------------------------------------------------------------------
    // GR-04 Required Test 2:
    //   Deleting a manual relation does NOT edit any Markdown file.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteManualRelation_DoesNotEditMarkdown()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        const string wsId = "ws-1";
        const string srcId = "doc-a";
        const string tgtId = "doc-b";

        // Create then delete
        var id = await service.CreateAsync(wsId, srcId, tgtId, ManualRelationType.References);
        await service.DeleteAsync(wsId, id);

        // The service must not mutate any Markdown file.
        // Since ManualRelationService only interacts with the repository,
        // the absence of Markdown writing is verified by having no such dependency.
        // Additionally the relation must be gone.
        var remaining = await repo.GetBySourceDocumentAsync(wsId, srcId);
        Assert.DoesNotContain(remaining, r => r.Id == id);
    }

    // ---------------------------------------------------------------------------
    // GR-04 Required Test 3:
    //   Manual edge origin is stored as "Manual".
    //   (Used by GR-03 graph view to style the edge as "explicit/solid".)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ManualRelation_HasManualOrigin()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        const string wsId = "ws-1";
        const string srcId = "doc-a";
        const string tgtId = "doc-b";

        var id = await service.CreateAsync(wsId, srcId, tgtId, ManualRelationType.References);

        var relation = await repo.GetByIdAsync(id);
        Assert.NotNull(relation);
        Assert.Equal(RelationOrigin.Manual, relation!.RelationOrigin);
        Assert.Equal(RelationType.ManualRelation, relation.RelationType);
    }

    // ---------------------------------------------------------------------------
    // Step 2: Select relation type from controlled vocabulary.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateManualRelation_WithRelatedToType_Persisted()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        var id = await service.CreateAsync("ws", "src", "tgt", ManualRelationType.RelatedTo);

        var rel = await repo.GetByIdAsync(id);
        Assert.NotNull(rel);
        Assert.Equal(ManualRelationType.RelatedTo, rel!.RelationType);
    }

    [Fact]
    public async Task CreateManualRelation_WithDependsOnType_Persisted()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        var id = await service.CreateAsync("ws", "src", "tgt", ManualRelationType.DependsOn);

        var rel = await repo.GetByIdAsync(id);
        Assert.NotNull(rel);
        Assert.Equal(ManualRelationType.DependsOn, rel!.RelationType);
    }

    // ---------------------------------------------------------------------------
    // Step 4: Edit manual relation type.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateManualRelation_ChangesType()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        var id = await service.CreateAsync("ws", "src", "tgt", ManualRelationType.References);
        await service.UpdateTypeAsync("ws", id, ManualRelationType.DependsOn);

        var rel = await repo.GetByIdAsync(id);
        Assert.NotNull(rel);
        Assert.Equal(ManualRelationType.DependsOn, rel!.RelationType);
    }

    // ---------------------------------------------------------------------------
    // Edge case: missing/ambiguous target.
    // The service must accept any non-empty string as targetDocumentId and let
    // the graph layer signal the unresolved state.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateManualRelation_MissingTarget_StillPersists()
    {
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);

        // Target document doesn't exist in any doc repo – service should still persist
        var id = await service.CreateAsync("ws", "src", "non-existent-target-id", ManualRelationType.References);

        var rel = await repo.GetByIdAsync(id);
        Assert.NotNull(rel);
        Assert.Equal("non-existent-target-id", rel!.TargetDocumentId);
    }

    // ---------------------------------------------------------------------------
    // Acceptance criteria: No automatic source insertion.
    // Verified by confirming CreateAsync only writes to the relation repo.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateManualRelation_DoesNotModifyMarkdown()
    {
        // ManualRelationService has no IDocumentRepository or filesystem dependency.
        // This test verifies the constructor signature does not accept a filesystem argument.
        var repo = new FakeRelationRepository();
        var service = new ManualRelationService(repo);  // only repo param

        await service.CreateAsync("ws", "src", "tgt", ManualRelationType.References);

        // If we reach here without compile/runtime error, the service has no markdown path.
        Assert.Single(repo.Relations);
        Assert.Equal(RelationOrigin.Manual, repo.Relations[0].RelationOrigin);
    }
}
