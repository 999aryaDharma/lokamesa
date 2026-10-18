using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-13 — Git push application service (spec 13 §14.4/§18/§19/§20/§23).
///
/// Push is <b>always explicit</b>: nothing in this repository calls it from
/// commit, save or any other flow (spec 13 §14.4/§23).
///
/// <list type="bullet">
/// <item><b>Push</b> runs through the SCM-01 controlled process boundary with a
/// structured argument list (<c>git push</c>, never a shell, never
/// <c>--force</c>) against the resolved true worktree root, which is also the
/// per-worktree mutation queue key, so concurrent push/fetch/commit operations
/// on one worktree serialize (spec 13 §19).</item>
/// <item>Push is scoped to the current branch's configured upstream exactly like
/// the SCM-11 fetch: it reuses the same configured-upstream resolution, and a
/// branch with <b>no configured upstream</b> is a typed first-class state that
/// runs no Git at all rather than pushing to a guessed default remote
/// (spec 13 §14.2/§14.4).</item>
/// <item>Failures stay typed and inspectable
/// (spec 13 §20) and credential material is never surfaced: any remote URL
/// userinfo in Git's diagnostics is redacted before it can reach an outcome
/// message, error or log line.</item>
/// </list>
///
/// The SCM-01 runner's protected environment disables interactive credential
/// prompting for every Git process it spawns (<c>GIT_TERMINAL_PROMPT=0</c>,
/// emptied <c>GIT_ASKPASS</c>/<c>SSH_ASKPASS</c> and
/// <c>GCM_INTERACTIVE=never</c>), so a remote that requires credentials fails
/// promptly and is typed
/// <see cref="GitPushOutcome.AuthenticationRequired"/> rather than blocking on a
/// prompt; the invocation is nevertheless still bounded by the runner's default
/// process timeout as a backstop. The configured credential <i>helper</i> stays
/// enabled, so cached credentials still work (spec 13 §14.4/§18).
/// </summary>
public sealed class GitPushService
{
    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;
    private readonly GitRemoteService _remotes;

