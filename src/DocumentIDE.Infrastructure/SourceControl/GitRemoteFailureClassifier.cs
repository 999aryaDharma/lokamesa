using System;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-11 — remote-operation failure signatures that complement the SCM-01
/// <see cref="GitFailureClassifier"/> (spec 13 §20).
///
/// The SCM-01 classifier owns the shared, local-repository vocabulary
/// (authentication, permissions, hooks, rejection, divergence, …) and has
/// already classified the runner's diagnostics into
/// <see cref="GitProcessResult.ErrorKind"/>. This type deliberately owns
/// <b>only</b> the signatures that are specific to a <i>remote</i> operation
/// and that SCM-01 leaves <see cref="GitProcessErrorKind.Unknown"/>. Keeping
/// them beside <see cref="GitFailureClassifier"/> — instead of a second copy of
/// the shared markers inside the fetch service — removes the drift risk of two
/// parallel marker lists (F5).
/// </summary>
public static class GitRemoteFailureClassifier
{
    /// <summary>
    /// Authentication wording specific to remote credential negotiation that
    /// SCM-01 does not classify, including an HTTP 401
    /// (<c>unable to access … The requested URL returned error: 401</c>), which
    /// is auth-required rather than host-unreachable (F5), and the errors Git
    /// and Git Credential Manager emit when prompting has been disabled.
    /// </summary>
    private static readonly string[] RemoteAuthenticationMarkers =
    {
        "authentication required",
        "could not read password",
        "invalid username or password",
        "support for password authentication was removed",
        "requested url returned error: 401",
        "cannot prompt",
        "unable to get password from user",
    };

    /// <summary>
    /// Git's wording when a remote cannot be reached: DNS/connection failures
    /// and a remote path that is not a usable repository (spec 13 §20). These
    /// are the remote, not local-repository, conditions SCM-01 leaves
    /// <see cref="GitProcessErrorKind.Unknown"/>.
    ///
    /// F9 product decision: this set intentionally conflates "host unreachable"
    /// with "the remote path is not a usable repository". Both require the same
    /// corrective user action (verify the remote URL and reachability) and both
    /// retain Git's exact diagnostic on the typed <c>Failure</c> payload, so V1
    /// does not split them into separate outcomes. See the sprint handoff.
    /// </summary>
    private static readonly string[] HostUnreachableMarkers =
    {
        "could not resolve host",
        "could not resolve hostname",
        "unable to access",
        "could not read from remote repository",
        "connection refused",
        "connection timed out",
        "network is unreachable",
        "no route to host",
        "failed to connect",
        "the remote end hung up",
        "does not appear to be a git repository",
        "repository not found",
        "operation timed out",
    };

    /// <summary>
    /// True when the failure's diagnostics identify a remote authentication
    /// requirement that SCM-01 does not already classify. Only Git-provided
    /// diagnostic text is inspected; no secret is copied into a result.
    /// </summary>
    public static bool IsAuthenticationRequired(GitProcessResult failure) =>
        MatchesAny(failure, RemoteAuthenticationMarkers);

    /// <summary>True when the failure's diagnostics identify an unreachable remote.</summary>
    public static bool IsHostUnreachable(GitProcessResult failure) =>
        MatchesAny(failure, HostUnreachableMarkers);

    private static bool MatchesAny(GitProcessResult failure, string[] markers) =>
        Array.Exists(
            markers,
            marker =>
                failure.StandardError.Contains(marker, StringComparison.OrdinalIgnoreCase)
                || failure.StandardOutput.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
