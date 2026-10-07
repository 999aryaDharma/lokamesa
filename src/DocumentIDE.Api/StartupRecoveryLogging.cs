using System;
using System.Linq;
using DocumentIDE.Core.Filesystem;
using Microsoft.Extensions.Logging;

namespace DocumentIDE.Api;

/// <summary>
/// Concise typed startup-recovery log summary shared by the API and desktop hosts.
/// Counts plus per-plan ids and non-clean details; no HTTP surface.
/// </summary>
public static class StartupRecoveryLogging
{
    public static void LogReport(ILogger logger, StartupRecoveryReport report)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(report);

        logger.LogInformation(
            "Startup recovery: interruptedPlans={InterruptedPlanCount} (markedFailed={MarkedFailedCount}), orphanApplyTemps={OrphanTempCount} (removed={RemovedCount}), clean={IsClean}",
            report.InterruptedPlans.Count,
            report.InterruptedPlans.Count(p => p.Outcome == InterruptedPlanRepairOutcome.MarkedFailed),
            report.OrphanTempFiles.Count,
            report.OrphanTempFiles.Count(f => f.Outcome == OrphanTempFileCleanupOutcome.Removed),
            report.IsClean);

        foreach (var plan in report.InterruptedPlans)
        {
            if (plan.Outcome == InterruptedPlanRepairOutcome.MarkedFailed)
            {
                logger.LogInformation(
                    "Startup recovery: interrupted plan {PlanId} in workspace {WorkspaceId} ({SelectedFileCount} selected files) was marked Failed; success was not inferred.",
                    plan.PlanId,
                    plan.WorkspaceId,
                    plan.SelectedFileCount);
            }
            else
            {
                logger.LogWarning(
                    "Startup recovery: interrupted plan {PlanId} in workspace {WorkspaceId} could not be repaired: {Detail}",
                    plan.PlanId,
                    plan.WorkspaceId,
                    plan.Detail);
            }
        }

        foreach (var temp in report.OrphanTempFiles.Where(f => f.Outcome != OrphanTempFileCleanupOutcome.Removed))
        {
            logger.LogWarning(
                "Startup recovery: orphan apply temp {Path} could not be removed: {Detail}",
                temp.Path,
                temp.Detail);
        }
    }
}
