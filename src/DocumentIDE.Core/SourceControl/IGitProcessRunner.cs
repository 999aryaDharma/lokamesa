using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-01 — backend process boundary for Git (spec 13 §18).
///
/// Frontend code cannot invoke <c>git.exe</c>; application services consume
/// Git exclusively through this contract, which is implemented by the
/// controlled process runner in Infrastructure. Implementations return typed,
/// inspectable results and never throw for operational Git failures.
/// </summary>
public interface IGitProcessRunner
{
    /// <summary>
    /// Probes the system Git executable (`git --version`). A machine without
    /// discoverable Git yields a typed unavailable state, never an exception.
    /// </summary>
    Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one Git invocation with structured arguments and a controlled
    /// working directory/environment. Operational outcomes (failure, timeout,
    /// cancellation, Git not installed) are reported through
    /// <see cref="GitProcessResult.Status"/>.
    /// </summary>
    Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default);
}
