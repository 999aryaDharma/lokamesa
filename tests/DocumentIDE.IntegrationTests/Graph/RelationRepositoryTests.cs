using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Graph;

public class RelationRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly RelationRepository _relationRepo;
    private readonly DocumentRepository _docRepo;
    private readonly WorkspaceRepository _workspaceRepo;

    public RelationRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_relation_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _relationRepo = new RelationRepository(_appDb);
        _docRepo = new DocumentRepository(_appDb);
        _workspaceRepo = new WorkspaceRepository(_appDb);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
        {
            try { File.Delete(walPath); } catch { }
        }
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
        {
            try { File.Delete(shmPath); } catch { }
        }
    }

    private async Task<(Workspace ws, Document docA, Document docB, Document docC)> CreateTestWorkspaceAndDocumentsAsync()
    {
        var ws = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Workspace",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        await _workspaceRepo.CreateAsync(ws);

        var docA = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\DocA.md",
            Filename = "DocA.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        var docB = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\DocB.md",
            Filename = "DocB.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        var docC = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\DocC.md",
            Filename = "DocC.md",
            Extension = ".md",
            DocumentType = "markdown"
        };

        await _docRepo.CreateAsync(docA);
        await _docRepo.CreateAsync(docB);
        await _docRepo.CreateAsync(docC);

        return (ws, docA, docB, docC);
    }

    [Fact]
    public async Task ReparseUpdatesDerivedLinks()
    {
        var (ws, docA, docB, docC) = await CreateTestWorkspaceAndDocumentsAsync();
        var docD = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\Docs\DocD.md",
            Filename = "DocD.md",
            Extension = ".md",
            DocumentType = "markdown"
        };
        await _docRepo.CreateAsync(docD);

        // Initial parse of DocA yields explicit derived links to DocB and DocC
        var initialDerived = new List<Relation>
        {
            new Relation
            {
                WorkspaceId = ws.Id,
                SourceDocumentId = docA.Id,
                TargetDocumentId = docB.Id,
                RelationType = RelationType.MarkdownLink,
                RelationOrigin = RelationOrigin.Explicit,
                Confidence = 1.0
            },
            new Relation
            {
                WorkspaceId = ws.Id,
                SourceDocumentId = docA.Id,
                TargetDocumentId = docC.Id,
                RelationType = RelationType.Wikilink,
                RelationOrigin = RelationOrigin.Explicit,
                Confidence = 1.0
            }
        };
        await _relationRepo.AddRangeAsync(initialDerived);

        var initialRelations = await _relationRepo.GetBySourceDocumentAsync(ws.Id, docA.Id);
        Assert.Equal(2, initialRelations.Count);

        // Reparse of DocA yields updated links to DocB and DocD (DocC was removed in DocA content)
        var updatedDerived = new List<Relation>
        {
            new Relation
            {
                WorkspaceId = ws.Id,
                SourceDocumentId = docA.Id,
                TargetDocumentId = docB.Id,
                RelationType = RelationType.MarkdownLink,
                RelationOrigin = RelationOrigin.Explicit,
                Confidence = 1.0
            },
            new Relation
            {
                WorkspaceId = ws.Id,
                SourceDocumentId = docA.Id,
                TargetDocumentId = docD.Id,
                RelationType = RelationType.FrontmatterDependency,
                RelationOrigin = RelationOrigin.Explicit,
                Confidence = 1.0
            }
        };

        await _relationRepo.ReplaceDerivedRelationsAsync(ws.Id, docA.Id, updatedDerived);

        var afterReparse = await _relationRepo.GetBySourceDocumentAsync(ws.Id, docA.Id);
        Assert.Equal(2, afterReparse.Count);
        Assert.Contains(afterReparse, r => r.TargetDocumentId == docB.Id && r.RelationType == RelationType.MarkdownLink);
        Assert.Contains(afterReparse, r => r.TargetDocumentId == docD.Id && r.RelationType == RelationType.FrontmatterDependency);
        Assert.DoesNotContain(afterReparse, r => r.TargetDocumentId == docC.Id);
    }

    [Fact]
    public async Task ManualRelationSurvivesParserRefresh()
    {
        var (ws, docA, docB, docC) = await CreateTestWorkspaceAndDocumentsAsync();

        // Add explicit derived link A -> B
        var derivedLink = new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docB.Id,
            RelationType = RelationType.MarkdownLink,
            RelationOrigin = RelationOrigin.Explicit,
            Confidence = 1.0
        };
        await _relationRepo.AddAsync(derivedLink);

        // Add manual relation A -> C
        var manualRel = new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docC.Id,
            RelationType = RelationType.ManualRelation,
            RelationOrigin = RelationOrigin.Manual,
            Confidence = 1.0,
            MetadataJson = "{\"userNote\":\"Manual relation added by user\"}"
        };
        await _relationRepo.AddAsync(manualRel);

        // Reparse DocA: derived links updated to point nowhere (e.g. all links removed from DocA)
        var newDerived = Array.Empty<Relation>();
        await _relationRepo.ReplaceDerivedRelationsAsync(ws.Id, docA.Id, newDerived);

        // Verify manual relation A -> C survives parser refresh completely intact
        var relations = await _relationRepo.GetBySourceDocumentAsync(ws.Id, docA.Id);
        Assert.Single(relations);
        var remaining = relations[0];
        Assert.Equal(manualRel.Id, remaining.Id);
        Assert.Equal(RelationOrigin.Manual, remaining.RelationOrigin);
        Assert.Equal(RelationType.ManualRelation, remaining.RelationType);
        Assert.Equal(docC.Id, remaining.TargetDocumentId);
    }

    [Fact]
    public async Task OriginAndTypeRemainDistinct_SemanticCannotMasqueradeAsExplicit()
    {
        var (ws, docA, docB, docC) = await CreateTestWorkspaceAndDocumentsAsync();

        // 1. Explicit link A -> B
        var explicitRel = new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docB.Id,
            RelationType = RelationType.Wikilink,
            RelationOrigin = RelationOrigin.Explicit,
            Confidence = 1.0
        };
        await _relationRepo.AddAsync(explicitRel);

        // 2. Semantic / Inferred link A -> C
        var semanticRel = new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docC.Id,
            RelationType = RelationType.SemanticSuggestion,
            RelationOrigin = RelationOrigin.Inferred,
            Confidence = 0.85,
            MetadataJson = "{\"embeddingSimilarity\":0.85}"
        };
        await _relationRepo.AddAsync(semanticRel);

        // Query explicit relations for workspace
        var explicitOnly = await _relationRepo.GetRelationsAsync(ws.Id, origin: RelationOrigin.Explicit);
        Assert.Single(explicitOnly);
        Assert.Equal(docB.Id, explicitOnly[0].TargetDocumentId);
        Assert.Equal(RelationOrigin.Explicit, explicitOnly[0].RelationOrigin);

        // Query inferred relations for workspace
        var inferredOnly = await _relationRepo.GetRelationsAsync(ws.Id, origin: RelationOrigin.Inferred);
        Assert.Single(inferredOnly);
        Assert.Equal(docC.Id, inferredOnly[0].TargetDocumentId);
        Assert.Equal(RelationOrigin.Inferred, inferredOnly[0].RelationOrigin);

        // Query explicit backlinks for DocC - must be empty!
        var backlinksDocC = await _relationRepo.GetBacklinksAsync(ws.Id, docC.Id);
        Assert.Empty(backlinksDocC);

        // Query explicit backlinks for DocB - returns explicit link
        var backlinksDocB = await _relationRepo.GetBacklinksAsync(ws.Id, docB.Id);
        Assert.Single(backlinksDocB);
        Assert.Equal(docA.Id, backlinksDocB[0].SourceDocumentId);
    }

    [Fact]
    public async Task Relations_SurviveDatabaseRestart()
    {
        var (ws, docA, docB, _) = await CreateTestWorkspaceAndDocumentsAsync();

        var rel = new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docB.Id,
            RelationType = RelationType.MarkdownLink,
            RelationOrigin = RelationOrigin.Explicit,
            Confidence = 1.0,
            MetadataJson = "{\"line\":42}"
        };
        await _relationRepo.AddAsync(rel);

        SqliteConnection.ClearAllPools();
        var restartDb = new AppDb(_dbPath);
        await restartDb.InitializeAsync();
        var restartRepo = new RelationRepository(restartDb);

        var fetched = await restartRepo.GetByIdAsync(rel.Id);
        Assert.NotNull(fetched);
        Assert.Equal(ws.Id, fetched!.WorkspaceId);
        Assert.Equal(docA.Id, fetched.SourceDocumentId);
        Assert.Equal(docB.Id, fetched.TargetDocumentId);
        Assert.Equal(RelationType.MarkdownLink, fetched.RelationType);
        Assert.Equal(RelationOrigin.Explicit, fetched.RelationOrigin);
        Assert.Equal(1.0, fetched.Confidence);
        Assert.Equal("{\"line\":42}", fetched.MetadataJson);
    }
}
