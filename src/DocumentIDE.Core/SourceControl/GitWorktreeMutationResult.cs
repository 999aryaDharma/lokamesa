using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-16 — typed outcome of creating a worktree from an existing Git ref
/// (spec 13 §16.3). A worktree is only ever created from a ref that already
/// exists: Document IDE never creates a branch as a side effect (no <c>-b</c>,
/// no remote-tracking DWIM), so "create worktree" can never grow into branch
/// management.
/// </summary>
public enum GitWorktreeCreateStatus
{
    /// <summary>Git created and registered the worktree at the requested location.</summary>
    Succeeded = 0,

    /// <summary>The request was malformed (missing working directory or target path, or an unusable ref token).</summary>
    InvalidRequest = 1,

    /// <summary>The target path failed path-safety validation (relative, traversal, invalid characters, <c>.git</c> segment, …).</summary>
    InvalidTargetPath = 2,

    /// <summary>Git could not be discovered/started (e.g. not installed).</summary>
    GitUnavailable = 3,

    /// <summary>The working directory is not inside a Git repository.</summary>
    NotARepository = 4,

    /// <summary>The requested ref does not resolve to an existing commit.</summary>
    RefNotFound = 5,

    /// <summary>
    /// The requested branch is already checked out in another worktree. Git
    /// refuses this itself; the typed preflight reports the conflicting
    /// location explicitly instead of surfacing a raw failure.
    /// </summary>
    BranchAlreadyCheckedOut = 6,

    /// <summary>The target path already exists or is already registered as a worktree.</summary>
    TargetAlreadyExists = 7,

    /// <summary>Git ran but the worktree creation failed; <see cref="GitWorktreeCreateResult.ProcessResult"/> is inspectable.</summary>
    CreateFailed = 8,

    /// <summary>The caller cancelled the operation.</summary>
    Cancelled = 9,

    /// <summary>The operation exceeded the runner timeout policy.</summary>
    TimedOut = 10,
}

/// <summary>SCM-16 — one "create worktree from existing ref" request.</summary>
/// <param name="WorkingDirectory">A directory inside the repository the worktree is created from (resolved to its true worktree root before any mutation).</param>
/// <param name="TargetPath">Fully qualified local path for the new worktree; must not exist (except as an empty directory) and must not be registered already.</param>
/// <param name="Ref">An existing local branch, tag, remote-tracking ref, or commit id. Any ref form that would require creating a branch is rejected.</param>
public sealed record GitWorktreeCreateRequest(
    string WorkingDirectory,
    string TargetPath,
    string Ref);

/// <summary>
/// SCM-16 — structured result of creating a worktree.
/// <see cref="Worktree"/> carries the read-only Git metadata of the created
/// worktree when it could be re-read after the mutation; a failed
/// post-create listing never hides the successful mutation, it is exposed via
/// <see cref="ListResult"/> instead.
/// </summary>
public sealed record GitWorktreeCreateResult(
    GitWorktreeCreateStatus Status,
    GitWorktreeInfo? Worktree,
    GitProcessResult? ProcessResult = null,
    GitWorktreeListResult? ListResult = null,
    string? ErrorMessage = null)
{
    /// <summary>True only when Git created and registered the worktree.</summary>
    public bool IsSuccess => Status == GitWorktreeCreateStatus.Succeeded;
}

/// <summary>
/// SCM-16 — typed outcome of removing a worktree (spec 13 §16.4).
/// Removal is destructive: dirty, locked, main, and missing worktrees are
/// blocked with typed states, and forced removal does not exist in V1.
/// </summary>
public enum GitWorktreeRemoveStatus
{
    /// <summary>Git removed and unregistered the clean worktree.</summary>
    Succeeded = 0,

    /// <summary>The request was malformed (missing working directory or worktree path).</summary>
    InvalidRequest = 1,

    /// <summary>The worktree path failed path-safety validation.</summary>
    InvalidTargetPath = 2,

    /// <summary>Git could not be discovered/started (e.g. not installed).</summary>
    GitUnavailable = 3,

    /// <summary>The working directory is not inside a Git repository.</summary>
    NotARepository = 4,

