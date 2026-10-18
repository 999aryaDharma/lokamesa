using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-07 — which commit flow ran (spec 13 §11). Commit and Commit All are
/// deliberately separate flows: the plain action commits the current staged
/// state, the convenience action stages eligible changes first.
/// </summary>
public enum GitCommitFlow
{
    /// <summary><c>git commit -m &lt;message&gt;</c> against the current staged state (spec 13 §11.1).</summary>
    Commit = 0,

    /// <summary>Stage all eligible changes, re-read status, then commit (spec 13 §11.2).</summary>
    CommitAll = 1,
}

/// <summary>
/// SCM-07 — typed outcome of one commit request (spec 13 §11/§20).
///
/// Failures are inspectable statuses with the originating
/// <see cref="GitProcessResult"/>, never silent fallbacks: "nothing staged"
/// never becomes an empty commit, a hook rejection never becomes a bypassed
/// hook, and a lock contention never becomes a partial commit.
/// </summary>
public enum GitCommitOutcome
{
    /// <summary>The commit was created; <see cref="GitCommitResult.Commit"/> carries its identifier.</summary>
    Succeeded = 0,

    /// <summary>A non-blank commit message is required; the request was rejected before Git ran.</summary>
    MessageRequired = 1,

    /// <summary>The message cannot be used as a Git argument (e.g. it contains a NUL character); nothing ran.</summary>
    MessageInvalid = 2,

    /// <summary>There is nothing staged to commit; no empty commit was created (spec 13 §11.3).</summary>
    NothingStaged = 3,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 4,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 5,

    /// <summary>Git reported that it has no author/committer identity to record the commit with.</summary>
    MissingIdentity = 6,

    /// <summary>A repository commit hook rejected the commit (spec 13 §11.1/§23); no commit was created.</summary>
    HookRejected = 7,

    /// <summary>The Git index is locked by another process (spec 13 §19/§20); nothing was committed.</summary>
    IndexLocked = 8,

    /// <summary>Git ran and failed for a reason this service does not classify further; inspect <see cref="GitCommitResult.Failure"/>.</summary>
    Failed = 9,

    /// <summary>
    /// The repository has unresolved conflicts, so the commit was refused before
    /// Git ran (spec 13 §21). No commit was created and the index is unchanged.
    /// </summary>
    ConflictedWorktree = 10,
}

/// <summary>
/// SCM-07 — summary metadata of the commit that was created (spec 13 §11.1
/// step 6 "show the resulting commit identifier").
/// </summary>
/// <param name="CommitId">Full commit object id.</param>
/// <param name="ShortId">Git's abbreviated commit id.</param>
/// <param name="Summary">Commit subject as Git recorded it (the message's first line).</param>
/// <param name="BranchName">Branch the commit was created on; <c>null</c> when HEAD is detached.</param>
public sealed record GitCommitInfo(
    string CommitId,
    string ShortId,
    string Summary,
    string? BranchName);

/// <summary>
/// SCM-07 — typed result of one Commit or Commit All request
/// (spec 13 §11/§20/§24).
///
/// On success <see cref="Commit"/> carries the new commit identifier and
/// <see cref="Status"/> the status re-read from Git after the commit (the real
/// Git index is the only staging authority; Document IDE keeps no commit or
/// staging snapshot of its own). The commit path never talks to a remote
/// (spec 13 §14.4/§23): there is no automatic push.
/// </summary>
/// <param name="Outcome">Typed outcome vocabulary.</param>
/// <param name="Flow">Which flow ran (Commit or Commit All).</param>
/// <param name="Commit">New commit identity/metadata on success; <c>null</c> otherwise.</param>
/// <param name="Status">Status re-read from Git after the operation; may itself be a typed failure.</param>
/// <param name="Failure">Originating typed Git failure when the outcome is not a success.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable rejection code for input rejections (message validation).</param>
/// <param name="StagedPaths">
/// For <see cref="GitCommitFlow.CommitAll"/>: the paths the flow staged and
/// committed, as Git reported them (spec 13 §11.2 steps 2–4). <c>null</c> for
/// the plain Commit flow, which never stages anything itself.
/// </param>
/// <param name="ConflictedPaths">
/// For <see cref="GitCommitOutcome.ConflictedWorktree"/>: the unmerged paths
/// that blocked the commit (spec 13 §21). <c>null</c> otherwise.
/// </param>
public sealed record GitCommitResult(
    GitCommitOutcome Outcome,
    GitCommitFlow Flow,
    GitCommitInfo? Commit,
    GitStatusResult? Status,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null,
    IReadOnlyList<string>? StagedPaths = null,
    IReadOnlyList<string>? ConflictedPaths = null)
{
    /// <summary>True only when the commit was created in the repository.</summary>
    public bool IsSuccess => Outcome == GitCommitOutcome.Succeeded;
}
