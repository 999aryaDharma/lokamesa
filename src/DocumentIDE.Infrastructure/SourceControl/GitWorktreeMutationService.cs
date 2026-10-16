using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-16 — safe Git worktree creation and removal (spec 13 §16.3–§16.5).
///
/// <list type="bullet">
/// <item>every Git invocation goes through the SCM-01
/// <see cref="IGitProcessRunner"/> (structured arguments, no shell, typed
/// failures); mutating worktree commands are auto-serialized on the
/// per-worktree mutation queue, which is why the true worktree root is
/// resolved with <c>git rev-parse --show-toplevel</c> before any mutation;</item>
/// <item>worktrees are created only from existing refs — never with
/// <c>-b</c>, and non-branch refs are detached at their resolved commit, so
/// Document IDE can never create a branch implicitly;</item>
/// <item>removal is never forced: dirty, locked, main, and missing worktrees
/// are blocked with typed states, and <c>git worktree remove</c> itself is a
/// second, independent backstop.</item>
/// </list>
/// </summary>
public sealed class GitWorktreeMutationService : IGitWorktreeMutationService
{
    private const string HeadsPrefix = "refs/heads/";

    private static readonly string[] ShowToplevelArguments = { "rev-parse", "--show-toplevel" };

    /// <summary>
    /// Spec 13 §6 preferred invocation, also used for the dirty-state removal
    /// preflight; <c>--no-optional-locks</c> avoids needless index locks.
    /// </summary>
    private static readonly string[] StatusArguments =
    {
        "--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch",
    };

    private static readonly string[] PruneArguments = { "worktree", "prune" };

    private readonly IGitProcessRunner _runner;
    private readonly IGitWorktreeService _worktrees;

