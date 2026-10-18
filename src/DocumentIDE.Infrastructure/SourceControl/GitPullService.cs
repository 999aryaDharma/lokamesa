using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-12 — Safe Fast-Forward Pull application service
/// (spec 13 §14.3/§18/§19/§20/§21/§23).
///
/// <list type="bullet">
/// <item><b>Preflight first.</b> A dirty or conflicted worktree is refused typed
/// <i>before</i> the pull runs, so <c>git pull</c> can never autostash,
/// overwrite or partially apply local work (spec 13 §14.3/§21).</item>
/// <item><b>Fast-forward only.</b> The invocation is exactly
/// <c>pull --ff-only --no-rebase</c>: the explicit <c>--no-rebase</c> prevents a
/// user's <c>pull.rebase</c>/<c>rebase.autoStash</c> configuration from changing
/// the behaviour, and no branch/refspec/force/merge option is ever passed
/// (spec 13 §14.3/§23). Divergence is a typed blocking state — no merge commit
/// and no rebase are ever created.</item>
/// <item><b>Serialized.</b> Pull mutates the working tree, so it runs through
/// the SCM-01 per-worktree mutation queue against the resolved true worktree
/// root (spec 13 §19).</item>
/// <item><b>Refresh after success.</b> Status is re-read from Git, the documents
/// whose paths changed are reindexed through the existing index seam, and the
/// operation is recorded in the same Document IDE History store as the other
/// Source Control mutations (spec 13 §14.3 step 4).</item>
/// </list>
///
/// The SCM-01 runner's protected environment disables interactive credential
/// prompting and pins <c>LC_ALL=C</c>, so authentication fails promptly typed
/// rather than blocking on a prompt, and failures stay inspectable
/// (spec 13 §18/§20). Any credential material in the diagnostics is redacted
/// before it can reach an outcome message, error or log line (spec 13 §23).
/// </summary>
public sealed class GitPullService
{
    /// <summary>Document IDE History operation-id prefix for a pull.</summary>
    private const string OperationIdPrefix = "scm-pull-";

    /// <summary>Bound on paths echoed in a preflight refusal message.</summary>
    private const int MaxListedPaths = 20;

    /// <summary>
    /// The complete, conservative pull invocation. <c>--ff-only</c> refuses any
    /// non-fast-forward outcome; <c>--no-rebase</c> makes that independent of the
    /// repository's <c>pull.rebase</c>/<c>rebase.autoStash</c> configuration.
    /// There is deliberately no branch, remote, refspec, <c>--force</c>,
    /// <c>--no-ff</c>, <c>--merge</c>, <c>--all</c> or <c>--tags</c> argument.
    /// </summary>
    private static readonly string[] PullArguments = { "pull", "--ff-only", "--no-rebase" };

    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;
    private readonly GitRemoteService _remotes;
    private readonly IDocumentRepository _documents;
    private readonly IBatchApplyReindexTrigger _reindex;
    private readonly IGitPullHistoryRecorder _history;

    public GitPullService(
        IGitProcessRunner runner,
        GitRepositoryService repositories,
        GitRemoteService remotes,
        IDocumentRepository documents,
        IBatchApplyReindexTrigger reindex,
        IGitPullHistoryRecorder history)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _remotes = remotes ?? throw new ArgumentNullException(nameof(remotes));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _reindex = reindex ?? throw new ArgumentNullException(nameof(reindex));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    /// <summary>
    /// Runs one conservative Pull against the resolved true worktree root.
    /// Returns a typed blocking state instead of running Git when the worktree
    /// is dirty, conflicted, diverged or has no configured upstream
    /// (spec 13 §14.2/§14.3/§21).
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public async Task<GitPullResult> PullAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        string operationId = OperationIdPrefix + Guid.NewGuid().ToString("N");

        // A worktree that vanished before the operation is a typed state, not an
        // exception and not a Git invocation against an arbitrary path.
        if (!PathExists(repositoryPath))
        {
            return Result(
                GitPullOutcome.WorktreeMissing,
                operationId,
                null,
                null,
                null,
                null,
                null,
                "The worktree path no longer exists; nothing was pulled.",
                "WorktreeMissing");
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            GitPullOutcome discovery = DiscoveryFailure(context);
            return Result(
                discovery,
                operationId,
                null,
                null,
                null,
                null,
                context.Failure,
                context.Failure?.ErrorMessage ?? "The repository could not be discovered; nothing was pulled.",
                discovery.ToString());
        }

        string worktreeRoot = context.WorktreeRoot!;

