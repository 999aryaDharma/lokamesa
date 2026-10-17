using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-11 — Git remote/tracking state and explicit Fetch application service
/// (spec 13 §14.1/§14.2/§18/§19/§20/§23).
///
/// <list type="bullet">
/// <item>Read <b>upstream/tracking state</b> from Git — the configured upstream
/// reference, the remote name and the ahead/behind counts (spec 13 §14.2). A
/// branch with no configured upstream is a first-class typed state, never a
/// silent <c>0/0</c>.</item>
/// <item><b>Fetch</b> runs through the SCM-01 controlled process boundary with
/// structured arguments (<c>git fetch</c>, never a shell), against the resolved
/// true worktree root — which is also the per-worktree mutation queue key, so
/// concurrent fetch/commit/stage operations on one worktree serialize
/// (spec 13 §19).</item>
/// </list>
///
/// Fetch never modifies working-tree document contents or the index
/// (spec 13 §14.1); it updates only remote-tracking state. Remote failures are
/// typed; the SCM-01 runner pins <c>GIT_TERMINAL_PROMPT=0</c> and closes child
/// stdin, so an authentication prompt fails fast as a typed
/// <see cref="GitFetchOutcome.AuthenticationRequired"/> instead of hanging.
/// </summary>
public sealed class GitRemoteService
{
    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;

