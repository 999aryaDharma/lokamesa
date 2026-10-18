namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-11 — typed outcome of reading a worktree's remote/tracking state
/// (spec 13 §14.1/§14.2).
///
/// A repository with <b>no configured upstream</b> is represented by
/// <see cref="NoUpstream"/> — a first-class, non-error state, never a silent
/// empty result and never a bogus <c>0/0</c> ahead/behind.
/// </summary>
public enum GitRemoteOutcome
{
    /// <summary>The worktree has a configured upstream; <see cref="GitRemoteStateResult.State"/> carries it.</summary>
    Available = 0,

    /// <summary>The worktree is a Git repository but the current branch has no configured upstream (spec 13 §14.2).</summary>
    NoUpstream = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>State could not be derived for a non-classifiable reason; inspect the failure.</summary>
    Failed = 4,
}

/// <summary>
/// SCM-11 — upstream/tracking state of one worktree, derived from Git
/// (<c>git status --porcelain=v2 -z --branch</c>; spec 13 §14.1/§14.2).
///
/// Git remains the authority for remote-tracking state (spec 13 §4.2/§24):
/// this is computed data and is never persisted as a parallel database.
/// </summary>
/// <param name="WorktreeRoot">Enclosing worktree root the state was read for.</param>
/// <param name="BranchName">Current branch name; <c>null</c> on a detached HEAD.</param>
/// <param name="IsDetached">True when HEAD is detached (no branch).</param>
/// <param name="HeadCommit">Current HEAD commit id, or <c>null</c> for an unborn branch.</param>
/// <param name="RemoteName">Remote of the configured upstream (e.g. <c>origin</c>); <c>null</c> without an upstream or when the upstream is a local branch.</param>
/// <param name="UpstreamReference">Configured upstream reference (e.g. <c>origin/main</c>); <c>null</c> without one.</param>
/// <param name="Ahead">Local commits not yet on the upstream; <c>null</c> when there is no upstream or when Git cannot compute the count (the upstream's remote-tracking ref is absent).</param>
/// <param name="Behind">Upstream commits not yet in local history; <c>null</c> when there is no upstream or when Git cannot compute the count (the upstream's remote-tracking ref is absent).</param>
public sealed record GitRemoteState(
    string WorktreeRoot,
    string? BranchName,
    bool IsDetached,
    string? HeadCommit,
    string? RemoteName,
    string? UpstreamReference,
    int? Ahead,
    int? Behind)
{
    /// <summary>
    /// True when Git reported a configured upstream branch (spec 13 §14.2).
    /// A configured upstream is independent of computable counters:
    /// <see cref="Ahead"/>/<see cref="Behind"/> are <c>null</c> when there is no
    /// upstream <i>or</i> when the upstream's remote-tracking ref is absent.
    /// </summary>
    public bool HasUpstream => !string.IsNullOrEmpty(UpstreamReference);
}

/// <summary>
/// SCM-11 — typed result of reading remote/tracking state.
///
/// <see cref="GitRemoteOutcome.NoUpstream"/> is a resolved, non-error state
/// (the state is present with <see cref="GitRemoteState.HasUpstream"/> false),
/// so callers can report "no upstream" explicitly instead of showing <c>0/0</c>
/// (spec 13 §14.2).
/// </summary>
/// <param name="Outcome">Typed outcome vocabulary.</param>
/// <param name="State">Derived state; present for <see cref="GitRemoteOutcome.Available"/> and <see cref="GitRemoteOutcome.NoUpstream"/>.</param>
/// <param name="Failure">Originating typed Git failure when the outcome is not a resolved state.</param>
public sealed record GitRemoteStateResult(
    GitRemoteOutcome Outcome,
    GitRemoteState? State,
    GitProcessResult? Failure = null)
{
    /// <summary>True for both resolved states (upstream present or absent); false for failures.</summary>
    public bool IsAvailable =>
        Outcome is GitRemoteOutcome.Available or GitRemoteOutcome.NoUpstream && State is not null;

    /// <summary>True only when a configured upstream is present.</summary>
    public bool HasUpstream => Outcome == GitRemoteOutcome.Available && State?.HasUpstream == true;
}

/// <summary>
/// SCM-11 — typed outcome of one explicit Fetch (<c>git fetch</c>, spec 13
/// §14.1). Failures are inspectable statuses with the originating
/// <see cref="GitProcessResult"/>, never silent fallbacks; "no upstream" is a
/// first-class non-error state, and remote failures are typed.
/// </summary>
public enum GitFetchOutcome
{
    /// <summary>Remote-tracking state was updated; <see cref="GitFetchResult.Remote"/> carries the re-read state.</summary>
    Succeeded = 0,

    /// <summary>The current branch has no configured upstream; nothing ran (spec 13 §14.2).</summary>
    NoUpstream = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>The remote required authentication that the disabled terminal prompt cannot supply; the fetch did not hang.</summary>
    AuthenticationRequired = 4,

    /// <summary>The remote could not be reached (DNS, connection, or the remote path is not a usable repository).</summary>
    HostUnreachable = 5,

    /// <summary>The remote rejected the fetch (spec 13 §20).</summary>
    Rejected = 6,

    /// <summary>The remote/local operation was denied by filesystem or remote permissions.</summary>
    PermissionDenied = 7,

    /// <summary>Git ran and failed for a reason this service does not classify further; inspect <see cref="GitFetchResult.Failure"/>.</summary>
    Failed = 8,
}

/// <summary>
/// SCM-11 — typed result of one explicit Fetch (spec 13 §14.1/§14.2/§20).
///
/// A successful fetch updates only remote-tracking state: the working-tree
/// document contents and the index are never touched (spec 13 §14.1). On
/// success <see cref="Remote"/> carries the tracking state re-read from Git
/// after the fetch, so the pane renders the updated ahead/behind.
/// </summary>
/// <param name="Outcome">Typed outcome vocabulary.</param>
/// <param name="Remote">Tracking state re-read after the fetch; also present for <see cref="GitFetchOutcome.NoUpstream"/>.</param>
/// <param name="Failure">Originating typed Git failure when the outcome is not a success.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable code for typed non-success outcomes.</param>
public sealed record GitFetchResult(
    GitFetchOutcome Outcome,
    GitRemoteState? Remote,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when the fetch ran and updated remote-tracking state.</summary>
    public bool IsSuccess => Outcome == GitFetchOutcome.Succeeded;
}
