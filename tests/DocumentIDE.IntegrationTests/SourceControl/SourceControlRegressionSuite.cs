using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-17 — Source Control regression suite (spec 11 §2/§26, spec 13
/// §14/§16/§19/§20/§23).
///
/// This is a cross-cutting release-hardening suite, not a re-implementation of
/// the per-sprint suites. It drives the real services against real, disposable
/// Git repositories and real local bare remotes (no network) and covers:
/// <list type="bullet">
/// <item><b>fixture matrix</b> — unborn HEAD, no repository, remote, diverged,
/// dirty, conflicted and a worktree set: every read/write route returns its
/// typed outcome instead of throwing,</item>
/// <item><b>concurrency</b> — stage/commit/fetch/pull/discard serialize per
/// worktree, different worktrees stay independent, and repeated concurrent
/// staging leaves a valid index,</item>
/// <item><b>remote/worktree integration</b> — fetch/pull/push round trip,
/// push rejection and unreachable-remote refusal paths, and the worktree
/// create/open/remove/prune lifecycle through the privileged API.</item>
/// </list>
/// </summary>
public sealed class SourceControlRegressionSuite : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SessionTokenHeader = "X-DocumentIDE-Session-Token";
    private const string TestOrigin = "http://localhost:5173";

    private readonly WebApplicationFactory<Program> _factory;

    public SourceControlRegressionSuite(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // =====================================================================
    // Step 1 — fixture repository matrix
    // =====================================================================

    /// <summary>
    /// Spec 13 §5/§6/§12/§20: a freshly initialized repository has an unborn
    /// HEAD. Context, status, history, remote, stage, commit, fetch, pull,
    /// push and worktree reads all answer with typed outcomes — no route throws
    /// and no read invents a commit.
    /// </summary>
    [Fact]
    public async Task FixtureMatrix_UnbornHead_EveryRouteReturnsTypedOutcome()
    {
        using var repo = TestGitRepository.Create($"scm17-unborn-{Guid.NewGuid():N}");
        ScmHarness harness = ScmHarness.Create();

        GitRepositoryContext context = await harness.Repositories.DiscoverAsync(repo.RootPath);
        Assert.Equal(GitRepositoryStatus.Available, context.Status);
        Assert.True(context.HasRepository);
        Assert.Null(context.HeadCommit);
        Assert.False(string.IsNullOrWhiteSpace(context.BranchName));
        Assert.False(context.IsDetached);

        GitStatusResult status = await harness.Repositories.GetStatusAsync(context.WorktreeRoot!);
        Assert.True(status.IsAvailable, status.Failure?.ErrorMessage);
        Assert.Empty(status.Snapshot!.Entries);

        GitHistoryResult history = await harness.History.GetHistoryAsync(repo.RootPath);
        Assert.Equal(GitHistoryStatus.Available, history.Status);
        Assert.Empty(history.Page!.Commits);

        GitRemoteStateResult remote = await harness.Remotes.GetRemoteStateAsync(repo.RootPath);
        Assert.Equal(GitRemoteOutcome.NoUpstream, remote.Outcome);

        GitFetchResult fetch = await harness.Remotes.FetchAsync(repo.RootPath);
        Assert.Equal(GitFetchOutcome.NoUpstream, fetch.Outcome);

        GitPullResult pull = await harness.Pulls.PullAsync(repo.RootPath);
        Assert.Equal(GitPullOutcome.NoUpstream, pull.Outcome);

        GitPushResult push = await harness.Pushes.PushAsync(repo.RootPath);
        Assert.Equal(GitPushOutcome.NoUpstream, push.Outcome);

        GitStageResult stageAll = await harness.Stages.StageAllAsync(repo.RootPath);
        Assert.Equal(GitStageOutcome.Succeeded, stageAll.Outcome);
        Assert.Empty(stageAll.Status!.Snapshot!.Entries);

        GitCommitResult commitAll = await harness.Commits.CommitAllAsync(repo.RootPath, "unborn");
        Assert.Equal(GitCommitOutcome.NothingStaged, commitAll.Outcome);

        GitWorktreeListResult worktrees = await harness.Worktrees.ListWorktreesAsync(repo.RootPath);
        Assert.True(worktrees.IsSuccess, worktrees.ErrorMessage);
        Assert.Single(worktrees.Worktrees);

        GitConflictStateResult conflicts = await harness.Conflicts.GetStateAsync(repo.RootPath);
        Assert.Equal(GitConflictStatus.NoConflicts, conflicts.Status);
    }

    /// <summary>
    /// Spec 13 §5/§20: a folder outside Git stays a valid Document IDE source.
    /// Every Source Control route reports the typed <c>NoRepository</c> /
    /// <c>NotARepository</c> state instead of throwing or running a mutation.
    /// </summary>
    [Fact]
    public async Task FixtureMatrix_PlainFolder_EveryRouteReturnsNoRepositoryWithoutThrowing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm17", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            ScmHarness harness = ScmHarness.Create();

            Assert.Equal(
                GitRepositoryStatus.NoRepository,
                (await harness.Repositories.DiscoverAsync(folder)).Status);
            Assert.Equal(
                GitRepositoryStatus.NoRepository,
                (await harness.Repositories.GetStatusAsync(folder)).Status);
            Assert.Equal(
                GitStageOutcome.NoRepository,
                (await harness.Stages.StageAllAsync(folder)).Outcome);
            Assert.Equal(
                GitCommitOutcome.NoRepository,
                (await harness.Commits.CommitAsync(folder, "message")).Outcome);
            Assert.Equal(
                GitRemoteOutcome.NoRepository,
                (await harness.Remotes.GetRemoteStateAsync(folder)).Outcome);
            Assert.Equal(
                GitFetchOutcome.NoRepository,
                (await harness.Remotes.FetchAsync(folder)).Outcome);
            Assert.Equal(
                GitPullOutcome.NoRepository,
                (await harness.Pulls.PullAsync(folder)).Outcome);
            Assert.Equal(
                GitPushOutcome.NoRepository,
                (await harness.Pushes.PushAsync(folder)).Outcome);
            Assert.Equal(
                GitHistoryStatus.NoRepository,
                (await harness.History.GetHistoryAsync(folder)).Status);
            Assert.Equal(
                GitConflictStatus.NoRepository,
                (await harness.Conflicts.GetStateAsync(folder)).Status);
            Assert.Equal(
                GitWorktreeListStatus.NotARepository,
                (await harness.Worktrees.ListWorktreesAsync(folder)).Status);
            Assert.Equal(
                GitWorktreeCreateStatus.NotARepository,
                (await harness.WorktreeMutations.CreateWorktreeAsync(
                    new GitWorktreeCreateRequest(folder, Path.Combine(folder, "wt"), "main"))).Status);
            Assert.Equal(
                GitRestoreOutcome.ReviewRequired,
                (await harness.Restores.DiscardAsync(folder, "a.md", reviewToken: null)).Outcome);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    /// <summary>
    /// Spec 13 §14.2/§14.3: a repository with a configured upstream reports
    /// ahead/behind and a clean fetch/pull round trip; a dirty worktree is
    /// refused typed <b>before</b> Git runs and the file stays byte-identical.
    /// </summary>
    [Fact]
    public async Task FixtureMatrix_DirtyWorktreeWithRemote_ReturnsTypedBlockingOutcome()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        string filePath = repo.WriteFile("docs/baseline.md", "locally edited");
        string before = File.ReadAllText(filePath);
        string headBefore = GitFetchTests.Head(repo);
        ScmHarness harness = ScmHarness.Create();

        GitRemoteStateResult remote = await harness.Remotes.GetRemoteStateAsync(repo.RootPath);
        Assert.Equal(GitRemoteOutcome.Available, remote.Outcome);
        Assert.True(remote.HasUpstream);
        Assert.Equal(0, remote.State!.Ahead);
        Assert.Equal(0, remote.State.Behind);

        GitPullResult pull = await harness.Pulls.PullAsync(repo.RootPath);
        Assert.Equal(GitPullOutcome.DirtyWorktree, pull.Outcome);
        Assert.True(pull.IsBlocking);
        Assert.Equal(headBefore, GitFetchTests.Head(repo));
        Assert.Equal(before, File.ReadAllText(filePath));

        // The write routes stay typed on the same fixture: nothing staged is
        // never an empty commit, and a successful stage/commit then works.
        Assert.Equal(
            GitCommitOutcome.NothingStaged,
            (await harness.Commits.CommitAsync(repo.RootPath, "nothing staged")).Outcome);

        GitStageResult staged = await harness.Stages.StageAsync(repo.RootPath, "docs/baseline.md");
        Assert.Equal(GitStageOutcome.Succeeded, staged.Outcome);

        GitCommitResult committed = await harness.Commits.CommitAsync(repo.RootPath, "dirty fixture commit");
        Assert.Equal(GitCommitOutcome.Succeeded, committed.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(committed.Commit!.CommitId));

        GitFetchResult fetched = await harness.Remotes.FetchAsync(repo.RootPath);
        Assert.Equal(GitFetchOutcome.Succeeded, fetched.Outcome);
        Assert.Equal(1, fetched.Remote!.Ahead);
    }

    /// <summary>
    /// Spec 13 §14.3/§20: diverged histories are refused as the typed blocking
    /// <c>Diverged</c> outcome; no merge commit and no rebase are created.
    /// </summary>
    [Fact]
    public async Task FixtureMatrix_DivergedRepository_PullRefusedTypedWithoutMergeCommit()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote.md", "remote", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            GitFetchTests.CommitFile(repo.RootPath, "docs/local.md", "local", "local commit");

            string headBefore = GitFetchTests.Head(repo);
            ScmHarness harness = ScmHarness.Create();

            GitPullResult result = await harness.Pulls.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.Diverged, result.Outcome);
            Assert.True(result.IsBlocking);
            Assert.Null(result.Refresh);
            Assert.Equal(headBefore, GitFetchTests.Head(repo));
            Assert.Equal(
                "0",
                TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-list", "--merges", "--count", "HEAD").Trim());
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>
    /// Spec 13 §20/§21: a real unmerged repository is a resolved state. Commit,
    /// commit-all and pull are refused typed with no repository change, and the
    /// conflict state is reported from Git.
    /// </summary>
    [Fact]
    public async Task FixtureMatrix_ConflictedRepository_WriteRoutesRefusedTyped()
    {
        using var repo = CreateRepositoryWithBaselineCommit("docs/agreement.md");
        CreateSameLineConflict(repo, "docs/agreement.md");
        string conflictPath = Path.Combine(repo.RootPath, "docs", "agreement.md");
        string before = File.ReadAllText(conflictPath);
        string headBefore = GitFetchTests.Head(repo);
        ScmHarness harness = ScmHarness.Create();

        GitConflictStateResult state = await harness.Conflicts.GetStateAsync(repo.RootPath);
        Assert.Equal(GitConflictStatus.Available, state.Status);
        Assert.True(state.HasConflicts);
        Assert.Contains(state.State!.Conflicts, conflict => conflict.Path == "docs/agreement.md");

        GitCommitResult commit = await harness.Commits.CommitAsync(repo.RootPath, "must not commit");
        Assert.Equal(GitCommitOutcome.ConflictedWorktree, commit.Outcome);

        GitCommitResult commitAll = await harness.Commits.CommitAllAsync(repo.RootPath, "must not commit");
        Assert.Equal(GitCommitOutcome.ConflictedWorktree, commitAll.Outcome);

        GitPullResult pull = await harness.Pulls.PullAsync(repo.RootPath);
        Assert.Equal(GitPullOutcome.ConflictedWorktree, pull.Outcome);

        GitRestoreResult discard = await harness.Restores.DiscardAsync(
            repo.RootPath,
            "docs/agreement.md",
            reviewToken: null);
        Assert.Equal(GitRestoreOutcome.ReviewRequired, discard.Outcome);

        Assert.Equal(headBefore, GitFetchTests.Head(repo));
        Assert.Equal(before, File.ReadAllText(conflictPath));
    }

    /// <summary>
    /// Spec 13 §16.1-§16.5: a worktree set is listed read-only, an unattached
    /// worktree yields the explicit attachment signal, removing the main
    /// worktree is refused, and refresh keeps every valid worktree.
    /// </summary>
    [Fact]
    public async Task FixtureMatrix_WorktreeSet_ListOpenRemoveAndRefreshAreTyped()
    {
        using var fixture = RegressionWorktreeFixture.Create("matrix");
        string linkedPath = fixture.AddDetachedWorktree("wt-matrix");
        ScmHarness harness = ScmHarness.Create();

        GitWorktreeListResult list = await harness.Worktrees.ListWorktreesAsync(fixture.Repo.RootPath);
        Assert.True(list.IsSuccess, list.ErrorMessage);
        Assert.Equal(2, list.Worktrees.Count);
        Assert.Single(list.Worktrees, worktree => worktree.IsMainWorktree);
        Assert.Single(list.Worktrees, worktree => PathEquals(worktree.Path, linkedPath));

        GitWorktreeOpenResult open = await harness.Worktrees.OpenWorktreeAsync(
            new GitWorktreeOpenRequest("scm17-workspace", fixture.Repo.RootPath, linkedPath));
        Assert.Equal(GitWorktreeOpenStatus.RequiresAttachmentFlow, open.Status);
        Assert.False(open.IsOpened);
        Assert.True(Directory.Exists(linkedPath));

        GitWorktreeRemoveResult mainRemoval = await harness.WorktreeMutations.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, fixture.Repo.RootPath));
        Assert.Equal(GitWorktreeRemoveStatus.IsMainWorktree, mainRemoval.Status);
        Assert.True(Directory.Exists(linkedPath));

        GitWorktreeRefreshResult refresh =
            await harness.WorktreeMutations.RefreshWorktreesAsync(fixture.Repo.RootPath);
        Assert.Equal(GitWorktreeRefreshStatus.Succeeded, refresh.Status);
        Assert.Equal(2, refresh.ListResult!.Worktrees.Count);

        GitWorktreeRemoveResult removed = await harness.WorktreeMutations.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, linkedPath));
        Assert.Equal(GitWorktreeRemoveStatus.Succeeded, removed.Status);
        Assert.False(Directory.Exists(linkedPath));
    }

    // =====================================================================
    // Step 3 — concurrency
    // =====================================================================

    /// <summary>
    /// Spec 13 §19: stage, commit, fetch, pull and discard all mutate shared
    /// repository/working-tree state and are serialized exactly once per
    /// worktree. Each of the five argument vectors is classified as a mutation,
    /// and the queue never runs two of them at once.
    /// </summary>
    [Fact]
    public async Task Concurrency_StageCommitFetchPullDiscard_SerializePerWorktree()
    {
        using var repo = TestGitRepository.Create($"scm17-serial-{Guid.NewGuid():N}");
        var queue = new GitWorktreeMutationQueue();

        string[][] mutations =
        {
            new[] { "add", "--", "docs/a.md" },
            new[] { "commit", "-m", "message" },
            new[] { "fetch" },
            new[] { "pull", "--ff-only", "--no-rebase" },
            new[] { "restore", "--worktree", "--", "docs/a.md" },
        };

        Assert.All(mutations, arguments => Assert.True(
            GitCommandClassifier.MutatesRepositoryState(arguments),
            $"`git {string.Join(' ', arguments)}` must be serialized."));

        int active = 0;
        int maxObserved = 0;
        Task<string>[] tasks = mutations.Select(arguments =>
            queue.EnqueueAsync(repo.RootPath, async () =>
            {
                int now = Interlocked.Increment(ref active);
                try
                {
                    UpdateMaximum(ref maxObserved, now);
                    await Task.Delay(30);
                    return arguments[0];
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            })).ToArray();

        string[] completed = await Task.WhenAll(tasks);

        Assert.Equal(mutations.Length, completed.Length);
        Assert.Equal(1, maxObserved);
        Assert.Equal(1, queue.MaxObservedConcurrency);
    }

    /// <summary>
    /// Spec 13 §19: two different worktrees remain independent — an operation
    /// on one never waits for the other. Uses two real worktree directories.
    /// </summary>
    [Fact]
    public async Task Concurrency_DifferentWorktrees_ProceedIndependently()
    {
        using var fixture = RegressionWorktreeFixture.Create("parallel");
        string linkedPath = fixture.AddDetachedWorktree("wt-parallel");
        var queue = new GitWorktreeMutationQueue();
        using var bothStarted = new CountdownEvent(2);

        Task<int>[] tasks =
        {
            queue.EnqueueAsync(fixture.Repo.RootPath, async () =>
            {
                await SignalAndWaitAsync(bothStarted);
                return 1;
            }),
            queue.EnqueueAsync(linkedPath, async () =>
            {
                await SignalAndWaitAsync(bothStarted);
                return 2;
            }),
        };

        int[] results = await Task.WhenAll(tasks);

        Assert.Equal(new[] { 1, 2 }, results);
        Assert.True(queue.MaxObservedConcurrency >= 2);
    }

    /// <summary>
    /// Spec 13 §19: repeated concurrent index writes against one worktree must
    /// leave a valid index — every mutation succeeds, every staged path is
    /// present exactly once, no lock file is left behind and `git fsck` is
    /// clean.
    /// </summary>
    [Fact]
    public async Task Concurrency_ConcurrentStages_DoNotCorruptIndex()
    {
        using var repo = TestGitRepository.Create($"scm17-index-{Guid.NewGuid():N}");
        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);

        string[] files = Enumerable.Range(0, 8).Select(index => $"docs/file-{index}.md").ToArray();
        foreach (string file in files)
        {
            repo.WriteFile(file, file);
        }

        Task<GitProcessResult>[] tasks = files
            .Select(file => runner.RunAsync(
                new GitProcessRequest(new[] { "add", "--", file }, repo.RootPath)))
            .ToArray();

        GitProcessResult[] results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.ErrorMessage));
        Assert.Equal(1, queue.MaxObservedConcurrency);

        var repositories = new GitRepositoryService(runner);
        GitStatusResult status = await repositories.GetStatusAsync(repo.RootPath);
        Assert.True(status.IsAvailable, status.Failure?.ErrorMessage);
        string[] staged = status.Snapshot!.Entries
            .Where(entry => entry.HasStagedChange)
            .Select(entry => entry.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(files.OrderBy(path => path, StringComparer.Ordinal), staged);

        Assert.False(
            File.Exists(Path.Combine(repo.RootPath, ".git", "index.lock")),
            "A concurrent stage left an index lock behind.");
        Assert.Equal(
            0,
            TestGitRepository.RunGit(repo.RootPath, "fsck", "--no-progress", "--no-dangling"));
    }

    // =====================================================================
    // Step 4 — remote and worktree integration
    // =====================================================================

    /// <summary>
    /// Spec 13 §14.1/§14.3/§14.4: against a real local bare remote, fetch only
    /// updates tracking state, pull fast-forwards, and an explicit push
    /// publishes the new commit. No network is used.
    /// </summary>
    [Fact]
    public async Task Remote_BareRemoteFetchPullAndPushRoundTrip()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote-roundtrip.md", "remote v1", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string remoteHead = TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim();

            ScmHarness harness = ScmHarness.Create();

            GitFetchResult fetch = await harness.Remotes.FetchAsync(repo.RootPath);
            Assert.Equal(GitFetchOutcome.Succeeded, fetch.Outcome);
            Assert.Equal(1, fetch.Remote!.Behind);
            Assert.False(File.Exists(Path.Combine(repo.RootPath, "docs", "remote-roundtrip.md")));

            GitPullResult pull = await harness.Pulls.PullAsync(repo.RootPath);
            Assert.Equal(GitPullOutcome.Succeeded, pull.Outcome);
            Assert.Equal(remoteHead, GitFetchTests.Head(repo));
            Assert.Equal(
                "remote v1",
                File.ReadAllText(Path.Combine(repo.RootPath, "docs", "remote-roundtrip.md")));

            GitFetchTests.CommitFile(repo.RootPath, "docs/local-roundtrip.md", "local v1", "local commit");
            string localHead = GitFetchTests.Head(repo);

            GitPushResult push = await harness.Pushes.PushAsync(repo.RootPath);
            Assert.Equal(GitPushOutcome.Succeeded, push.Outcome);
            Assert.Equal(
                localHead,
                TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim());
            Assert.Equal(0, push.Remote!.Ahead);

            Assert.Equal(
                "0",
                TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-list", "--merges", "--count", "HEAD").Trim());
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>
    /// Spec 13 §14.4/§20: a non-fast-forward push is the typed
    /// <c>Rejected</c> outcome; local history is preserved and the remote is
    /// unchanged.
    /// </summary>
    [Fact]
    public async Task Remote_PushRejectedWhenRemoteMovedAhead_IsTypedAndKeepsLocalHistory()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote-ahead.md", "remote", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string remoteHead = TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim();

            GitFetchTests.CommitFile(repo.RootPath, "docs/local-ahead.md", "local", "local commit");
            string localHead = GitFetchTests.Head(repo);
            ScmHarness harness = ScmHarness.Create();

            GitPushResult push = await harness.Pushes.PushAsync(repo.RootPath);

            Assert.Equal(GitPushOutcome.Rejected, push.Outcome);
            Assert.False(push.IsSuccess);
            Assert.Equal(localHead, GitFetchTests.Head(repo));
            Assert.Equal(
                remoteHead,
                TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim());
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>
    /// Spec 13 §14.1/§20: an unreachable remote is a typed refusal — fetch does
    /// not hang, does not throw, and leaves the worktree unchanged.
    /// </summary>
    [Fact]
    public async Task Remote_UnreachableRemoteFetchIsTypedRefusal()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        string missing = Path.Combine(Path.GetTempPath(), "scm17", $"missing-{Guid.NewGuid():N}.git");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "remote", "set-url", "origin", missing));
        string headBefore = GitFetchTests.Head(repo);
        ScmHarness harness = ScmHarness.Create();

        GitFetchResult fetch = await harness.Remotes.FetchAsync(repo.RootPath);

        Assert.NotEqual(GitFetchOutcome.Succeeded, fetch.Outcome);
        Assert.False(fetch.IsSuccess);
        Assert.NotNull(fetch.Message);
        Assert.Equal(headBefore, GitFetchTests.Head(repo));
    }

    /// <summary>
    /// Spec 13 §16/§18: the full worktree lifecycle through the privileged API:
    /// create from an existing branch, open (explicit attachment signal), list,
    /// remove and prune. The routes stay thin typed surfaces over real Git.
    /// </summary>
    [Fact]
    public async Task Remote_WorktreeApiLifecycle_CreateOpenRemovePrune()
    {
        using var fixture = RegressionWorktreeFixture.Create("api");
        string targetPath = fixture.NewTargetPath("wt-api");
        using HttpClient client = await CreateAuthorizedClientAsync();

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/source-control/worktrees",
            new SourceControlWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "feature-x"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        SourceControlWorktreeCreateResponse? created =
            await createResponse.Content.ReadFromJsonAsync<SourceControlWorktreeCreateResponse>();
        Assert.NotNull(created);
        Assert.True(created!.IsSuccess, created.Message);
        Assert.Equal("Succeeded", created.Status);
        Assert.Contains(created.Worktrees!, worktree => PathEquals(worktree.Path, targetPath));

        using HttpResponseMessage openResponse = await client.PostAsJsonAsync(
            "/api/source-control/worktrees/open",
            new SourceControlWorktreeOpenRequest(
                $"scm17-api-{Guid.NewGuid():N}",
                fixture.Repo.RootPath,
                targetPath));
        Assert.Equal(HttpStatusCode.OK, openResponse.StatusCode);
        SourceControlWorktreeOpenResponse? opened =
            await openResponse.Content.ReadFromJsonAsync<SourceControlWorktreeOpenResponse>();
        Assert.NotNull(opened);
        Assert.Equal("RequiresAttachmentFlow", opened!.Status);
        Assert.False(opened.IsAttachedToWorkspace);
        Assert.NotNull(opened.Worktree);

        using HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/source-control/worktrees?path={Uri.EscapeDataString(fixture.Repo.RootPath)}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        SourceControlWorktreeListResponse? listed =
            await listResponse.Content.ReadFromJsonAsync<SourceControlWorktreeListResponse>();
        Assert.NotNull(listed);
        Assert.Equal(2, listed!.Worktrees.Count);

        using var removeRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/source-control/worktrees")
        {
            Content = JsonContent.Create(
                new SourceControlWorktreeRemoveRequest(fixture.Repo.RootPath, targetPath)),
        };
        using HttpResponseMessage removeResponse = await client.SendAsync(removeRequest);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        SourceControlWorktreeRemoveResponse? removed =
            await removeResponse.Content.ReadFromJsonAsync<SourceControlWorktreeRemoveResponse>();
        Assert.NotNull(removed);
        Assert.True(removed!.IsSuccess, removed.Message);
        Assert.False(Directory.Exists(targetPath));

        using HttpResponseMessage pruneResponse = await client.PostAsJsonAsync(
            "/api/source-control/worktrees/prune",
            new SourceControlWorktreeRefreshRequest(fixture.Repo.RootPath));
        Assert.Equal(HttpStatusCode.OK, pruneResponse.StatusCode);
        SourceControlWorktreeRefreshResponse? pruned =
            await pruneResponse.Content.ReadFromJsonAsync<SourceControlWorktreeRefreshResponse>();
        Assert.NotNull(pruned);
        Assert.True(pruned!.IsSuccess, pruned.Message);
        Assert.Single(pruned.Worktrees!);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        HttpClient client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", TestOrigin);
        client.DefaultRequestHeaders.Add(SessionTokenHeader, tokenProvider.SessionToken);
        return await Task.FromResult(client);
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int previous;
        do
        {
            previous = Volatile.Read(ref maximum);
            if (value <= previous)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref maximum, value, previous) != previous);
    }

    private static async Task SignalAndWaitAsync(CountdownEvent barrier)
    {
        barrier.Signal();
        bool observed = await Task.Run(() => barrier.Wait(TimeSpan.FromSeconds(15)));
        if (!observed)
        {
            throw new TimeoutException(
                "Operations on different worktrees were serialized; they must stay independent.");
        }
    }

    private static TestGitRepository CreateRepositoryWithBaselineCommit(params string[] baselineFiles)
    {
        var repo = TestGitRepository.Create($"scm17-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "base");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

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

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static void DeleteDirectoryBestEffort(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup; a locked temp directory must not fail the test.
        }
    }

    /// <summary>
    /// Builds the whole SCM service graph against one real SCM-01 runner and
    /// queue, exactly like production composition (structured arguments, one
    /// per-worktree queue).
    /// </summary>
    private sealed class ScmHarness
    {
        private ScmHarness(
            GitRepositoryService repositories,
            GitStageService stages,
            GitCommitService commits,
            GitRemoteService remotes,
            GitPushService pushes,
            GitPullService pulls,
            GitHistoryService history,
            GitConflictService conflicts,
            GitWorktreeService worktrees,
            GitWorktreeMutationService worktreeMutations,
            GitRestoreService restores)
        {
            Repositories = repositories;
            Stages = stages;
            Commits = commits;
            Remotes = remotes;
            Pushes = pushes;
            Pulls = pulls;
            History = history;
            Conflicts = conflicts;
            Worktrees = worktrees;
            WorktreeMutations = worktreeMutations;
            Restores = restores;
        }

        public GitRepositoryService Repositories { get; }

        public GitStageService Stages { get; }

        public GitCommitService Commits { get; }

        public GitRemoteService Remotes { get; }

        public GitPushService Pushes { get; }

        public GitPullService Pulls { get; }

        public GitHistoryService History { get; }

        public GitConflictService Conflicts { get; }

        public GitWorktreeService Worktrees { get; }

        public GitWorktreeMutationService WorktreeMutations { get; }

        public GitRestoreService Restores { get; }

        public static ScmHarness Create()
        {
            var queue = new GitWorktreeMutationQueue();
            var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
            var repositories = new GitRepositoryService(runner);
            var stages = new GitStageService(runner, repositories);
            var commits = new GitCommitService(runner, repositories, stages);
            var remotes = new GitRemoteService(runner, repositories);
            var pushes = new GitPushService(runner, repositories, remotes);
            var pulls = new GitPullService(
                runner,
                repositories,
                remotes,
                new GitPullSafetyTests.FakeDocumentRepository(null),
                new GitPullSafetyTests.RecordingReindexTrigger(),
                new GitPullSafetyTests.TestPullHistoryRecorder());
            var history = new GitHistoryService(runner, repositories);
            var conflicts = new GitConflictService(
                repositories,
                new GitPullSafetyTests.FakeDocumentRepository(null));
            var worktrees = new GitWorktreeService(runner, new RegressionFolderSourceService());
            var worktreeMutations = new GitWorktreeMutationService(runner, worktrees);
            var restores = new GitRestoreService(
                runner,
                repositories,
                new RecoverySnapshotService(new GitRestoreSafetyTests.TestHistoryStorage(5L * 1024 * 1024 * 1024)),
                new GitRestoreSafetyTests.TestRestoreHistoryRecorder(),
                new GitPullSafetyTests.RecordingReindexTrigger(),
                new GitPullSafetyTests.FakeDocumentRepository(null));

            return new ScmHarness(
                repositories,
                stages,
                commits,
                remotes,
                pushes,
                pulls,
                history,
                conflicts,
                worktrees,
                worktreeMutations,
                restores);
        }
    }

    /// <summary>Open-worktree tests need a folder-source seam; nothing is attached.</summary>
    private sealed class RegressionFolderSourceService : IFolderSourceService
    {
        public Task<AttachFolderResult> AttachFolderAsync(string workspaceId, string rawFolderPath) =>
            throw new NotSupportedException("The regression suite never attaches a folder.");

        public Task<IReadOnlyList<FolderSource>> GetFolderSourcesAsync(string workspaceId) =>
            Task.FromResult<IReadOnlyList<FolderSource>>(Array.Empty<FolderSource>());

        public int CountSupportedFiles(string canonicalPath) => 0;
    }

    /// <summary>A disposable repository with one commit, a feature branch and a tag.</summary>
    private sealed class RegressionWorktreeFixture : IDisposable
    {
        private readonly List<string> _targetPaths = new();

        private RegressionWorktreeFixture(TestGitRepository repo)
        {
            Repo = repo;
        }

        public TestGitRepository Repo { get; }

        public static RegressionWorktreeFixture Create(string label)
        {
            TestGitRepository repo = TestGitRepository.Create($"scm17-{label}-{Guid.NewGuid():N}");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
            repo.WriteFile("docs/baseline.md", "baseline");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "branch", "feature-x"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "tag", "v1"));
            return new RegressionWorktreeFixture(repo);
        }

        public string NewTargetPath(string label)
        {
            string path = Path.Combine(Path.GetTempPath(), "scm17", $"{label}-{Guid.NewGuid():N}");
            _targetPaths.Add(path);
            return path;
        }

        public string AddDetachedWorktree(string label)
        {
            string path = NewTargetPath(label);
            Assert.Equal(
                0,
                TestGitRepository.RunGit(Repo.RootPath, "worktree", "add", "-q", "--detach", path));
            return path;
        }

        public void Dispose()
        {
            foreach (string targetPath in _targetPaths)
            {
                DeleteDirectoryBestEffort(targetPath);
            }

            Repo.Dispose();
        }
    }
}
