namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-06 — typed outcome of one stage/unstage request (spec 13 §10/§20).
/// Failures are inspectable statuses with the originating
/// <see cref="GitProcessResult"/>, never silent fallbacks.
/// </summary>
public enum GitStageOutcome
{
    /// <summary>The mutation succeeded; <see cref="GitStageResult.Status"/> carries the re-read status.</summary>
    Succeeded = 0,

    /// <summary>
    /// The client-supplied change path is not a worktree-relative file path
    /// (absolute, escaping the worktree, blank or a directory); nothing ran.
    /// </summary>
    RejectedPath = 1,

    /// <summary>The discovery path is not inside any Git worktree; nothing ran.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>Git ran and failed, or the worktree/discovery failed; inspect <see cref="GitStageResult.Failure"/>.</summary>
    Failed = 4,
}

/// <summary>
/// SCM-06 — typed result of a stage/unstage mutation (spec 13 §10/§20/§24).
///
/// On success the real Git index has been mutated and <see cref="Status"/>
/// carries a fresh status snapshot re-read from Git; Document IDE keeps no
/// staging snapshot of its own. On failure nothing was widened silently:
/// <see cref="Failure"/> (or <see cref="ErrorCode"/>/<see cref="Message"/> for
/// a rejected path) is the inspectable reason.
/// </summary>
/// <param name="Outcome">Typed outcome vocabulary.</param>
/// <param name="Status">Re-read status result after a successful mutation; may itself be a typed failure.</param>
/// <param name="Failure">Originating typed Git failure when the outcome is not a success.</param>
/// <param name="Message">Safe, human-readable context (never raw secrets).</param>
/// <param name="ErrorCode">Machine-readable rejection code when <see cref="Outcome"/> is <see cref="GitStageOutcome.RejectedPath"/>.</param>
public sealed record GitStageResult(
    GitStageOutcome Outcome,
    GitStatusResult? Status,
    GitProcessResult? Failure,
    string? Message,
    string? ErrorCode = null)
{
    /// <summary>True only when the mutation completed against Git.</summary>
    public bool IsSuccess => Outcome == GitStageOutcome.Succeeded;
}
