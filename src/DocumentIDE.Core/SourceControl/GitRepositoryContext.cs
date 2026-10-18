using System;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-02 — typed outcome of Git repository/worktree discovery (spec 13 §5).
/// A path outside a Git worktree remains a valid Document IDE source; it simply
/// has no Git state, represented by <see cref="NoRepository"/> rather than an
/// exception or a silent fallback.
/// </summary>
public enum GitRepositoryStatus
{
    /// <summary>The path resolves to a usable Git worktree.</summary>
    Available = 0,

    /// <summary>The path exists but is not inside any Git worktree; a valid non-Git source.</summary>
    NoRepository = 1,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 2,

    /// <summary>Discovery/status could not complete for a non-classifiable reason; inspect the failure.</summary>
    Failed = 3,
}

/// <summary>
/// SCM-02 — repository/worktree context discovered with Git itself
/// (<c>git rev-parse</c> family; spec 13 §5/§9.4).
///
/// Git remains the single authority for this state (spec 13 §4.2/§24): the
/// context is derived on demand and is never duplicated into SQLite.
/// </summary>
/// <param name="Status">Typed discovery outcome.</param>
/// <param name="RequestedPath">The canonicalized path discovery was requested for.</param>
/// <param name="RepositoryRoot">
/// The main repository root (the directory owning the common Git directory).
/// For a linked worktree this differs from <paramref name="WorktreeRoot"/>.
/// </param>
/// <param name="WorktreeRoot">The enclosing worktree root (<c>git rev-parse --show-toplevel</c>).</param>
/// <param name="GitDirReference">The worktree's Git directory (<c>git rev-parse --git-dir</c>), canonicalized.</param>
/// <param name="HeadCommit">Current HEAD commit id, or <c>null</c> for an unborn branch.</param>
/// <param name="BranchName">Current branch name, or <c>null</c> when detached/unborn-unavailable.</param>
/// <param name="IsDetached">True when HEAD is detached (no branch).</param>
/// <param name="UpstreamReference">Configured upstream reference (e.g. <c>origin/main</c>) when present.</param>
/// <param name="Failure">Typed Git failure when <see cref="Status"/> is not <see cref="GitRepositoryStatus.Available"/>.</param>
public sealed record GitRepositoryContext(
    GitRepositoryStatus Status,
    string RequestedPath,
    string? RepositoryRoot = null,
    string? WorktreeRoot = null,
    string? GitDirReference = null,
    string? HeadCommit = null,
    string? BranchName = null,
    bool IsDetached = false,
    string? UpstreamReference = null,
    GitProcessResult? Failure = null)
{
    /// <summary>True only when discovery produced a usable repository/worktree context.</summary>
    public bool HasRepository => Status == GitRepositoryStatus.Available;
}