    public GitRemoteService(IGitProcessRunner runner, GitRepositoryService repositories)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <summary>
    /// Reads the worktree's upstream/tracking state (spec 13 §14.1/§14.2) for a
    /// workspace folder or active-document path. The state is derived from the
    /// same machine-readable Git status the Source Control pane already uses
    /// (<c>branch.upstream</c> / <c>branch.ab</c>). A branch with no configured
    /// upstream returns <see cref="GitRemoteOutcome.NoUpstream"/> with the state
    /// present and ahead/behind <c>null</c> rather than a bogus <c>0/0</c>.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public async Task<GitRemoteStateResult> GetRemoteStateAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return new GitRemoteStateResult(DiscoveryFailure(context), null, context.Failure);
        }

        GitStatusResult status = await _repositories.GetStatusAsync(context.WorktreeRoot!, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            return new GitRemoteStateResult(MapStatusFailure(status), null, status.Failure);
        }

        GitStatusSnapshot snapshot = status.Snapshot;
        GitRemoteState state = new(
            snapshot.WorktreeRoot,
            snapshot.BranchName,
            snapshot.IsDetached,
            snapshot.HeadCommit,
            RemoteNameFromUpstream(snapshot.UpstreamReference),
            snapshot.UpstreamReference,
            snapshot.HasUpstream ? snapshot.Ahead : null,
            snapshot.HasUpstream ? snapshot.Behind : null);

        return new GitRemoteStateResult(
            snapshot.HasUpstream ? GitRemoteOutcome.Available : GitRemoteOutcome.NoUpstream,
            state);
    }

    /// <summary>
    /// Runs one explicit Fetch (<c>git fetch</c>, spec 13 §14.1) against the
    /// resolved true worktree root and re-reads the tracking state from Git.
    ///
    /// Fetch is scoped to the current branch's configured upstream remote
    /// (spec 13 §14.2): with no configured upstream, nothing runs and the typed
    /// <see cref="GitFetchOutcome.NoUpstream"/> state is returned. Fetch updates
    /// only remote-tracking state — working-tree document contents and the index
    /// are never touched (spec 13 §14.1). The invocation carries no
    /// <c>pull</c>/<c>push</c>/<c>merge</c>/<c>rebase</c> or scope-broadening
    /// option (spec 13 §14.4/§23).
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public async Task<GitFetchResult> FetchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            GitRemoteOutcome discovery = DiscoveryFailure(context);
            return new GitFetchResult(
                discovery switch
                {
                    GitRemoteOutcome.NoRepository => GitFetchOutcome.NoRepository,
                    GitRemoteOutcome.GitUnavailable => GitFetchOutcome.GitUnavailable,
                    _ => GitFetchOutcome.Failed,
                },
                null,
                context.Failure,
                context.Failure?.ErrorMessage ?? "The repository could not be discovered.");
        }

        string worktreeRoot = context.WorktreeRoot!;

        // No configured upstream: a first-class non-error state, and no fetch
        // runs (spec 13 §14.2). The state is still re-read so the pane can show
        // the explicit "no upstream" state.
        if (context.UpstreamReference is null)
        {
            GitRemoteStateResult noUpstream = await GetRemoteStateAsync(worktreeRoot, cancellationToken);
            return new GitFetchResult(
                GitFetchOutcome.NoUpstream,
                noUpstream.State,
                null,
                "The current branch has no configured upstream; nothing was fetched.",
                "NoUpstream");
        }

        // A structured argument list — exactly ["fetch"], never a shell string,
        // and never an option that widens the fetched scope (spec 13 §18/§23).
        // `fetch` is routed through the SCM-01 per-worktree mutation queue by the
        // runner's classifier, keyed by this working directory (spec 13 §19).
        GitProcessResult fetch = await _runner.RunAsync(
            new GitProcessRequest(new[] { "fetch" }, worktreeRoot),
            cancellationToken);

        if (!fetch.IsSuccess)
        {
            return ClassifyFetchFailure(fetch);
        }

        GitRemoteStateResult state = await GetRemoteStateAsync(worktreeRoot, cancellationToken);
        return new GitFetchResult(GitFetchOutcome.Succeeded, state.State, null, null);
    }

    /// <summary>
    /// Classifies a failed <c>git fetch</c> into a typed outcome
    /// (spec 13 §14.1/§20). Classification is conservative and ordered from the
    /// most unambiguous signature to the most heuristic; Git diagnostics are
    /// pinned to <c>LC_ALL=C</c> by the runner, so the markers are locale-stable.
    /// A raw failure is never surfaced as a success.
    /// </summary>
    private static GitFetchResult ClassifyFetchFailure(GitProcessResult failure)
    {
        if (failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            return Failure(
                GitFetchOutcome.GitUnavailable,
                failure,
                "Git is not available; nothing was fetched.",
                "GitUnavailable");
        }

        if (failure.ErrorKind == GitProcessErrorKind.GitAuthRequired
            || MatchesAny(failure, AuthenticationMarkers))
        {
            return Failure(
                GitFetchOutcome.AuthenticationRequired,
                failure,
                "The remote requires authentication, which is unavailable to this background operation; " +
                "no credentials were requested interactively and nothing was fetched.",
                "AuthenticationRequired");
        }

        if (failure.ErrorKind == GitProcessErrorKind.GitPermissionDenied
            || MatchesAny(failure, PermissionDeniedMarkers))
        {
            return Failure(
                GitFetchOutcome.PermissionDenied,
                failure,
                "Git was denied permission to read from the remote; nothing was fetched.",
                "PermissionDenied");
        }

        if (failure.ErrorKind == GitProcessErrorKind.GitPushRejected
            || MatchesAny(failure, RejectedMarkers))
        {
            return Failure(
                GitFetchOutcome.Rejected,
                failure,
                "The remote rejected the fetch; the tracking state was not updated.",
                "Rejected");
        }

        if (MatchesAny(failure, HostUnreachableMarkers))
        {
            return Failure(
                GitFetchOutcome.HostUnreachable,
                failure,
                "The remote could not be reached; the tracking state was not updated.",
                "HostUnreachable");
        }

        return Failure(
            GitFetchOutcome.Failed,
            failure,
            failure.ErrorMessage ?? "The Git fetch failed.",
            null);
    }

    private static GitFetchResult Failure(
        GitFetchOutcome outcome,
        GitProcessResult failure,
        string message,
        string? errorCode) =>
        new(outcome, null, failure, message, errorCode);

    /// <summary>
    /// True when any marker appears in the failure's streams. Only Git-provided
    /// diagnostic text is inspected; no secret is ever copied into a result
    /// message (the failure's own message stays the runner's generic text).
    /// </summary>
    private static bool MatchesAny(GitProcessResult failure, string[] markers) =>
        markers.Any(marker =>
            failure.StandardError.Contains(marker, StringComparison.OrdinalIgnoreCase)
            || failure.StandardOutput.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Git's wording when a remote needs credentials no disabled prompt can
    /// supply (spec 13 §14.4/§18). Matched as a fallback to the SCM-01
    /// classifier's own <see cref="GitProcessErrorKind.GitAuthRequired"/>.
    /// </summary>
    private static readonly string[] AuthenticationMarkers =
    {
        "authentication failed",
        "authentication required",
        "could not read username",
        "could not read password",
        "terminal prompts disabled",
        "permission denied (publickey",
        "invalid username or password",
        "support for password authentication was removed",
    };

    /// <summary>Git's wording when the operation was denied by permissions (spec 13 §20).</summary>
    private static readonly string[] PermissionDeniedMarkers =
    {
        "permission denied",
        "access is denied",
    };

    /// <summary>Git's wording when a remote rejects the request (spec 13 §20).</summary>
    private static readonly string[] RejectedMarkers =
    {
        "remote rejected",
        "! [rejected]",
        "[rejected]",
    };

    /// <summary>
    /// Git's wording when a remote cannot be reached: DNS/connection failures and
    /// a remote path that is not a usable repository (spec 13 §20). These
    /// signatures are the ones the SCM-01 classifier deliberately leaves
    /// <see cref="GitProcessErrorKind.Unknown"/> because they are remote, not
    /// local-repository, conditions.
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
    /// The remote name is the first path segment of the upstream reference
    /// (<c>origin/main</c> → <c>origin</c>); <c>null</c> without an upstream.
    /// </summary>
    private static string? RemoteNameFromUpstream(string? upstreamReference)
    {
        if (string.IsNullOrEmpty(upstreamReference))
        {
            return null;
        }

        int separator = upstreamReference.IndexOf('/');
        return separator <= 0 ? upstreamReference : upstreamReference[..separator];
    }

    private static GitRemoteOutcome DiscoveryFailure(GitRepositoryContext context) =>
        context.Status switch
        {
            GitRepositoryStatus.NoRepository => GitRemoteOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitRemoteOutcome.GitUnavailable,
            _ => GitRemoteOutcome.Failed,
        };

    private static GitRemoteOutcome MapStatusFailure(GitStatusResult status) =>
        status.Status switch
        {
            GitRepositoryStatus.NoRepository => GitRemoteOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitRemoteOutcome.GitUnavailable,
            _ => GitRemoteOutcome.Failed,
        };
}