    public GitWorktreeMutationService(IGitProcessRunner runner, IGitWorktreeService worktrees)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _worktrees = worktrees ?? throw new ArgumentNullException(nameof(worktrees));
    }

    /// <inheritdoc />
    public async Task<GitWorktreeCreateResult> CreateWorktreeAsync(
        GitWorktreeCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            return CreateInvalid("A repository working directory is required to create a worktree.");
        }

        if (string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return CreateInvalid("A worktree target path is required.");
        }

        if (!TryValidateWorktreePath(request.TargetPath, out string targetPath, out string targetError))
        {
            return new GitWorktreeCreateResult(
                GitWorktreeCreateStatus.InvalidTargetPath,
                null,
                ErrorMessage: targetError);
        }

        if (!TryValidateRefToken(request.Ref, out string refError))
        {
            return CreateInvalid(refError);
        }

        // Pure local preflight before any Git invocation: a file or non-empty
        // directory at the target can never become a worktree (Git refuses it
        // too), so it is rejected deterministically without a process spawn.
        if (TargetPathOccupied(targetPath))
        {
            return CreateTargetExists(
                $"'{targetPath}' already exists; choose a new path or an empty directory.");
        }

        (string? root, GitProcessResult rootProcess) =
            await ResolveWorktreeRootAsync(request.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return CreateFromProcessFailure(rootProcess);
        }

        GitWorktreeListResult list = await _worktrees.ListWorktreesAsync(root, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return CreateFromListFailure(list);
        }

        if (list.Worktrees.Any(worktree => PathEquals(worktree.Path, targetPath)))
        {
            return CreateTargetExists($"'{targetPath}' is already registered as a Git worktree.");
        }

        // The ref must resolve to an existing commit; nothing is invented.
        GitProcessResult resolve = await _runner.RunAsync(
            new GitProcessRequest(new[] { "rev-parse", "--verify", "--quiet", request.Ref + "^{commit}" }, root),
            cancellationToken).ConfigureAwait(false);

        if (resolve.Status != GitProcessStatus.Succeeded)
        {
            return resolve.Status == GitProcessStatus.Failed
                ? new GitWorktreeCreateResult(
                    GitWorktreeCreateStatus.RefNotFound,
                    null,
                    resolve,
                    ErrorMessage: $"Git ref '{request.Ref}' does not resolve to an existing commit.")
                : CreateFromProcessFailure(resolve);
        }

        string resolvedCommit = resolve.StandardOutput.Trim();
        if (resolvedCommit.Length == 0)
        {
            return new GitWorktreeCreateResult(
                GitWorktreeCreateStatus.RefNotFound,
                null,
                resolve,
                ErrorMessage: $"Git ref '{request.Ref}' does not resolve to an existing commit.");
        }

        // A local branch is checked out as-is; every other ref (tag,
        // remote-tracking ref, explicit commit) becomes a detached worktree at
        // the resolved commit, so Git's remote-tracking DWIM can never create a
        // local branch as a side effect of "create worktree".
        string? branchRef = null;
        if (TryResolveBranchRefName(request.Ref, out string candidateBranchRef))
        {
            GitProcessResult branchProbe = await _runner.RunAsync(
                new GitProcessRequest(new[] { "rev-parse", "--verify", "--quiet", candidateBranchRef }, root),
                cancellationToken).ConfigureAwait(false);

            if (branchProbe.IsSuccess && branchProbe.StandardOutput.Trim().Length > 0)
            {
                branchRef = candidateBranchRef;
            }
            else if (branchProbe.Status is GitProcessStatus.Unavailable
                or GitProcessStatus.Cancelled
                or GitProcessStatus.TimedOut)
            {
                return CreateFromProcessFailure(branchProbe);
            }
        }

        if (branchRef is not null
            && list.Worktrees.FirstOrDefault(
                worktree => string.Equals(worktree.BranchRef, branchRef, StringComparison.OrdinalIgnoreCase))
                is { } checkedOutAt)
        {
            return new GitWorktreeCreateResult(
                GitWorktreeCreateStatus.BranchAlreadyCheckedOut,
                null,
                ErrorMessage: $"Branch '{branchRef}' is already checked out at '{checkedOutAt.Path}'; Git cannot check it out in a second worktree.");
        }

        // `git worktree add <path> <commit-ish>` checks out an existing local
        // branch only when the token is a (short) branch name: a fully
        // qualified `refs/heads/<name>` is treated as a plain commit-ish and
        // creates a detached worktree instead (verified against git 2.51). The
        // short name is only ever used after `refs/heads/<name>` was verified
        // to exist, so Git's remote-tracking DWIM cannot invent a branch here;
        // a branch/tag name collision resolves deterministically in favor of
        // the branch (gitrevisions disambiguation order).
        string[] addArguments = branchRef is not null
            ? new[] { "worktree", "add", targetPath, branchRef[HeadsPrefix.Length..] }
            : new[] { "worktree", "add", "--detach", targetPath, resolvedCommit };

        // Mutating invocation: it runs in the resolved worktree root so the
        // SCM-01 runner serializes it on the per-worktree mutation queue.
        GitProcessResult add = await _runner.RunAsync(
            new GitProcessRequest(addArguments, root),
            cancellationToken).ConfigureAwait(false);

        // Read-only post-verification of the registry runs for both outcomes: a
        // failed `worktree add` may still have created the worktree (e.g. a
        // failing post-checkout hook keeps the checkout and exits non-zero),
        // and that partial state must stay inspectable instead of being hidden.
        GitWorktreeListResult refreshed = await _worktrees.ListWorktreesAsync(root, cancellationToken).ConfigureAwait(false);

        if (!add.IsSuccess)
        {
            return MapCreateFailure(add, refreshed);
        }

        GitWorktreeInfo? created = refreshed.IsSuccess
            ? refreshed.Worktrees.FirstOrDefault(worktree => PathEquals(worktree.Path, targetPath))
            : null;

        return new GitWorktreeCreateResult(GitWorktreeCreateStatus.Succeeded, created, add, refreshed);
    }

    /// <inheritdoc />
    public async Task<GitWorktreeRemoveResult> RemoveWorktreeAsync(
        GitWorktreeRemoveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            return RemoveInvalid("A repository working directory is required to remove a worktree.");
        }

        if (string.IsNullOrWhiteSpace(request.WorktreePath))
        {
            return RemoveInvalid("A worktree path is required.");
        }

        if (!TryValidateWorktreePath(request.WorktreePath, out string worktreePath, out string pathError))
        {
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.InvalidTargetPath,
                ErrorMessage: pathError);
        }

        (string? root, GitProcessResult rootProcess) =
            await ResolveWorktreeRootAsync(request.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return RemoveFromProcessFailure(
                rootProcess,
                failedStatus: GitWorktreeRemoveStatus.PreflightFailed);
        }

        GitWorktreeListResult list = await _worktrees.ListWorktreesAsync(root, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return RemoveFromListFailure(list);
        }

        // Containment: only the exact location Git registered can be removed.
        // The raw caller string is never passed to Git; Git's own canonical
        // path is, so a crafted path cannot retarget the removal.
        GitWorktreeInfo? match = list.Worktrees.FirstOrDefault(
            worktree => PathEquals(worktree.Path, worktreePath));

        if (match is null)
        {
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.NotRegistered,
                null,
                null,
                null,
                list,
                $"'{worktreePath}' is not a location Git registered as a worktree of this repository.");
        }

        if (match.IsMainWorktree)
        {
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.IsMainWorktree,
                match,
                null,
                null,
                list,
                $"'{match.Path}' is a main working tree; the main working tree is never removed.");
        }

        if (match.IsLocked)
        {
            string reason = string.IsNullOrWhiteSpace(match.LockReason) ? string.Empty : $" (reason: {match.LockReason})";
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.WorktreeLocked,
                match,
                null,
                null,
                list,
                $"'{match.Path}' is locked{reason}; forced removal does not exist in V1. Unlock it in Git first.");
        }

        if (!Directory.Exists(match.Path))
        {
            // Fail closed: a worktree whose directory is temporarily
            // unavailable (unmounted drive, offline network path) must never be
            // silently unregistered; refresh/prune is the safe action for stale
            // metadata.
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.Unavailable,
                match,
                null,
                null,
                list,
                $"'{match.Path}' is registered in Git but its directory is missing or unavailable; use Refresh Worktrees for stale metadata.");
        }

        // Dirty-state preflight (spec 13 §16.4) runs inside the worktree that
        // is about to be removed. Any failure to establish cleanliness blocks
        // the removal (fail closed); nothing is inferred from a failed read.
        GitProcessResult status = await _runner.RunAsync(
            new GitProcessRequest(StatusArguments, match.Path),
            cancellationToken).ConfigureAwait(false);

        if (status.Status != GitProcessStatus.Succeeded)
        {
            return RemoveFromPreflightFailure(status, match, list);
        }

        GitStatusSnapshot snapshot;
        try
        {
            snapshot = GitPorcelainV2Parser.Parse(match.Path, status.StandardOutput);
        }
        catch (FormatException exception)
        {
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.PreflightFailed,
                match,
                null,
                status,
                list,
                $"The cleanliness of '{match.Path}' could not be determined (unparseable status): {exception.Message}");
        }

        if (snapshot.Entries.Count > 0)
        {
            return new GitWorktreeRemoveResult(
                GitWorktreeRemoveStatus.DirtyWorktree,
                match,
                snapshot.Entries,
                status,
                list,
                $"'{match.Path}' contains uncommitted changes and cannot be removed; removal is never forced.");
        }

        // Windows cannot delete a process's own working directory, and Git
        // would fail the removal with "Permission denied" after already
        // unregistering the worktree. When the caller sits inside the worktree
        // being removed, the removal runs from the main working tree instead —
        // still serialized on the SCM-01 mutation queue — so the registry and
        // the directory are removed together.
        string invocationDirectory = PathEquals(root, match.Path)
            ? list.Worktrees.FirstOrDefault(worktree => worktree.IsMainWorktree)?.Path ?? root
            : root;

        GitProcessResult remove = await _runner.RunAsync(
            new GitProcessRequest(new[] { "worktree", "remove", match.Path }, invocationDirectory),
            cancellationToken).ConfigureAwait(false);

        // Read-only post-verification of the registry; a failed re-read never
        // hides the mutation outcome.
        GitWorktreeListResult refreshed = await _worktrees.ListWorktreesAsync(invocationDirectory, cancellationToken).ConfigureAwait(false);

        if (!remove.IsSuccess)
        {
            return MapRemoveFailure(remove, match, refreshed);
        }

        return new GitWorktreeRemoveResult(
            GitWorktreeRemoveStatus.Succeeded,
            match,
            null,
            remove,
            refreshed);
    }

    /// <inheritdoc />
    public async Task<GitWorktreeRefreshResult> RefreshWorktreesAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(
                "A working directory is required to refresh Git worktrees.",
                nameof(workingDirectory));
        }

        (string? root, GitProcessResult rootProcess) =
            await ResolveWorktreeRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return RefreshFromProcessFailure(rootProcess);
        }

        // `git worktree prune` only removes registration metadata whose
        // worktree directory is gone and that is not locked; Git refuses to
        // prune a valid worktree, so refresh can never delete real work.
        GitProcessResult prune = await _runner.RunAsync(
            new GitProcessRequest(PruneArguments, root),
            cancellationToken).ConfigureAwait(false);

        if (!prune.IsSuccess)
        {
            return RefreshFromProcessFailure(prune);
        }

        GitWorktreeListResult list = await _worktrees.ListWorktreesAsync(root, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess)
        {
            return RefreshFromListFailure(list);
        }

        return new GitWorktreeRefreshResult(GitWorktreeRefreshStatus.Succeeded, list, prune);
    }

    // -------------------------------------------------------------------------
    // Result mapping
    // -------------------------------------------------------------------------

    private static GitWorktreeCreateResult CreateInvalid(string message) =>
        new(GitWorktreeCreateStatus.InvalidRequest, null, ErrorMessage: message);

    private static GitWorktreeCreateResult CreateTargetExists(string message) =>
        new(GitWorktreeCreateStatus.TargetAlreadyExists, null, ErrorMessage: message);

    private static GitWorktreeCreateResult CreateFromProcessFailure(
        GitProcessResult process,
        GitWorktreeCreateStatus failedStatus = GitWorktreeCreateStatus.CreateFailed,
        GitWorktreeListResult? refreshed = null) =>
        new(
            MapCreateFailureKind(process, failedStatus),
            null,
            process,
            refreshed,
            ErrorMessage: process.ErrorMessage ?? "The Git worktree operation failed.");

    private static GitWorktreeCreateResult CreateFromListFailure(GitWorktreeListResult list) =>
        new(
            MapListFailure(list.Status, GitWorktreeCreateStatus.CreateFailed),
            null,
            list.ProcessResult,
            list,
            list.ErrorMessage ?? "Listing Git worktrees failed.");

    private static GitWorktreeCreateStatus MapCreateFailureKind(
        GitProcessResult process,
        GitWorktreeCreateStatus failedStatus)
    {
        if (process.Status == GitProcessStatus.Unavailable
            || process.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            return GitWorktreeCreateStatus.GitUnavailable;
        }

        if (process.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound)
        {
            return GitWorktreeCreateStatus.NotARepository;
        }

        return process.Status switch
        {
            GitProcessStatus.Cancelled => GitWorktreeCreateStatus.Cancelled,
            GitProcessStatus.TimedOut => GitWorktreeCreateStatus.TimedOut,
            _ => failedStatus,
        };
    }

    private static GitWorktreeCreateStatus MapListFailure(
        GitWorktreeListStatus status,
        GitWorktreeCreateStatus failedStatus) => status switch
        {
            GitWorktreeListStatus.GitUnavailable => GitWorktreeCreateStatus.GitUnavailable,
            GitWorktreeListStatus.NotARepository => GitWorktreeCreateStatus.NotARepository,
            GitWorktreeListStatus.Cancelled => GitWorktreeCreateStatus.Cancelled,
            GitWorktreeListStatus.TimedOut => GitWorktreeCreateStatus.TimedOut,
            _ => failedStatus,
        };

    private static GitWorktreeCreateResult MapCreateFailure(
        GitProcessResult add,
        GitWorktreeListResult refreshed)
    {
        if (add.Status == GitProcessStatus.Failed
            && ContainsAny(add, "is already checked out at", "is already used by worktree at"))
        {
            return new GitWorktreeCreateResult(
                GitWorktreeCreateStatus.BranchAlreadyCheckedOut,
                null,
                add,
                refreshed,
                ErrorMessage: add.StandardError.Trim());
        }

        if (add.Status == GitProcessStatus.Failed && ContainsAny(add, "already exists", "already registered"))
        {
            return new GitWorktreeCreateResult(
                GitWorktreeCreateStatus.TargetAlreadyExists,
                null,
                add,
                refreshed,
                ErrorMessage: add.StandardError.Trim());
        }

        return CreateFromProcessFailure(add, refreshed: refreshed);
    }

    private static GitWorktreeRemoveResult RemoveInvalid(string message) =>
        new(GitWorktreeRemoveStatus.InvalidRequest, ErrorMessage: message);

    private static GitWorktreeRemoveResult RemoveFromProcessFailure(
        GitProcessResult process,
        GitWorktreeInfo? worktree = null,
        GitWorktreeListResult? list = null,
        GitWorktreeRemoveStatus failedStatus = GitWorktreeRemoveStatus.RemoveFailed)
    {
        GitWorktreeRemoveStatus status;
        if (process.Status == GitProcessStatus.Unavailable
            || process.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            status = GitWorktreeRemoveStatus.GitUnavailable;
        }
        else if (process.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound)
        {
            status = GitWorktreeRemoveStatus.NotARepository;
        }
        else
        {
            status = process.Status switch
            {
                GitProcessStatus.Cancelled => GitWorktreeRemoveStatus.Cancelled,
                GitProcessStatus.TimedOut => GitWorktreeRemoveStatus.TimedOut,
                _ => failedStatus,
            };
        }

        return new GitWorktreeRemoveResult(
            status,
            worktree,
            null,
            process,
            list,
            process.ErrorMessage ?? "The Git worktree operation failed.");
    }

    private static GitWorktreeRemoveResult RemoveFromListFailure(GitWorktreeListResult list) =>
        new(
            list.Status switch
            {
                GitWorktreeListStatus.GitUnavailable => GitWorktreeRemoveStatus.GitUnavailable,
                GitWorktreeListStatus.NotARepository => GitWorktreeRemoveStatus.NotARepository,
                GitWorktreeListStatus.Cancelled => GitWorktreeRemoveStatus.Cancelled,
                GitWorktreeListStatus.TimedOut => GitWorktreeRemoveStatus.TimedOut,
                _ => GitWorktreeRemoveStatus.PreflightFailed,
            },
            null,
            null,
            list.ProcessResult,
            list,
            list.ErrorMessage ?? "Listing Git worktrees failed.");

    private static GitWorktreeRemoveResult RemoveFromPreflightFailure(
        GitProcessResult status,
        GitWorktreeInfo worktree,
        GitWorktreeListResult list)
    {
        GitWorktreeRemoveStatus outcome = status.Status switch
        {
            GitProcessStatus.Unavailable => GitWorktreeRemoveStatus.GitUnavailable,
            GitProcessStatus.Cancelled => GitWorktreeRemoveStatus.Cancelled,
            GitProcessStatus.TimedOut => GitWorktreeRemoveStatus.TimedOut,
            _ => GitWorktreeRemoveStatus.PreflightFailed,
        };

        string message = outcome == GitWorktreeRemoveStatus.PreflightFailed
            ? $"The cleanliness of '{worktree.Path}' could not be determined; nothing was removed. {status.ErrorMessage}"
            : status.ErrorMessage ?? "The Git worktree preflight did not complete.";

        return new GitWorktreeRemoveResult(outcome, worktree, null, status, list, message);
    }

    /// <summary>
    /// Classifies a failed <c>git worktree remove</c> by Git's own refusal
    /// vocabulary. The service preflights all of these conditions, so a match
    /// here means state changed between preflight and removal; the typed status
    /// still wins over a raw failure.
    /// </summary>
    private static GitWorktreeRemoveResult MapRemoveFailure(
        GitProcessResult remove,
        GitWorktreeInfo worktree,
        GitWorktreeListResult refreshed)
    {
        if (remove.Status == GitProcessStatus.Failed)
        {
            if (ContainsAny(remove, "contains modified or untracked files"))
            {
                return new GitWorktreeRemoveResult(
                    GitWorktreeRemoveStatus.DirtyWorktree,
                    worktree,
                    null,
                    remove,
                    refreshed,
                    remove.StandardError.Trim());
            }

            if (ContainsAny(remove, "cannot remove a locked working tree"))
            {
                return new GitWorktreeRemoveResult(
                    GitWorktreeRemoveStatus.WorktreeLocked,
                    worktree,
                    null,
                    remove,
                    refreshed,
                    remove.StandardError.Trim());
            }

            if (ContainsAny(remove, "is a main working tree"))
            {
                return new GitWorktreeRemoveResult(
                    GitWorktreeRemoveStatus.IsMainWorktree,
                    worktree,
                    null,
                    remove,
                    refreshed,
                    remove.StandardError.Trim());
            }

            if (ContainsAny(remove, "is not a working tree"))
            {
                return new GitWorktreeRemoveResult(
                    GitWorktreeRemoveStatus.NotRegistered,
                    worktree,
                    null,
                    remove,
                    refreshed,
                    remove.StandardError.Trim());
            }
        }

        return RemoveFromProcessFailure(remove, worktree, refreshed);
    }

    private static GitWorktreeRefreshResult RefreshFromProcessFailure(GitProcessResult process)
    {
        GitWorktreeRefreshStatus status;
        if (process.Status == GitProcessStatus.Unavailable
            || process.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            status = GitWorktreeRefreshStatus.GitUnavailable;
        }
        else if (process.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound)
        {
            status = GitWorktreeRefreshStatus.NotARepository;
        }
        else
        {
            status = process.Status switch
            {
                GitProcessStatus.Cancelled => GitWorktreeRefreshStatus.Cancelled,
                GitProcessStatus.TimedOut => GitWorktreeRefreshStatus.TimedOut,
                _ => GitWorktreeRefreshStatus.Failed,
            };
        }

        return new GitWorktreeRefreshResult(
            status,
            null,
            process,
            process.ErrorMessage ?? "Refreshing Git worktrees failed.");
    }

    private static GitWorktreeRefreshResult RefreshFromListFailure(GitWorktreeListResult list) =>
        new(
            list.Status switch
            {
                GitWorktreeListStatus.GitUnavailable => GitWorktreeRefreshStatus.GitUnavailable,
                GitWorktreeListStatus.NotARepository => GitWorktreeRefreshStatus.NotARepository,
                GitWorktreeListStatus.Cancelled => GitWorktreeRefreshStatus.Cancelled,
                GitWorktreeListStatus.TimedOut => GitWorktreeRefreshStatus.TimedOut,
                _ => GitWorktreeRefreshStatus.Failed,
            },
            list,
            list.ProcessResult,
            list.ErrorMessage ?? "Listing Git worktrees failed.");

    // -------------------------------------------------------------------------
    // Root resolution (SCM-01 queue-key compliance) and shared helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the true worktree root for a caller-provided directory with
    /// <c>git rev-parse --show-toplevel</c>. All mutating invocations run with
    /// this root as their working directory so the per-worktree mutation queue
    /// serializes operations on the same worktree (SCM-01 queue-key contract).
    /// </summary>
    private async Task<(string? Root, GitProcessResult Process)> ResolveWorktreeRootAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        GitProcessResult process = await _runner.RunAsync(
            new GitProcessRequest(ShowToplevelArguments, workingDirectory),
            cancellationToken).ConfigureAwait(false);

        if (!process.IsSuccess)
        {
            return (null, process);
        }

        string rawRoot = process.StandardOutput.Trim();
        try
        {
            if (rawRoot.Length == 0 || !Path.IsPathFullyQualified(rawRoot))
            {
                return (null, Malformed(
                    process,
                    "`git rev-parse --show-toplevel` did not report a fully qualified worktree root."));
            }

            return (Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawRoot)), process);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return (null, Malformed(
                process,
                $"`git rev-parse --show-toplevel` reported an unusable path: {exception.Message}"));
        }
    }

    private static GitProcessResult Malformed(GitProcessResult source, string message) =>
        source with
        {
            Status = GitProcessStatus.Failed,
            ErrorKind = GitProcessErrorKind.Unknown,
            ErrorMessage = message,
        };

    /// <summary>
    /// Validates a worktree target path independently of Git (FS-07 rules):
    /// fully qualified, no relative traversal/current-directory segments, no
    /// device/extended path prefixes, a real directory name (not a drive
    /// root), no <c>.git</c> segment, and every segment passing the strict
    /// FS-07 basename validator (invalid characters, reserved device names,
    /// ADS syntax, trailing dots/spaces).
    /// </summary>
    private static bool TryValidateWorktreePath(string? rawPath, out string canonicalPath, out string error)
    {
        canonicalPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "A worktree target path is required.";
            return false;
        }

        if (rawPath.StartsWith(@"\\?\", StringComparison.Ordinal)
            || rawPath.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            error = $"'{rawPath}' uses a device/extended path prefix, which is not supported for worktree targets.";
            return false;
        }

        if (!Path.IsPathFullyQualified(rawPath))
        {
            error = $"'{rawPath}' is not a fully qualified local path; relative paths are never resolved against the application's current directory.";
            return false;
        }

        // Raw segments are checked before Path.GetFullPath: traversal must
        // never be silently resolved away, and Windows path normalization
        // silently strips trailing dots/spaces ("wt." becomes "wt"), so the
        // normalized form alone would accept a subtly different path than the
        // caller wrote.
        foreach (string rawSegment in rawPath.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))
        {
            if (rawSegment.Length == 0)
            {
                continue;
            }

            if (rawSegment is "." or "..")
            {
                error = $"'{rawPath}' contains a relative navigation segment ('{rawSegment}').";
                return false;
            }

            // "C:" is the drive designator of the path root, not a segment.
            if (rawSegment.Length == 2 && rawSegment[1] == ':' && char.IsAsciiLetter(rawSegment[0]))
            {
                continue;
            }

            if (!PathSafetyValidator.IsValidBasename(rawSegment, out string? rawSegmentError))
            {
                error = $"'{rawPath}' contains an unsafe path segment: {rawSegmentError}";
                return false;
            }
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            error = $"'{rawPath}' is not a usable local path: {exception.Message}";
            return false;
        }

        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (root.Length == 0
            || string.Equals(
                Path.TrimEndingDirectorySeparator(fullPath),
                Path.TrimEndingDirectorySeparator(root),
                StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{rawPath}' resolves to a filesystem root, which cannot be a worktree target.";
            return false;
        }

        string remainder = fullPath[root.Length..];
        foreach (string segment in remainder.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{rawPath}' targets Git metadata ('.git'), which is never a valid worktree location.";
                return false;
            }

            if (!PathSafetyValidator.IsValidBasename(segment, out string? segmentError))
            {
                error = $"'{rawPath}' contains an unsafe path segment: {segmentError}";
                return false;
            }
        }

        canonicalPath = Path.TrimEndingDirectorySeparator(fullPath);
        return true;
    }

    /// <summary>
    /// Validates a ref token that is later passed to Git as one structured
    /// argument. A leading dash could be interpreted as an option and any
    /// revision-syntax character could smuggle in expression forms, so both
    /// are rejected before Git ever sees the value.
    /// </summary>
    private static bool TryValidateRefToken(string? rawRef, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawRef))
        {
            error = "A Git ref (existing branch, tag, or commit) is required.";
            return false;
        }

        string candidate = rawRef;
        if (candidate.Any(char.IsWhiteSpace))
        {
            error = $"Git ref '{rawRef}' must not contain whitespace.";
            return false;
        }

        if (candidate.StartsWith('-'))
        {
            error = $"Git ref '{rawRef}' must not start with '-'; ref values are never treated as options.";
            return false;
        }

        if (candidate.StartsWith('/') || candidate.EndsWith('/') || candidate.Contains("//"))
        {
            error = $"Git ref '{rawRef}' is not a valid ref name (invalid slash usage).";
            return false;
        }

        if (candidate.EndsWith('.') || candidate.Contains(".."))
        {
            error = $"Git ref '{rawRef}' is not a valid ref name (invalid dot usage).";
            return false;
        }

        if (candidate.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Git ref '{rawRef}' is not a valid ref name ('.lock' suffix).";
            return false;
        }

        if (candidate.Contains("@{", StringComparison.Ordinal) || candidate == "@")
        {
            error = $"Git ref '{rawRef}' is not a valid ref name (revision expression syntax).";
            return false;
        }

        foreach (char character in candidate)
        {
            if (character < 32 || character == 127 || "~^:?*[\\".Contains(character))
            {
                error = $"Git ref '{rawRef}' contains a character that is not allowed in ref names.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Maps a user-supplied ref token onto the local-branch ref it would check
    /// out, when it can name one at all. Fully qualified non-heads refs
    /// (<c>refs/tags/…</c>, <c>refs/remotes/…</c>) are never local branches.
    /// </summary>
    private static bool TryResolveBranchRefName(string refToken, out string branchRef)
    {
        if (refToken.StartsWith(HeadsPrefix, StringComparison.Ordinal))
        {
            branchRef = refToken;
            return true;
        }

        if (refToken.StartsWith("refs/", StringComparison.Ordinal))
        {
            branchRef = string.Empty;
            return false;
        }

        branchRef = HeadsPrefix + refToken;
        return true;
    }

    private static bool TargetPathOccupied(string path)
    {
        if (File.Exists(path))
        {
            return true;
        }

        if (!Directory.Exists(path))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception)
        {
            // An existing directory that cannot be inspected is refused rather
            // than guessed about; Git would fail on it anyway.
            return true;
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(GitProcessResult result, params string[] markers) =>
        markers.Any(marker =>
            result.StandardError.Contains(marker, StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
