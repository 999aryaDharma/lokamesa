using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

/// <summary>How an interrupted (stuck in <see cref="BatchPlanStatus.Executing"/>) plan was reconciled.</summary>
public enum InterruptedPlanRepairOutcome
{
    /// <summary>The plan was marked <see cref="BatchPlanStatus.Failed"/>; success was never inferred.</summary>
    MarkedFailed,

    /// <summary>The repository could not persist the repair; the plan needs manual attention.</summary>
    RepairFailed
}

/// <summary>Typed record of one interrupted operation found and reconciled at startup.</summary>
public sealed record InterruptedPlanRepair(
    string PlanId,
    string WorkspaceId,
    int SelectedFileCount,
    InterruptedPlanRepairOutcome Outcome,
    string? Detail);

/// <summary>How an orphan application temp artifact was handled.</summary>
public enum OrphanTempFileCleanupOutcome
{
    Removed,
    DeleteFailed
}

/// <summary>Typed record of one orphan <c>~apply-*.tmp</c> artifact found under a folder source.</summary>
public sealed record OrphanTempFileCleanup(
    string Path,
    OrphanTempFileCleanupOutcome Outcome,
    string? Detail);

/// <summary>Typed startup recovery report: what was repaired, what still needs attention.</summary>
public sealed record StartupRecoveryReport(
    IReadOnlyList<InterruptedPlanRepair> InterruptedPlans,
    IReadOnlyList<OrphanTempFileCleanup> OrphanTempFiles)
{
    /// <summary>True when every interrupted plan was reconciled and every orphan temp was removed.</summary>
    public bool IsClean =>
        InterruptedPlans.All(p => p.Outcome == InterruptedPlanRepairOutcome.MarkedFailed) &&
        OrphanTempFiles.All(f => f.Outcome == OrphanTempFileCleanupOutcome.Removed);
}

/// <summary>
/// QA-07 — Crash &amp; Interrupted-Operation Recovery.
/// One-shot startup repair. Reconciles batch plans left in <c>Executing</c> by a crash
/// (marked <c>Failed</c>, never <c>Completed</c>: success is never inferred from an
/// interrupted operation) and removes orphan transactional apply temp files from the
/// configured folder sources only, without following reparse points.
/// Call once at startup before background execution begins.
/// </summary>
public sealed class StartupRecoveryService
{
    private readonly IWorkspaceRepository _workspaces;
    private readonly IFolderSourceRepository _folderSources;
    private readonly IBatchPlanRepository _plans;

    public StartupRecoveryService(
        IWorkspaceRepository workspaces,
        IFolderSourceRepository folderSources,
        IBatchPlanRepository plans)
    {
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _folderSources = folderSources ?? throw new ArgumentNullException(nameof(folderSources));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
    }

    /// <summary>
    /// Repairs state left behind by a previous crashed process: every plan still in
    /// <c>Executing</c> is marked <c>Failed</c> (an interrupted operation is never
    /// completed or re-applied), and orphan apply temp files under configured source
    /// folders are deleted. Source documents are never modified. Returns a typed report;
    /// failed repairs/deletes are reported, never silently swallowed.
    /// </summary>
    public async Task<StartupRecoveryReport> RecoverAsync(CancellationToken cancellationToken = default)
    {
        var repairs = new List<InterruptedPlanRepair>();
        var cleanups = new List<OrphanTempFileCleanup>();

        foreach (var workspace in await _workspaces.GetAllAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ReconcileInterruptedPlansAsync(workspace.Id, repairs, cancellationToken);

            var sources = await _folderSources.GetByWorkspaceAsync(workspace.Id);
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(source.CanonicalPath)) continue;
                if (!Directory.Exists(source.CanonicalPath)) continue;
                CleanOrphanTemps(new DirectoryInfo(source.CanonicalPath), cleanups, cancellationToken);
            }
        }

        return new StartupRecoveryReport(repairs, cleanups);
    }

    private async Task ReconcileInterruptedPlansAsync(
        string workspaceId,
        List<InterruptedPlanRepair> repairs,
        CancellationToken cancellationToken)
    {
        var plans = await _plans.GetByWorkspaceAsync(workspaceId);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.Status != BatchPlanStatus.Executing) continue;

            var selectedFileCount = plan.Files.Count(f => f.Selected);
            try
            {
                // A plan can only be in Executing because the process that owned it is
                // gone: no execution can be live during startup recovery. It must never
                // be completed or re-applied; per-file history, where it exists, remains
                // the verification/undo surface.
                await _plans.UpdateStatusAsync(plan.Id, BatchPlanStatus.Failed);
                repairs.Add(new InterruptedPlanRepair(
                    plan.Id,
                    workspaceId,
                    selectedFileCount,
                    InterruptedPlanRepairOutcome.MarkedFailed,
                    "Interrupted while executing; marked Failed instead of inferring success."));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                repairs.Add(new InterruptedPlanRepair(
                    plan.Id,
                    workspaceId,
                    selectedFileCount,
                    InterruptedPlanRepairOutcome.RepairFailed,
                    ex.Message));
            }
        }
    }

    private static void CleanOrphanTemps(
        DirectoryInfo directory,
        List<OrphanTempFileCleanup> cleanups,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Never follow a reparse point: cleanup must not escape a configured source.
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return;

            // Materialized before deleting so the enumeration never observes a directory
            // mutating under it.
            foreach (var file in directory.EnumerateFiles().ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ApplyTempFiles.IsApplyTempFileName(file.Name)) continue;

                try
                {
                    file.Delete();
                    cleanups.Add(new OrphanTempFileCleanup(file.FullName, OrphanTempFileCleanupOutcome.Removed, null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // e.g. still locked by another handle: report typed, never throw away the report.
                    cleanups.Add(new OrphanTempFileCleanup(file.FullName, OrphanTempFileCleanupOutcome.DeleteFailed, ex.Message));
                }
            }

            foreach (var subDirectory in directory.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Mirror StartupReconciler: hidden/system folders are not part of the
                // document source surface, so nothing there is ours to clean.
                if ((subDirectory.Attributes & FileAttributes.Hidden) != 0 || subDirectory.Name.StartsWith('.')) continue;
                CleanOrphanTemps(subDirectory, cleanups, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // Unreadable directory: nothing discoverable there; the rest of the source still gets cleaned.
        }
    }
}