        // Step 1 — dirty/conflict preflight. This runs BEFORE the pull so Git can
        // never autostash, overwrite or partially apply local work.
        GitStatusResult status = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            GitPullOutcome outcome = MapStatusFailure(status, worktreeRoot);
            return Result(
                outcome,
                operationId,
                context.HeadCommit,
                context.HeadCommit,
                null,
                null,
                status.Failure,
                status.Failure?.ErrorMessage ?? "The worktree status could not be read; nothing was pulled.",
                outcome.ToString());
        }

        GitStatusSnapshot snapshot = status.Snapshot;

        IReadOnlyList<string> conflicted = snapshot.Entries
            .Where(entry => entry.IsConflicted)
            .Select(entry => entry.Path)
            .ToList();
        if (conflicted.Count > 0)
        {
            return Result(
                GitPullOutcome.ConflictedWorktree,
                operationId,
                snapshot.HeadCommit,
                snapshot.HeadCommit,
                null,
                null,
                null,
                "The worktree has unresolved conflicts; pull is blocked and no file was changed. " +
                DescribePaths(conflicted),
                "ConflictedWorktree");
        }

        IReadOnlyList<string> dirty = snapshot.Entries
            .Where(entry => entry.HasStagedChange || entry.HasWorkingTreeChange)
            .Select(entry => entry.Path)
            .ToList();
        if (dirty.Count > 0)
        {
            return Result(
                GitPullOutcome.DirtyWorktree,
                operationId,
                snapshot.HeadCommit,
                snapshot.HeadCommit,
                null,
                null,
                null,
                "The worktree has uncommitted changes; pull is blocked and no file was changed. " +
                "Commit or discard the changes before pulling. " + DescribePaths(dirty),
                "DirtyWorktree");
        }

        // A branch with no configured upstream is a first-class typed state and
        // runs no Git at all (spec 13 §14.2).
        if (!snapshot.HasUpstream)
        {
            return Result(
                GitPullOutcome.NoUpstream,
                operationId,
                snapshot.HeadCommit,
                snapshot.HeadCommit,
                null,
                null,
                null,
                "The current branch has no configured upstream; nothing was pulled.",
                "NoUpstream");
        }

        string? previousHead = snapshot.HeadCommit;

        // Step 2 — fast-forward-only pull. A structured argument list, exactly
        // ["pull", "--ff-only", "--no-rebase"], never a shell string and never an
        // option that could merge, rebase, force or widen the scope. `pull` is
        // routed through the SCM-01 per-worktree mutation queue by the runner's
        // classifier, keyed by this working directory (spec 13 §19).
        GitProcessResult pull = await _runner.RunAsync(
            new GitProcessRequest(PullArguments, worktreeRoot),
            cancellationToken);

        if (!pull.IsSuccess)
        {
            // Step 3 — classify divergence (and every other failure) typed.
            return await FailureAfterRunAsync(
                operationId,
                worktreeRoot,
                previousHead,
                pull,
                cancellationToken);
        }

        // Step 4 — refresh downstream document/index/source-control state.
        return await RefreshAfterSuccessAsync(
            operationId,
            worktreeRoot,
            previousHead,
            cancellationToken);
    }

    /// <summary>
    /// Step 4 — re-reads status from Git, reindexes the documents whose paths
    /// changed in the fast-forward, and records the operation in Document IDE
    /// History. A completed pull is never turned into a reported failure by a
    /// reindex/history warning; each warning is surfaced on the successful
    /// result instead.
    /// </summary>
    private async Task<GitPullResult> RefreshAfterSuccessAsync(
        string operationId,
        string worktreeRoot,
        string? previousHead,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        GitStatusResult refreshed = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        string? head = refreshed.Snapshot?.HeadCommit ?? previousHead;
        if (!refreshed.IsAvailable)
        {
            warnings.Add("The Git status could not be refreshed after the pull.");
        }

        IReadOnlyList<string> changedPaths = Array.Empty<string>();
        if (previousHead is not null && head is not null)
        {
            if (string.Equals(previousHead, head, StringComparison.OrdinalIgnoreCase))
            {
                changedPaths = Array.Empty<string>();
            }
            else
            {
                (changedPaths, string? diffWarning) = await ReadChangedPathsAsync(
                    worktreeRoot,
                    previousHead,
                    head,
                    cancellationToken);
                if (diffWarning is not null)
                {
                    warnings.Add(diffWarning);
                }
            }
        }

        int reindexed = 0;
        foreach (string path in changedPaths)
        {
            string? absolute = TryResolveWithinWorktree(worktreeRoot, path);
            if (absolute is null)
            {
                warnings.Add($"The changed path '{path}' resolved outside the worktree and was not reindexed.");
                continue;
            }

            try
            {
                var document = await _documents.GetByPathAsync(absolute);
                if (document is null)
                {
                    continue;
                }

                await _reindex.TriggerAsync(document.Id, CancellationToken.None);
                reindexed++;
            }
            catch (Exception exception)
            {
                warnings.Add($"The document at '{path}' could not be reindexed: {exception.Message}");
            }
        }

        GitRemoteStateResult remote = await _remotes.GetRemoteStateAsync(worktreeRoot, CancellationToken.None);

        bool historyRecorded = await TryRecordHistoryAsync(new GitPullHistoryRecord(
            operationId,
            GitPullOutcome.Succeeded.ToString(),
            worktreeRoot,
            previousHead,
            head,
            changedPaths,
            DateTimeOffset.UtcNow));

        if (!historyRecorded)
        {
            warnings.Add("Document IDE History could not record the pull operation.");
        }

        var refresh = new GitPullRefresh(
            changedPaths,
            reindexed,
            refreshed.IsAvailable,
            historyRecorded,
            refreshed);

        return Result(
            GitPullOutcome.Succeeded,
            operationId,
            previousHead,
            head,
            remote.State,
            refresh,
            null,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    /// <summary>
    /// Reads the worktree-relative paths changed between two commits with a
    /// structured read-only invocation (<c>git diff --name-only -z</c>). No
    /// filename is ever placed on a command line: the range is commit ids that
    /// Git itself reported. Renames are split into delete+add
    /// (<c>--no-renames</c>) so both the old and the new path are refreshed.
    /// </summary>
    private async Task<(IReadOnlyList<string> Paths, string? Warning)> ReadChangedPathsAsync(
        string worktreeRoot,
        string previousHead,
        string head,
        CancellationToken cancellationToken)
    {
        GitProcessResult diff = await _runner.RunAsync(
            new GitProcessRequest(
                new[] { "diff", "--name-only", "-z", "--no-renames", previousHead, head },
                worktreeRoot),
            cancellationToken);

        if (!diff.IsSuccess)
        {
            return (
                Array.Empty<string>(),
                "The list of documents changed by the pull could not be read from Git.");
        }

        var paths = diff.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Length > 0)
            .ToList();

        return (paths, null);
    }

    /// <summary>
    /// Step 3 — classifies a failed pull into a typed outcome (spec 13 §20).
    /// Divergence is a blocking state, never a raw error string: Git's own
    /// <c>Not possible to fast-forward</c> diagnostics (pinned to
    /// <c>LC_ALL=C</c>) are mapped before the generic failure, and the local
    /// history was never rewritten. Index-lock contention and remote failures
    /// are typed too; a raw failure is never surfaced as a success.
    /// </summary>
    private async Task<GitPullResult> FailureAfterRunAsync(
        string operationId,
        string worktreeRoot,
        string? previousHead,
        GitProcessResult failure,
        CancellationToken cancellationToken)
    {
        (GitPullOutcome outcome, string message, string? errorCode) = PathExists(worktreeRoot)
            ? ClassifyPullFailure(failure)
            : (
                GitPullOutcome.WorktreeMissing,
                "The worktree is no longer available; nothing was pulled.",
                "WorktreeMissing");

        string? head = previousHead;
        try
        {
            GitStatusResult status = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
            head = status.Snapshot?.HeadCommit ?? previousHead;
        }
        catch (Exception)
        {
            // The failure classification is already typed; a post-failure status
            // read must not replace it or turn it into an exception.
        }

        await TryRecordHistoryAsync(new GitPullHistoryRecord(
            operationId,
            outcome.ToString(),
            worktreeRoot,
            previousHead,
            head,
            Array.Empty<string>(),
            DateTimeOffset.UtcNow));

        return Result(
            outcome,
            operationId,
            previousHead,
            head,
            null,
            null,
            failure,
            message,
            errorCode);
    }

    /// <summary>
    /// Typed classification of a failed <c>git pull</c>. The SCM-01 classifier
    /// owns the shared vocabulary (index lock, divergence, authentication,
    /// permission, dirty/untracked conflicts); SCM-11's remote classifier owns
    /// the remote-only signatures. There is deliberately no third marker list.
    /// </summary>
    private static (GitPullOutcome Outcome, string Message, string? ErrorCode) ClassifyPullFailure(
        GitProcessResult failure)
    {
        if (failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            return (
                GitPullOutcome.GitUnavailable,
                "Git is not available; nothing was pulled.",
                "GitUnavailable");
        }

        GitProcessErrorKind kind = GitFailureClassifier.Classify(
            failure.ExitCode ?? 1,
            failure.StandardOutput,
            failure.StandardError);

        // Divergence is the core blocking state of this sprint: report it typed,
        // with the exact product statement, and never fall back to merge/rebase.
        if (kind == GitProcessErrorKind.GitPullNotFastForward)
        {
            return (
                GitPullOutcome.Diverged,
                "Local and remote history have diverged. No automatic merge/rebase was performed.",
                "Diverged");
        }

        if (kind == GitProcessErrorKind.GitLockContention)
        {
            return (
                GitPullOutcome.IndexLocked,
                "The Git index is locked by another operation; nothing was pulled.",
                "IndexLocked");
        }

        if (kind == GitProcessErrorKind.GitAuthRequired
            || GitRemoteFailureClassifier.IsAuthenticationRequired(failure))
        {
            return (
                GitPullOutcome.AuthenticationRequired,
                "The remote requires authentication, which is unavailable to this background operation; " +
                "no credentials were requested interactively and nothing was pulled.",
                "AuthenticationRequired");
        }

        if (kind == GitProcessErrorKind.GitPermissionDenied)
        {
            return (
                GitPullOutcome.PermissionDenied,
                "Git was denied permission while pulling; nothing was applied.",
                "PermissionDenied");
        }

        if (kind == GitProcessErrorKind.GitUntrackedConflict)
        {
            return (
                GitPullOutcome.DirtyWorktree,
                "The pull would overwrite untracked files; it was refused and nothing was changed.",
                "UntrackedConflict");
        }

        if (kind is GitProcessErrorKind.GitDirtyWorktree or GitProcessErrorKind.GitConflict)
        {
            return (
                GitPullOutcome.DirtyWorktree,
                "Git refused the pull because the worktree changed; nothing was changed.",
                "DirtyWorktree");
        }

        if (GitRemoteFailureClassifier.IsHostUnreachable(failure))
        {
            return (
                GitPullOutcome.HostUnreachable,
                "The remote could not be reached; nothing was pulled.",
                "HostUnreachable");
        }

        if (failure.StandardError.Contains("not a working tree", StringComparison.OrdinalIgnoreCase))
        {
            return (
                GitPullOutcome.WorktreeMissing,
                "The worktree is no longer available; nothing was pulled.",
                "WorktreeMissing");
        }

        return (
            GitPullOutcome.Failed,
            failure.ErrorMessage ?? "The Git pull failed; nothing was applied.",
            null);
    }

    /// <summary>Records one pull operation; a failed write is a typed warning, never a silent drop.</summary>
    private async Task<bool> TryRecordHistoryAsync(GitPullHistoryRecord record)
    {
        try
        {
            await _history.RecordAsync(record, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            // A failed Git mutation/history write must not be masked by an
            // application-history failure; the caller surfaces the warning.
            return false;
        }
    }

    /// <summary>
    /// The single place a <see cref="GitPullResult"/> is constructed, so every
    /// message and carried failure is credential-redacted (spec 13 §20/§23).
    /// </summary>
    private static GitPullResult Result(
        GitPullOutcome outcome,
        string operationId,
        string? previousHead,
        string? head,
        GitRemoteState? remote,
        GitPullRefresh? refresh,
        GitProcessResult? failure,
        string? message,
        string? errorCode = null) =>
        new(
            outcome,
            operationId,
            previousHead,
            head,
            remote,
            refresh,
            failure is null ? null : GitCredentialRedactor.Sanitize(failure),
            GitCredentialRedactor.Redact(message),
            errorCode);

    private static GitPullOutcome DiscoveryFailure(GitRepositoryContext context) =>
        context.Status switch
        {
            GitRepositoryStatus.NoRepository => GitPullOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitPullOutcome.GitUnavailable,
            _ => GitPullOutcome.Failed,
        };

    private static GitPullOutcome MapStatusFailure(GitStatusResult status, string worktreeRoot)
    {
        if (!PathExists(worktreeRoot))
        {
            return GitPullOutcome.WorktreeMissing;
        }

        return status.Status switch
        {
            GitRepositoryStatus.NoRepository => GitPullOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitPullOutcome.GitUnavailable,
            _ => GitPullOutcome.Failed,
        };
    }

    /// <summary>
    /// Resolves a Git-reported worktree-relative path and verifies containment
    /// (spec 13 §23): a path can never escape the resolved worktree root.
    /// </summary>
    private static string? TryResolveWithinWorktree(string worktreeRoot, string relativePath)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeRoot));
            string candidate = Path.GetFullPath(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            string prefix = root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string DescribePaths(IReadOnlyList<string> paths)
    {
        IEnumerable<string> listed = paths.Count <= MaxListedPaths
            ? paths
            : paths.Take(MaxListedPaths);

        string suffix = paths.Count > MaxListedPaths
            ? $" (and {paths.Count - MaxListedPaths} more)"
            : string.Empty;

        return $"Affected paths: {string.Join(", ", listed)}{suffix}.";
    }

    private static bool PathExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException)
        {
            return false;
        }
    }
}
