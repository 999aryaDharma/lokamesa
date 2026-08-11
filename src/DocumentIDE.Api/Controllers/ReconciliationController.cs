using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId}")]
public class ReconciliationController : ControllerBase
{
    private readonly IStartupReconciler _reconciler;

    public ReconciliationController(IStartupReconciler reconciler)
    {
        _reconciler = reconciler;
    }

    [HttpPost("reconcile")]
    public async Task<IActionResult> StartReconciliation(string workspaceId)
    {
        var result = await _reconciler.ReconcileWorkspaceAsync(workspaceId);
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
