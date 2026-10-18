using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-04 — Markdown Git diff application service (spec 13 §9.1/§9.3).
///
/// The service materializes both versions of one file — HEAD via
/// <c>git show HEAD:&lt;path&gt;</c>, the index via <c>git show :&lt;path&gt;</c>, the
/// working tree from a direct read of the file on disk — and renders a
/// deterministic line-based unified diff. It is strictly read-only:
/// <list type="bullet">
/// <item>every Git invocation goes through the SCM-01 controlled process
/// boundary with structured arguments and <c>--</c>-free object names that
/// cannot be confused with options,</item>
/// <item>the working-tree side is read, never written; no temporary file, no
/// index write and no optional index lock is taken,</item>
/// <item>the requested path is validated as a worktree-relative document path
/// (no absolute paths, no <c>..</c> segments) and containment is re-checked on
/// the combined path before any file access,</item>
/// <item>failures are typed <see cref="GitDiffResult"/> statuses with the
/// originating <see cref="GitProcessResult"/>, never exceptions or silently
/// empty diffs.</item>
/// </list>
/// </summary>
public sealed class GitDiffService
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly string[] HeadProbeArguments =
    {
        "rev-parse", "--verify", "--quiet", "HEAD",
    };

    private readonly IGitProcessRunner _runner;

    public GitDiffService(IGitProcessRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// Produces the read-only textual diff of one worktree-relative file for
    /// the requested version pair (spec 13 §9.1).
    /// </summary>
    /// <param name="worktreeRoot">
    /// The true worktree root (e.g. <see cref="GitRepositoryContext.WorktreeRoot"/>).
    /// </param>
    /// <param name="relativePath">
    /// Worktree-root-relative path exactly as Git reported it
    /// (<see cref="GitStatusEntry.Path"/>); treated as untrusted input and
    /// rejected when absolute or escaping the worktree.
    /// </param>
    /// <param name="target">Which version pair to compare.</param>
    /// <param name="originalPath">
    /// Optional pre-rename path from Git status
    /// (<see cref="GitStatusEntry.OriginalPath"/>). When the path does not
    /// exist in HEAD, the HEAD side is materialized from this path so a staged
    /// rename/copy shows its content change instead of "everything added".
    /// </param>
    /// <param name="cancellationToken">Cancels the Git invocations (typed, never thrown).</param>
    public async Task<GitDiffResult> GetFileDiffAsync(
        string worktreeRoot,
        string relativePath,
        GitDiffTarget target,
        string? originalPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown diff target.");
        }

        string canonicalRoot;
        string? rootError;
        if (!GitDocumentPath.TryCanonicalizeRoot(worktreeRoot, out canonicalRoot, out rootError))
        {
            return Failure(
                GitDiffStatus.Failed,
                LocalFailure(
                    GitProcessErrorKind.GitWorktreeInvalid,
                    $"Path '{worktreeRoot}' is not a usable filesystem path: {rootError}"));
        }

        if (!Directory.Exists(canonicalRoot))
        {
            // Spec 13 §20: a worktree that disappeared during an operation is a
            // typed failure, not an empty or guessed diff.
            return Failure(
                GitDiffStatus.Failed,
                LocalFailure(
                    GitProcessErrorKind.GitWorktreeInvalid,
                    $"The worktree directory '{canonicalRoot}' does not exist any more."));
        }

        if (!GitDocumentPath.TryNormalizeRelativePath(relativePath, out string gitPath)
            || !GitDocumentPath.TryResolveContainedPath(canonicalRoot, gitPath, out string fullPath))
        {
            return Failure(
                GitDiffStatus.PathRejected,
                LocalFailure(
                    GitProcessErrorKind.GitWorktreeInvalid,
                    $"The path '{relativePath}' is not a worktree-relative document path; " +
                    "the request was rejected before any Git or filesystem access."));
        }

        string? originalGitPath = null;
        if (!string.IsNullOrWhiteSpace(originalPath))
        {
            if (!GitDocumentPath.TryNormalizeRelativePath(originalPath, out originalGitPath)
                || !GitDocumentPath.TryResolveContainedPath(canonicalRoot, originalGitPath, out _))
            {
                return Failure(
                    GitDiffStatus.PathRejected,
                    LocalFailure(
                        GitProcessErrorKind.GitWorktreeInvalid,
                        $"The original path '{originalPath}' is not a worktree-relative document path; " +
                        "the request was rejected before any Git or filesystem access."));
            }
        }

        bool headRequired = target is GitDiffTarget.Staged or GitDiffTarget.WorkingTree;
        bool headExists = false;
        if (headRequired)
        {
            (headExists, GitProcessResult? headProbeFailure) =
                await ProbeHeadAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
            if (headProbeFailure is not null)
            {
                return Failure(MapFailure(headProbeFailure), headProbeFailure);
            }
        }

        (string leftLabel, string rightLabel) = LabelsFor(target);

        SideRead left;
        SideRead right;
        switch (target)
        {
            case GitDiffTarget.Staged:
                left = await ReadHeadSideAsync(canonicalRoot, gitPath, originalGitPath, headExists, cancellationToken)
                    .ConfigureAwait(false);
                right = await ReadIndexSideAsync(canonicalRoot, gitPath, cancellationToken).ConfigureAwait(false);
                break;

            case GitDiffTarget.WorkingTree:
                left = await ReadHeadSideAsync(canonicalRoot, gitPath, originalGitPath, headExists, cancellationToken)
                    .ConfigureAwait(false);
                right = ReadWorktreeSide(fullPath);
                break;

            default:
                left = await ReadIndexSideAsync(canonicalRoot, gitPath, cancellationToken).ConfigureAwait(false);
                right = ReadWorktreeSide(fullPath);
                break;
        }

        if (left.Failure is not null)
        {
            return Failure(MapFailure(left.Failure), left.Failure);
        }

        if (right.Failure is not null)
        {
            return Failure(MapFailure(right.Failure), right.Failure);
        }

        bool isBinary = left.Outcome == SideOutcome.Binary
                        || right.Outcome == SideOutcome.Binary
                        || left.Content.Contains('\0')
                        || right.Content.Contains('\0');
        bool leftExists = left.Outcome is SideOutcome.Present or SideOutcome.Binary;
        bool rightExists = right.Outcome is SideOutcome.Present or SideOutcome.Binary;

        IReadOnlyList<GitDiffHunk> hunks = Array.Empty<GitDiffHunk>();
        string unifiedText = string.Empty;
        if (!isBinary)
        {
            (string[] oldLines, bool oldEndsWithNewline) = SplitLines(left.Content);
            (string[] newLines, bool newEndsWithNewline) = SplitLines(right.Content);

            hunks = GitUnifiedDiffFormatter.ComputeHunks(
                oldLines, oldEndsWithNewline, newLines, newEndsWithNewline);
            unifiedText = GitUnifiedDiffFormatter.FormatUnifiedText(
                gitPath, leftLabel, rightLabel, leftExists, rightExists, hunks);
        }

        var diff = new GitDiff(
            target,
            gitPath,
            canonicalRoot,
            leftLabel,
            rightLabel,
            leftExists,
            rightExists,
            isBinary,
            hunks,
            unifiedText);

        return new GitDiffResult(GitDiffStatus.Available, diff);
    }

    private static (string Left, string Right) LabelsFor(GitDiffTarget target) => target switch
    {
        GitDiffTarget.Staged => ("HEAD", "index"),
        GitDiffTarget.WorkingTree => ("HEAD", "working tree"),
        GitDiffTarget.Unstaged => ("index", "working tree"),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown diff target."),
    };

    /// <summary>
    /// <c>rev-parse --verify --quiet HEAD</c>: distinguishes "repository with
    /// a born HEAD" from an unborn branch (exit 1, silent) and from real
    /// failures. An unborn repository diffs every file against nothing, which
    /// is the correct typed behavior instead of an error.
    /// </summary>
    private async Task<(bool Exists, GitProcessResult? Failure)> ProbeHeadAsync(
        string canonicalRoot,
        CancellationToken cancellationToken)
    {
        GitProcessResult probe = await _runner
            .RunAsync(new GitProcessRequest(HeadProbeArguments, canonicalRoot), cancellationToken)
            .ConfigureAwait(false);

        if (probe.IsSuccess)
        {
            return (true, null);
        }

        if (probe.Status == GitProcessStatus.Failed
            && probe.ExitCode == 1
            && string.IsNullOrWhiteSpace(probe.StandardError))
        {
            return (false, null);
        }

        return (false, probe);
    }

    private async Task<SideRead> ReadHeadSideAsync(
        string canonicalRoot,
        string gitPath,
        string? originalGitPath,
        bool headExists,
        CancellationToken cancellationToken)
    {
        if (!headExists)
        {
            return SideRead.Absent;
        }

        SideRead primary = await ShowHeadBlobAsync(canonicalRoot, gitPath, cancellationToken)
            .ConfigureAwait(false);
        if (primary.Outcome != SideOutcome.Absent)
        {
            return primary;
        }

        if (originalGitPath is not null
            && !string.Equals(originalGitPath, gitPath, StringComparison.Ordinal))
        {
            // Staged rename/copy: the new path does not exist in HEAD, but the
            // original path holds the content Git treats as the diff base.
            return await ShowHeadBlobAsync(canonicalRoot, originalGitPath, cancellationToken)
                .ConfigureAwait(false);
        }

        return SideRead.Absent;
    }

    private async Task<SideRead> ShowHeadBlobAsync(
        string canonicalRoot,
        string gitPath,
        CancellationToken cancellationToken)
    {
        GitProcessResult result = await _runner
            .RunAsync(
                new GitProcessRequest(
                    new[] { "--no-optional-locks", "show", $"HEAD:{gitPath}" },
                    canonicalRoot),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return SideRead.Present(result.StandardOutput);
        }

        if (result.Status == GitProcessStatus.Failed && GitErrorSignatures.IsHeadPathMissing(result))
        {
            return SideRead.Absent;
        }

        return SideRead.Failed(result);
    }

    private async Task<SideRead> ReadIndexSideAsync(
        string canonicalRoot,
        string gitPath,
        CancellationToken cancellationToken)
    {
        GitProcessResult result = await _runner
            .RunAsync(
                new GitProcessRequest(
                    new[] { "--no-optional-locks", "show", $":{gitPath}" },
                    canonicalRoot),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return SideRead.Present(result.StandardOutput);
        }

        if (result.Status == GitProcessStatus.Failed && GitErrorSignatures.IsIndexPathMissing(result))
        {
            // Not in the index: an untracked file (no index version to compare
            // against) or a file staged for deletion.
            return SideRead.Absent;
        }

        if (result.Status == GitProcessStatus.Failed && GitErrorSignatures.IsUnmergedIndexPath(result))
        {
            // Spec 13 §21: unmerged paths have no stage-0 version. Report the
            // conflict state explicitly instead of an empty or misleading diff.
            return SideRead.Failed(result with
            {
                ErrorKind = GitProcessErrorKind.GitConflict,
                ErrorMessage =
                    "The file is unmerged; the index has no stage-0 version to diff. " +
                    "Resolve the conflict before reviewing staged content (spec 13 §21).",
            });
        }

        return SideRead.Failed(result);
    }

    private static SideRead ReadWorktreeSide(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            // A deleted working-tree file is a valid diff side, not an error.
            return SideRead.Absent;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            return SideRead.Failed(LocalFailure(
                GitProcessErrorKind.GitPermissionDenied,
                $"The working-tree file '{fullPath}' could not be read: {exception.Message}"));
        }
        catch (Exception exception) when (
            exception is IOException or System.Security.SecurityException)
        {
            return SideRead.Failed(LocalFailure(
                GitProcessErrorKind.Unknown,
                $"The working-tree file '{fullPath}' could not be read: {exception.Message}"));
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return SideRead.Binary;
        }

        try
        {
            return SideRead.Present(StrictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8: report a binary change rather than silently
            // rendering replacement characters (spec 13 §9.3).
            return SideRead.Binary;
        }
    }

    /// <summary>
    /// Splits content into lines following the git/GNU convention: a trailing
    /// line terminator does not create an extra empty line, a lone <c>\r</c>
    /// per line (CRLF) is normalized away for comparison, and empty content
    /// has zero lines.
    /// </summary>
    private static (string[] Lines, bool EndsWithNewline) SplitLines(string content)
    {
        if (content.Length == 0)
        {
            return (Array.Empty<string>(), false);
        }

        bool endsWithNewline = content[^1] == '\n';
        string body = endsWithNewline ? content[..^1] : content;
        string[] lines = body.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].EndsWith('\r'))
            {
                lines[i] = lines[i][..^1];
            }
        }

        return (lines, endsWithNewline);
    }

    private static GitDiffStatus MapFailure(GitProcessResult failure) =>
        failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled
            ? GitDiffStatus.GitUnavailable
            : failure.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound
                ? GitDiffStatus.NoRepository
                : GitDiffStatus.Failed;

    private static GitProcessResult LocalFailure(GitProcessErrorKind kind, string message) =>
        new(GitProcessStatus.Failed, null, string.Empty, string.Empty, kind, message);

    private static GitDiffResult Failure(GitDiffStatus status, GitProcessResult failure) =>
        new(status, null, failure);

    private enum SideOutcome
    {
        Present,
        Absent,

        /// <summary>Present but not a textual (UTF-8) version; never rendered as a text diff (spec 13 §9.3).</summary>
        Binary,

        Failed,
    }

    private readonly record struct SideRead(SideOutcome Outcome, string Content, GitProcessResult? Failure)
    {
        public static SideRead Absent { get; } = new(SideOutcome.Absent, string.Empty, null);

        public static SideRead Binary { get; } = new(SideOutcome.Binary, string.Empty, null);

        public static SideRead Present(string content) => new(SideOutcome.Present, content, null);

        public static SideRead Failed(GitProcessResult failure) => new(SideOutcome.Failed, string.Empty, failure);
    }
}
