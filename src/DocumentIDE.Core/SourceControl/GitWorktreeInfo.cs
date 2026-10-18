using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-15 — typed outcome of `git worktree list --porcelain` (spec 13 §16.1).
/// Git remains the authority for the worktree registry; failures are
/// inspectable statuses, never silent fallbacks.
/// </summary>
public enum GitWorktreeListStatus
{
    /// <summary>Git ran and the porcelain output parsed cleanly.</summary>
    Succeeded = 0,

    /// <summary>Git could not be discovered/started (e.g. not installed).</summary>
    GitUnavailable = 1,

    /// <summary>The working directory is not inside a Git repository.</summary>
    NotARepository = 2,

    /// <summary>Git ran but the worktree listing failed (non-zero exit).</summary>
    Failed = 3,

    /// <summary>Git answered, but the porcelain output was malformed; no partial list is returned.</summary>
    ParseError = 4,

    /// <summary>The caller cancelled the operation.</summary>
    Cancelled = 5,

    /// <summary>The operation exceeded the runner timeout policy.</summary>
    TimedOut = 6,
}

/// <summary>
/// SCM-15 — one registered Git worktree as reported by
/// `git worktree list --porcelain` (spec 13 §16.1):
/// path, HEAD commit, branch or detached state, locked/prunable state.
///
/// <para>
/// <see cref="Path"/> is the canonical (fully qualified, separator-normalized)
/// local path. It is the only value the discovery/open flow ever exposes:
/// arbitrary paths that Git did not register can never enter the worktree
/// context. <see cref="IsMainWorktree"/> follows Git's documented ordering —
/// the main working tree is listed first.
/// </para>
/// </summary>
public sealed record GitWorktreeInfo(
    string Path,
    string HeadCommit,
    string? BranchRef,
    bool IsDetached,
    bool IsBare,
    bool IsLocked,
    string? LockReason,
    bool IsPrunable,
    string? PruneReason,
    bool IsMainWorktree)
{
    private const string HeadsPrefix = "refs/heads/";

    /// <summary>
    /// Read-only display name of the checked-out branch (the full ref minus
    /// <c>refs/heads/</c>), or <c>null</c> for a detached/bare worktree.
    /// Document IDE never manages branches; this is context metadata only.
    /// </summary>
    public string? BranchName =>
        BranchRef is null
            ? null
            : BranchRef.StartsWith(HeadsPrefix, StringComparison.Ordinal)
                ? BranchRef[HeadsPrefix.Length..]
                : BranchRef;
}

/// <summary>
/// SCM-15 — structured result of listing registered worktrees.
/// <see cref="ProcessResult"/> keeps the raw Git outcome inspectable; on
/// failure <see cref="Worktrees"/> is empty and no partial registry is acted on.
/// </summary>
public sealed record GitWorktreeListResult(
    GitWorktreeListStatus Status,
    IReadOnlyList<GitWorktreeInfo> Worktrees,
    GitProcessResult ProcessResult,
    string? ErrorMessage = null)
{
    /// <summary>True only when Git answered and the porcelain output parsed cleanly.</summary>
    public bool IsSuccess => Status == GitWorktreeListStatus.Succeeded;
}
