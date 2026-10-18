using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

public interface IStartupReconciler
{
    Task<WorkspaceReconciliationResult> ReconcileWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default);
    ReconciliationProgress GetProgress(string workspaceId);
    Task<IReadOnlyList<Document>> GetCachedWorkspaceDocumentsAsync(string workspaceId);
}
