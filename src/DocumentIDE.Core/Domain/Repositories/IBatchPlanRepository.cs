using System.Collections.Generic;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;

namespace DocumentIDE.Core.Domain.Repositories;

public interface IBatchPlanRepository
{
    Task SavePlanAsync(BatchOperationPlan plan);
    Task<BatchOperationPlan?> GetByIdAsync(string id);
    Task<BatchOperationPlan?> GetByIdempotencyKeyAsync(string workspaceId, string idempotencyKey);
    Task<IReadOnlyList<BatchOperationPlan>> GetByWorkspaceAsync(string workspaceId);
    Task UpdateStatusAsync(string planId, BatchPlanStatus newStatus);
    Task DeleteAsync(string id);
}
