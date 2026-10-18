using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-02 — repository/worktree/status application service for the Source
/// Control subsystem.
///
/// All Git state is derived through the SCM-01 controlled process boundary
/// (spec 13 §18): no shell, structured arguments, protected environment, typed
/// results. Nothing is duplicated into SQLite — Git stays the authority for
/// repository/worktree/status state (spec 13 §4.2/§24).
///
/// The service can additionally watch one worktree through the FS-03
/// <see cref="FileChangeCoordinator"/>: filesystem events are hints that reset
/// a debounce timer, and Git runs only after the burst has settled — never
/// inside the raw watcher callback (spec 13 §17).
/// </summary>
public sealed class GitRepositoryService : IDisposable
{
    /// <summary>
    /// Spec 13 §6 preferred invocation. <c>--no-optional-locks</c> keeps a
    /// background status refresh from taking optional index locks (spec 13 §18).
    /// </summary>
    private static readonly string[] StatusArguments =
    {
        "--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch",
    };

    private readonly IGitProcessRunner _runner;
    private readonly FileChangeCoordinator? _fileChangeCoordinator;
    private readonly TimeSpan _statusDebounceDelay;
    private readonly object _watchGate = new();

    private string? _watchedWorktreeRoot;
    private string? _watchedFolderSourceId;
    private Timer? _statusDebounceTimer;
    private GitStatusResult? _currentStatus;

    /// <summary>Raised after every completed refresh, including typed failures.</summary>
    public event EventHandler<GitStatusResult>? OnStatusChanged;

    /// <summary>The most recent refresh result for the watched worktree; <c>null</c> before the first refresh.</summary>
    public GitStatusResult? CurrentStatus => Volatile.Read(ref _currentStatus);

    public GitRepositoryService(IGitProcessRunner runner)
        : this(runner, null, null)
    {
    }

    public GitRepositoryService(
        IGitProcessRunner runner,
        FileChangeCoordinator? fileChangeCoordinator,
        TimeSpan? statusDebounceDelay = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _fileChangeCoordinator = fileChangeCoordinator;
        _statusDebounceDelay = statusDebounceDelay ?? TimeSpan.FromMilliseconds(300);

        if (_fileChangeCoordinator is not null)
        {
            _fileChangeCoordinator.OnFileChanged += HandleCoordinatorFileChanged;
        }
    }

    /// <summary>
    /// Discovers the enclosing Git repository/worktree for a workspace or
    /// active-document path (spec 13 §5). <paramref name="startPath"/> may
    /// point at a directory or a file. A non-Git folder yields the typed
    /// <see cref="GitRepositoryStatus.NoRepository"/> state.
    /// </summary>
    public async Task<GitRepositoryContext> DiscoverAsync(
        string startPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startPath);

        string requestedPath;
        try
        {
            requestedPath = Path.GetFullPath(startPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return new GitRepositoryContext(
                GitRepositoryStatus.Failed,
                startPath,
                Failure: LocalFailure($"Path '{startPath}' is not a usable filesystem path: {exception.Message}"));
        }

        // An active document path discovers the repository of its directory.
        string directory = File.Exists(requestedPath)
            ? Path.GetDirectoryName(requestedPath) ?? requestedPath
            : requestedPath;

        GitProcessResult revParse = await _runner.RunAsync(
            new GitProcessRequest(
                new[] { "rev-parse", "--show-toplevel", "--git-dir", "--git-common-dir" },
                directory),
            cancellationToken);

        if (!revParse.IsSuccess)
        {
            return new GitRepositoryContext(MapFailure(revParse), requestedPath, Failure: revParse);
        }

        string[] lines = SplitOutputLines(revParse.StandardOutput);
        if (lines.Length != 3)
        {
            return Malformed(revParse, requestedPath, "`git rev-parse --show-toplevel --git-dir --git-common-dir` returned an unexpected number of values.");
        }

        string? worktreeRoot = TryNormalizeGitPath(lines[0], directory);
        string? gitDir = TryNormalizeGitPath(lines[1], directory);
        string? commonDir = TryNormalizeGitPath(lines[2], directory);
        if (worktreeRoot is null || gitDir is null || commonDir is null)
        {
            return Malformed(revParse, requestedPath, "`git rev-parse` returned an unusable path value.");
        }

        // HEAD/branch/upstream are derived from Git too; an unborn branch is a
        // valid state (no HEAD commit yet, branch name present), and a detached
        // HEAD is represented explicitly instead of as an empty branch.
        GitProcessResult headProbe = await _runner.RunAsync(
            new GitProcessRequest(new[] { "rev-parse", "--verify", "--quiet", "HEAD" }, worktreeRoot),
            cancellationToken);
        string? headCommit = headProbe.IsSuccess && !string.IsNullOrWhiteSpace(headProbe.StandardOutput)
            ? headProbe.StandardOutput.Trim()
            : null;

        GitProcessResult branchProbe = await _runner.RunAsync(
            new GitProcessRequest(new[] { "symbolic-ref", "--short", "-q", "HEAD" }, worktreeRoot),
            cancellationToken);
        bool isDetached = !branchProbe.IsSuccess || string.IsNullOrWhiteSpace(branchProbe.StandardOutput);
        string? branchName = isDetached ? null : branchProbe.StandardOutput.Trim();

        GitProcessResult upstreamProbe = await _runner.RunAsync(
            new GitProcessRequest(
                new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" },
                worktreeRoot),
            cancellationToken);
        string? upstreamReference = upstreamProbe.IsSuccess && !string.IsNullOrWhiteSpace(upstreamProbe.StandardOutput)
            ? upstreamProbe.StandardOutput.Trim()
            : null;

        return new GitRepositoryContext(
            GitRepositoryStatus.Available,
            requestedPath,
            RepositoryRoot: DeriveRepositoryRoot(commonDir),
            WorktreeRoot: worktreeRoot,
            GitDirReference: gitDir,
            HeadCommit: headCommit,
            BranchName: branchName,
            IsDetached: isDetached,
            UpstreamReference: upstreamReference);
    }

