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
using DocumentIDE.Infrastructure.History;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-12 — Safe Fast-Forward Pull (spec 13 §14.3/§18/§19/§20/§21/§23).
///
/// These tests drive <see cref="GitPullService"/> against real Git repositories
/// and real local <b>bare remotes</b> (no network access), so divergence, a
/// dirty worktree and a conflicted worktree are genuine Git states rather than
/// simulated strings:
/// <list type="bullet">
/// <item>a clean behind-branch fast-forwards to the remote head and refreshes
/// status/documents/index,</item>
/// <item>a diverged history is refused typed with no merge commit and no
/// rebase,</item>
/// <item>a dirty or conflicted worktree is refused typed <b>before</b> the pull
/// runs and every file stays byte-identical,</item>
/// <item>the invocation is <c>pull --ff-only --no-rebase</c> and nothing
/// else,</item>
/// <item>failures (no upstream, index lock, Git missing, auth, unreachable
/// remote, vanished worktree) stay typed.</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitPullSafetyTests.cs</c>;
/// the frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/PullDialog.test.tsx</c>
/// (CAN-05 batch-B ruling, same as SCM-10/SCM-11/SCM-13).
/// </summary>
public sealed class GitPullSafetyTests
{
    // ---------------------------------------------------------------------
    // Step 2 — fast-forward-only pull (mandatory first failing test)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Clean fast-forward pull
    /// succeeds." The bare remote is advanced by one commit from a separate
    /// clone; pulling the clean behind-branch moves HEAD to the remote head,
    /// creates no merge commit, leaves a single-parent commit and refreshes the
    /// status/documents/index (spec 13 §14.3/§19).
    /// </summary>
    [Fact]
    public async Task CleanFastForwardPullSucceeds()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/pulled.md", "pulled v1", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));

            string remoteHead = TestGitRepository
                .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
                .Trim();
            string headBefore = GitFetchTests.Head(repo);

            var recording = new RecordingGitProcessRunner(
                new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
            Harness harness = CreateHarness(recording);

            GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.Succeeded, result.Outcome);
            Assert.True(result.IsSuccess);
            Assert.Null(result.Failure);
            Assert.Equal(headBefore, result.PreviousHead);
            Assert.Equal(remoteHead, result.Head);
            Assert.Equal(remoteHead, GitFetchTests.Head(repo));

            // The working tree really advanced.
            Assert.Equal(
                "pulled v1",
                File.ReadAllText(Path.Combine(repo.RootPath, "docs", "pulled.md")));

            // No merge commit was created: zero merge commits and a single parent.
            Assert.Equal(
                "0",
                TestGitRepository
                    .RunGitCaptureUtf8(repo.RootPath, "rev-list", "--merges", "--count", "HEAD")
                    .Trim());
            Assert.Equal(
                2,
                TestGitRepository
                    .RunGitCaptureUtf8(repo.RootPath, "rev-list", "--parents", "-n", "1", "HEAD")
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Length);

            // The refresh ran: status was re-read and the changed document is reported.
            Assert.NotNull(result.Refresh);
            Assert.True(result.Refresh!.StatusRefreshed);
            Assert.Contains("docs/pulled.md", result.Refresh.ChangedPaths);
            Assert.NotNull(result.Remote);
            Assert.Equal(0, result.Remote!.Behind);

