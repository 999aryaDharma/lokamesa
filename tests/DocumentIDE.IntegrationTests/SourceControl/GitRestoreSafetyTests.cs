using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.History;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-10 — Safe Git Discard &amp; Restore (spec 13 §15/§19/§20/§21/§23).
///
/// These tests drive <see cref="GitRestoreService"/> against real Git
/// repositories through the SCM-01 controlled process boundary:
/// <list type="bullet">
/// <item>a discard is destructive, so it requires an explicit reviewed
/// confirmation token — an unreviewed request is rejected typed and Git never
/// restores anything,</item>
/// <item>recovery coverage (HIST-01) exists before the mutation; when it cannot
/// be created the mutation is blocked and the file stays unchanged,</item>
/// <item>the restore is file-scoped: the reviewed path only, never a whole
/// repository, and unrelated files stay byte-identical,</item>
/// <item>conflicted and untracked paths are refused typed,</item>
/// <item>path containment rejects escapes before Git runs,</item>
/// <item>the operation is recorded in Document IDE History, the document is
/// reindexed and Git status is re-read.</item>
/// </list>
///
/// Planned-file mapping: the sprint lists
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitRestoreSafetyTests.cs</c>;
/// the frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/DiscardRestoreDialog.test.tsx</c>
/// (the same convention every previous SCM feature component uses).
/// </summary>
public sealed class GitRestoreSafetyTests
{
    // ---------------------------------------------------------------------
    // Step 1 — Diff/review confirmation (mandatory first failing test)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (mandatory RED-first behavior): "Dirty file discard
    /// requires explicit review." A dirty file is never discarded by an
    /// unreviewed call: the request is rejected typed, the working-tree content
    /// survives, no recovery snapshot is created and Git never runs a restore
    /// (spec 13 §15.1/§23).
    /// </summary>
    [Fact]
    public async Task DirtyFileDiscardRequiresExplicitReview()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string filePath = repo.WriteFile("docs/a.md", "unreviewed destructive change");
        Harness harness = CreateHarness();

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            reviewToken: null);

        Assert.Equal(GitRestoreOutcome.ReviewRequired, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("ReviewRequired", result.ErrorCode);
        Assert.Contains("review", result.Message!, StringComparison.OrdinalIgnoreCase);

        // The unreviewed content survived and nothing destructive ran.
        Assert.Equal("unreviewed destructive change", File.ReadAllText(filePath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
        Assert.Empty(harness.History.Records);
    }

    // ---------------------------------------------------------------------
    // Step 1 (continued) — the review shows what will be lost
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test "Add diff/review confirmation": the review exposes the
    /// affected file, the current diff, whether the change is staged and
    /// whether recovery coverage exists (spec 13 §15.1) before anything is
    /// discarded. Reading a review is strictly read-only.
    /// </summary>
    [Fact]
    public async Task ReviewShowsWhatWillBeLostBeforeDiscarding()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "reviewed edit");
        Harness harness = CreateHarness();

        GitRestoreReviewResult result = await harness.Service.ReviewAsync(repo.RootPath, "docs/a.md");

        Assert.Equal(GitRestoreReviewStatus.Available, result.Status);
        Assert.True(result.IsAvailable);
        GitRestoreReview review = Assert.IsType<GitRestoreReview>(result.Review);
        Assert.Equal(GitRestoreMode.DiscardWorktree, review.Mode);
        Assert.Equal("docs/a.md", review.Path);
        Assert.Equal("index", review.TargetSource);
        Assert.Null(review.SourceCommit);

        // The diff shows exactly the content the user is about to lose.
        Assert.Contains("-v1", review.Diff, StringComparison.Ordinal);
        Assert.Contains("+reviewed edit", review.Diff, StringComparison.Ordinal);
        Assert.False(review.IsDiffTruncated);

        Assert.False(review.IsStaged);
        Assert.False(review.IsConflicted);
        Assert.False(review.IsUntracked);
        Assert.True(review.HasWorkingTreeChange);
        Assert.Equal(GitChangeKind.Modified, review.WorkingTreeChange);
        Assert.True(review.RecoveryAvailable);
        Assert.True(review.IsApplicable);
        Assert.Null(review.BlockedReason);
        Assert.False(string.IsNullOrWhiteSpace(review.ReviewToken));
        Assert.Equal((long)"reviewed edit".Length, review.CurrentSizeBytes);

        // The review itself never mutates anything.
        Assert.Equal("reviewed edit", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
    }

    // ---------------------------------------------------------------------
    // Step 3 — file-scoped restore
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test "A single-file restore cannot replace unrelated files":
    /// with two dirty files, discarding the reviewed one restores only that
    /// path. The other file stays byte-identical, the index is unchanged and
    /// no commit is created (spec 13 §15.2/§23).
    /// </summary>
    [Fact]
    public async Task ReviewThenDiscardRestoresOnlyTheReviewedFile()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md", "docs/b.md" });
        string aPath = repo.WriteFile("docs/a.md", "a reviewed edit");
        string bPath = repo.WriteFile("docs/b.md", "b untouched edit \u00e9");
        byte[] bBytesBefore = File.ReadAllBytes(bPath);
        string headBefore = HeadCommit(repo.RootPath);
        Harness harness = CreateHarness();

        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");
        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);

        // Exactly the reviewed file returned to its committed content.
        Assert.Equal("v1", File.ReadAllText(aPath));
        // The unrelated file is byte-identical: the restore never widened.
        Assert.Equal(bBytesBefore, File.ReadAllBytes(bPath));
        Assert.Equal("b untouched edit \u00e9", File.ReadAllText(bPath));

        // No commit was created, HEAD is unchanged and the index stayed empty.
        Assert.Equal(headBefore, HeadCommit(repo.RootPath));
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only").Trim());

        // The refreshed status Git reported shows only the other dirty file.
        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(result.Status);
        Assert.True(refreshed.IsAvailable);
        Assert.Equal(
            new[] { "docs/b.md" },
            refreshed.Snapshot!.Entries
                .Where(entry => entry.HasWorkingTreeChange)
                .Select(entry => entry.Path)
                .ToArray());
    }

    // ---------------------------------------------------------------------
    // Step 2 — recovery snapshot before the mutation
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test "Restore creates recovery coverage when available": the
    /// HIST-01 snapshot exists (with the pre-restore content) at the exact
    /// moment Git is asked to restore, so no destructive change can precede
    /// recovery (spec 07 §12, master spec §101.9).
    /// </summary>
    [Fact]
    public async Task RecoverySnapshotExistsBeforeTheMutation()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "content that must be recoverable");
        Harness harness = CreateHarness();
        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");

        SnapshotMetadata? snapshotAtRestoreTime = null;
        string? snapshotCopyAtRestoreTime = null;
        harness.Runner.OnInvocation = request =>
        {
            if (!request.Arguments.Contains("restore", StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            // Read the storage at the instant Git is invoked: recovery coverage
            // must already exist.
            IReadOnlyCollection<SnapshotMetadata> metadata =
                harness.Storage.GetAllMetadataAsync().GetAwaiter().GetResult();
            snapshotAtRestoreTime = Assert.Single(metadata);
            string snapshotCopy = Path.Combine(
                harness.Storage.GetSnapshotRootPath(snapshotAtRestoreTime.SnapshotId),
                Assert.Single(snapshotAtRestoreTime.Files).SnapshotFileName);
            snapshotCopyAtRestoreTime = File.Exists(snapshotCopy)
                ? File.ReadAllText(snapshotCopy)
                : null;
        };

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.True(result.RecoveryRecorded);
        Assert.NotNull(result.SnapshotId);
        Assert.NotNull(snapshotAtRestoreTime);
        Assert.Equal(result.SnapshotId, snapshotAtRestoreTime!.SnapshotId);
        Assert.Equal("content that must be recoverable", snapshotCopyAtRestoreTime);
        Assert.Equal("v1", File.ReadAllText(aPath));

        // The snapshot record is part of Document IDE History and preserves the
        // pre-restore content for a conflict-aware undo.
        SnapshotMetadata stored = Assert.Single(await harness.Storage.GetAllMetadataAsync());
        SnapshotFileRecord file = Assert.Single(stored.Files);
        Assert.Equal(
            Path.GetFullPath(aPath),
            Path.GetFullPath(file.OriginalPath),
            StringComparer.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(file.OriginalFingerprint));
    }

    /// <summary>
    /// Master-spec fail-closed rule: "if recovery cannot be created, mutation
    /// is blocked". A storage that cannot hold the snapshot leaves the file
    /// untouched and Git never runs.
    /// </summary>
    [Fact]
    public async Task FailedRecoverySnapshotBlocksTheMutation()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "still here because recovery failed");
        Harness harness = CreateHarness(recoveryCapacityBytes: 0);
        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.RecoveryBlocked, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("RecoveryBlocked", result.ErrorCode);
        Assert.False(result.RecoveryRecorded);
        Assert.Null(result.SnapshotId);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        // Nothing ran and the file is byte-identical.
        Assert.Equal("still here because recovery failed", File.ReadAllText(aPath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
    }

    // ---------------------------------------------------------------------
    // Review staleness — newer work always wins (spec 07 §11)
    // ---------------------------------------------------------------------

    /// <summary>
    /// A reviewed confirmation describes one exact state. When the file changes
    /// after the review, the old token is refused typed and the newer content is
    /// preserved instead of being overwritten.
    /// </summary>
    [Fact]
    public async Task ReviewTokenIsInvalidatedWhenTheFileChangesAfterReview()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "reviewed content");
        Harness harness = CreateHarness();
        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");

        // The user (or Word) saves again between review and confirmation.
        repo.WriteFile("docs/a.md", "newer content written after the review");

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.ReviewStale, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("ReviewStale", result.ErrorCode);
        Assert.Equal("newer content written after the review", File.ReadAllText(aPath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
    }

    // ---------------------------------------------------------------------
    // Conflicted and untracked paths are protected (spec 13 §15/§21)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion "Conflicted files are protected from unsafe
    /// restore": an unmerged path is refused typed before Git runs, and the
    /// conflict state and its content are left untouched (spec 13 §21).
    /// </summary>
    [Fact]
    public async Task ConflictedFileIsRefusedTypedAndItsStateIsUntouched()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        CreateMergeConflict(repo, "docs/a.md");
        string conflictedContent = File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md"));
        Assert.Contains("<<<<<<<", conflictedContent, StringComparison.Ordinal);
        Harness harness = CreateHarness();

        GitRestoreReviewResult reviewResult = await harness.Service.ReviewAsync(repo.RootPath, "docs/a.md");
        Assert.Equal(GitRestoreReviewStatus.Available, reviewResult.Status);
        GitRestoreReview review = Assert.IsType<GitRestoreReview>(reviewResult.Review);
        Assert.True(review.IsConflicted);
        Assert.False(review.IsApplicable);
        Assert.Contains("conflict", review.BlockedReason!, StringComparison.OrdinalIgnoreCase);

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.ConflictedPath, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("ConflictedPath", result.ErrorCode);
        Assert.Equal(conflictedContent, File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());

        // The repository still reports the conflict; the refusal did not
        // silently resolve or discard it.
        Assert.Contains(
            "UU docs/a.md",
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "status", "--porcelain"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An untracked file has no Git version to restore. It is refused typed and
    /// never deleted by a discard (spec 13 §15).
    /// </summary>
    [Fact]
    public async Task UntrackedFileIsRefusedTypedAndNeverDeleted()
    {
        using var repo = CreateRepositoryWithBaselineCommit(Array.Empty<string>());
        string newPath = repo.WriteFile("docs/brand new.md", "untracked content \u00e9");
        Harness harness = CreateHarness();

        GitRestoreReviewResult reviewResult = await harness.Service.ReviewAsync(repo.RootPath, "docs/brand new.md");
        Assert.Equal(GitRestoreReviewStatus.Available, reviewResult.Status);
        GitRestoreReview review = Assert.IsType<GitRestoreReview>(reviewResult.Review);
        Assert.True(review.IsUntracked);
        Assert.False(review.IsApplicable);

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/brand new.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.UntrackedPath, result.Outcome);
        Assert.Equal("UntrackedPath", result.ErrorCode);
        Assert.True(File.Exists(newPath));
        Assert.Equal("untracked content \u00e9", File.ReadAllText(newPath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
    }

    /// <summary>
    /// Nothing to discard is a typed no-op: a clean file is neither touched nor
    /// reported as a successful mutation.
    /// </summary>
    [Fact]
    public async Task CleanFileHasNothingToDiscardAndIsUntouched()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        Harness harness = CreateHarness();

        GitRestoreReviewResult reviewResult = await harness.Service.ReviewAsync(repo.RootPath, "docs/a.md");
        Assert.Equal(GitRestoreReviewStatus.Available, reviewResult.Status);
        GitRestoreReview review = Assert.IsType<GitRestoreReview>(reviewResult.Review);
        Assert.False(review.IsApplicable);
        Assert.Contains("nothing", review.BlockedReason!, StringComparison.OrdinalIgnoreCase);

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.NoChanges, result.Outcome);
        Assert.Equal("NoChanges", result.ErrorCode);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
    }

    // ---------------------------------------------------------------------
    // Step 4 — reindex, refresh status, and record Document IDE History
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test "Restore reindexes and refreshes Git status" plus the
    /// acceptance criterion "Document IDE History records the operation": a
    /// registered document is reindexed, the refreshed status shows the file
    /// clean, the HIST-01 snapshot records the restored fingerprint and one
    /// typed Document IDE History record names the operation and its snapshot.
    /// </summary>
    [Fact]
    public async Task RestoreReindexesRefreshesStatusAndRecordsDocumentIdeHistory()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "history-bearing edit");
        string absolutePath = Path.GetFullPath(aPath);
        var document = new Document { Id = "doc-a", CurrentPath = absolutePath };
        Harness harness = CreateHarness(
            new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase) { [absolutePath] = document });
        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.StartsWith("scm-restore-", result.OperationId!, StringComparison.Ordinal);

        // Step 4: reindex and the refreshed status.
        Assert.True(result.Reindexed);
        Assert.Equal(new[] { "doc-a" }, harness.Reindex.DocumentIds.ToArray());
        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(result.Status);
        Assert.True(refreshed.IsAvailable);
        Assert.DoesNotContain(refreshed.Snapshot!.Entries, entry => entry.Path == "docs/a.md");

        // Acceptance criterion: Document IDE History recorded the operation.
        Assert.True(result.HistoryRecorded);
        GitRestoreHistoryRecord record = Assert.Single(harness.History.Records);
        Assert.Equal(result.OperationId, record.OperationId);
        Assert.Equal("DiscardWorktree", record.Mode);
        Assert.Equal("Succeeded", record.Outcome);
        Assert.Equal("doc-a", record.DocumentId);
        Assert.Equal("docs/a.md", record.Path);
        Assert.Equal(result.SnapshotId, record.SnapshotId);

        // The snapshot is undoable: it carries the post-restore fingerprint.
        SnapshotMetadata stored = Assert.Single(await harness.Storage.GetAllMetadataAsync());
        Assert.Equal(record.OperationId, stored.OperationId);
        SnapshotFileRecord file = Assert.Single(stored.Files);
        Assert.False(string.IsNullOrWhiteSpace(file.PostOperationFingerprint));
        Assert.NotEqual(file.OriginalFingerprint, file.PostOperationFingerprint);
    }

    // ---------------------------------------------------------------------
    // Step 3 — restore a selected document from a Git version (spec 13 §15.2)
    // ---------------------------------------------------------------------

    /// <summary>
    /// A version restore writes only the selected file's working tree from the
    /// selected commit: the index is unchanged, no commit is created and no
    /// other file is touched.
    /// </summary>
    [Fact]
    public async Task RestoreFromCommitWritesOnlyTheSelectedWorktreeFile()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md", "docs/b.md" });
        repo.WriteFile("docs/a.md", "second committed version");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "second version"));
        string secondCommit = HeadCommit(repo.RootPath);

        string aPath = repo.WriteFile("docs/a.md", "working version that will be replaced");
        string bPath = repo.WriteFile("docs/b.md", "unrelated working edit");
        byte[] bBytesBefore = File.ReadAllBytes(bPath);
        string indexBefore = TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only");
        Harness harness = CreateHarness();

        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md", secondCommit);
        Assert.Equal(GitRestoreMode.RestoreVersion, review.Mode);
        Assert.Equal(secondCommit, review.SourceCommit);
        Assert.Equal("commit", review.TargetSource);

        GitRestoreResult result = await harness.Service.RestoreFromCommitAsync(
            repo.RootPath,
            "docs/a.md",
            secondCommit,
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal("second committed version", File.ReadAllText(aPath));
        Assert.Equal(bBytesBefore, File.ReadAllBytes(bPath));

        // The restore is a working-tree-only write: the index, HEAD and history
        // are exactly as before, and no commit was created.
        Assert.Equal(secondCommit, HeadCommit(repo.RootPath));
        Assert.Equal(
            indexBefore,
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only"));
        // The restored file now matches its commit, so only the unrelated
        // working edit remains dirty.
        Assert.Equal(
            new[] { "docs/b.md" },
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--name-only", "-z")
                .Split('\0', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Only a commit object id is a version source: revision expressions and
    /// option-looking values are typed rejections before Git runs (spec 13 §23).
    /// </summary>
    [Theory]
    [InlineData("HEAD")]
    [InlineData("main")]
    [InlineData("--help")]
    [InlineData("abc")]
    [InlineData("zzzzzzzz")]
    public async Task VersionRestoreRejectsNonCommitIdentifiersWithoutRunningGit(string commit)
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "protected content");
        Harness harness = CreateHarness();

        GitRestoreReviewResult review = await harness.Service.ReviewAsync(repo.RootPath, "docs/a.md", commit);
        Assert.Equal(GitRestoreReviewStatus.RejectedPath, review.Status);
        Assert.Equal("InvalidCommitId", review.ErrorCode);

        GitRestoreResult result = await harness.Service.RestoreFromCommitAsync(
            repo.RootPath,
            "docs/a.md",
            commit,
            "some-reviewed-token");

        Assert.Equal(GitRestoreOutcome.RejectedPath, result.Outcome);
        Assert.Equal("InvalidCommitId", result.ErrorCode);
        Assert.Equal("protected content", File.ReadAllText(aPath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
    }

    /// <summary>
    /// A tracked file deleted in the working tree is restored (recreated) by a
    /// discard. There is no current content to preserve, so recovery is
    /// explicitly reported as not applicable instead of blocking a
    /// non-destructive recreation.
    /// </summary>
    [Fact]
    public async Task DeletedTrackedFileIsRecreatedAndReportsNoContentToPreserve()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = Path.Combine(repo.RootPath, "docs", "a.md");
        File.Delete(aPath);
        Harness harness = CreateHarness();

        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");
        Assert.True(review.IsApplicable);
        Assert.True(review.HasWorkingTreeChange);
        Assert.False(review.RecoveryAvailable);
        Assert.Contains("already deleted", review.RecoveryDetail, StringComparison.OrdinalIgnoreCase);
        Assert.Null(review.CurrentSizeBytes);

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.False(result.RecoveryRecorded);
        Assert.Null(result.SnapshotId);
        Assert.Equal("v1", File.ReadAllText(aPath));
        Assert.True(result.HistoryRecorded);
        Assert.Single(harness.History.Records);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
    }

    // ---------------------------------------------------------------------
    // Path containment — rejection before any destructive Git runs (§23)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Escaping, non-normalized, absolute and directory path arguments are
    /// rejected typed before any destructive Git invocation: nothing is
    /// restored, no snapshot is created and a file outside the worktree is
    /// untouched (spec 13 §23).
    /// </summary>
    [Theory]
    [InlineData("../outside.md")]
    [InlineData("docs/../../outside.md")]
    [InlineData("docs/../docs/a.md")]
    [InlineData("./docs/a.md")]
    [InlineData("docs//a.md")]
    [InlineData("docs")]
    [InlineData(".")]
    [InlineData("docs/weird:name.md")]
    [InlineData("\\rooted\\outside.md")]
    [InlineData("C:outside.md")]
    [InlineData("   ")]
    public async Task HostilePathsAreRejectedBeforeAnyDestructiveGitRuns(string changePath)
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "keep me");
        string outsidePath = Path.GetFullPath(Path.Combine(repo.RootPath, "..", "outside.md"));
        File.WriteAllText(outsidePath, "outside content");
        Harness harness = CreateHarness();

        GitRestoreReviewResult review = await harness.Service.ReviewAsync(repo.RootPath, changePath);
        Assert.Equal(GitRestoreReviewStatus.RejectedPath, review.Status);
        Assert.Null(review.Review);
        Assert.False(string.IsNullOrWhiteSpace(review.ErrorCode));

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            changePath,
            "some-reviewed-token");

        Assert.Equal(GitRestoreOutcome.RejectedPath, result.Outcome);
        Assert.Null(result.Status);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.Equal("outside content", File.ReadAllText(outsidePath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
        Assert.Empty(await harness.Storage.GetAllMetadataAsync());
    }

    /// <summary>
    /// Required edge case: a path with spaces, Unicode and pathspec-looking
    /// brackets is restored as exactly one literal path — never a glob that
    /// could widen to a sibling (spec 13 §23).
    /// </summary>
    [Fact]
    public async Task PathsWithSpacesUnicodeAndBracketsStayExactAndNeverWiden()
    {
        string bracketPath = "docs/notes[1] \u00e9.md";
        string lookalikePath = "docs/notes1 \u00e9.md";
        using var repo = CreateRepositoryWithBaselineCommit(
            new[] { bracketPath, lookalikePath },
            $"scm10 literal repo \u00fc {Guid.NewGuid():N}");
        string bracketFile = repo.WriteFile(bracketPath, "bracket edited");
        string lookalikeFile = repo.WriteFile(lookalikePath, "lookalike edited");
        Harness harness = CreateHarness();

        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, bracketPath);
        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            bracketPath,
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal("v1", File.ReadAllText(bracketFile));
        Assert.Equal("lookalike edited", File.ReadAllText(lookalikeFile));

        // The destructive invocation is exactly the structured, literal,
        // single-path form: no shell string, no "." pathspec, no --hard.
        GitProcessRequest restore = Assert.Single(
            harness.Runner.Requests,
            request => IsRestoreInvocation(request.Arguments.ToArray()));
        Assert.Equal(
            new[] { "restore", "--worktree", "--", bracketPath },
            restore.Arguments.ToArray());
        Assert.Equal("1", restore.EnvironmentOverrides!["GIT_LITERAL_PATHSPECS"]);
        Assert.Equal(Path.GetFullPath(repo.RootPath), restore.WorkingDirectory, StringComparer.OrdinalIgnoreCase);

        // No invocation anywhere used a destructive whole-repository form.
        Assert.DoesNotContain(harness.Runner.Invocations, arguments =>
            arguments.Contains("--hard", StringComparer.OrdinalIgnoreCase)
            || arguments.Contains("reset", StringComparer.OrdinalIgnoreCase)
            || arguments.Contains("checkout", StringComparer.OrdinalIgnoreCase)
            || arguments.Contains(".", StringComparer.Ordinal));
    }

    /// <summary>
    /// A discard restores the working tree from the index: staged content is
    /// preserved (the index is never rewritten) and only the unstaged edit is
    /// discarded.
    /// </summary>
    [Fact]
    public async Task DiscardPreservesStagedContentAndNeverRewritesTheIndex()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "staged version");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        repo.WriteFile("docs/a.md", "unstaged version");
        string stagedDiffBefore = TestGitRepository.RunGitCaptureUtf8(
            repo.RootPath, "diff", "--cached", "--name-only");
        Harness harness = CreateHarness();

        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");
        Assert.True(review.IsStaged);
        Assert.True(review.HasWorkingTreeChange);

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.Equal("staged version", File.ReadAllText(aPath));
        Assert.Equal(
            stagedDiffBefore,
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only"));
    }

    // ---------------------------------------------------------------------
    // Failure / edge cases (typed, never silent)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task MissingGitIsTypedUnavailableForReviewAndApply()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "protected");
        var unavailable = new UnavailableGitProcessRunner();
        var service = new GitRestoreService(
            unavailable,
            new GitRepositoryService(unavailable),
            new RecoverySnapshotService(new TestHistoryStorage(5L * 1024 * 1024 * 1024)),
            new TestRestoreHistoryRecorder(),
            new RecordingReindexTrigger(),
            new FakeDocumentRepository(null));

        GitRestoreReviewResult review = await service.ReviewAsync(repo.RootPath, "docs/a.md");
        Assert.Equal(GitRestoreReviewStatus.GitUnavailable, review.Status);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, review.Failure!.ErrorKind);

        GitRestoreResult result = await service.DiscardAsync(repo.RootPath, "docs/a.md", "token");
        Assert.Equal(GitRestoreOutcome.GitUnavailable, result.Outcome);
        Assert.Equal("protected", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
    }

    [Fact]
    public async Task NonGitFolderIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm10", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            Harness harness = CreateHarness();
            GitRestoreReviewResult review = await harness.Service.ReviewAsync(folder, "a.md");
            Assert.Equal(GitRestoreReviewStatus.NoRepository, review.Status);

            GitRestoreResult result = await harness.Service.DiscardAsync(folder, "a.md", "token");
            Assert.Equal(GitRestoreOutcome.NoRepository, result.Outcome);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.Failure!.ErrorKind);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    [Fact]
    public async Task WorktreeDisappearingIsTypedFailureNotAnException()
    {
        var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "gone");
        string root = repo.RootPath;
        repo.Dispose();
        DeleteDirectoryForcefully(root);
        Assert.False(Directory.Exists(root));
        Harness harness = CreateHarness();

        GitRestoreReviewResult review = await harness.Service.ReviewAsync(root, "docs/a.md");
        Assert.Equal(GitRestoreReviewStatus.Failed, review.Status);

        GitRestoreResult result = await harness.Service.DiscardAsync(root, "docs/a.md", "token");
        Assert.Equal(GitRestoreOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, result.Failure!.ErrorKind);
    }

    [Fact]
    public async Task IndexLockContentionIsTypedAndLeavesTheFileUnchanged()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "must survive the lock");
        string lockPath = Path.Combine(repo.RootPath, ".git", "index.lock");
        File.WriteAllText(lockPath, string.Empty);
        Harness harness = CreateHarness();

        try
        {
            GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");
            GitRestoreResult result = await harness.Service.DiscardAsync(
                repo.RootPath,
                "docs/a.md",
                review.ReviewToken);

            Assert.Equal(GitRestoreOutcome.Failed, result.Outcome);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitLockContention, result.Failure!.ErrorKind);
            Assert.Equal("must survive the lock", File.ReadAllText(aPath));
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    /// <summary>
    /// The real <see cref="JsonlHistoryStore"/> (the Document IDE History store
    /// used by the host) persists and reads back the discard/restore record.
    /// </summary>
    [Fact]
    public async Task DocumentIdeHistoryStorePersistsTheRestoreOperation()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "store-backed history edit");
        string historyRoot = Path.Combine(
            Path.GetTempPath(),
            "scm10-history",
            Guid.NewGuid().ToString("N"));
        var store = new JsonlHistoryStore(historyRoot);
        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var repositories = new GitRepositoryService(runner);
        var storage = new TestHistoryStorage(5L * 1024 * 1024 * 1024);
        var service = new GitRestoreService(
            runner,
            repositories,
            new RecoverySnapshotService(storage),
            store,
            new RecordingReindexTrigger(),
            new FakeDocumentRepository(null));

        GitRestoreReviewResult reviewResult = await service.ReviewAsync(repo.RootPath, "docs/a.md");
        GitRestoreReview review = Assert.IsType<GitRestoreReview>(reviewResult.Review);
        GitRestoreResult result = await service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.Succeeded, result.Outcome);
        Assert.True(result.HistoryRecorded);

        IReadOnlyList<GitRestoreHistoryRecord> records = await store.GetGitRestoreRecordsAsync(
            result.OperationId!);
        GitRestoreHistoryRecord record = Assert.Single(records);
        Assert.Equal(result.OperationId, record.OperationId);
        Assert.Equal("DiscardWorktree", record.Mode);
        Assert.Equal("Succeeded", record.Outcome);
        Assert.Equal("docs/a.md", record.Path);
        Assert.Equal(result.SnapshotId, record.SnapshotId);
    }

    /// <summary>
    /// The reviewed state can still change between the review and the moment
    /// Git runs. A test can mutate the file at the snapshot seam (after recovery
    /// metadata is saved, before Git is invoked) to prove the pre-mutation
    /// fingerprint check refuses the stale restore and keeps the newer content.
    /// </summary>
    [Fact]
    public async Task FileChangingDuringPreparationIsDetectedBeforeGitRuns()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "content at review time");
        Harness harness = CreateHarness();
        GitRestoreReview review = await ReviewAvailableAsync(harness, repo, "docs/a.md");

        bool changed = false;
        harness.Storage.OnMetadataSaved = () =>
        {
            if (changed)
            {
                return;
            }

            changed = true;
            repo.WriteFile("docs/a.md", "content saved while the snapshot was being prepared");
        };

        GitRestoreResult result = await harness.Service.DiscardAsync(
            repo.RootPath,
            "docs/a.md",
            review.ReviewToken);

        Assert.Equal(GitRestoreOutcome.ReviewStale, result.Outcome);
        Assert.Equal("ReviewStale", result.ErrorCode);
        Assert.Equal(
            "content saved while the snapshot was being prepared",
            File.ReadAllText(aPath));
        Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
    }

    /// <summary>
    /// A file the process cannot read (e.g. an exclusive external lock) is a
    /// typed failure, never an escaping exception, and nothing is mutated.
    /// </summary>
    [Fact]
    public async Task UnreadableFileIsTypedFailureAndNothingIsMutated()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "locked content");
        Harness harness = CreateHarness();

        using (new FileStream(aPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            GitRestoreReviewResult review = await harness.Service.ReviewAsync(repo.RootPath, "docs/a.md");
            Assert.Equal(GitRestoreReviewStatus.Failed, review.Status);
            Assert.False(string.IsNullOrWhiteSpace(review.Message));

            GitRestoreResult result = await harness.Service.DiscardAsync(
                repo.RootPath,
                "docs/a.md",
                "some-reviewed-token");
            Assert.Equal(GitRestoreOutcome.Failed, result.Outcome);
            Assert.DoesNotContain(harness.Runner.Invocations, IsRestoreInvocation);
            Assert.Empty(await harness.Storage.GetAllMetadataAsync());
        }

        Assert.Equal("locked content", File.ReadAllText(aPath));
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes a worktree even though Git leaves read-only pack/object files
    /// behind on Windows.
    /// </summary>
    private static void DeleteDirectoryForcefully(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Produces a real unmerged path: two branches modify the same file and the
    /// merge stops with a conflict.
    /// </summary>
    private static void CreateMergeConflict(TestGitRepository repo, string relativePath)
    {
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "feature"));
        repo.WriteFile(relativePath, "feature version");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qam", "feature"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "main"));
        repo.WriteFile(relativePath, "main version");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qam", "main"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "feature"));
    }

    private static async Task<GitRestoreReview> ReviewAvailableAsync(
        Harness harness,
        TestGitRepository repo,
        string path,
        string? commit = null)
    {
        GitRestoreReviewResult result = await harness.Service.ReviewAsync(repo.RootPath, path, commit);
        Assert.Equal(GitRestoreReviewStatus.Available, result.Status);
        return Assert.IsType<GitRestoreReview>(result.Review);
    }

    private static string HeadCommit(string worktreeRoot) =>
        TestGitRepository.RunGitCapture(worktreeRoot, "rev-parse", "HEAD").Trim();

    private static bool IsRestoreInvocation(string[] arguments) =>
        arguments.Any(argument =>
            argument.Equals("restore", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("checkout", StringComparison.OrdinalIgnoreCase)
            || argument.Equals("reset", StringComparison.OrdinalIgnoreCase));

    private static Harness CreateHarness(
        IDictionary<string, Document>? documentsByPath = null,
        long recoveryCapacityBytes = 5L * 1024 * 1024 * 1024)
    {
        var queue = new GitWorktreeMutationQueue();
        var innerRunner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var runner = new RecordingGitProcessRunner(innerRunner);
        var repositories = new GitRepositoryService(runner);
        var storage = new TestHistoryStorage(recoveryCapacityBytes);
        var history = new TestRestoreHistoryRecorder();
        var reindex = new RecordingReindexTrigger();
        var documents = new FakeDocumentRepository(documentsByPath);
        var service = new GitRestoreService(
            runner,
            repositories,
            new RecoverySnapshotService(storage),
            history,
            reindex,
            documents);

        return new Harness(service, runner, storage, history, reindex, documents);
    }

    /// <summary>
    /// Creates a repository on branch <c>main</c> with one baseline commit
    /// containing <paramref name="baselineFiles"/>, so later edits are reported
    /// as unstaged working-tree modifications.
    /// </summary>
    private static TestGitRepository CreateRepositoryWithBaselineCommit(
        string[] baselineFiles,
        string? directoryName = null)
    {
        var repo = TestGitRepository.Create(directoryName ?? $"scm10-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        repo.WriteFile("docs/baseline.md", "baseline");
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "v1");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    private sealed record Harness(
        GitRestoreService Service,
        RecordingGitProcessRunner Runner,
        TestHistoryStorage Storage,
        TestRestoreHistoryRecorder History,
        RecordingReindexTrigger Reindex,
        FakeDocumentRepository Documents);

    /// <summary>
    /// Records every structured Git invocation (and lets a test run a hook at
    /// the exact moment Git is asked to mutate) while delegating to the real
    /// SCM-01 runner.
    /// </summary>
    private sealed class RecordingGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;

        public RecordingGitProcessRunner(IGitProcessRunner inner) => _inner = inner;

        public List<string[]> Invocations { get; } = new();

        public List<GitProcessRequest> Requests { get; } = new();

        public Action<GitProcessRequest>? OnInvocation { get; set; }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(request.Arguments.ToArray());
            Requests.Add(request);
            OnInvocation?.Invoke(request);
            return _inner.RunAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Faithful in-test <see cref="IHistoryStorage"/> rooted in the temp
    /// directory: snapshot copies really land on disk and metadata is readable,
    /// so a test can assert that recovery coverage exists (HIST-01).
    /// </summary>
    public sealed class TestHistoryStorage : IHistoryStorage
    {
        private readonly string _root;
        private readonly long _capacityBytes;
        private readonly List<SnapshotMetadata> _metadata = new();

        public TestHistoryStorage(long capacityBytes)
        {
            _root = Path.Combine(Path.GetTempPath(), "scm10-snapshots", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _capacityBytes = capacityBytes;
        }

        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_root, snapshotId);

        /// <summary>Test seam: runs after metadata is saved (before Git runs).</summary>
        public Action? OnMetadataSaved { get; set; }

        public Task SaveMetadataAsync(SnapshotMetadata metadata)
        {
            long total = _metadata.Sum(entry => entry.SizeBytes);
            if (_capacityBytes > 0 && total + metadata.SizeBytes > _capacityBytes)
            {
                throw new InvalidOperationException("Insufficient capacity for new snapshot.");
            }

            _metadata.RemoveAll(entry => entry.SnapshotId == metadata.SnapshotId);
            _metadata.Add(metadata);
            OnMetadataSaved?.Invoke();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() =>
            Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(_metadata.ToArray());

        public Task DeleteSnapshotAsync(string snapshotId)
        {
            _metadata.RemoveAll(entry => entry.SnapshotId == snapshotId);
            return Task.CompletedTask;
        }

        public Task<long> GetTotalSizeAsync() =>
            Task.FromResult(_metadata.Sum(entry => entry.SizeBytes));

        public Task<long> GetCapacityBytesAsync() => Task.FromResult(_capacityBytes);
    }

    public sealed class TestRestoreHistoryRecorder : IGitRestoreHistoryRecorder
    {
        public List<GitRestoreHistoryRecord> Records { get; } = new();

        public Task RecordAsync(
            GitRestoreHistoryRecord record,
            CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReindexTrigger : IBatchApplyReindexTrigger
    {
        public List<string> DocumentIds { get; } = new();

        public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        private readonly IReadOnlyDictionary<string, Document> _byPath;

        public FakeDocumentRepository(IDictionary<string, Document>? byPath)
        {
            _byPath = byPath is null
                ? new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Document>(byPath, StringComparer.OrdinalIgnoreCase);
        }

        public Task<Document?> GetByIdAsync(string id) =>
            Task.FromResult(_byPath.Values.FirstOrDefault(document => document.Id == id));

        public Task<Document?> GetByPathAsync(string currentPath) =>
            Task.FromResult(_byPath.TryGetValue(currentPath, out Document? document) ? document : null);

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) =>
            Task.FromResult<Document?>(null);

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) =>
            Task.FromResult(new List<Document>());

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) =>
            Task.FromResult<IReadOnlyList<Document>>(Array.Empty<Document>());

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId) =>
            Task.FromResult<IReadOnlyList<Document>>(Array.Empty<Document>());

        public Task CreateAsync(Document document) => Task.CompletedTask;

        public Task UpdateAsync(Document document) => Task.CompletedTask;

        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    /// <summary>Simulates a host with no discoverable system Git.</summary>
    private sealed class UnavailableGitProcessRunner : IGitProcessRunner
    {
        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCapabilityInfo(
                GitCapabilityStatus.Unavailable,
                "git",
                Unavailable()));

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable());

        private static GitProcessResult Unavailable() => new(
            GitProcessStatus.Unavailable,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitNotInstalled,
            "System Git executable was not found.");
    }
}

