using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-10 — which destructive Git operation a review/apply pair describes
/// (spec 13 §15.1/§15.2).
/// </summary>
public enum GitRestoreMode
{
    /// <summary>
    /// Discard the working-tree changes of one file
    /// (<c>git restore --worktree -- &lt;path&gt;</c>): the file returns to the
    /// version in the index. The index and every other file are untouched.
    /// </summary>
    DiscardWorktree = 0,

    /// <summary>
    /// Restore one file's working-tree content from a selected commit
    /// (<c>git restore --source=&lt;commit&gt; --worktree -- &lt;path&gt;</c>).
    /// The index is untouched, so the restored content is a working-tree change.
    /// </summary>
    RestoreVersion = 1,
}

/// <summary>
/// SCM-10 — typed outcome of one discard/restore request (spec 13 §15/§20).
///
/// <see cref="ReviewRequired"/> and <see cref="ReviewStale"/> are input
/// rejections: nothing ran and the file is unchanged. <see cref="ConflictedPath"/>
/// and <see cref="UntrackedPath"/> are deliberate safety refusals: Git never
/// ran. <see cref="RecoveryBlocked"/> means the recovery snapshot (HIST-01)
/// could not be created, so the mutation was blocked by the master-spec rule
/// "if recovery cannot be created, mutation is blocked".
/// </summary>
public enum GitRestoreOutcome
{
    /// <summary>The restore completed; <see cref="GitRestoreResult.Status"/> carries the status re-read from Git.</summary>
    Succeeded = 0,

    /// <summary>No reviewed confirmation token was supplied; nothing ran.</summary>
    ReviewRequired = 1,

    /// <summary>The reviewed state changed after the review; nothing ran and the newer content survives.</summary>
    ReviewStale = 2,

    /// <summary>The request path/commit is not acceptable input; nothing ran.</summary>
    RejectedPath = 3,

    /// <summary>The path is a conflict participant; restoring it is refused typed and nothing ran (spec 13 §21).</summary>
    ConflictedPath = 4,

    /// <summary>The path is untracked, so there is no Git version to restore and it is never deleted (spec 13 §15).</summary>
    UntrackedPath = 5,

    /// <summary>Discard only: the file has no working-tree change, so there is nothing to discard; nothing ran.</summary>
    NoChanges = 6,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 7,

    /// <summary>The system Git executable is missing or unusable; nothing ran.</summary>
    GitUnavailable = 8,

    /// <summary>The recovery snapshot could not be created, so the mutation was blocked and the file is unchanged.</summary>
    RecoveryBlocked = 9,

    /// <summary>Git ran and failed (conflict, index lock, permission, ...); inspect <see cref="GitRestoreResult.Failure"/>.</summary>
    Failed = 10,
}

/// <summary>SCM-10 — typed outcome of one review read (spec 13 §15.1).</summary>
public enum GitRestoreReviewStatus
{
    /// <summary>The review was produced; <see cref="GitRestoreReviewResult.Review"/> is present.</summary>
    Available = 0,

    /// <summary>The request path/commit is not acceptable input; nothing ran.</summary>
    RejectedPath = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable; nothing ran.</summary>
    GitUnavailable = 3,

    /// <summary>Git ran and failed; inspect <see cref="GitRestoreReviewResult.Failure"/>.</summary>
    Failed = 4,
}