    /// <summary>The path is not registered as a worktree of the repository; no repository escape is possible.</summary>
    NotRegistered = 5,

    /// <summary>The main working tree cannot be removed through V1.</summary>
    IsMainWorktree = 6,

    /// <summary>The worktree is locked; forced removal is out of scope for V1.</summary>
    WorktreeLocked = 7,

    /// <summary>The worktree is registered but its directory is missing/unavailable; use the refresh action for stale metadata.</summary>
    Unavailable = 8,

    /// <summary>The worktree contains uncommitted changes (spec 13 §16.4); <see cref="GitWorktreeRemoveResult.Changes"/> lists them.</summary>
    DirtyWorktree = 9,

    /// <summary>A removal precondition could not be established (the worktree registry or the worktree's cleanliness could not be read); nothing was removed (fail closed).</summary>
    PreflightFailed = 10,

    /// <summary>Git ran but the removal failed; <see cref="GitWorktreeRemoveResult.ProcessResult"/> is inspectable.</summary>
    RemoveFailed = 11,

    /// <summary>The caller cancelled the operation.</summary>
    Cancelled = 12,

    /// <summary>The operation exceeded the runner timeout policy.</summary>
    TimedOut = 13,
}

/// <summary>SCM-16 — one "remove worktree" request.</summary>
/// <param name="WorkingDirectory">A directory inside the repository that owns the worktree (resolved to its true worktree root before any mutation).</param>
/// <param name="WorktreePath">Canonical path of the worktree to remove; must be exactly Git's registered location.</param>
public sealed record GitWorktreeRemoveRequest(
    string WorkingDirectory,
    string WorktreePath);

/// <summary>
/// SCM-16 — structured result of removing a worktree.
/// <see cref="Changes"/> carries the current change list when removal was
/// blocked because the worktree is dirty (spec 13 §16.4). <see cref="ListResult"/>
/// carries the refreshed registry after a successful removal when it could be
/// re-read.
/// </summary>
public sealed record GitWorktreeRemoveResult(
    GitWorktreeRemoveStatus Status,
    GitWorktreeInfo? Worktree = null,
    IReadOnlyList<GitStatusEntry>? Changes = null,
    GitProcessResult? ProcessResult = null,
    GitWorktreeListResult? ListResult = null,
    string? ErrorMessage = null)
{
    /// <summary>True only when Git removed and unregistered the worktree.</summary>
    public bool IsSuccess => Status == GitWorktreeRemoveStatus.Succeeded;
}

/// <summary>
/// SCM-16 — typed outcome of the safe "Refresh Worktrees" action
/// (spec 13 §16.5): Git worktree metadata refresh/prune followed by a fresh
/// listing. Pruning never deletes a valid existing worktree just because a
/// path is temporarily unavailable — Git only prunes metadata whose worktree
/// directory is gone and that is not locked.
/// </summary>
public enum GitWorktreeRefreshStatus
{
    /// <summary>Prune ran and the registry was re-read.</summary>
    Succeeded = 0,

    /// <summary>Git could not be discovered/started (e.g. not installed).</summary>
    GitUnavailable = 1,

    /// <summary>The working directory is not inside a Git repository.</summary>
    NotARepository = 2,

    /// <summary>The refresh failed; <see cref="GitWorktreeRefreshResult.ProcessResult"/> is inspectable.</summary>
    Failed = 3,

    /// <summary>The caller cancelled the operation.</summary>
    Cancelled = 4,

    /// <summary>The operation exceeded the runner timeout policy.</summary>
    TimedOut = 5,
}

/// <summary>
/// SCM-16 — structured result of refreshing worktree metadata.
/// On success <see cref="ListResult"/> is present and carries the refreshed,
/// Git-derived registry.
/// </summary>
public sealed record GitWorktreeRefreshResult(
    GitWorktreeRefreshStatus Status,
    GitWorktreeListResult? ListResult = null,
    GitProcessResult? ProcessResult = null,
    string? ErrorMessage = null)
{
    /// <summary>True only when prune succeeded and the registry was re-read.</summary>
    public bool IsSuccess => Status == GitWorktreeRefreshStatus.Succeeded;
}
