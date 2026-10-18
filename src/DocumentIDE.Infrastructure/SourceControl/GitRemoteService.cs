using System;
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
/// typed. The SCM-01 runner's protected environment disables interactive
/// credential prompting for every Git process it spawns
/// (<c>GIT_TERMINAL_PROMPT=0</c>, emptied <c>GIT_ASKPASS</c>/<c>SSH_ASKPASS</c>
/// and <c>GCM_INTERACTIVE=never</c>), so a remote that requires credentials
/// fails promptly and is typed
/// <see cref="GitFetchOutcome.AuthenticationRequired"/> rather than blocking on
/// a prompt; the invocation is nevertheless still bounded by the runner's
/// default process timeout as a backstop.
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

        return await ReadRemoteStateAsync(context.WorktreeRoot!, cancellationToken);
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

        // F1 — the fetch gate uses the SAME configured-upstream resolution as
        // GetRemoteStateAsync (ReadRemoteStateAsync), never
        // `git rev-parse @{upstream}`. That rev-parse exits 128 when the
        // configured upstream's remote-tracking ref is absent (the upstream
        // branch was deleted remotely, or a `fetch --prune` removed the ref),
        // which would permanently refuse a fetch for a branch that does have an
        // upstream. The configured upstream is still reported by Git's status
        // in that case, so the gate must key on it.
        GitRemoteStateResult upstream = await ReadRemoteStateAsync(worktreeRoot, cancellationToken);
        if (upstream.Outcome == GitRemoteOutcome.NoUpstream)
        {
            // No configured upstream: a first-class non-error state, and no
            // fetch runs (spec 13 §14.2). The state is still reported so the
            // pane can show the explicit "no upstream" state.
            return new GitFetchResult(
                GitFetchOutcome.NoUpstream,
                upstream.State,
                null,
                "The current branch has no configured upstream; nothing was fetched.",
                "NoUpstream");
        }

        if (upstream.Outcome != GitRemoteOutcome.Available)
        {
            // The upstream could not be read at all (status/failure): nothing
            // runs, and the failure stays typed rather than being downgraded.
            return new GitFetchResult(
                upstream.Outcome switch
                {
                    GitRemoteOutcome.NoRepository => GitFetchOutcome.NoRepository,
                    GitRemoteOutcome.GitUnavailable => GitFetchOutcome.GitUnavailable,
                    _ => GitFetchOutcome.Failed,
                },
                upstream.State,
                upstream.Failure,
                upstream.Failure?.ErrorMessage ?? "The remote state could not be read; nothing was fetched.");
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

        GitRemoteStateResult state = await ReadRemoteStateAsync(worktreeRoot, cancellationToken);
        return new GitFetchResult(GitFetchOutcome.Succeeded, state.State, null, null);
    }

    /// <summary>
    /// F1 — the single upstream/state resolution shared by the read route
    /// (<see cref="GetRemoteStateAsync"/>) and the fetch gate
    /// (<see cref="FetchAsync"/>), so the two can never disagree.
    ///
    /// The configured upstream is taken from the same machine-readable status
    /// projection both routes consume (<c>git status --porcelain=v2 -z --branch</c>
    /// → <c>branch.upstream</c>), which Git derives from the branch's configured
    /// upstream (<c>branch.&lt;name&gt;.remote</c> + <c>branch.&lt;name&gt;.merge</c>)
    /// and reports even when the remote-tracking ref is absent. It is
    /// deliberately not derived from <c>rev-parse @{upstream}</c> resolvability,
    /// which fails (exit 128) in exactly that case.
    /// </summary>
    private async Task<GitRemoteStateResult> ReadRemoteStateAsync(
        string worktreeRoot,
        CancellationToken cancellationToken)
    {
        GitStatusResult status = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            return new GitRemoteStateResult(MapStatusFailure(status), null, status.Failure);
        }

        GitStatusSnapshot snapshot = status.Snapshot;

        // F6 — the remote name comes from the branch's configured remote, not
        // from guessing at the upstream string. Only read it when an upstream
        // is actually configured.
        string? remoteName = snapshot.HasUpstream
            ? await ReadConfiguredRemoteNameAsync(worktreeRoot, snapshot.BranchName, cancellationToken)
            : null;

        GitRemoteState state = new(
            snapshot.WorktreeRoot,
            snapshot.BranchName,
            snapshot.IsDetached,
            snapshot.HeadCommit,
            remoteName,
            snapshot.UpstreamReference,
            snapshot.HasUpstream ? snapshot.Ahead : null,
            snapshot.HasUpstream ? snapshot.Behind : null);

        return new GitRemoteStateResult(
            snapshot.HasUpstream ? GitRemoteOutcome.Available : GitRemoteOutcome.NoUpstream,
            state);
    }

    /// <summary>
    /// F6 — resolves the branch's configured remote
    /// (<c>branch.&lt;name&gt;.remote</c>) from Git configuration. A local-branch
    /// upstream stores the sentinel <c>.</c> and has no remote at all, and a
    /// local branch whose own name contains a slash must not be mistaken for a
    /// remote, so the remote name is never parsed out of the upstream string.
    /// Returns <c>null</c> for a detached HEAD, an unset value, or the local
    /// sentinel.
    /// </summary>
    private async Task<string?> ReadConfiguredRemoteNameAsync(
        string worktreeRoot,
        string? branchName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(branchName))
        {
            return null;
        }

        GitProcessResult remote = await _runner.RunAsync(
            new GitProcessRequest(
                new[] { "config", "--get", $"branch.{branchName}.remote" },
                worktreeRoot),
            cancellationToken);

        // An unset key exits non-zero with no output: no upstream remote.
        if (!remote.IsSuccess)
        {
            return null;
        }

        string value = remote.StandardOutput.Trim();
        return value.Length == 0 || value == "." ? null : value;
    }

    /// <summary>
    /// Classifies a failed <c>git fetch</c> into a typed outcome
    /// (spec 13 §14.1/§20). Classification is conservative and ordered from the
    /// most unambiguous signature to the most heuristic; Git diagnostics are
    /// pinned to <c>LC_ALL=C</c> by the runner, so the markers are locale-stable.
    /// A raw failure is never surfaced as a success.
    ///
    /// F5 — the shared signatures (authentication, permissions, rejection) come
    /// from the SCM-01 <see cref="GitFailureClassifier"/> rather than a duplicate
    /// marker list; only the genuinely remote-only signatures live in
    /// <see cref="GitRemoteFailureClassifier"/>. The shared classification is
    /// re-derived here from the diagnostics so it still works when the failure
    /// carries no <see cref="GitProcessResult.ErrorKind"/>.
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

        GitProcessErrorKind kind = GitFailureClassifier.Classify(
            failure.ExitCode ?? 1,
            failure.StandardOutput,
            failure.StandardError);

        if (kind == GitProcessErrorKind.GitAuthRequired
            || GitRemoteFailureClassifier.IsAuthenticationRequired(failure))
        {
            return Failure(
                GitFetchOutcome.AuthenticationRequired,
                failure,
                "The remote requires authentication, which is unavailable to this background operation; " +
                "no credentials were requested interactively and nothing was fetched.",
                "AuthenticationRequired");
        }

        if (kind == GitProcessErrorKind.GitPermissionDenied)
        {
            return Failure(
                GitFetchOutcome.PermissionDenied,
                failure,
                "Git was denied permission to read from the remote; nothing was fetched.",
                "PermissionDenied");
        }

        if (kind == GitProcessErrorKind.GitPushRejected)
        {
            return Failure(
                GitFetchOutcome.Rejected,
                failure,
                "The remote rejected the fetch; the tracking state was not updated.",
                "Rejected");
        }

        if (GitRemoteFailureClassifier.IsHostUnreachable(failure))
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