    /// <summary>
    /// Reads one machine-readable Git status snapshot for a worktree
    /// (spec 13 §6). The snapshot distinguishes the staged/index side from the
    /// working-tree side per entry and covers untracked, ignored and unmerged
    /// entries. Callers should pass the true worktree root (e.g.
    /// <see cref="GitRepositoryContext.WorktreeRoot"/>); Git reports paths
    /// relative to the repository root either way.
    /// </summary>
    public async Task<GitStatusResult> GetStatusAsync(
        string worktreeRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);

        string workingDirectory;
        try
        {
            workingDirectory = Path.GetFullPath(worktreeRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return new GitStatusResult(
                GitRepositoryStatus.Failed,
                null,
                LocalFailure($"Path '{worktreeRoot}' is not a usable filesystem path: {exception.Message}"));
        }

        GitProcessResult result = await _runner.RunAsync(
            new GitProcessRequest(StatusArguments, workingDirectory),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return new GitStatusResult(MapFailure(result), null, result);
        }

        try
        {
            GitStatusSnapshot snapshot = GitPorcelainV2Parser.Parse(workingDirectory, result.StandardOutput);
            return new GitStatusResult(GitRepositoryStatus.Available, snapshot);
        }
        catch (FormatException exception)
        {
            // Typed, inspectable failure instead of propagating a parser
            // exception or silently returning partial data (spec 13 §20).
            return new GitStatusResult(
                GitRepositoryStatus.Failed,
                null,
                result with
                {
                    Status = GitProcessStatus.Failed,
                    ErrorKind = GitProcessErrorKind.Unknown,
                    ErrorMessage = $"Git status output could not be parsed: {exception.Message}",
                });
        }
    }

    /// <summary>
    /// Watches one worktree for filesystem hints and refreshes Git status in a
    /// debounced manner (spec 13 §17). <paramref name="worktreeOrPath"/> should
    /// be the true worktree root (e.g. <see cref="GitRepositoryContext.WorktreeRoot"/>);
    /// it is the caller's job to discover it first — the service never guesses
    /// a repository. Starting again replaces the previous watched worktree.
    /// </summary>
    public void StartWatching(string worktreeOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeOrPath);

