using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Batch;

public class BatchPlanRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly BatchPlanRepository _repository;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly DocumentRepository _documentRepository;

    public BatchPlanRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"batch_plan_test_{Guid.NewGuid()}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _repository = new BatchPlanRepository(_appDb);
        _workspaceRepository = new WorkspaceRepository(_appDb);
        _documentRepository = new DocumentRepository(_appDb);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private async Task<(Workspace workspace, Document document1, Document document2)> SeedWorkspaceAndDocumentsAsync()
    {
        var ws = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Workspace",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        await _workspaceRepository.CreateAsync(ws);

        var doc1 = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = "C:\\test\\doc1.docx",
            Filename = "doc1.docx",
            Extension = ".docx",
            DocumentType = "docx",
            ContentFingerprint = "fingerprint-1",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        await _documentRepository.CreateAsync(doc1);

        var doc2 = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = "C:\\test\\doc2.docx",
            Filename = "doc2.docx",
            Extension = ".docx",
            DocumentType = "docx",
            ContentFingerprint = "fingerprint-2",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        await _documentRepository.CreateAsync(doc2);

        return (ws, doc1, doc2);
    }

    [Fact]
    public async Task SavePlanAsync_And_GetByIdAsync_PersistsPlanAndFilesWithFingerprintsAndChangeIds()
    {
        var (ws, doc1, doc2) = await SeedWorkspaceAndDocumentsAsync();
        var planId = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddMinutes(45);

        var plan = new BatchOperationPlan
        {
            Id = planId,
            WorkspaceId = ws.Id,
            OperationType = BatchOperationType.SearchAndReplace,
            Status = BatchPlanStatus.Previewed,
            ScopeJson = "{\"type\":\"Folder\"}",
            ParametersJson = "{\"find\":\"Foo\",\"replace\":\"Bar\"}",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            IdempotencyKey = "key-abc-123",
            Files = new List<BatchOperationPlanFile>
            {
                new BatchOperationPlanFile
                {
                    PlanId = planId,
                    DocumentId = doc1.Id,
                    ExpectedFingerprint = "sha256-hash-11111111111111111111111111111111",
                    MatchCount = 5,
                    Selected = true,
                    SelectedChangeIds = new List<string> { "match-1", "match-2" },
                    PlanPayloadJson = "{\"fileProp\":\"val1\"}"
                },
                new BatchOperationPlanFile
                {
                    PlanId = planId,
                    DocumentId = doc2.Id,
                    ExpectedFingerprint = "sha256-hash-22222222222222222222222222222222",
                    MatchCount = 2,
                    Selected = false,
                    SelectedChangeIds = new List<string> { "match-3" },
                    PlanPayloadJson = null
                }
            }
        };

        // Act
        await _repository.SavePlanAsync(plan);

        // Read back
        var fetched = await _repository.GetByIdAsync(planId);

        // Assert
        Assert.NotNull(fetched);
        Assert.Equal(planId, fetched!.Id);
        Assert.Equal(ws.Id, fetched.WorkspaceId);
        Assert.Equal(BatchOperationType.SearchAndReplace, fetched.OperationType);
        Assert.Equal(BatchPlanStatus.Previewed, fetched.Status);
        Assert.Equal("{\"type\":\"Folder\"}", fetched.ScopeJson);
        Assert.Equal("{\"find\":\"Foo\",\"replace\":\"Bar\"}", fetched.ParametersJson);
        Assert.Equal("key-abc-123", fetched.IdempotencyKey);
        Assert.Equal(2, fetched.Files.Count);

        var file1 = fetched.Files.Find(f => f.DocumentId == doc1.Id);
        Assert.NotNull(file1);
        Assert.Equal("sha256-hash-11111111111111111111111111111111", file1!.ExpectedFingerprint);
        Assert.Equal(5, file1.MatchCount);
        Assert.True(file1.Selected);
        Assert.Equal(new[] { "match-1", "match-2" }, file1.SelectedChangeIds);
        Assert.Equal("{\"fileProp\":\"val1\"}", file1.PlanPayloadJson);

        var file2 = fetched.Files.Find(f => f.DocumentId == doc2.Id);
        Assert.NotNull(file2);
        Assert.Equal("sha256-hash-22222222222222222222222222222222", file2!.ExpectedFingerprint);
        Assert.False(file2.Selected);
    }

    [Fact]
    public async Task GetByIdempotencyKeyAsync_ReturnsPlan_WhenKeyMatches()
    {
        var (ws, doc1, _) = await SeedWorkspaceAndDocumentsAsync();
        var key = "unique-idempotency-key-" + Guid.NewGuid();

        var plan = new BatchOperationPlan
        {
            WorkspaceId = ws.Id,
            OperationType = BatchOperationType.Rename,
            Status = BatchPlanStatus.Previewed,
            IdempotencyKey = key,
            Files = new List<BatchOperationPlanFile>
            {
                new BatchOperationPlanFile
                {
                    DocumentId = doc1.Id,
                    ExpectedFingerprint = "fingerprint-xyz",
                    SelectedChangeIds = new List<string> { "rename-1" }
                }
            }
        };

        await _repository.SavePlanAsync(plan);

        var fetched = await _repository.GetByIdempotencyKeyAsync(ws.Id, key);
        Assert.NotNull(fetched);
        Assert.Equal(plan.Id, fetched!.Id);
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesStatusAndEnforcesCompletedPlanCannotApplyTwice()
    {
        var (ws, doc1, _) = await SeedWorkspaceAndDocumentsAsync();
        var plan = new BatchOperationPlan
        {
            WorkspaceId = ws.Id,
            OperationType = BatchOperationType.FontChange,
            Status = BatchPlanStatus.Previewed,
            Files = new List<BatchOperationPlanFile>
            {
                new BatchOperationPlanFile
                {
                    DocumentId = doc1.Id,
                    ExpectedFingerprint = "fingerprint-123"
                }
            }
        };
        await _repository.SavePlanAsync(plan);

        // Update status to Completed
        await _repository.UpdateStatusAsync(plan.Id, BatchPlanStatus.Completed);

        var updated = await _repository.GetByIdAsync(plan.Id);
        Assert.NotNull(updated);
        Assert.Equal(BatchPlanStatus.Completed, updated!.Status);

        // Verify completed plan cannot apply twice
        Assert.False(updated.CanApply(DateTimeOffset.UtcNow, out var reason));
        Assert.Contains("completed", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteAsync_RemovesPlanAndAssociatedFiles()
    {
        var (ws, doc1, _) = await SeedWorkspaceAndDocumentsAsync();
        var plan = new BatchOperationPlan
        {
            WorkspaceId = ws.Id,
            OperationType = BatchOperationType.PageSetup,
            Status = BatchPlanStatus.Draft,
            Files = new List<BatchOperationPlanFile>
            {
                new BatchOperationPlanFile
                {
                    DocumentId = doc1.Id,
                    ExpectedFingerprint = "fp"
                }
            }
        };
        await _repository.SavePlanAsync(plan);

        await _repository.DeleteAsync(plan.Id);

        var deleted = await _repository.GetByIdAsync(plan.Id);
        Assert.Null(deleted);
    }
}