/// <summary>
/// SCM-10 — the reviewed confirmation payload for one destructive operation
/// (spec 13 §15.1/§15.2).
///
/// It deliberately shows what will be lost — the affected file, the current
/// diff, whether the change is staged, the target Git version and whether
/// recovery coverage is available — and it carries a <see cref="ReviewToken"/>
/// that the apply call must echo back. The token is bound to the reviewed
/// state, so a file that changes after the review invalidates the
/// confirmation instead of being silently overwritten (spec 07 §11).
/// </summary>
/// <param name="Mode">Which operation the review describes.</param>
/// <param name="Path">Worktree-relative path exactly as Git reported it.</param>
/// <param name="SourceCommit">Commit the restore reads from; <c>null</c> for a discard.</param>
/// <param name="TargetSource">Human-readable target: <c>index</c> for a discard, <c>commit</c> for a version restore.</param>
/// <param name="Diff">The unified diff of what the operation would replace (may be empty).</param>
/// <param name="IsDiffTruncated">True when the diff exceeded the reviewed bound.</param>
/// <param name="IndexChange">The index side of the change, or <c>null</c> when Git reports no entry.</param>
/// <param name="WorkingTreeChange">The working-tree side of the change, or <c>null</c> when Git reports no entry.</param>
/// <param name="StatusCharacters">Raw porcelain status characters for inspectability.</param>
/// <param name="IsStaged">True when the file also has staged content (which a discard preserves).</param>
/// <param name="IsConflicted">True when the path is an unmerged conflict participant.</param>
/// <param name="IsUntracked">True when the path is untracked.</param>
/// <param name="HasWorkingTreeChange">True when the file has an unstaged working-tree change.</param>
/// <param name="RecoveryAvailable">True when a HIST-01 recovery snapshot can preserve the current content.</param>
/// <param name="RecoveryDetail">Safe explanation of the recovery availability.</param>
/// <param name="CurrentSizeBytes">Current on-disk size, or <c>null</c> when the file is absent.</param>
/// <param name="IsApplicable">False when this path must not be restored (conflict/untracked/no change).</param>
/// <param name="BlockedReason">Why the operation is not applicable; <c>null</c> when it is.</param>
/// <param name="ReviewToken">Opaque token the apply call must echo back.</param>
public sealed record GitRestoreReview(
    GitRestoreMode Mode,
    string Path,
    string? SourceCommit,
    string TargetSource,
    string Diff,
    bool IsDiffTruncated,
    GitChangeKind? IndexChange,
    GitChangeKind? WorkingTreeChange,
    string StatusCharacters,
    bool IsStaged,
    bool IsConflicted,
    bool IsUntracked,
    bool HasWorkingTreeChange,
    bool RecoveryAvailable,
    string RecoveryDetail,
    long? CurrentSizeBytes,
    bool IsApplicable,
    string? BlockedReason,
    string ReviewToken);

/// <summary>
/// SCM-10 — typed result of one review read (spec 13 §15.1/§20). A rejected
/// request is a typed status with nothing run; every Git-reported state stays
/// inspectable.
/// </summary>
public sealed record GitRestoreReviewResult(
    GitRestoreReviewStatus Status,
    GitRestoreReview? Review,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when a review payload is present.</summary>
    public bool IsAvailable => Status == GitRestoreReviewStatus.Available && Review is not null;
}

/// <summary>
/// SCM-10 — typed result of one discard/restore mutation (spec 13 §15/§20).
///
/// On success <see cref="Status"/> carries the status re-read from Git,
/// <see cref="SnapshotId"/> the recovery snapshot that was created before the
/// mutation, and <see cref="HistoryRecorded"/> whether the Document IDE History
/// record was written. On failure nothing was widened silently: the typed
/// outcome (plus <see cref="Failure"/>/<see cref="ErrorCode"/>) is the reason.
/// </summary>
public sealed record GitRestoreResult(
    GitRestoreOutcome Outcome,
    GitRestoreMode Mode,
    string? OperationId,
    string? SnapshotId,
    bool RecoveryRecorded,
    bool HistoryRecorded,
    bool Reindexed,
    GitStatusResult? Status,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when the Git mutation completed.</summary>
    public bool IsSuccess => Outcome == GitRestoreOutcome.Succeeded;
}

/// <summary>
/// SCM-10 — one Document IDE History record for a discard/restore operation
/// (spec 13 §4.3/§15.2 step 8). It is application-level metadata: Git remains
/// the authority for repository state and the record is never written into Git
/// (spec 13 §24).
/// </summary>
public sealed record GitRestoreHistoryRecord(
    string OperationId,
    string Mode,
    string Outcome,
    string? DocumentId,
    string Path,
    string WorktreeRoot,
    string? SourceCommit,
    string? SnapshotId,
    DateTimeOffset RecordedUtc);

/// <summary>
/// SCM-10 — Document IDE History recorder seam (spec 13 §15.2 step 8). The
/// existing JSONL history store implements it; a failed record write is a typed
/// warning on an otherwise successful restore, never a silent drop.
/// </summary>
public interface IGitRestoreHistoryRecorder
{
    Task RecordAsync(GitRestoreHistoryRecord record, CancellationToken cancellationToken = default);
}
