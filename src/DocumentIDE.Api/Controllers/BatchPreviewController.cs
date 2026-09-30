using DocumentIDE.Core.Batch;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

[ApiController]
[Route("api/batch-operations")]
public sealed class BatchPreviewController(BatchPreviewCoordinator previews, AppDb db) : ControllerBase
{
    [HttpGet("workspaces")]
    public async Task<IActionResult> Workspaces() => Ok((await new WorkspaceRepository(db).GetAllAsync()).Select(w => new { w.Id, w.Name }));

    [HttpGet("workspaces/{workspaceId}/scope-options")]
    public async Task<IActionResult> ScopeOptions(string workspaceId)
    {
        var options = await previews.GetScopeOptionsAsync(workspaceId);
        return options == null ? NotFound() : Ok(options);
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview(BatchPreviewRequest request)
    {
        try { var started = await previews.StartAsync(request); return Accepted(new { started.PlanId, started.JobId }); }
        catch (ArgumentException) { return BadRequest(new { errorCode = "InvalidScopeOrParameters" }); }
    }

    [HttpGet("{planId}")]
    public async Task<IActionResult> Get(string planId) { var preview = await previews.GetAsync(planId); return preview == null ? NotFound() : Ok(preview); }

    [HttpPut("{planId}/selection")]
    public async Task<IActionResult> Selection(string planId, BatchPreviewSelection selection)
    {
        try { var preview = await previews.SaveSelectionAsync(planId, selection); return preview == null ? NotFound() : Ok(preview); }
        catch (BatchPreviewConflictException ex) { return Conflict(new { errorCode = ex.Code }); }
    }

    [HttpPost("{planId}/cancel")]
    public async Task<IActionResult> Cancel(string planId) => await previews.CancelAsync(planId) ? NoContent() : NotFound();
}