            Assert.Contains(
                recording.Requests,
                request => request.Arguments.SequenceEqual(
                    new[] { "pull", "--ff-only", "--no-rebase" }));
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    // ---------------------------------------------------------------------
    // Step 3 — divergence is a typed blocking state, never merge/rebase
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 2: "Diverged history is rejected without merge/rebase."
    /// Both sides advance independently; the pull is refused typed, HEAD and the
    /// local commit survive byte-for-byte, no merge commit is created and no
    /// rebase rewrites history (spec 13 §14.3).
    /// </summary>
    [Fact]
    public async Task DivergedHistoryIsRefusedWithoutMergeOrRebase()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            // The remote advances by one commit.
            GitFetchTests.CommitFile(clone, "docs/remote-side.md", "remote", "remote side");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));

            // The local branch advances by a different commit: genuine divergence.
            GitFetchTests.CommitFile(repo.RootPath, "docs/local-side.md", "local", "local side");
            string headBefore = GitFetchTests.Head(repo);
            string logBefore = TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "--oneline", "HEAD");
            IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);
            string statusBefore = GitFetchTests.WorkingTreeStatus(repo.RootPath);

            Harness harness = CreateHarness();
            GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.Diverged, result.Outcome);
            Assert.True(result.IsBlocking);
            Assert.False(result.IsSuccess);
            Assert.Equal("Diverged", result.ErrorCode);
            Assert.Null(result.Refresh);
            Assert.Contains("diverged", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No automatic merge/rebase was performed", result.Message);

            // Nothing changed: same HEAD, same history, same working tree.
            Assert.Equal(headBefore, GitFetchTests.Head(repo));
            Assert.Equal(headBefore, result.PreviousHead);
            Assert.Equal(headBefore, result.Head);
            Assert.Equal(logBefore, TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "--oneline", "HEAD"));
            Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));
            Assert.Equal(statusBefore, GitFetchTests.WorkingTreeStatus(repo.RootPath));
            Assert.False(File.Exists(Path.Combine(repo.RootPath, "docs", "remote-side.md")));

            // No merge commit and no rebase.
            Assert.Equal(
                "0",
                TestGitRepository
                    .RunGitCaptureUtf8(repo.RootPath, "rev-list", "--merges", "--count", "HEAD")
                    .Trim());
            Assert.DoesNotContain(
                "rebase",
                TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "reflog", "--format=%gs"),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    // ---------------------------------------------------------------------
    // Step 1 — dirty/conflicted preflight (no Git pull may run)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 3a: "Dirty worktree is not silently overwritten." A tracked
    /// file is modified locally while the remote is ahead; the pull is refused
    /// typed <b>before</b> it runs and every file stays byte-identical (path,
    /// length, mtime, SHA-256), with the porcelain status unchanged.
    /// </summary>
    [Fact]
    public async Task DirtyWorktreeRefusalLeavesEveryFileByteIdentical()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote-side.md", "remote", "remote side");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));

            // A genuine local working-tree change (and a staged one) must block.
            File.WriteAllText(Path.Combine(repo.RootPath, "docs", "baseline.md"), "locally edited");
            repo.WriteFile("docs/staged.md", "staged");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/staged.md"));

            string headBefore = GitFetchTests.Head(repo);
            IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);
            string statusBefore = GitFetchTests.WorkingTreeStatus(repo.RootPath);

            Harness harness = CreateHarness();
            GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.DirtyWorktree, result.Outcome);
            Assert.True(result.IsBlocking);
            Assert.Equal("DirtyWorktree", result.ErrorCode);
            Assert.Null(result.Refresh);
            Assert.Contains("docs/baseline.md", result.Message);
            Assert.Contains("docs/staged.md", result.Message);

            // Pull never ran.
            Assert.DoesNotContain(
                harness.Runner.Requests,
                request => request.Arguments.Count > 0 && request.Arguments[0] == "pull");

            // Every file is byte-identical and the index is untouched.
            Assert.Equal(headBefore, GitFetchTests.Head(repo));
            Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));
            Assert.Equal(statusBefore, GitFetchTests.WorkingTreeStatus(repo.RootPath));
            Assert.Equal("locally edited", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "baseline.md")));
            Assert.False(File.Exists(Path.Combine(repo.RootPath, "docs", "remote-side.md")));
            Assert.Empty(harness.History.Records);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>
    /// Required test 3b: "Conflicted worktree is not silently overwritten." A
    /// genuine unmerged state (created by a real conflicting merge in the
    /// fixture) is refused typed before any pull; the conflict state, HEAD, and
    /// every file survive unchanged (spec 13 §21).
    /// </summary>
    [Fact]
    public async Task ConflictedWorktreeIsRefusedAndConflictStateIsPreserved()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            // Both sides change the same file: a genuine conflict.
            GitFetchTests.CommitFile(clone, "docs/baseline.md", "remote edit", "remote edit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            GitFetchTests.CommitFile(repo.RootPath, "docs/baseline.md", "local edit", "local edit");

            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "fetch", "-q", "origin"));
            // The fixture creates the unmerged state with raw Git (not the code under test).
            Assert.NotEqual(
                0,
                TestGitRepository.RunGit(repo.RootPath, "merge", "origin/main"));

            string headBefore = GitFetchTests.Head(repo);
            IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);
            string statusBefore = GitFetchTests.WorkingTreeStatus(repo.RootPath);
            Assert.Contains("UU", statusBefore);

            Harness harness = CreateHarness();
            GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.ConflictedWorktree, result.Outcome);
            Assert.True(result.IsBlocking);
            Assert.Equal("ConflictedWorktree", result.ErrorCode);
            Assert.Contains("docs/baseline.md", result.Message);

            Assert.DoesNotContain(
                harness.Runner.Requests,
                request => request.Arguments.Count > 0 && request.Arguments[0] == "pull");

            Assert.Equal(headBefore, GitFetchTests.Head(repo));
            Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));
            Assert.Equal(statusBefore, GitFetchTests.WorkingTreeStatus(repo.RootPath));
            Assert.Contains("UU", GitFetchTests.WorkingTreeStatus(repo.RootPath));
            Assert.Empty(harness.History.Records);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    // ---------------------------------------------------------------------
    // Step 4 — status/document/index refresh after a successful pull
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 4: "Status/search/index refresh after successful pull."
    /// The status is re-read (behind is 0 afterwards), the affected document is
    /// handed to the existing reindex trigger (the index/search seam) and the
    /// operation is recorded in Document IDE History with the changed path.
    /// </summary>
    [Fact]
    public async Task SuccessfulPullRefreshesStatusDocumentsAndIndex()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(
                clone,
                "docs/refresh.md",
                "refreshed content",
                "add refresh doc");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));

            string documentPath = Path.Combine(repo.RootPath, "docs", "refresh.md");
            var document = new Document
            {
                Id = "doc-refresh",
                CurrentPath = documentPath,
                Filename = "refresh.md",
                Extension = ".md",
            };
            var documents = new FakeDocumentRepository(
                new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase)
                {
                    [documentPath] = document,
                });

            Harness harness = CreateHarness(documents: documents);
            GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.Succeeded, result.Outcome);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Refresh);
            Assert.True(result.Refresh!.StatusRefreshed);
            Assert.Equal(new[] { "docs/refresh.md" }, result.Refresh.ChangedPaths);
            Assert.Equal(1, result.Refresh.ReindexedDocuments);
            Assert.Equal(new[] { "doc-refresh" }, harness.Reindex.DocumentIds);
            Assert.True(result.Refresh.HistoryRecorded);

            // The refreshed status is the post-pull state, not the preflight one.
            Assert.NotNull(result.Refresh.Status);
            Assert.Equal(0, result.Refresh.Status!.Snapshot!.Behind);
            Assert.Equal(0, result.Refresh.Status.Snapshot.Ahead);
            Assert.Equal(0, result.Remote!.Behind);

            GitPullHistoryRecord record = Assert.Single(harness.History.Records);
            Assert.Equal(GitPullOutcome.Succeeded.ToString(), record.Outcome);
            Assert.Equal(result.PreviousHead, record.PreviousHead);
            Assert.Equal(result.Head, record.Head);
            Assert.Contains("docs/refresh.md", record.ChangedPaths);
            Assert.Equal(
                Path.GetFullPath(repo.RootPath),
                Path.GetFullPath(record.WorktreeRoot),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    // ---------------------------------------------------------------------
    // §14.3/§19/§23 — invocation shape, worktree root, serialization
    // ---------------------------------------------------------------------

    /// <summary>
    /// The pull invocation is exactly <c>pull --ff-only --no-rebase</c> and no
    /// recorded invocation carries a merge/rebase/force/scope-broadening token
    /// or a branch/refspec argument, even when called from a nested directory
    /// (spec 13 §14.3/§18/§23).
    /// </summary>
    [Fact]
    public async Task PullCarriesOnlyFastForwardNoRebaseArguments()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/next.md", "next", "next");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string nested = Path.Combine(repo.RootPath, "docs");

            Harness harness = CreateHarness();
            GitPullResult result = await harness.Service.PullAsync(nested);
            Assert.Equal(GitPullOutcome.Succeeded, result.Outcome);

            GitProcessRequest pullRequest = Assert.Single(
                harness.Runner.Requests,
                request => request.Arguments.Count > 0 && request.Arguments[0] == "pull");
            Assert.Equal(
                new[] { "pull", "--ff-only", "--no-rebase" },
                pullRequest.Arguments);
            Assert.Equal(
                Path.GetFullPath(repo.RootPath),
                Path.GetFullPath(pullRequest.WorkingDirectory),
                StringComparer.OrdinalIgnoreCase);

            string[] forbidden =
            {
                "--rebase",
                "--no-ff",
                "--merge",
                "--force",
                "-f",
                "--all",
                "--tags",
                "--prune",
                "merge",
                "rebase",
            };
            foreach (GitProcessRequest request in harness.Runner.Requests)
            {
                foreach (string argument in request.Arguments)
                {
                    Assert.DoesNotContain(argument, forbidden);
                }
            }
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>
    /// Pull mutates the working tree, so concurrent pulls against the same
    /// worktree serialize on the SCM-01 per-worktree mutation queue, including a
    /// call resolving through a nested subdirectory (spec 13 §19).
    /// </summary>
    [Fact]
    public async Task PullRunsThroughThePerWorktreeMutationQueue()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/queued.md", "queued", "queued");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string nested = Path.Combine(repo.RootPath, "docs");

            var queue = new GitWorktreeMutationQueue();
            var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
            var repositories = new GitRepositoryService(runner);
            var service = new GitPullService(
                runner,
                repositories,
                new GitRemoteService(runner, repositories),
                new FakeDocumentRepository(null),
                new RecordingReindexTrigger(),
                new TestPullHistoryRecorder());

            GitPullResult[] results = await Task.WhenAll(
                service.PullAsync(repo.RootPath),
                service.PullAsync(nested),
                service.PullAsync(repo.RootPath));

            Assert.All(results, result => Assert.Equal(GitPullOutcome.Succeeded, result.Outcome));
            Assert.Equal(1, queue.MaxObservedConcurrency);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    // ---------------------------------------------------------------------
    // §20 — typed non-success outcomes
    // ---------------------------------------------------------------------

    /// <summary>A branch with no configured upstream is typed and runs no Git at all (spec 13 §14.2).</summary>
    [Fact]
    public async Task PullWithNoUpstreamIsTypedAndRunsNoGit()
    {
        using var repo = GitFetchTests.CreateRepositoryWithoutUpstream();

        Harness harness = CreateHarness();
        GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

        Assert.Equal(GitPullOutcome.NoUpstream, result.Outcome);
        Assert.Equal("NoUpstream", result.ErrorCode);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Failure);
        Assert.DoesNotContain(
            harness.Runner.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "pull");
        Assert.Empty(harness.History.Records);
    }

    /// <summary>An unavailable index lock is the typed <c>IndexLocked</c> state and nothing is applied (spec 13 §19/§20).</summary>
    [Fact]
    public async Task PullWhenIndexIsLockedIsTypedAndChangesNothing()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        var lockFailure = new GitProcessResult(
            GitProcessStatus.Failed,
            128,
            string.Empty,
            "fatal: Unable to create 'C:/repo/.git/index.lock': File exists.\n\n" +
            "Another git process seems to be running in this repository.",
            GitProcessErrorKind.None);

        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var recording = new RecordingGitProcessRunner(
            new ScriptedGitProcessRunner(inner, ("pull", lockFailure)));
        Harness harness = CreateHarness(recording);

        string headBefore = GitFetchTests.Head(repo);
        IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);

        GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

        Assert.Equal(GitPullOutcome.IndexLocked, result.Outcome);
        Assert.Equal("IndexLocked", result.ErrorCode);
        Assert.NotNull(result.Failure);
        Assert.Equal(headBefore, GitFetchTests.Head(repo));
        Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));
    }

    /// <summary>
    /// A remote that requires credentials fails promptly typed from Git's
    /// diagnostics and no credential material is surfaced (spec 13 §14.3/§20/§23).
    /// </summary>
    [Fact]
    public async Task PullAuthenticationFailureIsTypedAndRedacted()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        var authFailure = new GitProcessResult(
            GitProcessStatus.Failed,
            128,
            string.Empty,
            "fatal: unable to access 'https://alice:s3cr3t-pw@example.invalid/repo.git/': " +
            "The requested URL returned error: 401",
            GitProcessErrorKind.None);

        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var recording = new RecordingGitProcessRunner(
            new ScriptedGitProcessRunner(inner, ("pull", authFailure)));
        Harness harness = CreateHarness(recording);

        GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

        Assert.Equal(GitPullOutcome.AuthenticationRequired, result.Outcome);
        Assert.Equal("AuthenticationRequired", result.ErrorCode);
        Assert.NotNull(result.Failure);

        string surfaced = string.Join(
            "\n",
            result.Message,
            result.Failure!.ErrorMessage,
            result.Failure.StandardOutput,
            result.Failure.StandardError);
        Assert.DoesNotContain("s3cr3t-pw", surfaced);
        Assert.DoesNotContain("alice:", surfaced);
    }

    /// <summary>A remote that cannot be reached is typed, not a generic failure (spec 13 §20).</summary>
    [Fact]
    public async Task PullHostUnreachableIsTyped()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);

        // Remove the local bare remote: a genuine "cannot read from remote" failure.
        GitFetchTests.DeleteDirectoryBestEffort(remotePath);
        string headBefore = GitFetchTests.Head(repo);

        Harness harness = CreateHarness();
        GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

        Assert.Equal(GitPullOutcome.HostUnreachable, result.Outcome);
        Assert.Equal("HostUnreachable", result.ErrorCode);
        Assert.Equal(headBefore, GitFetchTests.Head(repo));
    }

    /// <summary>A machine without discoverable Git is typed, never an exception (spec 13 §5/§20).</summary>
    [Fact]
    public async Task PullWhenGitUnavailableIsTypedGitUnavailable()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        var runner = new UnavailableGitProcessRunner();
        var service = new GitPullService(
            runner,
            new GitRepositoryService(runner),
            new GitRemoteService(runner, new GitRepositoryService(runner)),
            new FakeDocumentRepository(null),
            new RecordingReindexTrigger(),
            new TestPullHistoryRecorder());

        GitPullResult result = await service.PullAsync(repo.RootPath);

        Assert.Equal(GitPullOutcome.GitUnavailable, result.Outcome);
        Assert.Equal("GitUnavailable", result.ErrorCode);
    }

    /// <summary>A worktree that disappears while the operation is running is typed (spec 13 §20 failure/edge cases).</summary>
    [Fact]
    public async Task PullOnDisappearedWorktreeIsTyped()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        string root = repo.RootPath;

        // The worktree is moved away at the exact moment Git is asked to pull, so
        // the disappearance happens during the operation, not before it.
        string moved = root + "-moved-" + Guid.NewGuid().ToString("N");
        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var recording = new RecordingGitProcessRunner(new MoveBeforePullRunner(inner, root, moved));
        try
        {
            Harness harness = CreateHarness(recording);

            GitPullResult result = await harness.Service.PullAsync(root);

            Assert.Equal(GitPullOutcome.WorktreeMissing, result.Outcome);
            Assert.Equal("WorktreeMissing", result.ErrorCode);
            Assert.False(result.IsSuccess);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(moved);
        }
    }

    /// <summary>A path outside any repository is typed <c>NoRepository</c> and Git never pulls (spec 13 §5).</summary>
    [Fact]
    public async Task PullOutsideRepositoryIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"scm12-norepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            Harness harness = CreateHarness();
            GitPullResult result = await harness.Service.PullAsync(folder);

            Assert.Equal(GitPullOutcome.NoRepository, result.Outcome);
            Assert.Equal("NoRepository", result.ErrorCode);
            Assert.DoesNotContain(
                harness.Runner.Requests,
                request => request.Arguments.Count > 0 && request.Arguments[0] == "pull");
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(folder);
        }
    }

    /// <summary>
    /// SCM-12 — a pull history record round-trips through the same JSONL store
    /// the other Source Control mutations use (spec 13 §14.3 step 4/§24).
    /// </summary>
    [Fact]
    public async Task PullHistoryRecordRoundTripsThroughTheSharedJsonlStore()
    {
        string root = Path.Combine(Path.GetTempPath(), $"scm12-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new JsonlHistoryStore(root);
            var record = new GitPullHistoryRecord(
                "scm-pull-roundtrip",
                GitPullOutcome.Succeeded.ToString(),
                @"C:\workspace",
                "1111111111111111111111111111111111111111",
                "2222222222222222222222222222222222222222",
                new[] { "docs/a.md" },
                DateTimeOffset.UtcNow);

            await store.RecordAsync(record);
            IReadOnlyList<GitPullHistoryRecord> records =
                await store.GetGitPullRecordsAsync("scm-pull-roundtrip");

            GitPullHistoryRecord loaded = Assert.Single(records);
            Assert.Equal(record.OperationId, loaded.OperationId);
            Assert.Equal(record.Outcome, loaded.Outcome);
            Assert.Equal(record.WorktreeRoot, loaded.WorktreeRoot);
            Assert.Equal(record.PreviousHead, loaded.PreviousHead);
            Assert.Equal(record.Head, loaded.Head);
            Assert.Equal(new[] { "docs/a.md" }, loaded.ChangedPaths);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(root);
        }
    }

    /// <summary>Paths with spaces, Unicode and shell metacharacters are passed structurally (spec 13 §18/§23).</summary>
    [Fact]
    public async Task PullHandlesPathsWithSpacesUnicodeAndSpecialCharacters()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(
            out string remotePath,
            directoryName: $"scm12 repo \u00fc \u6771 & (# {suffix}",
            remoteDirectoryName: $"scm12 remote \u00fc \u6771 & (# {suffix}.git");
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/unicode \u00e9.md", "content", "unicode path");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string remoteHead = TestGitRepository
                .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
                .Trim();

            Harness harness = CreateHarness();
            GitPullResult result = await harness.Service.PullAsync(repo.RootPath);

            Assert.Equal(GitPullOutcome.Succeeded, result.Outcome);
            Assert.Equal(remoteHead, GitFetchTests.Head(repo));
            Assert.True(File.Exists(Path.Combine(repo.RootPath, "docs", "unicode \u00e9.md")));
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    internal static Harness CreateHarness(
        RecordingGitProcessRunner? runner = null,
        IDocumentRepository? documents = null,
        TestPullHistoryRecorder? history = null,
        RecordingReindexTrigger? reindex = null)
    {
        var queue = new GitWorktreeMutationQueue();
        RecordingGitProcessRunner effective =
            runner ?? new RecordingGitProcessRunner(new GitProcessRunner(GitProcessRunnerOptions.Default, queue));
        var repositories = new GitRepositoryService(effective);
        var remotes = new GitRemoteService(effective, repositories);
        var trigger = reindex ?? new RecordingReindexTrigger();
        var recorder = history ?? new TestPullHistoryRecorder();
        var service = new GitPullService(
            effective,
            repositories,
            remotes,
            documents ?? new FakeDocumentRepository(null),
            trigger,
            recorder);

        return new Harness(service, effective, trigger, recorder);
    }

    internal sealed record Harness(
        GitPullService Service,
        RecordingGitProcessRunner Runner,
        RecordingReindexTrigger Reindex,
        TestPullHistoryRecorder History);

    /// <summary>Moves the worktree away exactly when Git is asked to pull, simulating a mid-operation disappearance.</summary>
    private sealed class MoveBeforePullRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;
        private readonly string _worktreeRoot;
        private readonly string _movedRoot;

        public MoveBeforePullRunner(IGitProcessRunner inner, string worktreeRoot, string movedRoot)
        {
            _inner = inner;
            _worktreeRoot = worktreeRoot;
            _movedRoot = movedRoot;
        }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Arguments.Count > 0 && request.Arguments[0] == "pull")
            {
                try
                {
                    Directory.Move(_worktreeRoot, _movedRoot);
                }
                catch (Exception)
                {
                    // Best effort: the assertion below reports the typed outcome.
                }
            }

            return _inner.RunAsync(request, cancellationToken);
        }
    }

    internal sealed class RecordingReindexTrigger : IBatchApplyReindexTrigger
    {
        public List<string> DocumentIds { get; } = new();

        public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    internal sealed class TestPullHistoryRecorder : IGitPullHistoryRecorder
    {
        public List<GitPullHistoryRecord> Records { get; } = new();

        public Task RecordAsync(GitPullHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeDocumentRepository : IDocumentRepository
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
}

/// <summary>
/// SCM-12 — privileged Source Control API surface for the conservative Pull
/// (spec 13 §14.3/§18/§20).
///
/// The route requires the localhost session token like every other Source
/// Control route. A fast-forward returns the typed success plus the refreshed
/// status; divergence is HTTP 200 with the typed blocking state (so the pane can
/// render it) and never a merge/rebase. A missing path is a typed 400 and Git
/// never runs.
/// </summary>
public sealed class GitPullApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GitPullApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>The pull route fast-forwards the branch and returns the refreshed state (spec 13 §14.3).</summary>
    [Fact]
    public async Task PullEndpointFastForwardsAndRefreshes()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/api-pull.md", "api pulled", "api remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string remoteHead = TestGitRepository
                .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
                .Trim();
            string headBefore = GitFetchTests.Head(repo);

            using var client = await CreateAuthorizedClientAsync();
            using var response = await client.PostAsJsonAsync(
                "/api/source-control/pull",
                new SourceControlPullRequest(repo.RootPath));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SourceControlPullResponse? body =
                await response.Content.ReadFromJsonAsync<SourceControlPullResponse>();

            Assert.NotNull(body);
            Assert.Equal("Succeeded", body!.Outcome);
            Assert.True(body.IsSuccess);
            Assert.False(body.IsBlocking);
            Assert.Null(body.ErrorCode);
            Assert.Equal(headBefore, body.PreviousHead);
            Assert.Equal(remoteHead, body.Head);
            Assert.Equal(remoteHead, GitFetchTests.Head(repo));
            Assert.Contains("docs/api-pull.md", body.ChangedPaths);
            Assert.True(body.StatusRefreshed);
            Assert.NotNull(body.Status);
            Assert.Equal(0, body.Status!.Behind);
            Assert.NotNull(body.Remote);
            Assert.Equal(0, body.Remote!.Behind);

            // No merge commit from the API path either.
            Assert.Equal(
                "0",
                TestGitRepository
                    .RunGitCaptureUtf8(repo.RootPath, "rev-list", "--merges", "--count", "HEAD")
                    .Trim());
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>Divergence is a typed HTTP 200 blocking state and no merge/rebase runs (spec 13 §14.3).</summary>
    [Fact]
    public async Task PullEndpointReportsDivergedAsBlocking()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote-api.md", "remote", "remote api side");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            GitFetchTests.CommitFile(repo.RootPath, "docs/local-api.md", "local", "local api side");
            string headBefore = GitFetchTests.Head(repo);

            using var client = await CreateAuthorizedClientAsync();
            using var response = await client.PostAsJsonAsync(
                "/api/source-control/pull",
                new SourceControlPullRequest(repo.RootPath));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SourceControlPullResponse? body =
                await response.Content.ReadFromJsonAsync<SourceControlPullResponse>();

            Assert.NotNull(body);
            Assert.Equal("Diverged", body!.Outcome);
            Assert.False(body.IsSuccess);
            Assert.True(body.IsBlocking);
            Assert.Equal("Diverged", body.ErrorCode);
            Assert.Contains("No automatic merge/rebase was performed", body.Message);
            Assert.Equal(headBefore, GitFetchTests.Head(repo));
            Assert.Equal(
                "0",
                TestGitRepository
                    .RunGitCaptureUtf8(repo.RootPath, "rev-list", "--merges", "--count", "HEAD")
                    .Trim());
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    /// <summary>A missing request body/path is a typed 400 and no Git runs (spec 13 §18).</summary>
    [Fact]
    public async Task PullEndpointRequiresPath()
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/pull",
            new SourceControlPullRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", payload.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// The pull route is guarded exactly like the other privileged Source Control
    /// routes: a missing token is 401, a hostile origin is 403 and a wrong token
    /// is 401, and no unauthenticated request reaches Git (spec 13 §18).
    /// </summary>
    [Fact]
    public async Task PullRouteRejectsMissingInvalidTokenAndHostileOrigin()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/guarded.md", "guarded", "guarded");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
            string headBefore = GitFetchTests.Head(repo);

            using (var anonymous = _factory.CreateClient())
            {
                using var response = await anonymous.PostAsJsonAsync(
                    "/api/source-control/pull",
                    new SourceControlPullRequest(repo.RootPath));
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }

            using (var scope = _factory.Services.CreateScope())
            {
                var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

                using var hostileOrigin = _factory.CreateClient();
                hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
                hostileOrigin.DefaultRequestHeaders.Add(
                    "X-DocumentIDE-Session-Token",
                    tokenProvider.SessionToken);
                using var hostileResponse = await hostileOrigin.PostAsJsonAsync(
                    "/api/source-control/pull",
                    new SourceControlPullRequest(repo.RootPath));
                Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

                using var wrongToken = _factory.CreateClient();
                wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
                wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
                using var wrongResponse = await wrongToken.PostAsJsonAsync(
                    "/api/source-control/pull",
                    new SourceControlPullRequest(repo.RootPath));
                Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
            }

            // No unauthenticated request reached Git: HEAD did not move.
            Assert.Equal(headBefore, GitFetchTests.Head(repo));
            Assert.False(File.Exists(Path.Combine(repo.RootPath, "docs", "guarded.md")));
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }
    }

    private Task<HttpClient> CreateAuthorizedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return Task.FromResult(client);
    }
}
