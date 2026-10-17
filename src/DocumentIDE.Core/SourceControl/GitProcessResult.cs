namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-01 — typed outcome of one controlled Git process invocation.
/// Failures are inspectable statuses, never silent fallbacks (spec 13 §20).
/// </summary>
public enum GitProcessStatus
{
    /// <summary>Git ran and exited with code 0.</summary>
    Succeeded = 0,

    /// <summary>Git could not be discovered/started (e.g. not installed).</summary>
    Unavailable = 1,

    /// <summary>Git started but the operation failed (non-zero exit or blocked precondition).</summary>
    Failed = 2,

    /// <summary>The timeout policy elapsed; the process tree was terminated.</summary>
    TimedOut = 3,

    /// <summary>The caller cancelled; the process tree was terminated.</summary>
    Cancelled = 4,
}

/// <summary>
/// SCM-01 — typed classification of a Git failure (spec 13 §20).
/// Classification is conservative: only unambiguous signatures are mapped,
/// everything else stays <see cref="Unknown"/> rather than guessing.
/// </summary>
public enum GitProcessErrorKind
{
    None = 0,
    Unknown = 1,
    GitNotInstalled = 2,
    GitRepositoryNotFound = 3,
    GitWorktreeInvalid = 4,
    GitLockContention = 5,
    GitAuthRequired = 6,
    GitPermissionDenied = 7,
    GitCommitHookFailed = 8,
    GitPushRejected = 9,
    GitPullNotFastForward = 10,
    GitConflict = 11,
    GitUntrackedConflict = 12,
    GitDirtyWorktree = 13,
}

/// <summary>
/// SCM-01 — structured result of one Git process invocation, including
/// stream capture and exit-code inspection. <see cref="Status"/> and
/// <see cref="ErrorKind"/> together are the inspectable typed outcome that
/// downstream Source Control sprints surface instead of raw exceptions.
/// </summary>
public sealed record GitProcessResult(
    GitProcessStatus Status,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    GitProcessErrorKind ErrorKind = GitProcessErrorKind.None,
    string? ErrorMessage = null)
{
    /// <summary>True only when Git ran and exited successfully.</summary>
    public bool IsSuccess => Status == GitProcessStatus.Succeeded;

    /// <summary>
    /// SCM-05 — raw stdout bytes when the request asked for
    /// <see cref="GitProcessRequest.CaptureBinaryOutput"/>; null otherwise.
    /// When present, <see cref="StandardOutput"/> is a lenient UTF-8 decode
    /// kept for diagnostics only — binary content must be read from here.
    /// </summary>
    public byte[]? StandardOutputBytes { get; init; }
}