/// <summary>
/// SCM-10 — privileged API half of the Safe Git Discard &amp; Restore suite
/// (spec 13 §15/§18/§20). The destructive routes sit behind the localhost
/// session-token guard, require the reviewed token returned by the review route
/// (a missing token is a typed 400 with nothing run), reject non-contained
/// paths typed before Git runs, and return the refreshed status payload after a
/// successful restore.
/// </summary>
public sealed class GitRestoreApiTests
{

    /// <summary>
    /// The full reviewed flow through the API: review shows the diff/recovery
    /// confirmation, then the separate discard action (with that token) restores
    /// exactly one file and returns the status re-read from Git.
    /// </summary>
    [Fact]
    public async Task ReviewThenDiscardEndpointIsFileScopedAndReturnsRefreshedStatus()
    {
        using var factory = new RestoreApiFactory();
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md", "docs/b.md" });
        string aPath = repo.WriteFile("docs/a.md", "api reviewed edit");
        string bPath = repo.WriteFile("docs/b.md", "api unrelated edit");
        byte[] bBytes = File.ReadAllBytes(bPath);

        using HttpClient client = CreateAuthorizedClient(factory);

        using var reviewResponse = await client.PostAsJsonAsync(
            "/api/source-control/discard/review",
            new SourceControlRestoreReviewRequest(repo.RootPath, "docs/a.md", null));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        SourceControlRestoreReviewResponse? review =
            await reviewResponse.Content.ReadFromJsonAsync<SourceControlRestoreReviewResponse>();

        Assert.NotNull(review);
        Assert.Equal("Available", review!.Status);
        Assert.True(review.IsAvailable);
        Assert.NotNull(review.Review);
        Assert.Equal("DiscardWorktree", review.Review!.Mode);
        Assert.Contains("+api reviewed edit", review.Review.Diff, StringComparison.Ordinal);
        Assert.True(review.Review.RecoveryAvailable);
        Assert.True(review.Review.IsApplicable);
        Assert.False(string.IsNullOrWhiteSpace(review.Review.ReviewToken));

        using var discardResponse = await client.PostAsJsonAsync(
            "/api/source-control/discard",
            new SourceControlRestoreRequest(repo.RootPath, "docs/a.md", null, review.Review.ReviewToken));
        Assert.Equal(HttpStatusCode.OK, discardResponse.StatusCode);
        SourceControlRestoreResponse? body =
            await discardResponse.Content.ReadFromJsonAsync<SourceControlRestoreResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Outcome);
        Assert.Equal("DiscardWorktree", body.Mode);
        Assert.True(body.IsSuccess);
        Assert.StartsWith("scm-restore-", body.OperationId!, StringComparison.Ordinal);
        Assert.NotNull(body.SnapshotId);
        Assert.True(body.RecoveryRecorded);
        Assert.True(body.HistoryRecorded);
        Assert.Null(body.ErrorCode);

