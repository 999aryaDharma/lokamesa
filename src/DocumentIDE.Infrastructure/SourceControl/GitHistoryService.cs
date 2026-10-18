using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-08 — Git commit history and file history application service
/// (spec 13 §9.4/§12/§18/§19/§20/§23).
///
/// Every read:
/// <list type="bullet">
/// <item>runs through the SCM-01 controlled process boundary with structured
/// arguments — never through a shell, and paths always travel after a
/// <c>--</c> separator,</item>
/// <item>uses the true worktree root resolved by
/// <see cref="GitRepositoryService.DiscoverAsync"/> as the working directory,
/// so a caller may pass a workspace folder or an active-document path,</item>
/// <item>is strictly read-only (<c>git log</c>/<c>git show</c> are in the
/// SCM-01 read-only vocabulary, so they never take the per-worktree mutation
/// queue and never touch the index) — Git history is never rewritten and no
/// Document IDE history row is consulted (spec 13 §4.2/§4.3/§12),</item>
/// <item>parses Git's <c>--format</c> output structurally: fields are
/// NUL-separated and a changed-file name can never blur a field boundary, and</item>
/// <item>returns typed, inspectable outcomes instead of exceptions or silent
/// empty pages (spec 13 §20).</item>
/// </list>
/// </summary>
public sealed class GitHistoryService
{
    /// <summary>
    /// Reads one commit's metadata per record, then Git's changed-file names.
    /// The leading <c>-c core.quotepath=false</c> keeps non-ASCII paths
    /// verbatim (the same convention SCM-06/SCM-07 test helpers use), and
    /// <c>%x00</c> is the NUL field separator, so a subject can never blur the
    /// field boundaries.
    /// </summary>
    private static readonly string[] HistoryPrefixArguments =
    {
        "-c", "core.quotepath=false",
        "--no-pager",
        "log",
        "--no-color",
        "--format=%H%x00%h%x00%an%x00%ae%x00%cI%x00%s",
        "--name-only",
    };

