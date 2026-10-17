using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Indexing;

namespace DocumentIDE.Infrastructure.Batch;

/// <summary>
/// BAT-04 reindex seam: routes a successfully mutated document back through the
/// IDX-05 incremental index coordinator so search/graph metadata never lag a
/// batch apply (or an undo) beyond the next reindex.
/// </summary>
public sealed class IndexCoordinatorReindexTrigger(IncrementalIndexCoordinator coordinator) : IBatchApplyReindexTrigger
{
    public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default) =>
        coordinator.ReindexDocumentAsync(documentId, cancellationToken);
}
