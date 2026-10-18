using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Graph;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Graph;

public class BacklinkServiceIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly RelationRepository _relationRepo;
    private readonly DocumentRepository _docRepo;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly BacklinkService _service;

    public BacklinkServiceIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_backlink_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _relationRepo = new RelationRepository(_appDb);
        _docRepo = new DocumentRepository(_appDb);
        _workspaceRepo = new WorkspaceRepository(_appDb);
        _service = new BacklinkService(_relationRepo, _docRepo);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) try { File.Delete(_dbPath); } catch { }
        if (File.Exists(_dbPath + "-wal")) try { File.Delete(_dbPath + "-wal"); } catch { }
        if (File.Exists(_dbPath + "-shm")) try { File.Delete(_dbPath + "-shm"); } catch { }
    }

    [Fact]
    public async Task EndToEnd_BacklinksAndInferredSections_SeparateAndDeterministic()
    {
        var ws = new Workspace { Id = "ws-real", Name = "Real WS" };
        await _workspaceRepo.CreateAsync(ws);

        var docA = new Document { Id = "doc-a", Filename = "DocA.md", CurrentPath = @"C:\Docs\DocA.md" };
        var docB = new Document { Id = "doc-b", Filename = "DocB.md", CurrentPath = @"C:\Docs\DocB.md" };
        var docC = new Document { Id = "doc-c", Filename = "DocC.md", CurrentPath = @"C:\Docs\DocC.md" };

        await _docRepo.CreateAsync(docA);
        await _docRepo.CreateAsync(docB);
        await _docRepo.CreateAsync(docC);

        // DocA links to DocB explicitly
        await _relationRepo.AddAsync(new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docB.Id,
            RelationType = RelationType.MarkdownLink,
            RelationOrigin = RelationOrigin.Explicit
        });

        // DocC links to DocB manually
        await _relationRepo.AddAsync(new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docC.Id,
            TargetDocumentId = docB.Id,
            RelationType = RelationType.ManualRelation,
            RelationOrigin = RelationOrigin.Manual
        });

        // DocA and DocB are semantically related (inferred)
        await _relationRepo.AddAsync(new Relation
        {
            WorkspaceId = ws.Id,
            SourceDocumentId = docA.Id,
            TargetDocumentId = docB.Id,
            RelationType = RelationType.SemanticSuggestion,
            RelationOrigin = RelationOrigin.Inferred,
            Confidence = 0.92
        });

        // Query inspector model for DocB
        var relations = await _service.GetInspectorRelationsAsync(ws.Id, docB.Id);

        // Backlinks section (Referenced By): should contain DocA (Explicit) and DocC (Manual)
        Assert.Equal(2, relations.Backlinks.Count);
        Assert.Equal("doc-a", relations.Backlinks[0].SourceDocumentId);
        Assert.Equal("DocA.md", relations.Backlinks[0].SourceDocumentName);
        Assert.Equal("doc-c", relations.Backlinks[1].SourceDocumentId);
        Assert.Equal("DocC.md", relations.Backlinks[1].SourceDocumentName);

        // Inferred section: should contain ONLY inferred relation (Confidence = 0.92)
        Assert.Single(relations.InferredRelations);
        Assert.Equal(RelationOrigin.Inferred, relations.InferredRelations[0].RelationOrigin);
        Assert.Equal(0.92, relations.InferredRelations[0].Confidence);

        // Unresolved: 0 (all resolved)
        Assert.Empty(relations.UnresolvedLinks);
    }
}
