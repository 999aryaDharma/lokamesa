namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-15 — typed outcome of the "open existing worktree" action
/// (spec 13 §16.2). Opening makes a Git-registered location the active
/// Document IDE filesystem/worktree context; it never switches branches,
/// creates branches, or mutates Git history.
/// </summary>
public enum GitWorktreeOpenStatus
{
    /// <summary>The worktree was already attached to the workspace and is now the active context.</summary>
    Opened = 0,

    /// <summary>
    /// The worktree is registered in Git but not attached to the current
    /// workspace. The caller must route the user through the existing
    /// folder-source attachment flow (FS-01); nothing is attached or
    /// detached automatically.
    /// </summary>
    RequiresAttachmentFlow = 1,

    /// <summary>The requested path is not a location Git registered as a worktree; no repository escape is possible.</summary>
    NotRegistered = 2,

    /// <summary>The worktree is registered but its directory is missing/unavailable (e.g. prunable).</summary>
    Unavailable = 3,

    /// <summary>The request was malformed (missing ids, or a non-canonical worktree path).</summary>
    InvalidRequest = 4,

    /// <summary>The underlying `git worktree list --porcelain` failed; see <see cref="GitWorktreeOpenResult.ListResult"/>.</summary>
    ListFailed = 5,

    /// <summary>The caller cancelled the operation.</summary>
    Cancelled = 6,

    /// <summary>The operation exceeded the runner timeout policy.</summary>
    TimedOut = 7,
}

/// <summary>
/// SCM-15 — one "open existing worktree" request.
/// </summary>
/// <param name="WorkspaceId">The active Document IDE workspace whose folder sources are consulted.</param>
/// <param name="WorkingDirectory">A directory inside the repository whose registered worktrees are listed.</param>
/// <param name="WorktreePath">The worktree to open; must resolve to a location Git registered as a worktree.</param>
public sealed record GitWorktreeOpenRequest(
    string WorkspaceId,
    string WorkingDirectory,
    string WorktreePath);

/// <summary>
/// SCM-15 — structured result of opening an existing worktree.
/// <see cref="Worktree"/> carries the read-only Git metadata of the matched
/// worktree; <see cref="ListResult"/> keeps the underlying listing
/// inspectable when the open failed during discovery.
/// </summary>
public sealed record GitWorktreeOpenResult(
    GitWorktreeOpenStatus Status,
    GitWorktreeInfo? Worktree,
    bool IsAttachedToWorkspace,
    string? ErrorMessage = null,
    GitWorktreeListResult? ListResult = null)
{
    /// <summary>True only when the worktree became the active filesystem context.</summary>
    public bool IsOpened => Status == GitWorktreeOpenStatus.Opened;
}