        string worktreeRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeOrPath));

        lock (_watchGate)
        {
            if (_watchedFolderSourceId is not null)
            {
                _fileChangeCoordinator?.StopWatching(_watchedFolderSourceId);
                _statusDebounceTimer?.Dispose();
                _statusDebounceTimer = null;
            }

            _watchedWorktreeRoot = worktreeRoot;
            _watchedFolderSourceId = Guid.NewGuid().ToString("N");

            if (_fileChangeCoordinator is not null)
            {
                _fileChangeCoordinator.StartWatching(new FolderSource
                {
                    Id = _watchedFolderSourceId,
                    CanonicalPath = worktreeRoot,
                    DisplayName = worktreeRoot,
                });
            }
        }
    }

    /// <summary>
    /// Stops filesystem-driven refreshes for the watched worktree. The last
    /// <see cref="CurrentStatus"/> remains until the next refresh.
    /// </summary>
    public void StopWatching()
    {
        lock (_watchGate)
        {
            if (_watchedFolderSourceId is not null)
            {
                _fileChangeCoordinator?.StopWatching(_watchedFolderSourceId);
            }

            _watchedFolderSourceId = null;
            _watchedWorktreeRoot = null;
            _statusDebounceTimer?.Dispose();
            _statusDebounceTimer = null;
        }
    }

    /// <summary>
    /// Explicitly pulls one status refresh for the watched worktree (e.g. when
    /// the Source Control surface regains focus). Returns <c>null</c> when no
    /// worktree is watched; the service never guesses a repository.
    /// </summary>
    public async Task<GitStatusResult?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        string? worktreeRoot;
        lock (_watchGate)
        {
            worktreeRoot = _watchedWorktreeRoot;
        }

        if (worktreeRoot is null)
        {
            return null;
        }

        GitStatusResult result = await GetStatusAsync(worktreeRoot, cancellationToken).ConfigureAwait(false);

        Volatile.Write(ref _currentStatus, result);
        OnStatusChanged?.Invoke(this, result);
        return result;
    }

    /// <summary>
    /// Raw coordinator callback. This is a hint only: it resets the debounce
    /// timer and never runs Git on the watcher path (spec 13 §17).
    /// </summary>
    private void HandleCoordinatorFileChanged(object? sender, FileChangeEvent changeEvent)
    {
        lock (_watchGate)
        {
            if (_watchedWorktreeRoot is null)
            {
                return;
            }

            if (!IsWithinWorktree(changeEvent.Path, _watchedWorktreeRoot)
                && !IsWithinWorktree(changeEvent.OldPath, _watchedWorktreeRoot))
            {
                return;
            }

            _statusDebounceTimer ??= new Timer(
                _ => _ = RefreshFromTimerAsync(),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _statusDebounceTimer.Change(_statusDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshFromTimerAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        catch
        {
            // A background hint must never surface an unobserved exception;
            // RefreshAsync already converts Git failures into typed results.
        }
    }

    private static bool IsWithinWorktree(string? path, string worktreeRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return false;
        }

        return fullPath.StartsWith(worktreeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, worktreeRoot, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        StopWatching();

        if (_fileChangeCoordinator is not null)
        {
            _fileChangeCoordinator.OnFileChanged -= HandleCoordinatorFileChanged;
        }
    }

    /// <summary>
    /// Maps a failed Git invocation onto the discovery status vocabulary.
    /// </summary>
    private static GitRepositoryStatus MapFailure(GitProcessResult failure)
    {
        if (failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            return GitRepositoryStatus.GitUnavailable;
        }

        if (failure.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound)
        {
            return GitRepositoryStatus.NoRepository;
        }

        return GitRepositoryStatus.Failed;
    }

    private static GitRepositoryContext Malformed(GitProcessResult source, string requestedPath, string message) =>
        new(
            GitRepositoryStatus.Failed,
            requestedPath,
            Failure: source with
            {
                Status = GitProcessStatus.Failed,
                ErrorKind = GitProcessErrorKind.Unknown,
                ErrorMessage = message,
            });

    private static GitProcessResult LocalFailure(string message) =>
        new(
            GitProcessStatus.Failed,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitWorktreeInvalid,
            message);

    /// <summary>
    /// The repository root is the directory owning the common Git directory;
    /// for linked worktrees this is the main worktree, not the called worktree
    /// (spec 13 §5/§16.1).
    /// </summary>
    private static string DeriveRepositoryRoot(string commonGitDirectory)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(commonGitDirectory);
        string name = Path.GetFileName(trimmed);
        if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(trimmed) ?? trimmed;
        }

        return trimmed;
    }

    /// <summary>
    /// Converts a path value reported by Git (forward slashes, possibly
    /// relative to the called directory) into a canonical local path.
    /// </summary>
    private static string? TryNormalizeGitPath(string gitOutputLine, string baseDirectory)
    {
        string value = gitOutputLine.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            value = value.Replace('/', Path.DirectorySeparatorChar);
            string full = Path.IsPathRooted(value)
                ? value
                : Path.Combine(baseDirectory, value);
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(full));
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

    private static string[] SplitOutputLines(string output) =>
        output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
}