        Assert.NotNull(body.Status);
        Assert.Equal("Available", body.Status!.Status);
        Assert.Equal(
            new[] { "docs/b.md" },
            body.Status.Sections.Unstaged.Select(entry => entry.Path).ToArray());
        Assert.Empty(body.Status.Sections.Staged);

        Assert.Equal("v1", File.ReadAllText(aPath));
        Assert.Equal(bBytes, File.ReadAllBytes(bPath));

        // The Document IDE History record really landed in the history store
        // the host was configured with.
        GitRestoreHistoryRecord record = Assert.Single(factory.History.Records);
        Assert.Equal(body.OperationId, record.OperationId);
        Assert.Equal(body.SnapshotId, record.SnapshotId);
    }

    /// <summary>
    /// A destructive route is never reachable without the reviewed token: the
    /// request is a typed 400, nothing is restored and no snapshot is created.
    /// </summary>
    [Fact]
    public async Task DiscardEndpointWithoutReviewTokenIsTypedBadRequestAndChangesNothing()
    {
        using var factory = new RestoreApiFactory();
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "protected without review");

        using HttpClient client = CreateAuthorizedClient(factory);
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/discard",
            new SourceControlRestoreRequest(repo.RootPath, "docs/a.md", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument rejection = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ReviewRequired", rejection.RootElement.GetProperty("errorCode").GetString());

        Assert.Equal("protected without review", File.ReadAllText(aPath));
        Assert.Empty(factory.History.Records);
        Assert.Empty(await factory.Storage.GetAllMetadataAsync());
    }

    /// <summary>
    /// Path containment is enforced before Git runs: an escaping request is a
    /// typed 400 and the file outside the worktree is untouched.
    /// </summary>
    [Fact]
    public async Task DiscardEndpointRejectsEscapingPathWithTypedBadRequest()
    {
        using var factory = new RestoreApiFactory();
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "keep api");
        string outsidePath = Path.GetFullPath(Path.Combine(repo.RootPath, "..", "outside.md"));
        File.WriteAllText(outsidePath, "outside content");

        using HttpClient client = CreateAuthorizedClient(factory);
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/discard",
            new SourceControlRestoreRequest(repo.RootPath, "../outside.md", null, "some-reviewed-token"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument rejection = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PathRejected", rejection.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("outside content", File.ReadAllText(outsidePath));
        Assert.Equal("keep api", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
    }

    /// <summary>
    /// The restore-from-version route writes only the selected file and returns
    /// the refreshed status; HEAD and the index stay unchanged.
    /// </summary>
    [Fact]
    public async Task RestoreEndpointRestoresOnlyTheSelectedFileFromTheSelectedCommit()
    {
        using var factory = new RestoreApiFactory();
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "commit to restore");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "restore target"));
        string commit = TestGitRepository.RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim();
        string aPath = repo.WriteFile("docs/a.md", "working edit to replace");

        using HttpClient client = CreateAuthorizedClient(factory);
        using var reviewResponse = await client.PostAsJsonAsync(
            "/api/source-control/discard/review",
            new SourceControlRestoreReviewRequest(repo.RootPath, "docs/a.md", commit));
        SourceControlRestoreReviewResponse? review =
            await reviewResponse.Content.ReadFromJsonAsync<SourceControlRestoreReviewResponse>();
        Assert.Equal("RestoreVersion", review!.Review!.Mode);
        Assert.Equal(commit, review.Review.SourceCommit);

        using var restoreResponse = await client.PostAsJsonAsync(
            "/api/source-control/restore",
            new SourceControlRestoreRequest(repo.RootPath, "docs/a.md", commit, review.Review.ReviewToken));
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        SourceControlRestoreResponse? body =
            await restoreResponse.Content.ReadFromJsonAsync<SourceControlRestoreResponse>();

        Assert.Equal("Succeeded", body!.Outcome);
        Assert.Equal(commit, TestGitRepository.RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim());
        Assert.Equal("commit to restore", File.ReadAllText(aPath));
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    /// <summary>
    /// Every destructive route is guarded exactly like the other SCM routes:
    /// missing token ⇒ 401, hostile Origin ⇒ 403, invalid token ⇒ 401, and no
    /// unauthenticated request reached Git.
    /// </summary>
    [Fact]
    public async Task DiscardRoutesRejectMissingInvalidTokenAndHostileOrigin()
    {
        using var factory = new RestoreApiFactory();
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "guarded content");

        using (HttpClient anonymous = factory.CreateClient())
        {
            using var content = JsonContent.Create(
                new SourceControlRestoreRequest(repo.RootPath, "docs/a.md", null, "token"));
            using var response = await anonymous.PostAsync("/api/source-control/discard", content);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

            using HttpClient hostileOrigin = factory.CreateClient();
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add(
                "X-DocumentIDE-Session-Token",
                tokenProvider.SessionToken);
            using var hostileContent = JsonContent.Create(
                new SourceControlRestoreReviewRequest(repo.RootPath, "docs/a.md", null));
            using var hostileResponse = await hostileOrigin.PostAsync(
                "/api/source-control/discard/review",
                hostileContent);
            Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

            using HttpClient wrongToken = factory.CreateClient();
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            using var wrongContent = JsonContent.Create(
                new SourceControlRestoreRequest(repo.RootPath, "docs/a.md", null, "token"));
            using var wrongResponse = await wrongToken.PostAsync("/api/source-control/discard", wrongContent);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        }

        Assert.Equal("guarded content", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.Empty(factory.History.Records);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static HttpClient CreateAuthorizedClient(RestoreApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return client;
    }

    private static TestGitRepository CreateRepositoryWithBaselineCommit(string[] baselineFiles)
    {
        var repo = TestGitRepository.Create($"scm10-api-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        repo.WriteFile("docs/baseline.md", "baseline");
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "v1");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    /// <summary>
    /// Host overrides so an API test never writes to the real
    /// <c>%LOCALAPPDATA%</c> history/snapshot store: recovery storage and the
    /// Document IDE History recorder are replaced with test instances.
    /// </summary>
    public sealed class RestoreApiFactory : WebApplicationFactory<Program>
    {
        public GitRestoreSafetyTests.TestHistoryStorage Storage { get; } = new(5L * 1024 * 1024 * 1024);

        public GitRestoreSafetyTests.TestRestoreHistoryRecorder History { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHistoryStorage>();
                services.AddSingleton<IHistoryStorage>(Storage);
                services.RemoveAll<IGitRestoreHistoryRecorder>();
                services.AddSingleton<IGitRestoreHistoryRecorder>(History);
            });
        }
    }
}
