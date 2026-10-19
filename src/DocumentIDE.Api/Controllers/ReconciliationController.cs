using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Graph;
using DocumentIDE.Core.Indexing;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId}")]
public class ReconciliationController : ControllerBase
{
    private readonly IStartupReconciler _reconciler;
    private readonly IncrementalIndexCoordinator _indexCoordinator;
    private readonly MarkdownRelationIndexer _relationIndexer;

    public ReconciliationController(
        IStartupReconciler reconciler,
        IncrementalIndexCoordinator indexCoordinator,
        MarkdownRelationIndexer relationIndexer)
    {
        _reconciler = reconciler;
        _indexCoordinator = indexCoordinator;
        _relationIndexer = relationIndexer;
    }

    /// <summary>
    /// FS-04 + IDX-06: reconcile the workspace against the filesystem, then bring
    /// the search index into agreement with the reconciled documents. The second
    /// step is what makes a freshly attached folder searchable: reconciliation
    /// only writes <c>documents</c> rows (with <c>indexStatus = "pending"</c>),
    /// so without this call <c>search_content</c> stays empty and every query
    /// returns nothing. The third step derives the explicit relations the graph
    /// view renders (GR-01/GR-03); without it <c>GET .../graph</c> returns nodes
    /// with <c>edges: []</c>.
    /// </summary>
    [HttpPost("reconcile")]
    public async Task<IActionResult> StartReconciliation(string workspaceId)
    {
        var result = await _reconciler.ReconcileWorkspaceAsync(workspaceId);
        await _indexCoordinator.ReindexWorkspaceAsync(workspaceId);
        await _relationIndexer.ReindexWorkspaceAsync(workspaceId);
        return Ok(result);
    }

    [HttpGet("reconcile/status")]
    public IActionResult GetReconciliationStatus(string workspaceId)
    {
        var progress = _reconciler.GetProgress(workspaceId);
        return Ok(progress);
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetCachedDocuments(string workspaceId)
    {
        var docs = await _reconciler.GetCachedWorkspaceDocumentsAsync(workspaceId);
        return Ok(docs);
    }
}
