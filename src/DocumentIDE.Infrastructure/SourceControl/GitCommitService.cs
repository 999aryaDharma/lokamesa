using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-07 — Git commit application service (spec 13 §9.4/§11/§19/§20/§23).
///
/// Every commit:
/// <list type="bullet">
/// <item>runs through the SCM-01 controlled process boundary with structured
/// arguments — never through a shell; the commit message is one <c>-m</c>
/// argument and is never concatenated into a command string,</item>
/// <item>is serialized per worktree by the SCM-01 mutation queue: the runner is
/// invoked with the true worktree root (<c>git rev-parse --show-toplevel</c>
/// via <see cref="GitRepositoryService.DiscoverAsync"/>) as the working
/// directory, which is also the queue key contract,</item>
/// <item>uses the current staged state only — the plain Commit path never
/// passes <c>-a</c>/<c>--all</c>, so unstaged-only changes are never silently
/// included (spec 13 §9.4/§11.1),</item>
/// <item>leaves repository hooks enabled — a failing hook is surfaced as a
/// typed failure instead of being bypassed with <c>--no-verify</c>
/// (spec 13 §11.1/§23),</item>
/// <item>never creates an empty commit (no <c>--allow-empty</c>) and never
/// talks to a remote (no automatic push; spec 13 §11.3/§14.4/§23), and</item>
/// <item>re-reads status from Git after the commit.</item>
/// </list>
/// </summary>
public sealed class GitCommitService
{
    /// <summary>
    /// Reads the new commit's identity and subject in one read-only invocation.
    /// <c>%x00</c> is the NUL separator, so a subject can never blur the field
    /// boundaries; commit ids are hex and <c>%s</c> is a single line by
    /// definition.
    /// </summary>
    private static readonly string[] CommitMetadataArguments =
    {
        "log", "-1", "--format=%H%x00%h%x00%s",
    };

    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;
    private readonly GitStageService _stages;

