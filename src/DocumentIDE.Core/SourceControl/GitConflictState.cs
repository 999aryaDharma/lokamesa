using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-14 — typed outcome of one unmerged-state read (spec 13 §20/§21).
///
/// A conflicted repository is a resolved, inspectable state — never an
/// exception and never a silent empty list: <see cref="Available"/> means the
/// unmerged entries below are present, <see cref="NoConflicts"/> means Git was
/// read successfully and reports none, and every other value is a typed
/// inability to observe Git safely.
/// </summary>
public enum GitConflictStatus
{
    /// <summary>Git reported one or more unmerged entries; <see cref="GitConflictState.Conflicts"/> carries them.</summary>
    Available = 0,

    /// <summary>Git status was read successfully and reports no unmerged entries.</summary>
    NoConflicts = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing was read.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>The worktree disappeared before the conflict state could be read.</summary>
    WorktreeMissing = 4,

    /// <summary>The Git index is locked by another operation, so safety cannot be established.</summary>
    IndexLocked = 5,

    /// <summary>Git status could not be read or parsed; no safe conclusion was drawn.</summary>
    Unreadable = 6,

    /// <summary>Discovery/status failed for a reason this service does not classify further.</summary>
    Failed = 7,
}

/// <summary>
/// SCM-14 — the unsafe Source Control actions the conflict shell reasons about
/// (spec 13 §21). <see cref="Commit"/> and <see cref="CommitAll"/> are separate
/// flows exactly as in SCM-07; <see cref="Discard"/> and <see cref="Restore"/>
/// are path-scoped; <see cref="Pull"/> is whole-worktree.
/// </summary>
public enum GitConflictAction
{
    /// <summary>Commit the current staged state.</summary>
    Commit = 0,

    /// <summary>Stage eligible changes, then commit.</summary>
    CommitAll = 1,

    /// <summary>Discard one file's working-tree change.</summary>
    Discard = 2,

    /// <summary>Restore one file from a selected commit.</summary>
    Restore = 3,

    /// <summary>Pull the configured upstream with the fast-forward-only policy.</summary>
    Pull = 4,
}

/// <summary>
/// SCM-14 — how an affected document can be opened for manual resolution.
/// <see cref="OpenInWord"/> means the caller's existing guarded Word open route;
/// <see cref="OpenDocument"/> means the caller's existing document-open seam
/// (e.g. the Markdown editor). The shell never opens an arbitrary path itself.
/// </summary>
public enum GitConflictOpenAction
{
    /// <summary>No registered document exists for the path; the shell must not open it.</summary>
    None = 0,

    /// <summary>Open the registered DOCX through the existing guarded Word route.</summary>
    OpenInWord = 1,

    /// <summary>Open the registered document through the existing document-open seam.</summary>
    OpenDocument = 2,
}

/// <summary>
/// SCM-14 — one real unmerged Git entry (spec 13 §21).
///
/// <paramref name="Path"/> is the worktree-relative path exactly as Git
/// reported it; <paramref name="Kind"/> is SCM-02's porcelain-v2 conflict
/// classification (this sprint does not re-parse status). The entry carries a
/// <see cref="DocumentId"/> only for an already-registered document and never a
/// local filesystem path — the existing open action is identified by document
/// id alone.
/// </summary>
/// <param name="Path">Worktree-relative path exactly as Git reported it.</param>
/// <param name="Kind">SCM-02 conflict kind (UU/AA/DD/AU/UA/DU/UD or None).</param>
/// <param name="StatusCharacters">Two-character porcelain status, preserved verbatim.</param>
/// <param name="DocumentId">Registered document id, or <c>null</c> when the path is not registered.</param>
/// <param name="OpenAction">How the caller may open it for manual resolution.</param>
public sealed record GitConflictEntry(
    string Path,
    GitConflictKind Kind,
    string StatusCharacters,
    string? DocumentId,
    GitConflictOpenAction OpenAction)
{
    /// <summary>True when the conflicted path is a DOCX document (never auto-merged; spec 13 §21).</summary>
    public bool IsDocx => Path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the caller can offer the existing manual-open action.</summary>
    public bool CanOpenDocument => OpenAction != GitConflictOpenAction.None && DocumentId is not null;
}

/// <summary>
/// SCM-14 — read-only snapshot of one worktree's unmerged state (spec 13 §21).
/// The snapshot is derived on demand from Git; it is never persisted
/// (spec 13 §4.2/§24).
/// </summary>
/// <param name="WorktreeRoot">Resolved true worktree root.</param>
/// <param name="BranchName">Current branch, or <c>null</c> when detached.</param>
/// <param name="IsDetached">True when HEAD is detached.</param>
/// <param name="HeadCommit">Current HEAD commit, or <c>null</c> for an unborn branch.</param>
/// <param name="Conflicts">The unmerged entries Git reported, in Git's order.</param>
public sealed record GitConflictState(
    string WorktreeRoot,
    string? BranchName,
    bool IsDetached,
    string? HeadCommit,
    IReadOnlyList<GitConflictEntry> Conflicts);

/// <summary>
/// SCM-14 — typed result of one unmerged-state read (spec 13 §20/§21).
/// Failures carry their originating redacted <see cref="GitProcessResult"/> and
/// a human-readable <paramref name="Message"/>; no credential can reach either.
/// </summary>
public sealed record GitConflictStateResult(
    GitConflictStatus Status,
    GitConflictState? State,
    GitProcessResult? Failure,
    string? Message)
{
    /// <summary>True only when Git reported at least one unmerged entry.</summary>
    public bool HasConflicts => Status == GitConflictStatus.Available && State is { Conflicts.Count: > 0 };
}

/// <summary>
/// SCM-14 — typed answer to "may this unsafe action run?" (spec 13 §21).
///
/// <see cref="IsBlocked"/> is deliberate: a conflicted repository (or a state
/// that could not be read safely) refuses the action before it can overwrite
/// conflict state. <see cref="ConflictedPaths"/> names exactly which entries
/// caused the block so the caller can render it.
/// </summary>
/// <param name="Action">The action that was evaluated.</param>
/// <param name="IsBlocked">True when the action must not run.</param>
/// <param name="ErrorCode">Machine-readable reason (<c>ConflictedWorktree</c>, <c>ConflictedPath</c>, a typed status, or <c>Allowed</c>).</param>
/// <param name="Message">Safe explanation naming the action and why it is blocked.</param>
/// <param name="ConflictedPaths">Worktree-relative conflict paths behind the decision.</param>
public sealed record GitConflictActionDecision(
    GitConflictAction Action,
    bool IsBlocked,
    string? ErrorCode,
    string Message,
    IReadOnlyList<string> ConflictedPaths);