    public GitPushService(
        IGitProcessRunner runner,
        GitRepositoryService repositories,
        GitRemoteService remotes)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _remotes = remotes ?? throw new ArgumentNullException(nameof(remotes));
    }

    /// <summary>
    /// Runs one explicit Push (<c>git push</c>, spec 13 §14.4) against the
    /// resolved true worktree root and re-reads the tracking state from Git.
    ///
    /// Push is scoped to the current branch's configured upstream: with no
    /// configured upstream, nothing runs and the typed
    /// <see cref="GitPushOutcome.NoUpstream"/> state is returned (spec 13
    /// §14.2). No branch, remote or refspec argument is ever passed, so Git
    /// pushes the current branch to its configured upstream and the Document IDE
    /// surface can never force-push or broaden the pushed scope
    /// (spec 13 §14.4/§23).
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public async Task<GitPushResult> PushAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return Result(
                DiscoveryFailure(context),
                null,
                context.Failure,
                context.Failure?.ErrorMessage ?? "The repository could not be discovered.");
        }

        string worktreeRoot = context.WorktreeRoot!;

        // The push gate uses the SAME configured-upstream resolution as the
        // SCM-11 fetch (GitRemoteService.GetRemoteStateAsync), so the two remote
        // actions can never disagree about which branch is tracked.
        GitRemoteStateResult upstream = await _remotes.GetRemoteStateAsync(worktreeRoot, cancellationToken);
        if (upstream.Outcome == GitRemoteOutcome.NoUpstream)
        {
            // No configured upstream: a first-class non-error state, and no push
            // runs (spec 13 §14.2/§14.4). The state is still reported so the pane
            // can show the explicit "no upstream" state.
            return Result(
                GitPushOutcome.NoUpstream,
                upstream.State,
                null,
                "The current branch has no configured upstream; nothing was pushed.",
                "NoUpstream");
        }

        if (upstream.Outcome != GitRemoteOutcome.Available)
        {
            // The upstream could not be read at all (status/failure): nothing
            // runs, and the failure stays typed rather than being downgraded.
            return Result(
                upstream.Outcome switch
                {
                    GitRemoteOutcome.NoRepository => GitPushOutcome.NoRepository,
                    GitRemoteOutcome.GitUnavailable => GitPushOutcome.GitUnavailable,
                    _ => GitPushOutcome.Failed,
                },
                upstream.State,
                upstream.Failure,
                upstream.Failure?.ErrorMessage ?? "The remote state could not be read; nothing was pushed.");
        }

        // A structured argument list — exactly ["push"], never a shell string,
        // and never an option that widens the pushed scope or force-pushes
        // (spec 13 §14.4/§18/§23). `push` is routed through the SCM-01
        // per-worktree mutation queue by the runner's classifier, keyed by this
        // working directory (spec 13 §19).
        GitProcessResult push = await _runner.RunAsync(
            new GitProcessRequest(new[] { "push" }, worktreeRoot),
            cancellationToken);

        if (!push.IsSuccess)
        {
            return ClassifyPushFailure(push);
        }

        GitRemoteStateResult state = await _remotes.GetRemoteStateAsync(worktreeRoot, cancellationToken);
        return Result(GitPushOutcome.Succeeded, state.State, null, null);
    }

    /// <summary>
    /// Classifies a failed <c>git push</c> into a typed outcome
    /// (spec 13 §14.4/§20). Classification is conservative and ordered from the
    /// most unambiguous signature to the most heuristic; Git diagnostics are
    /// pinned to <c>LC_ALL=C</c> by the runner, so the markers are
    /// locale-stable. A raw failure is never surfaced as a success.
    ///
    /// The shared signatures (authentication, permissions, rejection) come from
    /// the SCM-01 <see cref="GitFailureClassifier"/>, and the genuinely
    /// remote-only signatures from SCM-11's
    /// <see cref="GitRemoteFailureClassifier"/> — there is deliberately no
    /// second marker list here. The shared classification is re-derived from the
    /// diagnostics so it still works when the failure carries no
    /// <see cref="GitProcessResult.ErrorKind"/>.
    /// </summary>
    private static GitPushResult ClassifyPushFailure(GitProcessResult failure)
    {
        if (failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            return Failure(
                GitPushOutcome.GitUnavailable,
                failure,
                "Git is not available; nothing was pushed.",
                "GitUnavailable");
        }

        GitProcessErrorKind kind = GitFailureClassifier.Classify(
            failure.ExitCode ?? 1,
            failure.StandardOutput,
            failure.StandardError);

        // Authentication is checked before the remote-reachability wording,
        // because Git reports an HTTP 401 as "unable to access …" too.
        if (kind == GitProcessErrorKind.GitAuthRequired
            || GitRemoteFailureClassifier.IsAuthenticationRequired(failure))
        {
            return Failure(
                GitPushOutcome.AuthenticationRequired,
                failure,
                "The remote requires authentication, which is unavailable to this background operation; " +
                "no credentials were requested interactively and nothing was pushed.",
                "AuthenticationRequired");
        }

        if (kind == GitProcessErrorKind.GitPermissionDenied)
        {
            return Failure(
                GitPushOutcome.PermissionDenied,
                failure,
                "The remote denied permission for this push; nothing was pushed.",
                "PermissionDenied");
        }

        if (kind == GitProcessErrorKind.GitPushRejected)
        {
            return Failure(
                GitPushOutcome.Rejected,
                failure,
                "The remote rejected the push (not a fast-forward or rejected by a repository hook); " +
                "no local commit was changed.",
                "Rejected");
        }

        if (GitRemoteFailureClassifier.IsHostUnreachable(failure))
        {
            return Failure(
                GitPushOutcome.HostUnreachable,
                failure,
                "The remote could not be reached; nothing was pushed.",
                "HostUnreachable");
        }

        return Failure(
            GitPushOutcome.Failed,
            failure,
            failure.ErrorMessage ?? "The Git push failed.",
            null);
    }

    /// <summary>
    /// Builds a typed failure result. The failure payload and every message go
    /// through <see cref="GitCredentialRedactor"/>, so no credential from a
    /// remote URL can reach an outcome message, error or log line.
    /// </summary>
    private static GitPushResult Failure(
        GitPushOutcome outcome,
        GitProcessResult failure,
        string message,
        string? errorCode) =>
        Result(outcome, null, failure, message, errorCode);

    /// <summary>
    /// The single place a <see cref="GitPushResult"/> is constructed, so every
    /// carried failure and message is credential-redacted (spec 13
    /// §14.4/§20/§23).
    /// </summary>
    private static GitPushResult Result(
        GitPushOutcome outcome,
        GitRemoteState? remote,
        GitProcessResult? failure,
        string? message,
        string? errorCode = null) =>
        new(
            outcome,
            remote,
            failure is null ? null : GitCredentialRedactor.Sanitize(failure),
            GitCredentialRedactor.Redact(message),
            errorCode);

    private static GitPushOutcome DiscoveryFailure(GitRepositoryContext context) =>
        context.Status switch
        {
            GitRepositoryStatus.NoRepository => GitPushOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitPushOutcome.GitUnavailable,
            _ => GitPushOutcome.Failed,
        };
}
