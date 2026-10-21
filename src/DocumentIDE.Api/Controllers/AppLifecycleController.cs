using DocumentIDE.Api.Lifecycle;
using DocumentIDE.Core.Activity;
using DocumentIDE.Core.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// DEV-UPD — control surface for the local dev auto-updater.
///
///   GET  /api/app/status            -> { version, sha, busy, reasons, startedAtUtc }
///   POST /api/app/prepare-restart   -> { ok, reason? } after the UI saves dirty buffers
///   POST /api/app/quit              -> 202 after the flush, or 409 { ok:false, reason:"busy" }
///
/// Every route is loopback-only and requires the per-run control token (or the
/// UI session token) through <c>PrivilegedRequestGuard</c>. The control token is
/// never accepted on document or mutation routes.
/// </summary>
[ApiController]
[Route("api/app")]
public sealed class AppLifecycleController(
    ActivityGate activity,
    IRestartFlushBridge restartFlush,
    IHostApplicationLifetime lifetime,
    BuildInfo buildInfo,
    AppLifecycleState state) : ControllerBase
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(20);

    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        version = buildInfo.Version,
        sha = buildInfo.Sha,
        busy = activity.IsBusy,
        reasons = activity.ActiveReasons,
        startedAtUtc = state.StartedAtUtc,
    });

    [HttpPost("prepare-restart")]
    public async Task<IActionResult> PrepareRestart(CancellationToken cancellationToken)
    {
        // Contract: a refused flush is HTTP 200 with { ok: false, reason }.
        // It is a control result, not an HTTP error; the watcher treats any
        // non-ok result (busy, flush-timeout, save failure) as "defer".
        // A restart never interrupts a backend mutation. The watcher polls
        // status first, and this check closes the race between that poll and
        // the flush request.
        if (activity.IsBusy)
        {
            return Ok(new { ok = false, reason = "busy" });
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FlushTimeout);

        try
        {
            var result = await restartFlush.PrepareRestartAsync(timeout.Token);
            return Ok(new { ok = result.Ok, reason = result.Reason });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Ok(new { ok = false, reason = "flush-timeout" });
        }
    }

    [HttpPost("quit")]
    public IActionResult Quit()
    {
        // prepare-restart checked the gate before the flush; a mutation can
        // begin during the flush, so quit closes the flush-to-quit race.
        if (activity.IsBusy)
        {
            return Conflict(new { ok = false, reason = "busy" });
        }

        // Stop only after the 202 has been written, and give a mutation that
        // entered the gate between the check and the response time to finish.
        // The watcher waits for the process; if the gate stays busy, the host
        // stays alive and the next poll defers instead of interrupting work.
        Response.OnCompleted(async () =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (activity.IsBusy && DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            if (!activity.IsBusy)
            {
                lifetime.StopApplication();
            }
        });

        return Accepted(new { ok = true });
    }
}
