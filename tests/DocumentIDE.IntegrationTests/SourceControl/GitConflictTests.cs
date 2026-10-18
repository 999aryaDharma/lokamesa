using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-14 — Git Conflict State &amp; Resolution Shell
/// (spec 13 §20/§21/§23).
///
/// The conflict fixture is a <b>real</b> Git merge conflict built from two
/// branches that touch the same line, not a fabricated snapshot. The suite
/// proves that:
/// <list type="bullet">
/// <item>the unmerged state is parsed and reported typed (all seven porcelain
/// conflict kinds come from real merges),</item>
/// <item>commit / discard-restore / pull are refused typed while the repository
/// is unmerged and the repository stays byte-identical,</item>
/// <item>a DOCX conflict is reported and never auto-merged (bytes unchanged, no
/// merge output written),</item>
/// <item>manual resolution routes through the existing document-open seam and
/// carries only a document id — never a new filesystem path.</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitConflictTests.cs</c>;
/// the frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/ConflictPanel.test.tsx</c>
/// (CAN-05 batch-B ruling, same as SCM-10/SCM-11/SCM-12/SCM-13).
/// </summary>
public sealed class GitConflictTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GitConflictTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------
    // Step 1 — parse the real unmerged state
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Conflict fixture is detected."
    /// Two branches change the same line, the merge conflicts, and the service
    /// reports the real unmerged entry — kind, status characters, worktree and
    /// HEAD — without inventing a parallel status parser (spec 13 §6/§21).
    /// </summary>
    [Fact]
    public async Task ConflictFixtureIsDetected()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md");
        CreateSameLineConflict(repo, "docs/agreement.md");
        string headDuringConflict = Head(repo);

        ConflictHarness harness = CreateHarness();
        GitConflictStateResult result = await harness.Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.Available, result.Status);
        Assert.True(result.HasConflicts);
        Assert.Null(result.Failure);
        Assert.NotNull(result.State);
        Assert.Equal(
            Path.GetFullPath(repo.RootPath),
            result.State!.WorktreeRoot,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("main", result.State.BranchName);
        Assert.False(result.State.IsDetached);
        Assert.Equal(headDuringConflict, result.State.HeadCommit);

        GitConflictEntry conflict = Assert.Single(result.State.Conflicts);
        Assert.Equal("docs/agreement.md", conflict.Path);
        Assert.Equal(GitConflictKind.BothModified, conflict.Kind);
        Assert.Equal("UU", conflict.StatusCharacters);
        Assert.False(conflict.IsDocx);
        Assert.Equal(GitConflictOpenAction.None, conflict.OpenAction);
        Assert.Null(conflict.DocumentId);
    }

    // ---------------------------------------------------------------------
    // Step 1 — conflict kinds and typed outcomes
    // ---------------------------------------------------------------------

    /// <summary>
    /// Spec 13 §21: every conflict kind Git reports is passed through typed from
    /// SCM-02's snapshot. This fixture is a genuine merge with modify/modify,
    /// modify/delete and add/add conflicts.
    /// </summary>
    [Fact]
    public async Task ModifyDeleteAndAddAddConflictsAreReportedTyped()
    {
        using var repo = CreateRepositoryWithBaselineCommit(
            "docs/both-modified.md",
            "docs/deleted-by-them.md",
            "docs/deleted-by-us.md");

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "side"));
        repo.WriteFile("docs/both-modified.md", "theirs");
        File.Delete(Path.Combine(repo.RootPath, "docs", "deleted-by-them.md"));
        repo.WriteFile("docs/deleted-by-us.md", "theirs");
        repo.WriteFile("docs/both-added.md", "theirs");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "their side"));

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        repo.WriteFile("docs/both-modified.md", "ours");
        repo.WriteFile("docs/deleted-by-them.md", "ours");
        File.Delete(Path.Combine(repo.RootPath, "docs", "deleted-by-us.md"));
        repo.WriteFile("docs/both-added.md", "ours");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "our side"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));

        GitConflictStateResult result = await CreateHarness().Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.Available, result.Status);
        var kinds = result.State!.Conflicts.ToDictionary(
            conflict => conflict.Path,
            conflict => (conflict.Kind, conflict.StatusCharacters),
            StringComparer.Ordinal);

        Assert.Equal((GitConflictKind.BothModified, "UU"), kinds["docs/both-modified.md"]);
        Assert.Equal((GitConflictKind.DeletedByThem, "UD"), kinds["docs/deleted-by-them.md"]);
        Assert.Equal((GitConflictKind.DeletedByUs, "DU"), kinds["docs/deleted-by-us.md"]);
        Assert.Equal((GitConflictKind.BothAdded, "AA"), kinds["docs/both-added.md"]);
        Assert.Equal(4, kinds.Count);
    }

    /// <summary>
    /// Spec 13 §21: a rename/rename conflict reports both-deleted and both
    /// added-by-one-side kinds — DD, AU and UA — from a real merge.
    /// </summary>
    [Fact]
    public async Task RenameRenameConflictReportsBothDeletedAndAddedKinds()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/old.md");

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "side"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "mv", "docs/old.md", "docs/theirs.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "their rename"));

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "mv", "docs/old.md", "docs/ours.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "our rename"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));

        GitConflictStateResult result = await CreateHarness().Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.Available, result.Status);
        var kinds = result.State!.Conflicts.ToDictionary(
            conflict => conflict.Path,
            conflict => (conflict.Kind, conflict.StatusCharacters),
            StringComparer.Ordinal);

        Assert.Equal((GitConflictKind.BothDeleted, "DD"), kinds["docs/old.md"]);
        Assert.Equal((GitConflictKind.AddedByUs, "AU"), kinds["docs/ours.md"]);
        Assert.Equal((GitConflictKind.AddedByThem, "UA"), kinds["docs/theirs.md"]);
        Assert.Equal(3, kinds.Count);
    }

    /// <summary>
    /// Spec 13 §21: a DOCX conflict is reported like any other conflict and is
    /// never auto-merged — the service writes nothing, the file's bytes are
    /// unchanged and no merge artifact appears in the worktree.
    /// </summary>
    [Fact]
    public async Task DocxConflictIsReportedAndNeverAutoMerged()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/keep.md");
        string relativePath = "docs/contract.docx";
        byte[] baseBytes = BuildDocxLikeBytes("base");
        byte[] ourBytes = BuildDocxLikeBytes("ours");
        byte[] theirBytes = BuildDocxLikeBytes("theirs");
        File.WriteAllBytes(Path.Combine(repo.RootPath, "docs", "contract.docx"), baseBytes);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline docx"));

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "side"));
        File.WriteAllBytes(Path.Combine(repo.RootPath, relativePath), theirBytes);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "their docx"));

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        File.WriteAllBytes(Path.Combine(repo.RootPath, relativePath), ourBytes);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "our docx"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));

        Dictionary<string, string> before = SnapshotWorktree(repo.RootPath);

        GitConflictStateResult result = await CreateHarness().Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.Available, result.Status);
        GitConflictEntry conflict = Assert.Single(result.State!.Conflicts);
        Assert.Equal(relativePath, conflict.Path);
        Assert.Equal(GitConflictKind.BothModified, conflict.Kind);
        Assert.True(conflict.IsDocx);

        // Nothing was written by the shell: same files, same bytes, no merge
        // output such as *.orig/*.rej or a merged replacement.
        Dictionary<string, string> after = SnapshotWorktree(repo.RootPath);
        Assert.Equal(before.Keys.OrderBy(key => key), after.Keys.OrderBy(key => key));
        foreach ((string path, string hash) in before)
        {
            Assert.Equal(hash, after[path]);
        }

        Assert.Equal(ourBytes, File.ReadAllBytes(Path.Combine(repo.RootPath, relativePath)));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(repo.RootPath, "*", SearchOption.AllDirectories),
            path => path.EndsWith(".orig", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".rej", StringComparison.OrdinalIgnoreCase));

        GitConflictActionDecision commit = await CreateHarness().Service.CheckActionAsync(
            repo.RootPath,
            GitConflictAction.Commit);
        Assert.True(commit.IsBlocked);
        Assert.Equal("ConflictedWorktree", commit.ErrorCode);
        Assert.Equal(new[] { relativePath }, commit.ConflictedPaths);
    }

    [Fact]
    public async Task CleanRepositoryIsTypedNoConflicts()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/clean.md");

        GitConflictStateResult result = await CreateHarness().Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.NoConflicts, result.Status);
        Assert.False(result.HasConflicts);
        Assert.Null(result.Failure);
        Assert.NotNull(result.State);
        Assert.Empty(result.State!.Conflicts);
        Assert.Equal("main", result.State.BranchName);
    }

    [Fact]
    public async Task NonGitFolderIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm14", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            GitConflictStateResult result = await CreateHarness().Service.GetStateAsync(folder);

            Assert.Equal(GitConflictStatus.NoRepository, result.Status);
            Assert.False(result.HasConflicts);
            Assert.Null(result.State);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.Failure!.ErrorKind);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    [Fact]
    public async Task VanishedWorktreeIsTyped()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm14", $"gone-{Guid.NewGuid():N}");

        GitConflictStateResult result = await CreateHarness().Service.GetStateAsync(folder);

        Assert.Equal(GitConflictStatus.WorktreeMissing, result.Status);
        Assert.False(result.HasConflicts);
        Assert.Null(result.State);
    }

    [Fact]
    public async Task GitUnavailableIsTyped()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/a.md");

        GitConflictStateResult result = await CreateHarness(new UnavailableRunner())
            .Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.GitUnavailable, result.Status);
        Assert.False(result.HasConflicts);
        Assert.Null(result.State);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, result.Failure!.ErrorKind);
    }

    [Fact]
    public async Task LockedIndexIsTypedAndRedactedDiagnosticsSurvive()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/a.md");
        var lockFailure = new GitProcessResult(
            GitProcessStatus.Failed,
            128,
            string.Empty,
            "fatal: Unable to create '.git/index.lock': File exists.",
            GitProcessErrorKind.GitLockContention,
            "index locked");

        Task<GitProcessResult> Run(GitProcessRequest request, CancellationToken cancellationToken) =>
            request.Arguments.Count > 0 && request.Arguments.Contains("status")
                ? Task.FromResult(lockFailure)
                : RealRunner().RunAsync(request, cancellationToken);

        GitConflictStateResult result = await CreateHarness(new DelegateRunner(Run))
            .Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.IndexLocked, result.Status);
        Assert.Null(result.State);
        Assert.Contains("locked", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnreadableStatusIsTypedAndNeverSilentlyClean()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/a.md");
        var garbage = new GitProcessResult(
            GitProcessStatus.Succeeded,
            0,
            "this-is-not-porcelain-v2",
            string.Empty);

        Task<GitProcessResult> Run(GitProcessRequest request, CancellationToken cancellationToken) =>
            request.Arguments.Count > 0 && request.Arguments.Contains("status")
                ? Task.FromResult(garbage)
                : RealRunner().RunAsync(request, cancellationToken);

        GitConflictStateResult result = await CreateHarness(new DelegateRunner(Run))
            .Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.Unreadable, result.Status);
        Assert.False(result.HasConflicts);
        Assert.Null(result.State);
        Assert.NotNull(result.Failure);
    }

    /// <summary>Credential material in Git diagnostics never reaches the outcome (spec 13 §23).</summary>
    [Fact]
    public async Task ConflictFailureDiagnosticsNeverExposeRemoteUserInfo()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/a.md");
        var failure = new GitProcessResult(
            GitProcessStatus.Failed,
            128,
            string.Empty,
            "fatal: unable to access 'https://user:super-secret@example.invalid/repo.git/'",
            GitProcessErrorKind.Unknown,
            "fatal: unable to access 'https://user:super-secret@example.invalid/repo.git/'");

        Task<GitProcessResult> Run(GitProcessRequest request, CancellationToken cancellationToken) =>
            request.Arguments.Count > 0 && request.Arguments.Contains("status")
                ? Task.FromResult(failure)
                : RealRunner().RunAsync(request, cancellationToken);

        GitConflictStateResult result = await CreateHarness(new DelegateRunner(Run))
            .Service.GetStateAsync(repo.RootPath);

        Assert.Equal(GitConflictStatus.Unreadable, result.Status);
        Assert.DoesNotContain("super-secret", result.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", result.Failure!.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", result.Failure.StandardError, StringComparison.Ordinal);
        Assert.Contains("https://***@example.invalid/repo.git/", result.Message!, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // Step 2 — unsafe actions are refused typed while unmerged
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 2a: "Unsafe commit actions are blocked while unmerged."
    /// Commit and Commit All are refused typed before Git runs: no commit is
    /// created, HEAD and the index are unchanged, and every file stays
    /// byte-identical (spec 13 §21).
    /// </summary>
    [Fact]
    public async Task CommitAndCommitAllAreRefusedWhileUnmergedAndRepositoryIsUnchanged()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md");
        CreateSameLineConflict(repo, "docs/agreement.md");

        string headBefore = Head(repo);
        string statusBefore = WorkingTreeStatus(repo);
        int commitsBefore = CommitCount(repo);
        Dictionary<string, string> treeBefore = SnapshotWorktree(repo.RootPath);

        GitCommitService commits = CreateCommitService();
        GitCommitResult commit = await commits.CommitAsync(repo.RootPath, "must not be created");
        GitCommitResult commitAll = await commits.CommitAllAsync(repo.RootPath, "must not be created");

        foreach (GitCommitResult result in new[] { commit, commitAll })
        {
            Assert.Equal(GitCommitOutcome.ConflictedWorktree, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Equal("ConflictedWorktree", result.ErrorCode);
            Assert.Null(result.Commit);
            Assert.Equal(new[] { "docs/agreement.md" }, result.ConflictedPaths);
            Assert.Contains("conflict", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(commitsBefore, CommitCount(repo));
        Assert.Equal(statusBefore, WorkingTreeStatus(repo));
        Assert.Equal(treeBefore, SnapshotWorktree(repo.RootPath));
    }

    /// <summary>
    /// Required test 2b: the service-level decision blocks commit/commit-all/
    /// pull globally and discard/restore only on the conflicted path.
    /// </summary>
    [Fact]
    public async Task ConflictActionDecisionsAreTypedAndPathScoped()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md", "docs/other.md");
        CreateSameLineConflict(repo, "docs/agreement.md");
        ConflictHarness harness = CreateHarness();

        foreach (GitConflictAction action in new[]
                 {
                     GitConflictAction.Commit,
                     GitConflictAction.CommitAll,
                     GitConflictAction.Pull,
                 })
        {
            GitConflictActionDecision decision = await harness.Service.CheckActionAsync(repo.RootPath, action);
            Assert.True(decision.IsBlocked);
            Assert.Equal("ConflictedWorktree", decision.ErrorCode);
            Assert.Equal(new[] { "docs/agreement.md" }, decision.ConflictedPaths);
            Assert.Contains("blocked", decision.Message, StringComparison.OrdinalIgnoreCase);
        }

        GitConflictActionDecision discardConflict = await harness.Service.CheckActionAsync(
            repo.RootPath,
            GitConflictAction.Discard,
            "docs/agreement.md");
        Assert.True(discardConflict.IsBlocked);
        Assert.Equal("ConflictedPath", discardConflict.ErrorCode);

        GitConflictActionDecision restoreConflict = await harness.Service.CheckActionAsync(
            repo.RootPath,
            GitConflictAction.Restore,
            "docs/agreement.md");
        Assert.True(restoreConflict.IsBlocked);
        Assert.Equal("ConflictedPath", restoreConflict.ErrorCode);

        // A different, non-conflicted file is not blocked by an unrelated conflict.
        GitConflictActionDecision discardOther = await harness.Service.CheckActionAsync(
            repo.RootPath,
            GitConflictAction.Discard,
            "docs/other.md");
        Assert.False(discardOther.IsBlocked);
        Assert.Equal("Allowed", discardOther.ErrorCode);
    }

    /// <summary>
    /// Required test 2c: SCM-10's discard/restore refusal is reachable through
    /// the privileged API and consistent with the conflict shell; the conflicted
    /// file is untouched (spec 13 §15/§21).
    /// </summary>
    [Fact]
    public async Task DiscardOfConflictedPathIsRefusedTypedAndFileIsUnchanged()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md");
        CreateSameLineConflict(repo, "docs/agreement.md");
        byte[] before = File.ReadAllBytes(Path.Combine(repo.RootPath, "docs", "agreement.md"));
        string statusBefore = WorkingTreeStatus(repo);

        using HttpClient client = await CreateAuthorizedClientAsync();

        using HttpResponseMessage reviewResponse = await client.PostAsJsonAsync(
            "/api/source-control/discard/review",
            new SourceControlRestoreReviewRequest(repo.RootPath, "docs/agreement.md", null));
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        SourceControlRestoreReviewResponse? review =
            await reviewResponse.Content.ReadFromJsonAsync<SourceControlRestoreReviewResponse>();
        Assert.NotNull(review);
        Assert.True(review!.IsAvailable);
        Assert.NotNull(review.Review);
        Assert.True(review.Review!.IsConflicted);
        Assert.False(review.Review.IsApplicable);
        Assert.Contains("conflict", review.Review.BlockedReason!, StringComparison.OrdinalIgnoreCase);

        using HttpResponseMessage applyResponse = await client.PostAsJsonAsync(
            "/api/source-control/discard",
            new SourceControlRestoreRequest(
                repo.RootPath,
                "docs/agreement.md",
                null,
                review.Review.ReviewToken));
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);
        SourceControlRestoreResponse? applied =
            await applyResponse.Content.ReadFromJsonAsync<SourceControlRestoreResponse>();
        Assert.NotNull(applied);
        Assert.Equal("ConflictedPath", applied!.Outcome);
        Assert.False(applied.IsSuccess);

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(repo.RootPath, "docs", "agreement.md")));
        Assert.Equal(statusBefore, WorkingTreeStatus(repo));
    }

    /// <summary>
    /// Required test 2d: SCM-12's pull preflight is reachable while unmerged and
    /// runs no Git pull at all; the worktree stays byte-identical.
    /// </summary>
    [Fact]
    public async Task PullIsRefusedWhileUnmergedAndNoPullRuns()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "side"));
        repo.WriteFile("docs/baseline.md", "theirs");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "their change"));

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        repo.WriteFile("docs/baseline.md", "ours");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "our change"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));

        string headBefore = GitFetchTests.Head(repo);
        string statusBefore = GitFetchTests.WorkingTreeStatus(repo.RootPath);
        IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);

        var recording = new RecordingGitProcessRunner(RealRunner());
        GitPullSafetyTests.Harness harness = GitPullSafetyTests.CreateHarness(recording);

        GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

        Assert.Equal(GitPullOutcome.ConflictedWorktree, result.Outcome);
        Assert.True(result.IsBlocking);
        Assert.False(result.IsSuccess);
        Assert.Equal("ConflictedWorktree", result.ErrorCode);
        Assert.Contains("docs/baseline.md", result.Message, StringComparison.Ordinal);

        Assert.DoesNotContain(
            recording.Invocations,
            arguments => arguments.Contains("pull"));

        Assert.Equal(headBefore, GitFetchTests.Head(repo));
        Assert.Equal(statusBefore, GitFetchTests.WorkingTreeStatus(repo.RootPath));
        Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));
    }

    // ---------------------------------------------------------------------
    // Step 3/4 — conflict API, blocked actions and manual-open actions
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 3: "Conflict summary is visible and actionable." The
    /// privileged route reports the real conflict kinds, the blocked actions,
    /// and a manual-open action identified by document id only — never a local
    /// filesystem path (spec 13 §21).
    /// </summary>
    [Fact]
    public async Task ConflictRouteReportsSummaryBlockedActionsAndManualOpenActions()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md", "docs/contract.docx");
        string agreementPath = Path.Combine(repo.RootPath, "docs", "agreement.md");
        string contractPath = Path.Combine(repo.RootPath, "docs", "contract.docx");

        string agreementId = await RegisterDocumentAsync(agreementPath, "md");
        string contractId = await RegisterDocumentAsync(contractPath, "docx");

        CreateSameLineConflict(repo, "docs/agreement.md", "docs/contract.docx");

        using HttpClient client = await CreateAuthorizedClientAsync();
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/source-control/conflicts?path={Uri.EscapeDataString(repo.RootPath)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string json = await response.Content.ReadAsStringAsync();
        SourceControlConflictResponse? summary =
            JsonSerializer.Deserialize<SourceControlConflictResponse>(json, JsonOptions());
        Assert.NotNull(summary);
        Assert.Equal("Available", summary!.Status);
        Assert.True(summary.HasRepository);
        Assert.True(summary.HasConflicts);
        Assert.Equal(2, summary.Conflicts.Count);

        SourceControlConflictEntryResponse agreement = Assert.Single(
            summary.Conflicts,
            conflict => conflict.Path == "docs/agreement.md");
        Assert.Equal("BothModified", agreement.ConflictKind);
        Assert.Equal("UU", agreement.StatusCharacters);
        Assert.False(agreement.IsDocx);
        Assert.Equal("OpenDocument", agreement.OpenAction);
        Assert.Equal(agreementId, agreement.DocumentId);

        SourceControlConflictEntryResponse contract = Assert.Single(
            summary.Conflicts,
            conflict => conflict.Path == "docs/contract.docx");
        Assert.True(contract.IsDocx);
        Assert.Equal("OpenInWord", contract.OpenAction);
        Assert.Equal(contractId, contract.DocumentId);

        // Every unsafe action is refused typed with the action named.
        foreach (string action in new[] { "Commit", "Commit All", "Pull", "Discard", "Restore" })
        {
            SourceControlBlockedActionResponse blocked = Assert.Single(
                summary.BlockedActions,
                item => item.Action == action);
            Assert.Equal("ConflictedWorktree", blocked.ErrorCode);
            Assert.Contains("blocked", blocked.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/agreement.md", blocked.AffectedPaths);
            Assert.Contains("docs/contract.docx", blocked.AffectedPaths);
        }

        // The open action carries only the document id. The conflict entry has
        // no absolute path property at all, so no new filesystem path reaches
        // the web surface through this route.
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement first = document.RootElement
            .GetProperty("conflicts")
            .EnumerateArray()
            .Single(element => element.GetProperty("path").GetString() == "docs/agreement.md");
        Assert.Equal(
            new[]
            {
                "conflictKind", "documentId", "isDocx", "openAction", "path", "statusCharacters",
            },
            first.EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray());

        // The existing Word open route remains the guarded seam: a call without
        // the privileged session token is rejected before any document can open.
        using HttpResponseMessage anonymous = await _factory.CreateClient().PostAsJsonAsync(
            "/api/word/open",
            new OpenWordDocumentRequest(contractId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    /// <summary>
    /// Spec 13 §21: an affected path that is not a registered Document IDE
    /// document gets no open action — the shell never offers an arbitrary-path
    /// opener.
    /// </summary>
    [Fact]
    public async Task ConflictRouteDoesNotOfferAnOpenActionForUnregisteredDocuments()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/unregistered.md");
        CreateSameLineConflict(repo, "docs/unregistered.md");

        using HttpClient client = await CreateAuthorizedClientAsync();
        SourceControlConflictResponse? summary = await client.GetFromJsonAsync<SourceControlConflictResponse>(
            $"/api/source-control/conflicts?path={Uri.EscapeDataString(repo.RootPath)}");

        Assert.NotNull(summary);
        SourceControlConflictEntryResponse conflict = Assert.Single(summary!.Conflicts);
        Assert.Equal("None", conflict.OpenAction);
        Assert.Null(conflict.DocumentId);
    }

    /// <summary>
    /// Spec 13 §21: a repository with no unmerged entries answers the typed
    /// <c>NoConflicts</c> state with nothing blocked.
    /// </summary>
    [Fact]
    public async Task ConflictRouteReportsTypedNoConflictsWithoutBlockedActions()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/clean.md");

        using HttpClient client = await CreateAuthorizedClientAsync();
        SourceControlConflictResponse? summary = await client.GetFromJsonAsync<SourceControlConflictResponse>(
            $"/api/source-control/conflicts?path={Uri.EscapeDataString(repo.RootPath)}");

        Assert.NotNull(summary);
        Assert.Equal("NoConflicts", summary!.Status);
        Assert.True(summary.HasRepository);
        Assert.False(summary.HasConflicts);
        Assert.Empty(summary.Conflicts);
        Assert.Empty(summary.BlockedActions);
    }

    /// <summary>
    /// The API boundary refuses commit and commit-all while unmerged: HTTP 200,
    /// typed <c>ConflictedWorktree</c>, no commit created (spec 13 §21).
    /// </summary>
    [Fact]
    public async Task CommitRoutesAreRefusedTypedWhileUnmerged()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md");
        CreateSameLineConflict(repo, "docs/agreement.md");
        string headBefore = Head(repo);
        int commitsBefore = CommitCount(repo);

        using HttpClient client = await CreateAuthorizedClientAsync();
        foreach (string route in new[] { "commit", "commit-all" })
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/source-control/{route}",
                new SourceControlCommitRequest(repo.RootPath, "must not be created"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            SourceControlCommitResponse? result =
                await response.Content.ReadFromJsonAsync<SourceControlCommitResponse>();
            Assert.NotNull(result);
            Assert.Equal("ConflictedWorktree", result!.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Commit);
            Assert.Null(result.StagedPaths);
            Assert.Equal(new[] { "docs/agreement.md" }, result.ConflictedPaths);
        }

        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(commitsBefore, CommitCount(repo));
    }

    [Fact]
    public async Task ConflictRoutesRejectMissingOrInvalidTokenAndMissingPath()
    {
        string path = Uri.EscapeDataString(Path.GetTempPath());

        using (var anonymous = _factory.CreateClient())
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync($"/api/source-control/conflicts?path={path}")).StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

            using var hostileOrigin = _factory.CreateClient();
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await hostileOrigin.GetAsync($"/api/source-control/conflicts?path={path}")).StatusCode);

            using var wrongToken = _factory.CreateClient();
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await wrongToken.GetAsync($"/api/source-control/conflicts?path={path}")).StatusCode);
        }

        using HttpClient authorized = await CreateAuthorizedClientAsync();
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await authorized.GetAsync("/api/source-control/conflicts")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await authorized.GetAsync("/api/source-control/conflicts?path=%20")).StatusCode);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static ConflictHarness CreateHarness(
        IGitProcessRunner? runner = null,
        IDocumentRepository? documents = null)
    {
        var queue = new GitWorktreeMutationQueue();
        IGitProcessRunner effective =
            runner ?? new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var repositories = new GitRepositoryService(effective);
        var service = new GitConflictService(repositories, documents ?? new FakeDocumentRepository());
        return new ConflictHarness(service);
    }

    /// <summary>
    /// Creates a repository on <c>main</c> with one baseline commit containing
    /// <paramref name="baselineFiles"/>, so a later change is a real modification.
    /// </summary>
    private static TestGitRepository CreateRepositoryWithBaselineCommit(params string[] baselineFiles)
    {
        var repo = TestGitRepository.Create($"scm14-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "base");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    /// <summary>
    /// Builds a genuine same-line merge conflict: <c>side</c> and <c>main</c>
    /// both change the same line after the baseline commit, and the merge fails.
    /// </summary>
    private static void CreateSameLineConflict(TestGitRepository repo, params string[] relativePaths)
    {
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "side"));
        foreach (string relativePath in relativePaths)
        {
            repo.WriteFile(relativePath, "theirs");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "their change"));

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        foreach (string relativePath in relativePaths)
        {
            repo.WriteFile(relativePath, "ours");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "our change"));

        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));
    }

    private static string Head(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-parse", "HEAD").Trim();

    private static string WorkingTreeStatus(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "status", "--porcelain");

    /// <summary>
    /// Deterministic binary-looking DOCX bytes (a ZIP local-file header plus a
    /// payload). The shell must treat the file as opaque: Git's binary conflict
    /// detection is enough, and no OOXML parsing is involved (spec 13 §21).
    /// </summary>
    private static byte[] BuildDocxLikeBytes(string label)
    {
        // A NUL byte makes Git treat the pseudo-DOCX as binary, exactly like a
        // real ZIP container. Git then leaves "ours" untouched instead of
        // writing text conflict markers, which is the state this test asserts.
        byte[] text = Encoding.UTF8.GetBytes($"PK\u0003\u0004-ooxml-{label}-");
        byte[] payload = new byte[text.Length + 4];
        text.CopyTo(payload, 0);
        payload[^4] = 0x00;
        payload[^3] = 0x01;
        payload[^2] = 0x02;
        payload[^1] = 0x03;
        return payload;
    }

    /// <summary>
    /// Hashes every file under the worktree root (including <c>.git</c>) so a
    /// test can prove the shell wrote nothing at all.
    /// </summary>
    private static Dictionary<string, string> SnapshotWorktree(string rootPath)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            using FileStream stream = File.OpenRead(file);
            snapshot[Path.GetRelativePath(rootPath, file)] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return snapshot;
    }

    private static GitProcessRunner RealRunner() =>
        new(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());

    private static GitCommitService CreateCommitService(IGitProcessRunner? runner = null)
    {
        IGitProcessRunner effective = runner ?? RealRunner();
        var repositories = new GitRepositoryService(effective);
        var stages = new GitStageService(effective, repositories);
        return new GitCommitService(effective, repositories, stages);
    }

    private static int CommitCount(TestGitRepository repo) =>
        int.Parse(
            TestGitRepository
                .RunGitCaptureUtf8(repo.RootPath, "rev-list", "--count", "HEAD")
                .Trim());

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return await Task.FromResult(client);
    }

    private async Task<string> RegisterDocumentAsync(string absolutePath, string extension)
    {
        using var scope = _factory.Services.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        string id = $"scm14-doc-{Guid.NewGuid():N}";
        await documents.CreateAsync(new Document
        {
            Id = id,
            CurrentPath = absolutePath,
            Filename = Path.GetFileName(absolutePath),
            Extension = extension,
            DocumentType = extension == "docx" ? "docx" : "markdown",
            SizeBytes = new FileInfo(absolutePath).Length,
            AvailabilityStatus = "available",
            IndexStatus = "indexed",
        });
        return id;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a locked .git file must not fail the test run.
        }
    }

    private sealed class DelegateRunner : IGitProcessRunner
    {
        private readonly Func<GitProcessRequest, CancellationToken, Task<GitProcessResult>> _run;

        public DelegateRunner(Func<GitProcessRequest, CancellationToken, Task<GitProcessResult>> run)
        {
            _run = run;
        }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCapabilityInfo(
                GitCapabilityStatus.Available,
                "git",
                new GitProcessResult(GitProcessStatus.Succeeded, 0, "git version 2.test", string.Empty),
                "2.test"));

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default) => _run(request, cancellationToken);
    }

    private sealed class UnavailableRunner : IGitProcessRunner
    {
        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCapabilityInfo(
                GitCapabilityStatus.Unavailable,
                "git",
                new GitProcessResult(
                    GitProcessStatus.Unavailable,
                    null,
                    string.Empty,
                    string.Empty,
                    GitProcessErrorKind.GitNotInstalled,
                    "Git is not installed.")));

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitProcessResult(
                GitProcessStatus.Unavailable,
                null,
                string.Empty,
                string.Empty,
                GitProcessErrorKind.GitNotInstalled,
                "Git is not installed."));
    }

    private sealed record ConflictHarness(GitConflictService Service);

    /// <summary>
    /// Minimal document repository for the conflict shell: only the read-only
    /// path lookup the conflict service uses is meaningful.
    /// </summary>
    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        private readonly IReadOnlyDictionary<string, Document> _byPath;

        public FakeDocumentRepository(IDictionary<string, Document>? byPath = null)
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

        public Task MarkMissingAsync(string id) => Task.CompletedTask;

        public Task<IReadOnlyList<Document>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Document>>(_byPath.Values.ToList());

        public Task<IReadOnlyList<Document>> GetByFingerprintAsync(string fingerprint) =>
            Task.FromResult<IReadOnlyList<Document>>(Array.Empty<Document>());
    }
}
