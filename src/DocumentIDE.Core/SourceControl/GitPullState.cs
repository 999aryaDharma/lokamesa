using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-12 — typed outcome of one conservative Pull (spec 13 §14.3/§20).
///
/// Pull is <b>fast-forward only</b>: it never creates a merge commit and never
/// rebases. The blocking states (<see cref="Diverged"/>,
/// <see cref="DirtyWorktree"/>, <see cref="ConflictedWorktree"/>) are resolved
/// outcomes that changed nothing, not raw error strings.
/// </summary>
public enum GitPullOutcome
{
    /// <summary>The branch fast-forwarded to its configured upstream; nothing was merged or rebased.</summary>
    Succeeded = 0,

    /// <summary>The current branch has no configured upstream; nothing ran (spec 13 §14.2/§14.3).</summary>
    NoUpstream = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>The worktree has staged/unstaged tracked changes; nothing ran and no file changed.</summary>
    DirtyWorktree = 4,

    /// <summary>The worktree has unmerged entries; nothing ran and no conflict was touched (spec 13 §21).</summary>
    ConflictedWorktree = 5,

    /// <summary>Local and remote history have diverged; no merge/rebase was performed and nothing changed.</summary>
    Diverged = 6,

    /// <summary>The Git index was locked by another process; nothing ran (or the pull failed before changing anything).</summary>
    IndexLocked = 7,

    /// <summary>The remote required authentication that the disabled terminal prompt cannot supply.</summary>
    AuthenticationRequired = 8,

    /// <summary>The filesystem or remote denied permission.</summary>
    PermissionDenied = 9,

    /// <summary>The remote could not be reached (DNS, connection, or the remote path is not a usable repository).</summary>
    HostUnreachable = 10,

    /// <summary>The worktree disappeared before or during the operation; nothing was pulled.</summary>
    WorktreeMissing = 11,

    /// <summary>Git ran and failed for a reason this service does not classify further; inspect <see cref="GitPullResult.Failure"/>.</summary>
    Failed = 12,
}

/// <summary>
/// SCM-12 — downstream refresh performed after a successful fast-forward pull
/// (spec 13 §14.3 step 4/§17). The status is re-read from Git, the documents
/// whose paths changed are reindexed through the existing index seam, and the
/// operation is recorded in Document IDE History.
/// </summary>
/// <param name="ChangedPaths">Worktree-relative paths changed by the fast-forward (empty when already up to date).</param>
/// <param name="ReindexedDocuments">How many registered documents were handed to the reindex trigger.</param>
/// <param name="StatusRefreshed">True when the post-pull Git status was re-read successfully.</param>
/// <param name="HistoryRecorded">True when the Document IDE History record was written.</param>
/// <param name="Status">The status re-read from Git after the pull.</param>
public sealed record GitPullRefresh(
    IReadOnlyList<string> ChangedPaths,
    int ReindexedDocuments,
    bool StatusRefreshed,
    bool HistoryRecorded,
    GitStatusResult? Status);

/// <summary>
/// SCM-12 — typed result of one conservative Pull (spec 13 §14.3/§19/§20).
///
/// A successful result carries the previous and new HEAD plus the downstream
/// refresh. A blocking result (<see cref="IsBlocking"/>) carries no refreshed
/// state because nothing ran: divergence never creates a merge commit, a dirty
/// or conflicted worktree is refused before Git can autostash, overwrite or
/// partially apply anything. No failure payload ever carries credential
/// material (spec 13 §23).
/// </summary>
/// <param name="Outcome">Typed outcome vocabulary.</param>
/// <param name="OperationId">Document IDE History operation id for this pull.</param>
/// <param name="PreviousHead">HEAD before the pull (for an unborn branch: <c>null</c>).</param>
/// <param name="Head">HEAD after the pull; equal to <paramref name="PreviousHead"/> when nothing moved.</param>
/// <param name="Remote">Tracking state re-read after the pull.</param>
/// <param name="Refresh">Status/document/index refresh performed after a successful pull.</param>
/// <param name="Failure">Originating typed Git failure when the outcome is not a success.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable code for typed non-success outcomes.</param>
public sealed record GitPullResult(
    GitPullOutcome Outcome,
    string? OperationId,
    string? PreviousHead,
    string? Head,
    GitRemoteState? Remote,
    GitPullRefresh? Refresh,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when the branch fast-forwarded (or was already up to date).</summary>
    public bool IsSuccess => Outcome == GitPullOutcome.Succeeded;

    /// <summary>
    /// True for the deliberate conservative refusals that the UI must show as a
    /// blocking state: diverged history, a dirty worktree or conflicted entries.
    /// Nothing ran for these outcomes.
    /// </summary>
    public bool IsBlocking =>
        Outcome is GitPullOutcome.Diverged
            or GitPullOutcome.DirtyWorktree
            or GitPullOutcome.ConflictedWorktree;
}

/// <summary>
/// SCM-12 — one Document IDE History record for a pull (spec 13 §4.3/§14.3).
/// It is derived application metadata written to the same JSONL history store
/// as batch apply/undo and SCM-10 restores; Git stays the authority for
/// repository state and the record is never written into Git (spec 13 §24).
/// </summary>
public sealed record GitPullHistoryRecord(
    string OperationId,
    string Outcome,
    string WorktreeRoot,
    string? PreviousHead,
    string? Head,
    IReadOnlyList<string> ChangedPaths,
    DateTimeOffset RecordedUtc);

/// <summary>
/// SCM-12 — Document IDE History recorder seam (spec 13 §14.3 step 4). The
/// existing JSONL history store implements it; a failed record write is a typed
/// warning on an otherwise successful pull, never a silent drop.
/// </summary>
public interface IGitPullHistoryRecorder
{
    Task RecordAsync(GitPullHistoryRecord record, CancellationToken cancellationToken = default);
}
