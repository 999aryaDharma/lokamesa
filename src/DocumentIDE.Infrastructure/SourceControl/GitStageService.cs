using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-06 — Git stage/unstage application service (spec 13 §9.4/§10/§19/§23).
///
/// Every mutation:
/// <list type="bullet">
/// <item>runs through the SCM-01 controlled process boundary with structured
/// arguments and a <c>--</c> separator before paths — never through a shell,</item>
/// <item>is serialized per worktree by the SCM-01 mutation queue: the runner is
/// invoked with the true worktree root (<c>git rev-parse --show-toplevel</c>
/// via <see cref="GitRepositoryService.DiscoverAsync"/>) as the working
/// directory, which is also the queue key contract,</item>
/// <item>validates the requested path as a contained, normalized, literal
/// worktree-relative file path before Git runs — escapes, traversal, absolute
/// paths and directory targets are rejected typed,</item>
/// <item>is scoped to exactly the active repository/worktree,</item>
/// <item>re-reads status from Git after the mutation, and</item>
/// <item>keeps no proprietary staging state: the real Git index is the only
/// staging authority (spec 13 §10/§24).</item>
/// </list>
/// </summary>
public sealed class GitStageService
{
    private const int MaxRelativePathLength = 4096;

    /// <summary>
    /// Forces Git to treat every pathspec literally — no globbing and no
    /// pathspec magic (spec 13 §23): status paths are untrusted data, and a
    /// file named <c>notes[1].md</c> must stage exactly that file rather than
    /// a pattern match. This only adds a variable to the invocation; the
    /// SCM-01 protected environment policy stays non-overridable.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LiteralPathspecEnvironment =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_LITERAL_PATHSPECS"] = "1",
        };

    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;

    public GitStageService(IGitProcessRunner runner, GitRepositoryService repositories)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <summary>
    /// Stages one worktree-relative file (<c>git add -- &lt;path&gt;</c>,
    /// spec 13 §9.4) and returns the re-read status.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative path as Git reported it; treated as untrusted input.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitStageResult> StageAsync(
        string repositoryPath,
        string? relativePath,
        CancellationToken cancellationToken = default) =>
        MutateFileAsync(repositoryPath, relativePath, stage: true, "stage", cancellationToken);

    /// <summary>
    /// Unstages one worktree-relative file (<c>git restore --staged -- &lt;path&gt;</c>,
    /// spec 13 §9.4) and returns the re-read status. Working-tree edits are
    /// never touched.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative path as Git reported it; treated as untrusted input.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitStageResult> UnstageAsync(
        string repositoryPath,
        string? relativePath,
        CancellationToken cancellationToken = default) =>
        MutateFileAsync(repositoryPath, relativePath, stage: false, "unstage", cancellationToken);

    /// <summary>
    /// Stages every eligible change in the active worktree
    /// (<c>git add --all -- .</c>, spec 13 §9.4/§11.2): modified, deleted and
    /// untracked files; ignored files are never included and nothing outside
    /// the resolved worktree root is touched.
    /// </summary>
    public async Task<GitStageResult> StageAllAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return DiscoveryFailure(context);
        }

        return await MutateAsync(
            context.WorktreeRoot!,
            new[] { "add", "--all", "--", "." },
            cancellationToken);
    }

    /// <summary>
    /// Unstages every staged path in the active worktree
    /// (<c>git restore --staged -- .</c>, spec 13 §9.4); working-tree content
    /// is never touched and nothing outside the resolved worktree root is
    /// affected.
    /// </summary>
    public async Task<GitStageResult> UnstageAllAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return DiscoveryFailure(context);
        }

        return await MutateAsync(
            context.WorktreeRoot!,
            new[] { "restore", "--staged", "--", "." },
            cancellationToken);
    }

    /// <summary>
    /// Shared single-file mutation pipeline: discovery → path validation →
    /// Git mutation with the true worktree root as working directory → status
    /// re-read.
    /// </summary>
    private async Task<GitStageResult> MutateFileAsync(
        string repositoryPath,
        string? relativePath,
        bool stage,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Rejected(
                "PathRequired",
                $"A worktree-relative file path is required for a single-file {operationLabel}.");
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return DiscoveryFailure(context);
        }

        string worktreeRoot = context.WorktreeRoot!;
        (string? gitPath, string? errorCode, string? message) = ResolveContainedPath(worktreeRoot, relativePath);
        if (gitPath is null)
        {
            return Rejected(errorCode!, message!);
        }

        string[] arguments = stage
            ? new[] { "add", "--", gitPath }
            : new[] { "restore", "--staged", "--", gitPath };
        return await MutateAsync(worktreeRoot, arguments, cancellationToken);
    }

    /// <summary>
    /// Runs one mutation against the worktree and re-reads status from Git
    /// afterwards (spec 13 §10). The runner routes mutating invocations through
    /// the per-worktree mutation queue (spec 13 §19).
    /// </summary>
    private async Task<GitStageResult> MutateAsync(
        string worktreeRoot,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        GitProcessResult raw = await _runner.RunAsync(
            new GitProcessRequest(arguments, worktreeRoot, EnvironmentOverrides: LiteralPathspecEnvironment),
            cancellationToken);

        if (!raw.IsSuccess)
        {
            return new GitStageResult(
                GitStageOutcome.Failed,
                null,
                raw,
                raw.ErrorMessage ?? "The Git stage operation failed.");
        }

        GitStatusResult refreshed = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        return new GitStageResult(GitStageOutcome.Succeeded, refreshed, null, null);
    }

    /// <summary>
    /// Validates one client-supplied change path as a contained, normalized,
    /// literal, worktree-relative file path (spec 13 §23). Rejection happens
    /// before any Git or filesystem access, so a rejected request can never
    /// widen scope or touch a path outside the selected worktree. The returned
    /// value is the forward-slash form passed to Git after <c>--</c>.
    /// </summary>
    private static (string? GitPath, string? ErrorCode, string? Message) ResolveContainedPath(
        string worktreeRoot,
        string relativePath)
    {
        string candidate = relativePath.Replace('\\', '/').Trim();
        if (candidate.Length == 0)
        {
            return (null, "PathRequired", "A worktree-relative file path is required.");
        }

        if (candidate.Length > MaxRelativePathLength
            || candidate.StartsWith('/')
            || Path.IsPathRooted(candidate))
        {
            return (
                null,
                "AbsolutePathNotAllowed",
                $"The path '{relativePath}' is not a worktree-relative file path: " +
                "absolute, rooted and drive-qualified paths are rejected.");
        }

        if (candidate.Contains(':'))
        {
            // Colon-carrying paths are invalid on Windows and are Git
            // pathspec/object syntax on every platform; the client contract is
            // the verbatim status path.
            return (
                null,
                "PathInvalid",
                $"The path '{relativePath}' is not a valid repository path: ':' is not accepted.");
        }

        foreach (string segment in candidate.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return (
                    null,
                    "PathNotNormalized",
                    $"The path '{relativePath}' is not a normalized worktree-relative file path: " +
                    "empty, '.' and '..' segments are rejected.");
            }
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeRoot));
            string combined = Path.GetFullPath(
                Path.Combine(root, candidate.Replace('/', Path.DirectorySeparatorChar)));
            string relative = Path.GetRelativePath(root, combined);
            if (relative == ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                return (
                    null,
                    "PathOutsideWorktree",
                    $"The path '{relativePath}' resolves outside the worktree root; the request was rejected.");
            }

            // A single-file action must stay a single-file action: a directory
            // target would silently stage the whole subtree.
            if (Directory.Exists(combined))
            {
                return (
                    null,
                    "PathNotAFile",
                    $"The path '{relativePath}' is a directory; single-file stage/unstage only accepts file paths.");
            }

            return (candidate, null, null);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return (
                null,
                "PathInvalid",
                $"The path '{relativePath}' is not a usable filesystem path: {exception.Message}");
        }
    }

    private static GitStageResult Rejected(string errorCode, string message) =>
        new(GitStageOutcome.RejectedPath, null, null, message, errorCode);

    private static GitStageResult DiscoveryFailure(GitRepositoryContext context) => new(
        context.Status switch
        {
            GitRepositoryStatus.NoRepository => GitStageOutcome.NoRepository,
            GitRepositoryStatus.GitUnavailable => GitStageOutcome.GitUnavailable,
            _ => GitStageOutcome.Failed,
        },
        null,
        context.Failure,
        context.Failure?.ErrorMessage);
}