    public GitCommitService(
        IGitProcessRunner runner,
        GitRepositoryService repositories,
        GitStageService stages)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
    }

    /// <summary>
    /// Commits the current staged state (<c>git commit -m &lt;message&gt;</c>,
    /// spec 13 §11.1). Unstaged working-tree changes are never included.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="message">The commit message; required and passed as a structured argument.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitCommitResult> CommitAsync(
        string repositoryPath,
        string? message,
        CancellationToken cancellationToken = default) =>
        RunFlowAsync(repositoryPath, message, GitCommitFlow.Commit, cancellationToken);

    /// <summary>
    /// Commits all eligible changes (<c>git add --all -- .</c> then
    /// <c>git commit -m &lt;message&gt;</c>, spec 13 §11.2). This is an
    /// explicitly distinct flow from <see cref="CommitAsync"/>: it is the only
    /// path that stages anything itself, it reuses the SCM-06 stage service,
    /// and it reports which paths it staged.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="message">The commit message; required and passed as a structured argument.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitCommitResult> CommitAllAsync(
        string repositoryPath,
        string? message,
        CancellationToken cancellationToken = default) =>
        RunFlowAsync(repositoryPath, message, GitCommitFlow.CommitAll, cancellationToken);

    private async Task<GitCommitResult> RunFlowAsync(
        string repositoryPath,
        string? message,
        GitCommitFlow flow,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // Message validation happens before any Git invocation — and before
        // Commit All stages anything: a rejected message must never leave the
        // index mutated by a flow that then reports no commit (spec 13 §11.1).
        GitCommitResult? rejection = ValidateMessage(flow, message);
        if (rejection is not null)
        {
            return rejection;
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return DiscoveryFailure(flow, context);
        }

        string worktreeRoot = context.WorktreeRoot!;

        // Commit All is the only flow that stages: it stages every eligible
        // change through the SCM-06 pipeline, keeps the paths Git reported as
        // staged for the response, and only then commits (spec 13 §11.2).
        IReadOnlyList<string>? stagedPaths = null;
        if (flow == GitCommitFlow.CommitAll)
        {
            GitStageResult staged = await _stages.StageAllAsync(worktreeRoot, cancellationToken);
            if (!staged.IsSuccess)
            {
                return StageFailure(flow, staged);
            }

            stagedPaths = staged.Status?.Snapshot?.Entries
                .Where(entry => entry.HasStagedChange)
                .Select(entry => entry.Path)
                .ToArray();
        }

        // ValidateMessage guarantees a non-blank message from here on.
        return await CommitCoreAsync(worktreeRoot, flow, message!, stagedPaths, cancellationToken);
    }

    /// <summary>
    /// Maps a failed SCM-06 stage-all to a typed commit result: the commit
    /// never runs when staging failed, so a partial staging can never be
    /// reported as a commit (spec 13 §11.2/§20).
    /// </summary>
    private static GitCommitResult StageFailure(GitCommitFlow flow, GitStageResult staged) => new(
        staged.Outcome switch
        {
            GitStageOutcome.NoRepository => GitCommitOutcome.NoRepository,
            GitStageOutcome.GitUnavailable => GitCommitOutcome.GitUnavailable,
            _ => GitCommitOutcome.Failed,
        },
        flow,
        null,
        staged.Status,
        staged.Failure,
        staged.Message ?? "The Commit All staging step failed; no commit was created.",
        staged.ErrorCode);

    /// <summary>
    /// Validates the client-supplied commit message. Rejection is typed and
    /// happens before Git runs, so a rejected message can never be interpreted
    /// as an option or reach a shell (spec 13 §18/§23).
    /// </summary>
    private static GitCommitResult? ValidateMessage(GitCommitFlow flow, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new GitCommitResult(
                GitCommitOutcome.MessageRequired,
                flow,
                null,
                null,
                null,
                "A commit message is required; no commit was created.",
                "MessageRequired");
        }

        if (message.Contains('\0'))
        {
            return new GitCommitResult(
                GitCommitOutcome.MessageInvalid,
                flow,
                null,
                null,
                null,
                "The commit message contains an unsupported NUL character; no commit was created.",
                "MessageInvalid");
        }

        return null;
    }

    /// <summary>
    /// Shared commit pipeline: run the commit against the resolved worktree
    /// root, read the created commit's identity, then re-read status from Git.
    /// </summary>
    private async Task<GitCommitResult> CommitCoreAsync(
        string worktreeRoot,
        GitCommitFlow flow,
        string message,
        IReadOnlyList<string>? stagedPaths,
        CancellationToken cancellationToken)
    {
        // Nothing staged is never an empty commit (spec 13 §11.3): the index is
        // the authority, so a clean staged state is reported typed and Git is
        // not invoked at all. An unresolved conflict keeps Git as the reporting
        // authority instead (its failure is classified below).
        GitStatusResult preStatus = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        if (preStatus.IsAvailable && preStatus.Snapshot is not null)
        {
            bool hasStaged = preStatus.Snapshot.Entries.Any(entry => entry.HasStagedChange);
            bool hasConflicts = preStatus.Snapshot.Entries.Any(entry => entry.IsConflicted);
            if (!hasStaged && !hasConflicts)
            {
                return new GitCommitResult(
                    GitCommitOutcome.NothingStaged,
                    flow,
                    null,
                    preStatus,
                    null,
                    "There is nothing staged to commit; no empty commit was created.",
                    "NothingStaged",
                    stagedPaths);
            }
        }

        GitProcessResult commit = await _runner.RunAsync(
            new GitProcessRequest(new[] { "commit", "-m", message }, worktreeRoot),
            cancellationToken);

        if (!commit.IsSuccess)
        {
            return new GitCommitResult(
                GitCommitOutcome.Failed,
                flow,
                null,
                null,
                commit,
                commit.ErrorMessage ?? "The Git commit failed.",
                null,
                stagedPaths);
        }

        GitCommitInfo? info = await ReadCommitInfoAsync(worktreeRoot, cancellationToken);
        GitStatusResult refreshed = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);

        // The commit exists; report the branch it landed on from the same
        // status re-read the response carries (spec 13 §11.1 step 6).
        if (info is not null)
        {
            info = info with { BranchName = refreshed.Snapshot?.BranchName };
        }

        return new GitCommitResult(
            GitCommitOutcome.Succeeded,
            flow,
            info,
            refreshed,
            null,
            null,
            null,
            stagedPaths);
    }

    /// <summary>
    /// Reads the created commit's identity and subject. A failure here does not
    /// turn a created commit into a reported failure: the commit exists, so the
    /// identity is reported as unavailable with an inspectable message.
    /// </summary>
    private async Task<GitCommitInfo?> ReadCommitInfoAsync(
        string worktreeRoot,
        CancellationToken cancellationToken)
    {
        GitProcessResult metadata = await _runner.RunAsync(
            new GitProcessRequest(CommitMetadataArguments, worktreeRoot),
            cancellationToken);

        if (!metadata.IsSuccess)
        {
            return null;
        }

        string[] fields = metadata.StandardOutput
            .Split('\0')
            .Select(field => field.Trim('\r', '\n'))
            .ToArray();

        if (fields.Length < 3
            || string.IsNullOrWhiteSpace(fields[0])
            || string.IsNullOrWhiteSpace(fields[1]))
        {
            return null;
        }

        return new GitCommitInfo(fields[0], fields[1], fields[2], null);
    }

    private static GitCommitResult DiscoveryFailure(GitCommitFlow flow, GitRepositoryContext context) => new(
        context.Status switch
        {
            GitRepositoryStatus.NoRepository => GitCommitOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitCommitOutcome.GitUnavailable,
            _ => GitCommitOutcome.Failed,
        },
        flow,
        null,
        null,
        context.Failure,
        context.Failure?.ErrorMessage);
}
