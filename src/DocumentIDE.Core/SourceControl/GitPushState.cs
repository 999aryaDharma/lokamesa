namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-13 — typed outcome of one explicit Push (<c>git push</c>, spec 13 §14.4).
/// Failures are inspectable statuses with the originating
/// <see cref="GitProcessResult"/>, never silent fallbacks; "no upstream" is a
/// first-class non-error state, and remote failures are typed.
/// </summary>
public enum GitPushOutcome
{
    /// <summary>The configured upstream's branch was updated; <see cref="GitPushResult.Remote"/> carries the re-read state.</summary>
    Succeeded = 0,

    /// <summary>The current branch has no configured upstream; nothing ran (spec 13 §14.2/§14.4).</summary>
    NoUpstream = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>The remote required authentication that the disabled terminal prompt cannot supply; the push did not hang.</summary>
    AuthenticationRequired = 4,

    /// <summary>The remote rejected the push (non-fast-forward or a rejecting hook); local commits are preserved.</summary>
    Rejected = 5,

    /// <summary>The remote/local operation was denied by filesystem or remote permissions.</summary>
    PermissionDenied = 6,

    /// <summary>The remote could not be reached (DNS, connection, or the remote path is not a usable repository).</summary>
    HostUnreachable = 7,

    /// <summary>Git ran and failed for a reason this service does not classify further; inspect <see cref="GitPushResult.Failure"/>.</summary>
    Failed = 8,
}

/// <summary>
/// SCM-13 — typed result of one explicit Push (spec 13 §14.4/§19/§20).
///
/// An explicit push updates the branch configured as the current branch's
/// upstream; it never force-pushes and never guesses a default remote for a
/// branch without a configured upstream (that is the typed
/// <see cref="GitPushOutcome.NoUpstream"/> state). A rejected push never
/// rewrites local history: <see cref="Remote"/> stays <c>null</c> and the local
/// commits remain in place.
/// </summary>
/// <param name="Outcome">Typed outcome vocabulary.</param>
/// <param name="Remote">Tracking state re-read after the push; also present for <see cref="GitPushOutcome.NoUpstream"/>.</param>
/// <param name="Failure">Originating typed Git failure when the outcome is not a success.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable code for typed non-success outcomes.</param>
public sealed record GitPushResult(
    GitPushOutcome Outcome,
    GitRemoteState? Remote,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when the push ran and updated the configured upstream's branch.</summary>
    public bool IsSuccess => Outcome == GitPushOutcome.Succeeded;
}
