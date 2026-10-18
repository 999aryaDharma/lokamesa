using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.Core.Tests.Batch;

public class BatchPlanRepositoryTests
{
    private static string CreateTempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"batplan_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SaveAndRetrieveByIdempotencyKey_Works()
    {
        // Arrange — fresh isolated DB
        var dbPath = CreateTempDbPath();
        var appDb = new AppDb(dbPath);
        await appDb.InitializeAsync();

        // Seed workspace so the FK constraint (batch_operation_plans.workspace_id → workspaces.id) is satisfied
        var workspaceRepo = new WorkspaceRepository(appDb);
        var workspace = new Workspace
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Workspace",
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O")
        };
        await workspaceRepo.CreateAsync(workspace);

        var repo = new BatchPlanRepository(appDb);

        var plan = new BatchOperationPlan
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = workspace.Id,    // use the seeded workspace id
            OperationType = BatchOperationType.SearchAndReplace,
            Status = BatchPlanStatus.Previewed,
            ScopeJson = "{}",
            ParametersJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IdempotencyKey = "idem-key-123",
            Files = new()
        };

        // Act
        await repo.SavePlanAsync(plan);
        var retrieved = await repo.GetByIdempotencyKeyAsync(plan.WorkspaceId, "idem-key-123");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(plan.Id, retrieved!.Id);
        Assert.Equal(plan.IdempotencyKey, retrieved.IdempotencyKey);

        // Cleanup
        if (File.Exists(dbPath)) try { File.Delete(dbPath); } catch { }
    }
}
