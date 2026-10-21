using DocumentIDE.Core.Activity;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.History;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.History;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

[ApiController]
[Route("api/batch-operations")]
public sealed class BatchPreviewController(
    BatchPreviewCoordinator previews,
    BatchApplyCoordinator applies,
    UndoService undo,
    JsonlHistoryStore history,
    AppDb db,
    ActivityGate activity) : ControllerBase
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

    /// <summary>
    /// BAT-04 apply: runs the reviewed exact Search &amp; Replace mutation for a
    /// previewed plan. The reviewed engine owns fingerprint staleness, external
    /// edit detection, recovery snapshot and atomic replace; an expired,
    /// executing, failed or already-completed plan is rejected (spec 07 §19,
    /// master §82 "destructive batch requires a reviewed deterministic plan").
    /// </summary>
    [HttpPost("{planId}/apply")]
    public async Task<IActionResult> Apply(string planId, CancellationToken cancellationToken)
    {
        using var lease = activity.Enter("batch-apply");
        try
        {
            var result = await applies.ApplyAsync(planId, cancellationToken);
            return Ok(ProjectApply(result));
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "UnknownPlan", StringComparison.Ordinal))
        {
            return NotFound(new { errorCode = "UnknownPlan" });
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, "UnsupportedOperationType", StringComparison.Ordinal)
                                                  || string.Equals(ex.Message, "InvalidPlanParameters", StringComparison.Ordinal))
        {
            return BadRequest(new { errorCode = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            // Expired / completed / executing / failed plan: the client re-previews.
            return Conflict(new { errorCode = "PlanNotApplyable", detail = ex.Message });
        }
    }

    /// <summary>
    /// HIST-02 conflict-aware undo of a prior apply (by plan/operation id). Files
    /// changed since the operation are reported as conflicts and never overwritten.
    /// </summary>
    [HttpPost("{planId}/undo")]
    public async Task<IActionResult> Undo(string planId, CancellationToken cancellationToken)
    {
        using var lease = activity.Enter("batch-undo");
        try
        {
            var result = await undo.UndoAsync(planId, cancellationToken);
            return Ok(ProjectUndo(result));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no recovery snapshot exists", StringComparison.Ordinal))
        {
            return NotFound(new { errorCode = "UnknownOperation" });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { errorCode = "UndoUnavailable", detail = ex.Message });
        }
    }

    /// <summary>BAT-11 owner-side apply/undo history for a plan.</summary>
    [HttpGet("{planId}/history")]
    public async Task<IActionResult> History(string planId, CancellationToken cancellationToken) =>
        Ok(new
        {
            planId,
            apply = (await history.GetApplyRecordsAsync(planId, cancellationToken)).Select(record => new
            {
                record.PlanId,
                record.DocumentId,
                record.FilePath,
                outcome = record.Outcome.ToString(),
                record.ReasonCode,
                record.Detail,
                record.SnapshotId,
                record.RecordedUtc,
            }),
            undo = (await history.GetUndoRecordsAsync(planId, cancellationToken)).Select(record => new
            {
                record.OperationId,
                status = record.Status.ToString(),
                record.RecordedUtc,
            }),
        });

    // The web client reads string statuses (matching the preview contract served by
    // the same controller); engine enums stay numeric only inside Core/Infrastructure.
    private static object ProjectApply(BatchApplyResult result) => new
    {
        result.PlanId,
        result.SnapshotId,
        status = result.Status.ToString(),
        files = result.Files.Select(file => new
        {
            file.DocumentId,
            file.FilePath,
            outcome = file.Outcome.ToString(),
            file.ReasonCode,
            file.Detail,
        }).ToArray(),
    };

    private static object ProjectUndo(UndoResult result) => new
    {
        result.OperationId,
        result.SnapshotId,
        status = result.Status.ToString(),
        files = result.Files.Select(file => new
        {
            file.DocumentId,
            file.FilePath,
            outcome = file.Outcome.ToString(),
            file.ReasonCode,
            file.Detail,
        }).ToArray(),
    };
}
