namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-01 — Git executable capability state.
/// </summary>
public enum GitCapabilityStatus
{
    /// <summary>The system Git executable was discovered and answered `git --version`.</summary>
    Available = 0,

    /// <summary>Git is missing, not discoverable, or did not answer the version probe.</summary>
    Unavailable = 1,
}

/// <summary>
/// SCM-01 — result of the Git capability/version probe.
/// A machine without discoverable Git yields this typed <see cref="Unavailable"/>
/// state; it never throws and never pretends Git is usable.
/// </summary>
public sealed record GitCapabilityInfo(
    GitCapabilityStatus Status,
    string Executable,
    GitProcessResult ProbeResult,
    string? Version = null)
{
    public bool IsAvailable => Status == GitCapabilityStatus.Available;

    /// <summary>Human-readable reason when <see cref="IsAvailable"/> is false.</summary>
    public string? ErrorMessage => IsAvailable ? null : ProbeResult.ErrorMessage;
}
