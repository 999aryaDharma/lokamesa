using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Graph;
using Xunit;

namespace DocumentIDE.Core.Tests.Graph;

public class BacklinkServiceTests
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

        public Task DeleteAsync(string id)
        {
            Relations.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType)
        {
            Relations.RemoveAll(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId && r.TargetDocumentId == targetDocumentId && r.RelationType == relationType && r.RelationOrigin == RelationOrigin.Manual);
            return Task.CompletedTask;
        }

        public Task<Relation?> GetByIdAsync(string id)
        {
            return Task.FromResult(Relations.FirstOrDefault(r => r.Id == id));
        }

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

        public Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations)
        {
            Relations.RemoveAll(r => r.WorkspaceId == workspaceId && r.SourceDocumentId == sourceDocumentId && r.RelationOrigin == RelationOrigin.Explicit);
            Relations.AddRange(newDerivedRelations);
            return Task.CompletedTask;
        }
    }

    private class FakeDocumentRepository : IDocumentRepository
    {
        public List<Document> Documents { get; } = new();

        public Task CreateAsync(Document document)
        {
            Documents.Add(document);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            Documents.RemoveAll(d => d.Id == id);
            return Task.CompletedTask;
        }

        public Task<Document?> GetByIdAsync(string id)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));
        }

        public Task<Document?> GetByPathAsync(string currentPath)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => string.Equals(d.CurrentPath, currentPath, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId)
        {
            return Task.FromResult(Documents.FirstOrDefault(d => d.FilesystemVolumeId == volumeId && d.FilesystemObjectId == objectId));
        }

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId)
        {
            return Task.FromResult<IReadOnlyList<Document>>(Documents);
        }

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath)
        {
            return Task.FromResult<IReadOnlyList<Document>>(Documents);
        }

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint)
        {
            return Task.FromResult(Documents.Where(d => d.ContentFingerprint == fingerprint).ToList());
        }

        public Task UpdateAsync(Document document)
        {
            var idx = Documents.FindIndex(d => d.Id == document.Id);
            if (idx >= 0) Documents[idx] = document;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BacklinksUpdateAfterSave()
    {
        var relationRepo = new FakeRelationRepository();
        var docRepo = new FakeDocumentRepository();
        var service = new BacklinkService(relationRepo, docRepo);

        string wsId = "ws-1";
        var docTarget = new Document { Id = "target-1", Filename = "Target.md", CurrentPath = "/ws/Target.md" };
        var docSourceA = new Document { Id = "source-a", Filename = "SourceA.md", CurrentPath = "/ws/SourceA.md" };
        var docSourceB = new Document { Id = "source-b", Filename = "SourceB.md", CurrentPath = "/ws/SourceB.md" };

        await docRepo.CreateAsync(docTarget);
        await docRepo.CreateAsync(docSourceA);
        await docRepo.CreateAsync(docSourceB);

        // Initial state: SourceA links to Target
        await relationRepo.AddAsync(new Relation
        {
            Id = "rel-1",
            WorkspaceId = wsId,
            SourceDocumentId = docSourceA.Id,
            TargetDocumentId = docTarget.Id,
            RelationType = RelationType.MarkdownLink,
            RelationOrigin = RelationOrigin.Explicit
        });

        var initialModel = await service.GetInspectorRelationsAsync(wsId, docTarget.Id);
        Assert.Single(initialModel.Backlinks);
        Assert.Equal("source-a", initialModel.Backlinks[0].SourceDocumentId);

        // Save docSourceB: adding a link to docTarget
        var updatedRelationsForSourceB = new List<Relation>
        {
            new Relation
            {
                Id = "rel-2",
                WorkspaceId = wsId,
                SourceDocumentId = docSourceB.Id,
                TargetDocumentId = docTarget.Id,
                RelationType = RelationType.Wikilink,
                RelationOrigin = RelationOrigin.Explicit
            }
        };
        await relationRepo.ReplaceDerivedRelationsAsync(wsId, docSourceB.Id, updatedRelationsForSourceB);

        var updatedModel = await service.GetInspectorRelationsAsync(wsId, docTarget.Id);
        Assert.Equal(2, updatedModel.Backlinks.Count);
        Assert.Contains(updatedModel.Backlinks, b => b.SourceDocumentId == "source-a");
        Assert.Contains(updatedModel.Backlinks, b => b.SourceDocumentId == "source-b");
    }

    [Fact]
    public async Task ExplicitAndInferredSectionsAreSeparate()
    {
        var relationRepo = new FakeRelationRepository();
        var docRepo = new FakeDocumentRepository();
        var service = new BacklinkService(relationRepo, docRepo);

        string wsId = "ws-1";
        var docTarget = new Document { Id = "target-1", Filename = "Target.md", CurrentPath = "/ws/Target.md" };
        var docSourceExp = new Document { Id = "source-exp", Filename = "Explicit.md", CurrentPath = "/ws/Explicit.md" };
        var docSourceInf = new Document { Id = "source-inf", Filename = "Inferred.md", CurrentPath = "/ws/Inferred.md" };

        await docRepo.CreateAsync(docTarget);
        await docRepo.CreateAsync(docSourceExp);
        await docRepo.CreateAsync(docSourceInf);

        // Explicit link
        await relationRepo.AddAsync(new Relation
        {
            Id = "rel-exp",
            WorkspaceId = wsId,
            SourceDocumentId = docSourceExp.Id,
            TargetDocumentId = docTarget.Id,
            RelationType = RelationType.MarkdownLink,
            RelationOrigin = RelationOrigin.Explicit
        });

        // Inferred relation
        await relationRepo.AddAsync(new Relation
        {
            Id = "rel-inf",
            WorkspaceId = wsId,
            SourceDocumentId = docSourceInf.Id,
            TargetDocumentId = docTarget.Id,
            RelationType = RelationType.SemanticSuggestion,
            RelationOrigin = RelationOrigin.Inferred,
            Confidence = 0.88
        });

        var model = await service.GetInspectorRelationsAsync(wsId, docTarget.Id);

        // Backlinks section must ONLY contain explicit/manual inbound relations
        Assert.Single(model.Backlinks);
        Assert.Equal("source-exp", model.Backlinks[0].SourceDocumentId);
        Assert.Equal(RelationOrigin.Explicit, model.Backlinks[0].RelationOrigin);

        // InferredRelations section must ONLY contain inferred relations
        Assert.Single(model.InferredRelations);
        Assert.Equal("source-inf", model.InferredRelations[0].SourceDocumentId);
        Assert.Equal(RelationOrigin.Inferred, model.InferredRelations[0].RelationOrigin);
        Assert.Equal(0.88, model.InferredRelations[0].Confidence);
    }

    [Fact]
    public async Task MissingTargetIsShownWithoutCrash()
    {
        var relationRepo = new FakeRelationRepository();
        var docRepo = new FakeDocumentRepository();
        var service = new BacklinkService(relationRepo, docRepo);

        string wsId = "ws-1";
        var docSource = new Document { Id = "source-1", Filename = "Source.md", CurrentPath = "/ws/Source.md" };
        await docRepo.CreateAsync(docSource);

        // Relation points to "missing-target-id" which does not exist in DocumentRepo
        await relationRepo.AddAsync(new Relation
        {
            Id = "rel-missing",
            WorkspaceId = wsId,
            SourceDocumentId = docSource.Id,
            TargetDocumentId = "missing-target-id",
            RelationType = RelationType.Wikilink,
            RelationOrigin = RelationOrigin.Explicit,
            MetadataJson = "{\"rawTarget\":\"non-existent-file.md\"}"
        });

        var model = await service.GetInspectorRelationsAsync(wsId, docSource.Id);

        // Should return 1 reference item with IsResolved = false and TargetDocumentName set to rawTarget without crashing
        Assert.Single(model.References);
        var item = model.References[0];
        Assert.False(item.IsResolved);
        Assert.Equal("non-existent-file.md", item.TargetDocumentName);
        Assert.Null(item.TargetDocumentPath);

        // UnresolvedLinks section should also contain this item
        Assert.Single(model.UnresolvedLinks);
        Assert.Equal("rel-missing", model.UnresolvedLinks[0].RelationId);
    }
}