    /// <summary>
    /// Forces Git to treat the file-history pathspec literally — no globbing
    /// and no pathspec magic (spec 13 §23): a document named
    /// <c>notes[1].md</c> must be looked up as exactly that file.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LiteralPathspecEnvironment =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_LITERAL_PATHSPECS"] = "1",
        };

    private const int MetadataFieldCount = 6;

    /// <summary>
    /// The same exit-code HEAD probe SCM-05 uses (<see cref="DocxGitDiffProvider"/>):
    /// a born HEAD exits 0 with an object id, an unborn branch exits 1 with an
    /// empty stderr, and anything else is a real failure. Unlike a stderr
    /// substring it does not depend on Git's English message wording.
    /// </summary>
    private static readonly string[] HeadProbeArguments =
    {
        "rev-parse", "--verify", "--quiet", "HEAD",
    };

    /// <summary>
    /// Largest document version this surface serves in one read (256 KiB). A
    /// larger blob is reported with its true size and no content — it is never
    /// read — so one click cannot pull an unbounded payload into the pane
    /// (spec 13 §25).
    /// </summary>
    public const int MaxVersionBytes = 256 * 1024;

    /// <summary>
    /// Bytes inspected for the text/binary decision. Mirrors Git's own
    /// heuristic (a NUL byte in the leading window means binary) so the pane
    /// never renders a byte blob as text (spec 13 §9.3/§12).
    /// </summary>
    private const int BinarySniffBytes = 8000;

    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;

    public GitHistoryService(IGitProcessRunner runner, GitRepositoryService repositories)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <summary>
    /// Reads one page of the repository's commit history, newest first
    /// (<c>git log</c>, spec 13 §9.4/§12).
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="query">Pagination window; <c>null</c> reads the first default page.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitHistoryResult> GetHistoryAsync(
        string repositoryPath,
        GitHistoryQuery? query = null,
        CancellationToken cancellationToken = default) =>
        ReadHistoryAsync(
            repositoryPath,
            relativePath: null,
            fileScoped: false,
            query ?? GitHistoryQuery.Default,
            cancellationToken);

    /// <summary>
    /// Reads one page of the commits that touched one selected document
    /// (<c>git log -- &lt;path&gt;</c>, spec 13 §9.4/§12).
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative document path as Git reported it; treated as untrusted input.</param>
    /// <param name="query">Pagination window; <c>null</c> reads the first default page.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitHistoryResult> GetFileHistoryAsync(
        string repositoryPath,
        string? relativePath,
        GitHistoryQuery? query = null,
        CancellationToken cancellationToken = default) =>
        ReadHistoryAsync(
            repositoryPath,
            relativePath,
            fileScoped: true,
            query ?? GitHistoryQuery.Default,
            cancellationToken);

    /// <summary>
    /// Reads the selected document as it was committed in one selected commit
    /// (<c>git show &lt;commit&gt;:&lt;path&gt;</c>, spec 13 §12).
    ///
    /// The commit id and the document path are untrusted input: both are
    /// validated before Git runs, so a request can never turn into an
    /// arbitrary revision or option lookup (spec 13 §23). The read is a
    /// pure projection of the committed blob — it never touches the index,
    /// the working tree or HEAD.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative document path as Git reported it.</param>
    /// <param name="commitId">Full or abbreviated commit object id to read the version from.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public async Task<GitFileVersionResult> GetFileVersionAsync(
        string repositoryPath,
        string? relativePath,
        string? commitId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // Only a commit object id (full or abbreviated) is a commit
        // identifier. Revision expressions (`HEAD`, `HEAD~1`, `main`),
        // option-looking values and non-hex text are rejected typed before any
        // Git invocation (spec 13 §23).
        string revision = commitId?.Trim() ?? string.Empty;
        if (!IsCommitIdentifier(revision))
        {
            return VersionRejected(
                GitHistoryStatus.InvalidRequest,
                "InvalidCommitId",
                "A commit object id is required to inspect a document version; " +
                "the request was rejected before Git ran.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return VersionRejected(
                GitHistoryStatus.InvalidRequest,
                "PathRequired",
                "A worktree-relative document path is required to inspect a document version.");
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return VersionDiscoveryFailure(context);
        }

        string worktreeRoot = context.WorktreeRoot!;

        // Untrusted input: a path that is absolute, rooted, unnormalized or
        // escaping is rejected before any Git or filesystem access, so a
        // version read can never leave the selected worktree (spec 13 §23).
        if (!GitDocumentPath.TryNormalizeRelativePath(relativePath, out string gitPath)
            || !GitDocumentPath.TryResolveContainedPath(worktreeRoot, gitPath, out _))
        {
            return VersionRejected(
                GitHistoryStatus.PathRejected,
                "PathRejected",
                $"The path '{relativePath}' is not a worktree-relative document path; " +
                "the request was rejected before any Git or filesystem access.");
        }

        // Probe the committed blob's size before reading anything. A committed
        // blob may be arbitrarily large (multi-gigabyte), and materialising it
        // just to clip it afterwards would defeat the bound and could overflow
        // the byte-array limit; `git cat-file -s` answers with the object size
        // without streaming the payload (spec 13 §25).
        GitProcessResult sizeProbe = await _runner.RunAsync(
            new GitProcessRequest(
                new[] { "--no-optional-locks", "cat-file", "-s", $"{revision}:{gitPath}" },
                worktreeRoot),
            cancellationToken);

        if (!sizeProbe.IsSuccess)
        {
            return VersionFailure(sizeProbe, revision, gitPath);
        }

        if (!long.TryParse(
                sizeProbe.StandardOutput.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long blobSize))
        {
            // Without a trustworthy size the read cannot be proven bounded, so
            // nothing is read and the outcome stays typed and inspectable.
            string message = $"Git did not report a blob size for '{gitPath}' at commit {revision}.";
            return new GitFileVersionResult(
                GitHistoryStatus.Failed,
                null,
                sizeProbe with
                {
                    Status = GitProcessStatus.Failed,
                    ErrorKind = GitProcessErrorKind.Unknown,
                    ErrorMessage = message,
                },
                message);
        }

        if (blobSize > MaxVersionBytes)
        {
            // Over the served bound: report the true size and the truncation,
            // but do not read a single byte of the payload. Content stays null
            // (nothing was read) and IsBinary stays false (nothing was sniffed);
            // IsTruncated is the pane's "too large to preview" state.
            return new GitFileVersionResult(
                GitHistoryStatus.Available,
                new GitFileVersion(
                    revision,
                    gitPath,
                    Content: null,
                    IsBinary: false,
                    IsTruncated: true,
                    SizeBytes: blobSize),
                Failure: null);
        }

        GitProcessResult result = await _runner.RunAsync(
            new GitProcessRequest(
                // `--no-optional-locks` keeps this inspection from taking the
                // index lock, and the byte capture is mandatory: a DOCX blob
                // must never pass through a lossy text decode (spec 13 §9.3).
                new[] { "--no-optional-locks", "show", $"{revision}:{gitPath}" },
                worktreeRoot)
            {
                CaptureBinaryOutput = true,
            },
            cancellationToken);

        if (result.IsSuccess)
        {
            byte[] bytes = result.StandardOutputBytes ?? Array.Empty<byte>();
            return new GitFileVersionResult(
                GitHistoryStatus.Available,
                BuildVersion(revision, gitPath, bytes),
                Failure: null);
        }

        return VersionFailure(result, revision, gitPath);
    }

    /// <summary>
    /// Maps a failed version read (size probe or blob read) onto the typed
    /// version outcome: a path that is absent at that commit is a real answer,
    /// every other failure stays inspectable.
    /// </summary>
    private static GitFileVersionResult VersionFailure(
        GitProcessResult result,
        string revision,
        string gitPath)
    {
        if (result.Status == GitProcessStatus.Failed && GitErrorSignatures.IsHeadPathMissing(result))
        {
            // The path simply does not exist at that commit: a real answer the
            // pane renders as "not in this commit", never an empty string
            // pretending to be content (spec 13 §12).
            return new GitFileVersionResult(
                GitHistoryStatus.NotFound,
                null,
                result,
                $"'{gitPath}' does not exist at commit {revision}.");
        }

        return new GitFileVersionResult(MapFailure(result), null, result, result.ErrorMessage);
    }

    /// <summary>
    /// Shared history read: discover the worktree, validate the file scope,
    /// run <c>git log</c> with the requested window (plus one commit, so "is
    /// there more" is Git's answer rather than a guess), then map the output
    /// onto a typed page.
    /// </summary>
    private async Task<GitHistoryResult> ReadHistoryAsync(
        string repositoryPath,
        string? relativePath,
        bool fileScoped,
        GitHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        // Git cannot serve an unbounded or nonsensical window: reject it typed
        // before Git runs, so no client can ask for the whole history in one
        // call (spec 13 §25).
        if (query.Skip < 0 || query.Limit < 1 || query.Limit > GitHistoryQuery.MaxLimit)
        {
            bool invalidSkip = query.Skip < 0;
            return Rejected(
                GitHistoryStatus.InvalidRequest,
                invalidSkip ? "InvalidSkip" : "InvalidLimit",
                invalidSkip
                    ? "The history skip must not be negative; the request was rejected before Git ran."
                    : $"The history page limit must be between 1 and {GitHistoryQuery.MaxLimit}; " +
                      "the request was rejected before Git ran.");
        }

        // A file-history request without a document is an input rejection: a
        // missing path must never silently widen into the whole repository
        // history (spec 13 §12/§23).
        if (fileScoped && string.IsNullOrWhiteSpace(relativePath))
        {
            return Rejected(
                GitHistoryStatus.InvalidRequest,
                "PathRequired",
                "A worktree-relative document path is required for file history.");
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return DiscoveryFailure(context);
        }

        string worktreeRoot = context.WorktreeRoot!;
        string? gitPath = null;
        if (relativePath is not null)
        {
            // Untrusted input: absolute, rooted, unnormalized and escaping
            // paths are rejected before any Git or filesystem access, so a
            // file-history read can never leave the selected worktree
            // (spec 13 §23).
            if (!GitDocumentPath.TryNormalizeRelativePath(relativePath, out gitPath)
                || !GitDocumentPath.TryResolveContainedPath(worktreeRoot, gitPath, out _))
            {
                return Rejected(
                    GitHistoryStatus.PathRejected,
                    "PathRejected",
                    $"The path '{relativePath}' is not a worktree-relative document path; " +
                    "the request was rejected before any Git or filesystem access.");
            }
        }

        var arguments = new List<string>(HistoryPrefixArguments)
        {
            $"--skip={query.Skip}",
            // One extra commit: its presence is Git's own answer to "has more".
            $"--max-count={query.Limit + 1}",
        };
        if (gitPath is not null)
        {
            arguments.Add("--");
            arguments.Add(gitPath);
        }

        GitProcessResult result = await _runner.RunAsync(
            new GitProcessRequest(
                arguments,
                worktreeRoot,
                EnvironmentOverrides: LiteralPathspecEnvironment),
            cancellationToken);

        if (!result.IsSuccess)
        {
            // An unborn HEAD is a normal repository state ("no commits yet"),
            // not a failure the user must read: report an available, empty page
            // and keep the read's window (spec 13 §20). The decision comes from
            // the same exit-code probe SCM-05 uses, not from Git's stderr
            // wording, so it survives a message-wording change.
            if (result.Status == GitProcessStatus.Failed)
            {
                GitProcessResult headProbe = await _runner.RunAsync(
                    new GitProcessRequest(HeadProbeArguments, worktreeRoot),
                    cancellationToken);

                if (GitErrorSignatures.IsUnbornHeadProbe(headProbe))
                {
                    return new GitHistoryResult(
                        GitHistoryStatus.Available,
                        new GitHistoryPage(
                            Array.Empty<GitHistoryEntry>(),
                            query.Skip,
                            query.Limit,
                            HasMore: false,
                            NextSkip: null),
                        Failure: null,
                        Message: "This worktree has no commits on the current branch yet.");
                }
            }

            return new GitHistoryResult(MapFailure(result), null, result, result.ErrorMessage);
        }

        try
        {
            IReadOnlyList<GitHistoryEntry> parsed = ParseCommits(result.StandardOutput);
            bool hasMore = parsed.Count > query.Limit;
            IReadOnlyList<GitHistoryEntry> page = hasMore
                ? parsed.Take(query.Limit).ToArray()
                : parsed;

            return new GitHistoryResult(
                GitHistoryStatus.Available,
                new GitHistoryPage(
                    page,
                    query.Skip,
                    query.Limit,
                    hasMore,
                    hasMore ? query.Skip + query.Limit : null),
                Failure: null);
        }
        catch (FormatException exception)
        {
            // Typed, inspectable failure instead of a parser exception or a
            // silently truncated history (spec 13 §20).
            return new GitHistoryResult(
                GitHistoryStatus.Failed,
                null,
                result with
                {
                    Status = GitProcessStatus.Failed,
                    ErrorKind = GitProcessErrorKind.Unknown,
                    ErrorMessage = $"Git history output could not be parsed: {exception.Message}",
                });
        }
    }

    /// <summary>
    /// Parses <c>git log --format</c> output whose format is
    /// <c>%H%x00%h%x00%an%x00%ae%x00%cI%x00%s</c> followed by
    /// <c>--name-only</c>.
    ///
    /// The framing is structural rather than textual: a line containing NUL
    /// bytes is always a commit header (six NUL-separated fields), and every
    /// other non-empty line is a changed file name of the record that is
    /// currently open. Git guarantees the field contents are single-line, so a
    /// commit subject cannot be mistaken for a file name.
    /// </summary>
    /// <exception cref="FormatException">The output does not match the requested format.</exception>
    private static IReadOnlyList<GitHistoryEntry> ParseCommits(string output)
    {
        var commits = new List<GitHistoryEntry>();
        var fields = new List<string>(MetadataFieldCount);
        int fileCount = 0;
        bool hasOpenRecord = false;

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Contains('\0'))
            {
                if (hasOpenRecord)
                {
                    commits.Add(BuildEntry(fields, fileCount));
                }

                fields = new List<string>(line.Split('\0'));
                if (fields.Count != MetadataFieldCount || !IsCommitId(fields[0]))
                {
                    throw new FormatException(
                        $"Expected {MetadataFieldCount} NUL-separated commit metadata fields, " +
                        $"found {fields.Count}.");
                }

                fileCount = 0;
                hasOpenRecord = true;
                continue;
            }

            if (line.Length == 0)
            {
                // Blank separator Git emits between commit records.
                continue;
            }

            if (!hasOpenRecord)
            {
                throw new FormatException("A changed-file name appeared before any commit record.");
            }

            fileCount++;
        }

        if (hasOpenRecord)
        {
            commits.Add(BuildEntry(fields, fileCount));
        }

        return commits;
    }

    private static GitHistoryEntry BuildEntry(IReadOnlyList<string> fields, int fileCount) =>
        new(
            fields[0].Trim(),
            fields[1].Trim(),
            fields[2],
            fields[3],
            fields[4].Trim(),
            fields[5],
            fileCount);

    /// <summary>
    /// Git's <c>%H</c> is always a full object id (40 hex characters for SHA-1
    /// repositories, 64 for SHA-256 ones). Checking the shape keeps a drifted
    /// format from being reported as a commit.
    /// </summary>
    private static bool IsCommitId(string value)
    {
        string candidate = value.Trim();
        if (candidate.Length is not (40 or 64))
        {
            return false;
        }

        foreach (char character in candidate)
        {
            bool isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when the value can be a Git commit object id: full or abbreviated
    /// hexadecimal. Revision expressions, options and arbitrary text are not
    /// commit identifiers and must never reach Git (spec 13 §23).
    /// </summary>
    private static bool IsCommitIdentifier(string value)
    {
        if (value.Length is < 4 or > 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool isHex = character is >= '0' and <= '9'
                or >= 'a' and <= 'f'
                or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Projects one committed blob onto a typed version: the true size is kept,
    /// binary blobs never become text, and text beyond the served bound is
    /// clipped but explicitly flagged (spec 13 §9.3/§12/§25).
    ///
    /// The size probe normally keeps this clip unreachable; it stays as a
    /// defensive invariant so the served bound can never be exceeded even if a
    /// read ever disagreed with the probe.
    /// </summary>
    private static GitFileVersion BuildVersion(string commitId, string path, byte[] bytes)
    {
        bool isBinary = LooksBinary(bytes);
        bool isTruncated = bytes.Length > MaxVersionBytes;
        string? content = isBinary
            ? null
            : Encoding.UTF8.GetString(
                bytes,
                0,
                isTruncated ? CompleteUtf8PrefixLength(bytes, MaxVersionBytes) : bytes.Length);

        return new GitFileVersion(commitId, path, content, isBinary, isTruncated, bytes.Length);
    }

    /// <summary>
    /// Clips a blob to <paramref name="max"/> bytes without cutting a multi-byte
    /// UTF-8 sequence in half (which would end the served text on U+FFFD).
    /// </summary>
    private static int CompleteUtf8PrefixLength(byte[] bytes, int max)
    {
        int length = Math.Min(max, bytes.Length);

        // Walk back over the trailing continuation bytes to the lead byte of
        // the last sequence (a UTF-8 sequence is at most four bytes long).
        int start = length;
        int floor = Math.Max(0, length - 4);
        while (start > floor && (bytes[start - 1] & 0xC0) == 0x80)
        {
            start--;
        }

        if (start == 0)
        {
            return length;
        }

        start--;
        int expected = Utf8SequenceLength(bytes[start]);
        return expected > 0 && start + expected > length ? start : length;
    }

    /// <summary>Length of the UTF-8 sequence introduced by one lead byte.</summary>
    private static int Utf8SequenceLength(byte lead) =>
        lead < 0x80 ? 1
        : (lead & 0xE0) == 0xC0 ? 2
        : (lead & 0xF0) == 0xE0 ? 3
        : (lead & 0xF8) == 0xF0 ? 4
        : 0;

    /// <summary>
    /// True when the blob must not be rendered as text.
    ///
    /// A NUL byte in the leading window is Git's own binary signal. Beyond that
    /// signal, a blob that is not valid UTF-8 - or that is dense with control
    /// characters - is binary too: a lenient decode would otherwise hand the
    /// pane replacement-character garbage (spec 13 §9.3/§12).
    /// </summary>
    private static bool LooksBinary(byte[] bytes)
    {
        int inspected = Math.Min(bytes.Length, BinarySniffBytes);
        if (inspected == 0)
        {
            return false;
        }

        int controlCount = 0;
        for (int index = 0; index < inspected; index++)
        {
            byte value = bytes[index];
            if (value == 0)
            {
                return true;
            }

            bool allowedControl = value is 0x09 or 0x0A or 0x0C or 0x0D;
            if ((value < 0x20 && !allowedControl) || value == 0x7F)
            {
                controlCount++;
            }
        }

        // No text document is 10% control characters; a byte blob usually is.
        if (controlCount * 10 > inspected)
        {
            return true;
        }

        return !IsUtf8Text(bytes, inspected);
    }

    /// <summary>
    /// Validates the sniff window as UTF-8. A multi-byte sequence cut by the end
    /// of the window is acceptable only when the blob continues past it; an
    /// incomplete sequence in a complete blob is binary.
    /// </summary>
    private static bool IsUtf8Text(byte[] bytes, int count)
    {
        int index = 0;
        while (index < count)
        {
            byte lead = bytes[index];
            if (lead < 0x80)
            {
                index++;
                continue;
            }

            int continuationBytes = Utf8SequenceLength(lead) - 1;
            if (continuationBytes < 1)
            {
                // A stray continuation byte or an invalid lead byte.
                return false;
            }

            if (index + continuationBytes >= count)
            {
                for (int tail = index + 1; tail < count; tail++)
                {
                    if ((bytes[tail] & 0xC0) != 0x80)
                    {
                        return false;
                    }
                }

                return count < bytes.Length;
            }

            int codePoint = lead & (0x7F >> continuationBytes);
            for (int offset = 1; offset <= continuationBytes; offset++)
            {
                byte next = bytes[index + offset];
                if ((next & 0xC0) != 0x80)
                {
                    return false;
                }

                codePoint = (codePoint << 6) | (next & 0x3F);
            }

            int minimum = continuationBytes == 1 ? 0x80 : continuationBytes == 2 ? 0x800 : 0x10000;
            if (codePoint < minimum
                || codePoint > 0x10FFFF
                || (codePoint >= 0xD800 && codePoint <= 0xDFFF))
            {
                // Overlong encodings, out-of-range code points and surrogates.
                return false;
            }

            index += continuationBytes + 1;
        }

        return true;
    }

    /// <summary>Maps a failed Git invocation onto the history status vocabulary.</summary>
    private static GitHistoryStatus MapFailure(GitProcessResult failure)
    {
        if (failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled)
        {
            return GitHistoryStatus.GitUnavailable;
        }

        return failure.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound
            ? GitHistoryStatus.NoRepository
            : GitHistoryStatus.Failed;
    }

    /// <summary>
    /// Maps a failed repository discovery (or a failed Git read) onto a typed
    /// result that keeps the originating failure inspectable.
    /// </summary>
    private static GitHistoryResult DiscoveryFailure(GitRepositoryContext context) =>
        new(
            context.Status switch
            {
                GitRepositoryStatus.NoRepository => GitHistoryStatus.NoRepository,
                GitRepositoryStatus.GitUnavailable => GitHistoryStatus.GitUnavailable,
                _ => GitHistoryStatus.Failed,
            },
            null,
            context.Failure,
            context.Failure?.ErrorMessage);

    /// <summary>
    /// A typed input rejection: nothing ran, so the client can fix the request
    /// instead of reading a misleading result.
    /// </summary>
    private static GitHistoryResult Rejected(GitHistoryStatus status, string errorCode, string message) =>
        new(status, null, null, message, errorCode);

    /// <summary>A typed version-input rejection: nothing ran and nothing was read.</summary>
    private static GitFileVersionResult VersionRejected(
        GitHistoryStatus status,
        string errorCode,
        string message) =>
        new(status, null, null, message, errorCode);

    /// <summary>Maps a failed repository discovery for a version read onto a typed result.</summary>
    private static GitFileVersionResult VersionDiscoveryFailure(GitRepositoryContext context) =>
        new(
            context.Status switch
            {
                GitRepositoryStatus.NoRepository => GitHistoryStatus.NoRepository,
                GitRepositoryStatus.GitUnavailable => GitHistoryStatus.GitUnavailable,
                _ => GitHistoryStatus.Failed,
            },
            null,
            context.Failure,
            context.Failure?.ErrorMessage);
}
