using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.History;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// HIST-04 — read-only Document IDE operation history for screen 10.
///
/// Append-only by contract: there is no mutation route here. Undo stays on the
/// existing reviewed <c>POST /api/batch-operations/{planId}/undo</c> path, so this
/// controller cannot broaden a destructive action or bypass a recovery snapshot.
/// Every failure is a typed <c>Unavailable</c> payload (HTTP 200); <c>400</c> is
/// reserved for input rejected before any read.
/// </summary>
[ApiController]
[Route("api/history")]
public sealed class HistoryController(
    IOperationHistoryRepository operations,
    IHistoryStorage storage) : ControllerBase
{
    private const string Available = "Available";
    private const string Unavailable = "Unavailable";

    /// <summary>
    /// One bounded page of persisted operations, newest first. An empty history is
    /// an <c>Available</c> empty page; only a read failure is <c>Unavailable</c>.
    /// </summary>
    [HttpGet("operations")]
    public async Task<IActionResult> Operations(
        [FromQuery] int? skip,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!OperationHistoryQuery.TryNormalize(skip, limit, out var query, out var errorCode, out var error))
        {
            return BadRequest(new { errorCode, error });
        }

        try
        {
            OperationHistoryPage page = await operations.GetPageAsync(query.Skip, query.Limit, cancellationToken);
            return Ok(new
            {
                status = Available,
                entries = page.Entries.Select(ProjectEntry).ToArray(),
                skip = page.Skip,
                limit = page.Limit,
                totalCount = page.TotalCount,
                hasMore = page.HasMore,
                nextSkip = page.NextSkip,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No exception text, path or storage detail is returned: a failed read is
            // a typed state, never an empty list that could read as "no operations".
            return Ok(new
            {
                status = Unavailable,
                errorCode = "HistoryUnavailable",
                error = "The Document IDE operation history could not be read.",
                entries = Array.Empty<object>(),
                skip = query.Skip,
                limit = query.Limit,
                totalCount = 0,
                hasMore = false,
                nextSkip = (int?)null,
            });
        }
    }

    /// <summary>
    /// The real history storage usage (snapshot bytes and the configured capacity),
    /// read from the same <see cref="IHistoryStorage"/> the apply path uses.
    /// </summary>
    [HttpGet("storage")]
    public async Task<IActionResult> Storage(CancellationToken cancellationToken)
    {
        try
        {
            var usedBytes = await storage.GetTotalSizeAsync();
            var capacityBytes = await storage.GetCapacityBytesAsync();
            return Ok(new
            {
                status = Available,
                usedBytes,
                capacityBytes,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Ok(new
            {
                status = Unavailable,
                errorCode = "HistoryStorageUnavailable",
                error = "The Document IDE history storage usage could not be read.",
                usedBytes = (long?)null,
                capacityBytes = (long?)null,
            });
        }
    }

    private static object ProjectEntry(OperationHistoryRecord record) => new
    {
        operationId = record.OperationId,
        planId = record.PlanId,
        operationType = record.Kind,
        status = record.Outcome.ToString(),
        recordedUtc = record.CompletedUtc,
        startedUtc = record.StartedUtc,
        completedUtc = record.CompletedUtc,
        affectedFileCount = record.AffectedFileCount,
        snapshotId = record.SnapshotId,
        files = record.Files
            .Select(file => new
            {
                documentId = file.DocumentId,
                outcome = file.Outcome.ToString(),
                reasonCode = file.ReasonCode,
                detail = file.Detail,
            })
            .ToArray(),
        undo = record.Undo is null
            ? null
            : new
            {
                status = record.Undo.Status.ToString(),
                files = record.Undo.Files
                    .Select(file => new
                    {
                        documentId = file.DocumentId,
                        outcome = file.Outcome.ToString(),
                        reasonCode = file.ReasonCode,
                        detail = file.Detail,
                    })
                    .ToArray(),
            },
    };
}
