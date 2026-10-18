using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-10 — safe Git discard &amp; restore application service
/// (spec 13 §15/§19/§20/§21/§23).
///
/// Discard/restore is destructive, so the service exposes a two-step contract
/// that a caller cannot skip:
/// <list type="bullet">
/// <item><see cref="ReviewAsync"/> describes exactly what would be lost — the
/// affected path, the current diff, the staged state, the target Git version
/// and recovery availability — and returns a review token bound to that
/// state,</item>
/// <item><see cref="DiscardAsync"/>/<see cref="RestoreFromCommitAsync"/> require
/// that token. A missing token is a typed <see cref="GitRestoreOutcome.ReviewRequired"/>
/// rejection and a token whose state changed is a typed
/// <see cref="GitRestoreOutcome.ReviewStale"/> rejection; in both cases Git
/// never runs and the file is untouched.</item>
/// </list>
///
/// Every mutation:
/// <list type="bullet">
/// <item>is file-scoped: exactly one validated worktree-relative path travels
/// after <c>--</c> with <c>GIT_LITERAL_PATHSPECS=1</c> — there is no
/// <c>.</c> pathspec, no <c>--hard</c> reset and no whole-repository form,</item>
/// <item>creates HIST-01 recovery coverage before Git runs; if the snapshot
/// cannot be created the mutation is blocked,</item>
/// <item>refuses conflicted and untracked paths typed before Git runs
/// (spec 13 §21),</item>
/// <item>runs through the SCM-01 controlled process boundary with structured
/// arguments and the per-worktree mutation queue,</item>
/// <item>reindexes the document, records the operation in Document IDE History
/// and re-reads Git status afterwards.</item>
/// </list>
/// </summary>
public sealed class GitRestoreService
{
    /// <summary>
    /// Forces Git to treat the one reviewed path literally — no globbing and no
    /// pathspec magic (spec 13 §23): a document named <c>notes[1].md</c> must be
    /// restored as exactly that file, never as a pattern that could widen the
    /// destructive operation to a sibling.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LiteralPathspecEnvironment =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_LITERAL_PATHSPECS"] = "1",
        };

    /// <summary>
    /// Largest diff text rendered in a review (64 KiB of characters). The
    /// review is a confirmation surface, not a full-file transport; a larger
    /// diff is clipped and flagged rather than pulled into the pane unbounded
    /// (spec 13 §15.1/§25).
    /// </summary>
    public const int MaxReviewDiffCharacters = 64 * 1024;

    /// <summary>Document IDE History operation-id prefix for a restore/discard.</summary>
    public const string OperationIdPrefix = "scm-restore-";

    private readonly IGitProcessRunner _runner;
    private readonly GitRepositoryService _repositories;
    private readonly RecoverySnapshotService _snapshots;
    private readonly IGitRestoreHistoryRecorder _history;
    private readonly IBatchApplyReindexTrigger _reindex;
    private readonly IDocumentRepository _documents;

    public GitRestoreService(
        IGitProcessRunner runner,
        GitRepositoryService repositories,
        RecoverySnapshotService snapshots,
        IGitRestoreHistoryRecorder history,
        IBatchApplyReindexTrigger reindex,
        IDocumentRepository documents)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _reindex = reindex ?? throw new ArgumentNullException(nameof(reindex));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <summary>
    /// Discards one file's working-tree changes after an explicit review
    /// (<c>git restore --worktree -- &lt;path&gt;</c>, spec 13 §15.1). The
    /// index is never touched, so staged content survives and unrelated files
    /// are never affected.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative path as Git reported it; treated as untrusted input.</param>
    /// <param name="reviewToken">Token returned by <see cref="ReviewAsync"/>; required, nothing runs without it.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitRestoreResult> DiscardAsync(
        string repositoryPath,
        string? relativePath,
        string? reviewToken,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            GitRestoreMode.DiscardWorktree,
            repositoryPath,
            relativePath,
            commitId: null,
            reviewToken,
            cancellationToken);

    /// <summary>
    /// Restores one file's working-tree content from a selected commit after an
    /// explicit review (<c>git restore --source=&lt;commit&gt; --worktree -- &lt;path&gt;</c>,
    /// spec 13 §15.2). The index is untouched, so the restored content appears
    /// as a working-tree change and no commit is created.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative path as Git reported it; treated as untrusted input.</param>
    /// <param name="commitId">Full or abbreviated commit object id to restore from.</param>
    /// <param name="reviewToken">Token returned by <see cref="ReviewAsync"/>; required, nothing runs without it.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public Task<GitRestoreResult> RestoreFromCommitAsync(
        string repositoryPath,
        string? relativePath,
        string? commitId,
        string? reviewToken,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            GitRestoreMode.RestoreVersion,
            repositoryPath,
            relativePath,
            commitId,
            reviewToken,
            cancellationToken);

    /// <summary>
    /// Produces the reviewed confirmation payload for one destructive
    /// operation (spec 13 §15.1/§15.2). Strictly read-only: it reads Git status
    /// and a diff and never touches the index, the working tree or HEAD.
    /// </summary>
    /// <param name="repositoryPath">Workspace folder or active-document path used to discover the repository.</param>
    /// <param name="relativePath">Worktree-root-relative path as Git reported it; treated as untrusted input.</param>
    /// <param name="commitId">Commit to restore from; <c>null</c> reviews a working-tree discard.</param>
    /// <param name="cancellationToken">Cancels the Git invocation (typed, never thrown).</param>
    public async Task<GitRestoreReviewResult> ReviewAsync(
        string repositoryPath,
        string? relativePath,
        string? commitId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ReviewRejected(
                GitRestoreReviewStatus.RejectedPath,
                "PathRequired",
                "A worktree-relative file path is required to review a discard or restore.");
        }

        GitRestoreMode mode = string.IsNullOrWhiteSpace(commitId)
            ? GitRestoreMode.DiscardWorktree
            : GitRestoreMode.RestoreVersion;

        string? sourceCommit = null;
        if (mode == GitRestoreMode.RestoreVersion)
        {
            if (!IsCommitIdentifier(commitId))
            {
                return ReviewRejected(
                    GitRestoreReviewStatus.RejectedPath,
                    "InvalidCommitId",
                    "A commit object id is required to review a version restore; " +
                    "the request was rejected before Git ran.");
            }

            sourceCommit = commitId!.Trim();
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return new GitRestoreReviewResult(
                context.Status switch
                {
                    GitRepositoryStatus.NoRepository => GitRestoreReviewStatus.NoRepository,
                    GitRepositoryStatus.GitUnavailable => GitRestoreReviewStatus.GitUnavailable,
                    _ => GitRestoreReviewStatus.Failed,
                },
                null,
                context.Failure,
                context.Failure?.ErrorMessage);
        }

        string worktreeRoot = context.WorktreeRoot!;
        if (!TryResolveTarget(worktreeRoot, relativePath, out string gitPath, out string absolutePath, out string errorCode, out string message))
        {
            return ReviewRejected(GitRestoreReviewStatus.RejectedPath, errorCode, message);
        }

        GitStatusResult status = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            return new GitRestoreReviewResult(
                GitRestoreReviewStatus.Failed,
                null,
                status.Failure,
                status.Failure?.ErrorMessage ?? "Git status could not be read for the review.");
        }

        GitStatusEntry? entry = FindEntry(status.Snapshot, gitPath);
        (bool exists, long? sizeBytes, string? fingerprint, string? readError) =
            await ReadCurrentStateAsync(absolutePath, cancellationToken);
        if (readError is not null)
        {
            return ReviewRejected(
                GitRestoreReviewStatus.Failed,
                "Unreadable",
                $"The file '{gitPath}' could not be read for the review: {readError}");
        }

        string diff = string.Empty;
        bool diffTruncated = false;
        string[] diffArguments = mode == GitRestoreMode.DiscardWorktree
            ? new[] { "-c", "core.quotepath=false", "diff", "--no-color", "--no-ext-diff", "--no-textconv", "--", gitPath }
            : new[] { "-c", "core.quotepath=false", "diff", "--no-color", "--no-ext-diff", "--no-textconv", sourceCommit!, "--", gitPath };
        GitProcessResult diffResult = await _runner.RunAsync(
            new GitProcessRequest(diffArguments, worktreeRoot, EnvironmentOverrides: LiteralPathspecEnvironment),
            cancellationToken);

        if (!diffResult.IsSuccess)
        {
            return new GitRestoreReviewResult(
                GitRestoreReviewStatus.Failed,
                null,
                diffResult,
                diffResult.ErrorMessage ?? "Git could not produce the reviewed diff.");
        }

        string rawDiff = diffResult.StandardOutput;
        if (rawDiff.Length > MaxReviewDiffCharacters)
        {
            diff = rawDiff[..MaxReviewDiffCharacters];
            diffTruncated = true;
        }
        else
        {
            diff = rawDiff;
        }

        bool isConflicted = entry?.IsConflicted ?? false;
        bool isUntracked = entry?.IsUntracked ?? false;
        bool hasWorkingTreeChange = entry?.HasWorkingTreeChange ?? false;
        bool isStaged = entry?.HasStagedChange ?? false;

        bool recoveryAvailable = exists;
        string recoveryDetail = exists
            ? "A recovery snapshot is written to Document IDE History before the restore; " +
              "if it cannot be created the restore is blocked."
            : "The file is already deleted in the working tree, so there is no current content " +
              "to preserve; the restore recreates it from Git.";

        (bool applicable, string? blockedReason) = ReviewApplicability(
            mode,
            entry,
            isConflicted,
            isUntracked,
            hasWorkingTreeChange);

        string token = ComputeReviewToken(
            mode,
            worktreeRoot,
            gitPath,
            sourceCommit,
            fingerprint,
            entry?.StatusCharacters ?? "??");

        return new GitRestoreReviewResult(
            GitRestoreReviewStatus.Available,
            new GitRestoreReview(
                mode,
                gitPath,
                sourceCommit,
                mode == GitRestoreMode.DiscardWorktree ? "index" : "commit",
                diff,
                diffTruncated,
                entry?.IndexChange,
                entry?.WorkingTreeChange,
                entry?.StatusCharacters ?? "??",
                isStaged,
                isConflicted,
                isUntracked,
                hasWorkingTreeChange,
                recoveryAvailable,
                recoveryDetail,
                sizeBytes,
                applicable,
                blockedReason,
                token),
            null,
            null);
    }

    private async Task<GitRestoreResult> ApplyAsync(
        GitRestoreMode mode,
        string repositoryPath,
        string? relativePath,
        string? commitId,
        string? reviewToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return RejectedPath(
                mode,
                "PathRequired",
                "A worktree-relative file path is required for a discard or restore.");
        }

        // A destructive Git operation is never implicit: without the reviewed
        // confirmation token nothing runs at all (spec 13 §15.1/§23). This is
        // checked before discovery so an unreviewed request performs no Git
        // work whatsoever.
        if (string.IsNullOrWhiteSpace(reviewToken))
        {
            return new GitRestoreResult(
                GitRestoreOutcome.ReviewRequired,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                "A discard/restore requires an explicit reviewed confirmation; nothing was changed.",
                "ReviewRequired");
        }

        string? sourceCommit = null;
        if (mode == GitRestoreMode.RestoreVersion)
        {
            if (!IsCommitIdentifier(commitId))
            {
                return RejectedPath(
                    mode,
                    "InvalidCommitId",
                    "A commit object id is required to restore a document version; " +
                    "the request was rejected before Git ran.");
            }

            sourceCommit = commitId!.Trim();
        }

        GitRepositoryContext context = await _repositories.DiscoverAsync(repositoryPath, cancellationToken);
        if (!context.HasRepository)
        {
            return new GitRestoreResult(
                context.Status switch
                {
                    GitRepositoryStatus.NoRepository => GitRestoreOutcome.NoRepository,
                    GitRepositoryStatus.GitUnavailable => GitRestoreOutcome.GitUnavailable,
                    _ => GitRestoreOutcome.Failed,
                },
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                context.Failure,
                context.Failure?.ErrorMessage);
        }

        string worktreeRoot = context.WorktreeRoot!;
        if (!TryResolveTarget(worktreeRoot, relativePath, out string gitPath, out string absolutePath, out string errorCode, out string message))
        {
            return RejectedPath(mode, errorCode, message);
        }

        GitStatusResult status = await _repositories.GetStatusAsync(worktreeRoot, cancellationToken);
        if (!status.IsAvailable || status.Snapshot is null)
        {
            return new GitRestoreResult(
                GitRestoreOutcome.Failed,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                status.Failure,
                status.Failure?.ErrorMessage ?? "Git status could not be read before the restore.");
        }

        GitStatusEntry? entry = FindEntry(status.Snapshot, gitPath);
        if (entry?.IsConflicted == true)
        {
            // A conflict participant is never silently overwritten (spec 13 §21).
            return new GitRestoreResult(
                GitRestoreOutcome.ConflictedPath,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                $"'{gitPath}' is an unmerged conflict participant; the restore was refused. " +
                "Resolve the conflict first.",
                "ConflictedPath");
        }

        if (entry?.IsUntracked == true)
        {
            // There is no Git version to restore, and an untracked file is never
            // deleted by a discard/restore (spec 13 §15).
            return new GitRestoreResult(
                GitRestoreOutcome.UntrackedPath,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                $"'{gitPath}' is untracked; there is no Git version to restore it from.",
                "UntrackedPath");
        }

        if (mode == GitRestoreMode.DiscardWorktree && entry?.HasWorkingTreeChange != true)
        {
            return new GitRestoreResult(
                GitRestoreOutcome.NoChanges,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                $"'{gitPath}' has no working-tree change to discard.",
                "NoChanges");
        }

        (bool exists, _, string? fingerprint, string? readError) =
            await ReadCurrentStateAsync(absolutePath, cancellationToken);
        if (readError is not null)
        {
            return new GitRestoreResult(
                GitRestoreOutcome.Failed,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                $"The file '{gitPath}' could not be read before the restore: {readError}",
                "Unreadable");
        }

        string expectedToken = ComputeReviewToken(
            mode,
            worktreeRoot,
            gitPath,
            sourceCommit,
            fingerprint,
            entry?.StatusCharacters ?? "??");

        if (!string.Equals(expectedToken, reviewToken.Trim(), StringComparison.Ordinal))
        {
            // The reviewed state changed: the newer content wins and the user
            // must review again (spec 07 §11).
            return new GitRestoreResult(
                GitRestoreOutcome.ReviewStale,
                mode,
                null,
                null,
                false,
                false,
                false,
                null,
                null,
                "The file changed after it was reviewed; nothing was restored. Review the change again.",
                "ReviewStale");
        }

        string operationId = OperationIdPrefix + Guid.NewGuid().ToString("N");
        string? documentId = null;
        if (exists)
        {
            documentId = (await _documents.GetByPathAsync(absolutePath))?.Id;
        }

        SnapshotMetadata? snapshot = null;
        if (exists)
        {
            // HIST-01 recovery coverage before any destructive change. If it
            // cannot be created, the mutation is blocked (master spec §101.9).
            try
            {
                snapshot = await _snapshots.CreateSnapshotAsync(
                    operationId,
                    new[] { new SnapshotSourceFile(documentId ?? string.Empty, absolutePath) });
            }
            catch (OperationCanceledException)
            {
                return new GitRestoreResult(
                    GitRestoreOutcome.Failed,
                    mode,
                    null,
                    null,
                    false,
                    false,
                    false,
                    null,
                    null,
                    "The restore was cancelled before any change; the file is unchanged.",
                    "Cancelled");
            }
            catch (Exception exception)
            {
                return new GitRestoreResult(
                    GitRestoreOutcome.RecoveryBlocked,
                    mode,
                    null,
                    null,
                    false,
                    false,
                    false,
                    null,
                    null,
                    "Recovery coverage could not be created, so the restore was blocked and " +
                    $"the file is unchanged: {exception.Message}",
                    "RecoveryBlocked");
            }
        }

        string[] arguments = mode == GitRestoreMode.DiscardWorktree
            ? new[] { "restore", "--worktree", "--", gitPath }
            : new[] { "restore", $"--source={sourceCommit}", "--worktree", "--", gitPath };

        // Narrow the check→mutation window: the reviewed content must still be
        // the content on disk when Git runs. If the file changed while the
        // recovery snapshot was being prepared (e.g. Word saved again), the
        // newer content wins and the restore is refused typed.
        (bool existsBeforeMutation, _, string? fingerprintBeforeMutation, string? recheckError) =
            await ReadCurrentStateAsync(absolutePath, cancellationToken);
        if (recheckError is not null)
        {
            return new GitRestoreResult(
                GitRestoreOutcome.Failed,
                mode,
                operationId,
                snapshot?.SnapshotId,
                snapshot is not null,
                false,
                false,
                null,
                null,
                $"The file '{gitPath}' could not be read before the restore: {recheckError}",
                "Unreadable");
        }

        if (existsBeforeMutation != exists
            || !string.Equals(fingerprintBeforeMutation, fingerprint, StringComparison.Ordinal))
        {
            return new GitRestoreResult(
                GitRestoreOutcome.ReviewStale,
                mode,
                operationId,
                snapshot?.SnapshotId,
                snapshot is not null,
                false,
                false,
                null,
                null,
                "The file changed while the restore was being prepared; nothing was restored. " +
                "Review the change again.",
                "ReviewStale");
        }

        GitProcessResult raw = await _runner.RunAsync(
            new GitProcessRequest(arguments, worktreeRoot, EnvironmentOverrides: LiteralPathspecEnvironment),
            cancellationToken);

        if (!raw.IsSuccess)
        {
            GitRestoreOutcome outcome = raw.Status == GitProcessStatus.Unavailable
                || raw.ErrorKind == GitProcessErrorKind.GitNotInstalled
                ? GitRestoreOutcome.GitUnavailable
                : GitRestoreOutcome.Failed;

            await TryRecordHistoryAsync(new GitRestoreHistoryRecord(
                operationId,
                mode.ToString(),
                outcome.ToString(),
                documentId,
                gitPath,
                worktreeRoot,
                sourceCommit,
                snapshot?.SnapshotId,
                DateTimeOffset.UtcNow));

            return new GitRestoreResult(
                outcome,
                mode,
                operationId,
                snapshot?.SnapshotId,
                snapshot is not null,
                false,
                false,
                null,
                raw,
                raw.ErrorMessage ?? "The Git restore failed; the file is unchanged.");
        }

        // The file is restored at this point. Neither the post-operation
        // fingerprint, the reindex nor the history write may turn a completed
        // restore into a reported failure; each is surfaced as a typed warning.
        var warnings = new List<string>();
        bool historyRecorded = false;
        bool reindexed = false;

        if (documentId is not null && snapshot is not null)
        {
            try
            {
                string postFingerprint = await FingerprintAsync(absolutePath, CancellationToken.None);
                await _snapshots.RecordPostOperationFingerprintAsync(
                    snapshot,
                    documentId,
                    postFingerprint);
            }
            catch (Exception exception)
            {
                warnings.Add($"The recovery snapshot could not record the restored fingerprint: {exception.Message}");
            }
        }

        if (documentId is not null)
        {
            try
            {
                await _reindex.TriggerAsync(documentId, CancellationToken.None);
                reindexed = true;
            }
            catch (Exception exception)
            {
                warnings.Add($"The document could not be reindexed: {exception.Message}");
            }
        }
        else
        {
            warnings.Add("The restored file is not registered as a Document IDE document, so no reindex was triggered.");
        }

        GitStatusResult refreshed = await _repositories.GetStatusAsync(worktreeRoot, CancellationToken.None);

        try
        {
            await _history.RecordAsync(new GitRestoreHistoryRecord(
                operationId,
                mode.ToString(),
                GitRestoreOutcome.Succeeded.ToString(),
                documentId,
                gitPath,
                worktreeRoot,
                sourceCommit,
                snapshot?.SnapshotId,
                DateTimeOffset.UtcNow));
            historyRecorded = true;
        }
        catch (Exception exception)
        {
            warnings.Add($"Document IDE History could not record the operation: {exception.Message}");
        }

        return new GitRestoreResult(
            GitRestoreOutcome.Succeeded,
            mode,
            operationId,
            snapshot?.SnapshotId,
            snapshot is not null,
            historyRecorded,
            reindexed,
            refreshed,
            null,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    /// <summary>
    /// Resolves one client-supplied path as a contained worktree file path
    /// (spec 13 §23). Rejection happens before any Git mutation, so a rejected
    /// request can never widen scope or touch a path outside the worktree.
    /// </summary>
    private static bool TryResolveTarget(
        string worktreeRoot,
        string relativePath,
        out string gitPath,
        out string absolutePath,
        out string errorCode,
        out string message)
    {
        gitPath = string.Empty;
        absolutePath = string.Empty;
        errorCode = string.Empty;
        message = string.Empty;

        if (!GitDocumentPath.TryNormalizeRelativePath(relativePath, out gitPath))
        {
            errorCode = "PathRejected";
            message = $"The path '{relativePath}' is not a worktree-relative file path: " +
                      "absolute, rooted, drive-qualified, empty and '..' segments are rejected.";
            return false;
        }

        if (!GitDocumentPath.TryResolveContainedPath(worktreeRoot, gitPath, out absolutePath))
        {
            errorCode = "PathOutsideWorktree";
            message = $"The path '{relativePath}' resolves outside the worktree root; the request was rejected.";
            return false;
        }

        // A single-file operation must stay a single-file operation: a directory
        // target would silently discard or restore a whole subtree.
        try
        {
            if (Directory.Exists(absolutePath))
            {
                errorCode = "PathNotAFile";
                message = $"The path '{relativePath}' is a directory; discard/restore only accepts file paths.";
                return false;
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or System.Security.SecurityException)
        {
            errorCode = "PathInvalid";
            message = $"The path '{relativePath}' is not a usable filesystem path: {exception.Message}";
            return false;
        }

        return true;
    }

    private static GitStatusEntry? FindEntry(GitStatusSnapshot snapshot, string gitPath) =>
        snapshot.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Path, gitPath, StringComparison.Ordinal));

    private static (bool Applicable, string? BlockedReason) ReviewApplicability(
        GitRestoreMode mode,
        GitStatusEntry? entry,
        bool isConflicted,
        bool isUntracked,
        bool hasWorkingTreeChange)
    {
        if (isConflicted)
        {
            return (false, "The file is an unmerged conflict participant; resolve the conflict first.");
        }

        if (isUntracked)
        {
            return (false, "The file is untracked, so there is no Git version to restore it from.");
        }

        if (mode == GitRestoreMode.DiscardWorktree && !hasWorkingTreeChange)
        {
            return (false, "The file has no working-tree change to discard; there is nothing to restore.");
        }

        return (true, null);
    }

    /// <summary>
    /// Binds a confirmation to the exact reviewed state (mode, worktree, path,
    /// target commit, current content fingerprint and Git status). Any change
    /// after the review produces a different token, so a stale confirmation is
    /// rejected instead of overwriting newer work (spec 07 §11).
    /// </summary>
    private static string ComputeReviewToken(
        GitRestoreMode mode,
        string worktreeRoot,
        string gitPath,
        string? sourceCommit,
        string? fingerprint,
        string statusCharacters)
    {
        string material = string.Join(
            '\n',
            mode.ToString(),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreeRoot)),
            gitPath,
            sourceCommit ?? string.Empty,
            fingerprint ?? "<missing>",
            statusCharacters);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>
    /// True when the value can be a Git commit object id: full or abbreviated
    /// hexadecimal. Revision expressions, option-looking values and arbitrary
    /// text never reach Git (spec 13 §23).
    /// </summary>
    private static bool IsCommitIdentifier(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (candidate.Length is < 4 or > 64)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            bool isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<string> FingerprintAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Reads the current on-disk state of the target (existence, size,
    /// fingerprint). An unreadable or externally locked file is a typed error
    /// value instead of an exception escaping the API (spec 13 §20/§23).
    /// </summary>
    private static async Task<(bool Exists, long? SizeBytes, string? Fingerprint, string? Error)> ReadCurrentStateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return (false, null, null, null);
            }

            long sizeBytes = new FileInfo(path).Length;
            string fingerprint = await FingerprintAsync(path, cancellationToken);
            return (true, sizeBytes, fingerprint, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return (File.Exists(path), null, null, exception.Message);
        }
    }

    private async Task TryRecordHistoryAsync(GitRestoreHistoryRecord record)
    {
        try
        {
            await _history.RecordAsync(record, CancellationToken.None);
        }
        catch
        {
            // A failed Git mutation must not be masked by a history-write
            // failure; the typed Git failure is the answer the user needs.
        }
    }

    private static GitRestoreResult RejectedPath(
        GitRestoreMode mode,
        string errorCode,
        string message) =>
        new(
            GitRestoreOutcome.RejectedPath,
            mode,
            null,
            null,
            false,
            false,
            false,
            null,
            null,
            message,
            errorCode);

    private static GitRestoreReviewResult ReviewRejected(
        GitRestoreReviewStatus status,
        string errorCode,
        string message) =>
        new(status, null, null, message, errorCode);
}
