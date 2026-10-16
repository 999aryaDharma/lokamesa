using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-15 — Git worktree discovery and open-existing-worktree context
/// (spec 13 §16).
///
/// Discovery is derived from Git itself (`git worktree list --porcelain`,
/// invoked exclusively through the SCM-01 <see cref="IGitProcessRunner"/>);
/// Document IDE never invents a parallel worktree registry, never switches a
/// branch merely to open a worktree, and never mutates Git history.
///
/// Path handling is canonical and conservative: every path in the porcelain
/// output is fully qualified and canonicalized, and the open action only ever
/// accepts a location Git has registered as a worktree. A malformed registry
/// yields a typed <see cref="GitWorktreeListStatus.ParseError"/> instead of a
/// partial list.
/// </summary>
public sealed class GitWorktreeService : IGitWorktreeService
{
    private static readonly string[] ListArguments = { "worktree", "list", "--porcelain" };

    private readonly IGitProcessRunner _runner;
    private readonly IFolderSourceService _folderSourceService;
    private GitWorktreeInfo? _activeWorktree;

    public GitWorktreeService(IGitProcessRunner runner, IFolderSourceService folderSourceService)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _folderSourceService = folderSourceService ?? throw new ArgumentNullException(nameof(folderSourceService));
    }

    /// <inheritdoc />
    public GitWorktreeInfo? ActiveWorktree => Volatile.Read(ref _activeWorktree);

    /// <inheritdoc />
    public async Task<GitWorktreeOpenResult> OpenWorktreeAsync(
        GitWorktreeOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            return Invalid("A workspace id is required to open a worktree.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            return Invalid("A repository working directory is required to open a worktree.");
        }

        if (string.IsNullOrWhiteSpace(request.WorktreePath))
        {
            return Invalid("A worktree path is required.");
        }

        if (!TryCanonicalizeWorktreePath(request.WorktreePath, out string canonicalWorktreePath))
        {
            return Invalid($"Worktree path '{request.WorktreePath}' is not a canonical fully qualified local path.");
        }

        GitWorktreeListResult list = await ListWorktreesAsync(request.WorkingDirectory, cancellationToken);

        if (!list.IsSuccess)
        {
            GitWorktreeOpenStatus status = list.Status switch
            {
                GitWorktreeListStatus.Cancelled => GitWorktreeOpenStatus.Cancelled,
                GitWorktreeListStatus.TimedOut => GitWorktreeOpenStatus.TimedOut,
                _ => GitWorktreeOpenStatus.ListFailed,
            };

            return new GitWorktreeOpenResult(status, null, false, list.ErrorMessage, list);
        }

        // Containment: the canonical target must be one of the locations Git
        // registered as a worktree for this repository. Anything else —
        // including traversal forms that resolve outside every registered
        // root — is rejected typed and never becomes the active context.
        GitWorktreeInfo? match = list.Worktrees.FirstOrDefault(
            worktree => PathEquals(worktree.Path, canonicalWorktreePath));

        if (match is null)
        {
            return new GitWorktreeOpenResult(
                GitWorktreeOpenStatus.NotRegistered,
                null,
                false,
                $"'{canonicalWorktreePath}' is not a location Git registered as a worktree of the active repository.");
        }

        if (!Directory.Exists(match.Path))
        {
            return new GitWorktreeOpenResult(
                GitWorktreeOpenStatus.Unavailable,
                match,
                false,
                $"Worktree '{match.Path}' is registered in Git but its directory is missing or unavailable.");
        }

        IReadOnlyList<FolderSource> workspaceSources =
            await _folderSourceService.GetFolderSourcesAsync(request.WorkspaceId);

        bool isAttached = workspaceSources.Any(
            source => !string.IsNullOrWhiteSpace(source.CanonicalPath)
                      && PathEquals(source.CanonicalPath, match.Path));

        if (!isAttached)
        {
            // Spec 13 §16.2: attaching a worktree is an explicit user choice
            // through the existing folder-source flow. Nothing is attached,
            // detached, or reorganized silently.
            return new GitWorktreeOpenResult(
                GitWorktreeOpenStatus.RequiresAttachmentFlow,
                match,
                false,
                $"Worktree '{match.Path}' is not attached to this workspace; use the existing folder source attachment flow first.");
        }

        Volatile.Write(ref _activeWorktree, match);
        return new GitWorktreeOpenResult(GitWorktreeOpenStatus.Opened, match, true);
    }

    /// <inheritdoc />
    public async Task<GitWorktreeListResult> ListWorktreesAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(
                "A working directory is required to list Git worktrees.",
                nameof(workingDirectory));
        }

        GitProcessResult process = await _runner.RunAsync(
            new GitProcessRequest(ListArguments, workingDirectory),
            cancellationToken);

        switch (process.Status)
        {
            case GitProcessStatus.Succeeded:
                return ParsePorcelainList(process);

            case GitProcessStatus.Unavailable:
                return Failure(
                    GitWorktreeListStatus.GitUnavailable,
                    process,
                    process.ErrorMessage ?? "Git is not installed or is not discoverable.");

            case GitProcessStatus.Cancelled:
                return Failure(
                    GitWorktreeListStatus.Cancelled,
                    process,
                    "Listing Git worktrees was cancelled.");

            case GitProcessStatus.TimedOut:
                return Failure(
                    GitWorktreeListStatus.TimedOut,
                    process,
                    "Listing Git worktrees exceeded the time limit and was terminated.");

            case GitProcessStatus.Failed when process.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound:
                return Failure(
                    GitWorktreeListStatus.NotARepository,
                    process,
                    process.ErrorMessage);

            default:
                return Failure(
                    GitWorktreeListStatus.Failed,
                    process,
                    process.ErrorMessage ?? "`git worktree list --porcelain` failed.");
        }
    }

    private static GitWorktreeListResult Failure(
        GitWorktreeListStatus status,
        GitProcessResult process,
        string? message) =>
        new(status, Array.Empty<GitWorktreeInfo>(), process, message);

    private static GitWorktreeOpenResult Invalid(string message) =>
        new(GitWorktreeOpenStatus.InvalidRequest, null, false, message);

    /// <summary>
    /// Canonicalizes a requested worktree path without ever resolving it
    /// against the application's current directory: only fully qualified
    /// local paths are accepted, so a relative/traversal request can never
    /// silently point at an unintended location.
    /// </summary>
    private static bool TryCanonicalizeWorktreePath(string rawPath, out string canonicalPath)
    {
        try
        {
            if (!Path.IsPathFullyQualified(rawPath))
            {
                canonicalPath = string.Empty;
                return false;
            }

            canonicalPath = Path.GetFullPath(rawPath);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            canonicalPath = string.Empty;
            return false;
        }
    }

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static GitWorktreeListResult ParsePorcelainList(GitProcessResult process)
    {
        List<GitWorktreeInfo> worktrees;
        try
        {
            worktrees = PorcelainWorktreeParser.Parse(process.StandardOutput);
        }
        catch (GitWorktreeParseException exception)
        {
            return Failure(GitWorktreeListStatus.ParseError, process, exception.Message);
        }

        if (worktrees.Count == 0)
        {
            return Failure(
                GitWorktreeListStatus.ParseError,
                process,
                "`git worktree list --porcelain` produced no worktree entries.");
        }

        return new GitWorktreeListResult(GitWorktreeListStatus.Succeeded, worktrees, process);
    }

    /// <summary>
    /// Parses the stable porcelain format of `git worktree list --porcelain`.
    /// Each blank-line-separated entry starts with `worktree &lt;path&gt;` and
    /// carries `HEAD &lt;oid&gt;` plus exactly one of `branch &lt;ref&gt;` /
    /// `detached` (or `bare`), followed by optional `locked`/`prunable`
    /// attributes. Unknown attribute lines are skipped for forward
    /// compatibility; structural violations fail typed.
    /// </summary>
    private static class PorcelainWorktreeParser
    {
        public static List<GitWorktreeInfo> Parse(string porcelainOutput)
        {
            var worktrees = new List<GitWorktreeInfo>();
            EntryBuilder? current = null;
            int lineNumber = 0;

            foreach (string rawLine in porcelainOutput.Split('\n'))
            {
                lineNumber++;
                string line = rawLine.TrimEnd('\r');

                if (line.Length == 0)
                {
                    Flush(current, worktrees);
                    current = null;
                    continue;
                }

                (string key, string value) = SplitAttribute(line);
                if (current is null)
                {
                    if (!string.Equals(key, "worktree", StringComparison.Ordinal))
                    {
                        throw ParseError(
                            lineNumber,
                            $"expected 'worktree <path>' to start an entry but found '{key}'.");
                    }

                    current = new EntryBuilder();
                }

                current.Apply(key, value, lineNumber);
            }

            Flush(current, worktrees);
            return worktrees;

            static void Flush(EntryBuilder? builder, List<GitWorktreeInfo> target)
            {
                if (builder is null)
                {
                    return;
                }

                target.Add(builder.Build(target.Count == 0));
            }
        }

        private static (string Key, string Value) SplitAttribute(string line)
        {
            int separator = line.IndexOf(' ');
            return separator < 0
                ? (line, string.Empty)
                : (line[..separator], line[(separator + 1)..]);
        }

        private static GitWorktreeParseException ParseError(int lineNumber, string detail) =>
            new($"Malformed `git worktree list --porcelain` output at line {lineNumber}: {detail}");

        private sealed class EntryBuilder
        {
            private string? _rawPath;
            private string? _headCommit;
            private string? _branchRef;
            private bool _detached;
            private bool _bare;
            private bool _locked;
            private string? _lockReason;
            private bool _prunable;
            private string? _pruneReason;

            public void Apply(string key, string value, int lineNumber)
            {
                switch (key)
                {
                    case "worktree":
                        if (_rawPath is not null)
                        {
                            throw ParseError(lineNumber, "duplicate 'worktree' attribute in one entry.");
                        }

                        if (value.Length == 0)
                        {
                            throw ParseError(lineNumber, "'worktree' attribute carries no path.");
                        }

                        _rawPath = value;
                        break;

                    case "HEAD":
                        _headCommit = value;
                        break;

                    case "branch":
                        _branchRef = value;
                        break;

                    case "detached":
                        _detached = true;
                        break;

                    case "bare":
                        _bare = true;
                        break;

                    case "locked":
                        _locked = true;
                        _lockReason = value.Length == 0 ? null : value;
                        break;

                    case "prunable":
                        _prunable = true;
                        _pruneReason = value.Length == 0 ? null : value;
                        break;

                    default:
                        // Unknown attributes are skipped on purpose: Git
                        // documents the porcelain format as extensible.
                        break;
                }
            }

            public GitWorktreeInfo Build(bool isMainWorktree)
            {
                if (_rawPath is null)
                {
                    throw new GitWorktreeParseException(
                        "Malformed `git worktree list --porcelain` output: an entry has no 'worktree' path.");
                }

                if (string.IsNullOrEmpty(_headCommit))
                {
                    throw new GitWorktreeParseException(
                        $"Malformed `git worktree list --porcelain` output: worktree '{_rawPath}' has no 'HEAD' attribute.");
                }

                // A registered worktree path is always absolute. Reject relative
                // or otherwise uncanonicalizable paths instead of resolving them
                // against the application's current directory, so a crafted
                // registry entry can never escape into an unintended location.
                string canonicalPath;
                try
                {
                    if (!Path.IsPathFullyQualified(_rawPath))
                    {
                        throw new GitWorktreeParseException(
                            $"Malformed `git worktree list --porcelain` output: worktree path '{_rawPath}' is not fully qualified.");
                    }

                    canonicalPath = Path.GetFullPath(_rawPath);
                }
                catch (GitWorktreeParseException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                        or NotSupportedException
                        or PathTooLongException
                        or System.Security.SecurityException)
                {
                    throw new GitWorktreeParseException(
                        $"Malformed `git worktree list --porcelain` output: worktree path '{_rawPath}' is not a valid local path ({exception.Message}).");
                }

                return new GitWorktreeInfo(
                    canonicalPath,
                    _headCommit!,
                    _branchRef,
                    _detached,
                    _bare,
                    _locked,
                    _lockReason,
                    _prunable,
                    _pruneReason,
                    isMainWorktree);
            }
        }
    }
}

/// <summary>SCM-15 — typed parse failure for malformed porcelain worktree output.</summary>
public sealed class GitWorktreeParseException : Exception
{
    public GitWorktreeParseException(string message)
        : base(message)
    {
    }
}
