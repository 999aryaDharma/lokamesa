using System;
using System.Collections.Generic;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-01 — configuration for <see cref="GitProcessRunner"/>.
/// </summary>
public sealed class GitProcessRunnerOptions
{
    /// <summary>
    /// SCM-01 — process-level policy applied to every spawned Git process
    /// (spec 13 §18/§23). A hidden process must never hang waiting for
    /// interactive credentials, a pager, or an editor, and stderr signatures
    /// must not depend on the host UI locale. These keys always win over any
    /// request-level environment override; a caller cannot disable them.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ProtectedEnvironment =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_PAGER"] = "cat",
            ["LC_ALL"] = "C",
            ["GIT_EDITOR"] = ":",
            ["GIT_SEQUENCE_EDITOR"] = ":",
        };

    /// <summary>Default options: system Git from <c>PATH</c>, bounded timeouts.</summary>
    public static GitProcessRunnerOptions Default { get; } = new();

    /// <summary>
    /// Git executable to invoke. <c>null</c> (default) resolves the system
    /// <c>git</c> from <c>PATH</c>. Tests point this at a missing executable to
    /// exercise the typed unavailable state.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// Timeout policy for regular invocations (spec 13 §18). A request may
    /// override this; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
    /// disables the timeout for long remote operations.
    /// </summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Timeout for the `git --version` capability probe.</summary>
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// SCM-01 — computes the environment variables applied to a spawned Git
    /// process: request overrides first, protected policy last, so the policy
    /// can never be disabled by a caller (spec 13 §18/§23).
    /// </summary>
    public static IReadOnlyDictionary<string, string> ResolveEnvironmentVariables(
        IReadOnlyDictionary<string, string>? overrides)
    {
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (overrides is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> variable in overrides)
            {
                if (!ProtectedEnvironment.ContainsKey(variable.Key))
                {
                    effective[variable.Key] = variable.Value;
                }
            }
        }

        foreach (KeyValuePair<string, string> variable in ProtectedEnvironment)
        {
            effective[variable.Key] = variable.Value;
        }

        return effective;
    }
}
