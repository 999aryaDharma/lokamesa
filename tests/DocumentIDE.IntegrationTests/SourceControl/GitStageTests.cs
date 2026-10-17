using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-06 — Git Stage &amp; Unstage (spec 13 §9.4/§10/§19/§23).
///
/// These tests drive <see cref="GitStageService"/> against real Git repositories
/// through the SCM-01 controlled process boundary and the SCM-01 per-worktree
/// mutation queue:
/// <list type="bullet">
/// <item>staging writes the real Git index only — the working tree is never
/// rewritten and Document IDE keeps no staging snapshot of its own,</item>
/// <item>unstaging preserves working-tree edits,</item>
/// <item>stage-all/unstage-all match the expected Git state,</item>
/// <item>paths with spaces, Unicode and wildcard-like characters stay exact,</item>
/// <item>path containment rejects escapes before Git runs,</item>
/// <item>failures are typed and inspectable (missing Git, gone worktree, index
/// lock contention), and</item>
/// <item>every mutation is serialized per worktree and re-reads status.</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitStageTests.cs</c>
/// (no <c>DocumentIDE.UnitTests</c> project exists in this repository); the
/// frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/ChangeActions.test.tsx</c>
/// (CAN-05 batch-B ruling).
/// </summary>
public sealed class GitStageTests
{
    // ---------------------------------------------------------------------
    // Step 1 — Stage single file (first failing test)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Stage updates index state only."
    /// Staging writes the index; the working tree file is untouched and the
    /// real Git index remains the only staging state (spec 13 §10/§24).
    /// </summary>
    [Fact]
    public async Task StageUpdatesIndexStateOnly()
    {
        string[] files = { "docs/a.md", "docs/b.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        string aPath = repo.WriteFile("docs/a.md", "a v2");
        repo.WriteFile("docs/b.md", "b v2");
        string contentBefore = File.ReadAllText(aPath);

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.StageAsync(repo.RootPath, "docs/a.md");

        Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
        Assert.Null(result.Failure);

        // Step 4: the mutation itself returns the re-read status.
        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(result.Status);
        Assert.True(refreshed.IsAvailable);

        GitStatusEntry staged = Assert.Single(refreshed.Snapshot!.Entries.Where(entry => entry.HasStagedChange));
        Assert.Equal("docs/a.md", staged.Path);
        Assert.Equal(GitChangeKind.Modified, staged.IndexChange);
        Assert.Equal(GitChangeKind.Unmodified, staged.WorkingTreeChange);

        GitStatusEntry unstaged = Assert.Single(refreshed.Snapshot.Entries.Where(entry => entry.HasWorkingTreeChange));
        Assert.Equal("docs/b.md", unstaged.Path);

        // Index state only: the working tree content was not rewritten.
        Assert.Equal(contentBefore, File.ReadAllText(aPath));

        // The real Git index is the only staging state (spec 13 §10/§24).
        Assert.Equal(
            "docs/a.md",
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
        Assert.Equal(
            "docs/b.md",
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--name-only").Trim());
    }

    // ---------------------------------------------------------------------
    // Step 2 — Unstage single file
    // ---------------------------------------------------------------------

    /// <summary>Required test 2: "Unstage preserves working-tree edits."</summary>
    [Fact]
    public async Task UnstagePreservesWorkingTreeEdits()
    {
        string[] files = { "docs/a.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        string aPath = repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string contentBefore = File.ReadAllText(aPath);

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.UnstageAsync(repo.RootPath, "docs/a.md");

        Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(result.Status);
        Assert.True(refreshed.IsAvailable);

        Assert.Empty(refreshed.Snapshot!.Entries.Where(entry => entry.HasStagedChange));
        GitStatusEntry unstaged = Assert.Single(refreshed.Snapshot.Entries.Where(entry => entry.HasWorkingTreeChange));
        Assert.Equal("docs/a.md", unstaged.Path);
        Assert.Equal(GitChangeKind.Modified, unstaged.WorkingTreeChange);

        // Unstaging restores the index side to HEAD; the working-tree edit survives.
        Assert.Equal(contentBefore, File.ReadAllText(aPath));
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task UnstageNewlyAddedFileReturnsItToUntrackedWithoutDeletingIt()
    {
        using var repo = CreateRepositoryWithBaselineCommit(Array.Empty<string>());
        string newPath = repo.WriteFile("docs/new.md", "brand new");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/new.md"));

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.UnstageAsync(repo.RootPath, "docs/new.md");

        Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(result.Status);
        Assert.Empty(refreshed.Snapshot!.Entries.Where(entry => entry.HasStagedChange));

        GitStatusEntry untracked = Assert.Single(refreshed.Snapshot.Entries.Where(entry => entry.IsUntracked));
        Assert.Equal("docs/new.md", untracked.Path);
        Assert.Equal("brand new", File.ReadAllText(newPath));
    }

    // ---------------------------------------------------------------------
    // Step 3 — Stage All / Unstage All
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 3: "Stage All and Unstage All match expected Git state."
    /// Stage All covers modified + deleted + untracked-but-not-ignored files in
    /// the active worktree only; Unstage All returns exactly those to their
    /// former state without touching the working tree (spec 13 §10/§11.2).
    /// </summary>
    [Fact]
    public async Task StageAllAndUnstageAllMatchExpectedGitState()
    {
        string[] files = { "docs/modified.md", "docs/deleted.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile(".gitignore", "docs/ignored.md\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", ".gitignore"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "ignore"));
        repo.WriteFile("docs/modified.md", "modified v2");
        File.Delete(Path.Combine(repo.RootPath, "docs", "deleted.md"));
        repo.WriteFile("docs/untracked \u00e9.md", "untracked");
        repo.WriteFile("docs/ignored.md", "ignored");

        (GitStageService service, _) = CreateService();
        GitStageResult stageAll = await service.StageAllAsync(repo.RootPath);

        Assert.Equal(GitStageOutcome.Succeeded, stageAll.Outcome);
        Assert.True(Assert.IsType<GitStatusResult>(stageAll.Status).IsAvailable);
        Assert.Equal(
            new[] { "docs/deleted.md", "docs/modified.md", "docs/untracked \u00e9.md" },
            StagedNames(repo).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Empty(UnstagedNames(repo));
        Assert.Empty(UntrackedNames(repo));

        GitStageResult unstageAll = await service.UnstageAllAsync(repo.RootPath);

        Assert.Equal(GitStageOutcome.Succeeded, unstageAll.Outcome);
        Assert.True(Assert.IsType<GitStatusResult>(unstageAll.Status).IsAvailable);
        Assert.Empty(StagedNames(repo));
        Assert.Equal(
            new[] { "docs/deleted.md", "docs/modified.md" },
            UnstagedNames(repo).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "docs/untracked \u00e9.md" }, UntrackedNames(repo).ToArray());

        // Ignored files are never silently included by Stage All.
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "ls-files", "--cached", "--", "docs/ignored.md").Trim());
        Assert.Equal("ignored", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "ignored.md")));
    }

    // ---------------------------------------------------------------------
    // Required test 4 — spaces, Unicode and wildcard-like path characters
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 4: "Path names with spaces and Unicode remain correct."
    /// Path arguments are literal data (spec 13 §23): a <c>[</c> in a filename
    /// must stage exactly that file, never a wildcard match, and the stage and
    /// unstage invocations must treat the reported status path verbatim.
    /// </summary>
    [Fact]
    public async Task PathsWithSpacesAndUnicodeRemainCorrect()
    {
        string spaced = "docs/my notes \u00e9\u00e0\u00fc.md";
        using var repo = CreateRepositoryWithBaselineCommit(
            new[] { spaced },
            $"scm06 repo \u00fc (v2) {Guid.NewGuid():N}");
        repo.WriteFile(spaced, "edited");
        string bracket = "docs/notes[1].md";
        string globLookalike = "docs/notes1.md";
        File.WriteAllText(Path.Combine(repo.RootPath, "docs", "notes[1].md"), "literal brackets");
        repo.WriteFile(globLookalike, "glob target");

        (GitStageService service, _) = CreateService();
        GitStageResult spacedResult = await service.StageAsync(repo.RootPath, spaced);
        Assert.Equal(GitStageOutcome.Succeeded, spacedResult.Outcome);
        GitStageResult bracketResult = await service.StageAsync(repo.RootPath, bracket);
        Assert.Equal(GitStageOutcome.Succeeded, bracketResult.Outcome);

        Assert.Equal(
            new[] { spaced, bracket },
            StagedNames(repo).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        // The wildcard look-alike was not silently staged by the bracket filename.
        Assert.Empty(UnstagedNames(repo));
        Assert.Equal(new[] { globLookalike }, UntrackedNames(repo).ToArray());

        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(bracketResult.Status);
        Assert.Equal(
            new[] { spaced, bracket },
            refreshed.Snapshot!.Entries
                .Where(entry => entry.HasStagedChange)
                .Select(entry => entry.Path)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());

        // Unstage uses the same literal pathspec semantics.
        GitStageResult unstageResult = await service.UnstageAsync(repo.RootPath, bracket);
        Assert.Equal(GitStageOutcome.Succeeded, unstageResult.Outcome);
        Assert.Equal(new[] { spaced }, StagedNames(repo).ToArray());
        Assert.Empty(UnstagedNames(repo));
        Assert.Equal(
            new[] { globLookalike, bracket },
            UntrackedNames(repo).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    // ---------------------------------------------------------------------
    // Step 5 — Path containment (reject escapes before Git runs)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Escaping, non-normalized, absolute and non-file path arguments are
    /// rejected before any Git invocation (spec 13 §23): the request never
    /// widens beyond the selected worktree, nothing is staged, and files
    /// outside the worktree are never touched.
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
    public async Task EscapingAndNonFilePathArgumentsAreRejectedBeforeGitRuns(string changePath)
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");

        // The escape would have a real target: one file outside the worktree.
        string outsidePath = Path.GetFullPath(Path.Combine(repo.RootPath, "..", "outside.md"));
        File.WriteAllText(outsidePath, "outside content");

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.StageAsync(repo.RootPath, changePath);

        Assert.Equal(GitStageOutcome.RejectedPath, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        // Nothing was staged and nothing outside the worktree was touched.
        Assert.Empty(StagedNames(repo));
        Assert.Equal(new[] { "docs/a.md" }, UnstagedNames(repo).ToArray());
        Assert.Equal("a v2", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.Equal("outside content", File.ReadAllText(outsidePath));
    }

    [Fact]
    public async Task AbsolutePathsAreRejectedEvenWhenTheyPointInsideTheWorktree()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        string absoluteInside = Path.Combine(repo.RootPath, "docs", "a.md");

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.StageAsync(repo.RootPath, absoluteInside);

        Assert.Equal(GitStageOutcome.RejectedPath, result.Outcome);
        Assert.Equal("AbsolutePathNotAllowed", result.ErrorCode);
        Assert.Empty(StagedNames(repo));
    }

    // ---------------------------------------------------------------------
    // Concurrency — per-worktree mutation queue (spec 13 §19)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Stage/unstage mutate the shared index, so they must run through the
    /// SCM-01 per-worktree mutation queue keyed by the true worktree root
    /// (spec 13 §19). This occupies the worktree slot directly on the queue and
    /// proves the service invocation waits for it: an unqueued or
    /// wrongly-keyed mutation would complete while the slot is still held.
    /// </summary>
    [Fact]
    public async Task StagingIsSerializedThroughThePerWorktreeMutationQueue()
    {
        string[] files = { "docs/a.md", "docs/b.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/a.md", "a v2");
        repo.WriteFile("docs/b.md", "b v2");

        (GitStageService service, GitWorktreeMutationQueue queue) = CreateService();

        var slotEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> occupying = queue.EnqueueAsync(repo.RootPath, async () =>
        {
            slotEntered.SetResult();
            await releaseSlot.Task;
            return 0;
        });
        await slotEntered.Task;

        Task<GitStageResult> staged = service.StageAsync(repo.RootPath, "docs/a.md");
        await Task.Delay(400);

        // The stage request must be blocked behind the occupied worktree slot.
        Assert.False(staged.IsCompleted);

        releaseSlot.SetResult();
        await occupying;
        GitStageResult result = await staged;

        Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, queue.MaxObservedConcurrency);
        Assert.Equal(new[] { "docs/a.md" }, StagedNames(repo).ToArray());
    }

    // ---------------------------------------------------------------------
    // Failure / edge cases (typed, never silent)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StageWithMissingGitIsTypedGitUnavailable()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        var runner = new UnavailableGitProcessRunner();
        var service = new GitStageService(runner, new GitRepositoryService(runner));

        GitStageResult result = await service.StageAsync(repo.RootPath, "docs/a.md");

        Assert.Equal(GitStageOutcome.GitUnavailable, result.Outcome);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, result.Failure!.ErrorKind);
        Assert.Empty(StagedNames(repo));
    }

    [Fact]
    public async Task StageInNonGitFolderIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm06", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            (GitStageService service, _) = CreateService();
            GitStageResult result = await service.StageAsync(folder, "a.md");

            Assert.Equal(GitStageOutcome.NoRepository, result.Outcome);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.Failure!.ErrorKind);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    [Fact]
    public async Task StageWhenWorktreeDisappearsIsTypedFailureNotAnException()
    {
        var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string root = repo.RootPath;
        repo.Dispose();
        DeleteDirectoryForcefully(root);
        Assert.False(Directory.Exists(root));

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.StageAsync(root, "docs/a.md");

        Assert.Equal(GitStageOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, result.Failure!.ErrorKind);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task StageWithIndexLockContentionIsTypedAndStagesNothing()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        string lockPath = Path.Combine(repo.RootPath, ".git", "index.lock");
        File.WriteAllText(lockPath, string.Empty);

        try
        {
            (GitStageService service, _) = CreateService();
            GitStageResult result = await service.StageAsync(repo.RootPath, "docs/a.md");

            Assert.Equal(GitStageOutcome.Failed, result.Outcome);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitLockContention, result.Failure!.ErrorKind);
            Assert.Empty(StagedNames(repo));
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    /// <summary>
    /// Documented limitation (typed, never silent): Git's
    /// <c>restore --staged</c> resolves against HEAD, so before the first
    /// commit there is no HEAD to restore from and unstage fails typed with the
    /// index unchanged. Downstream commit flows (SCM-07) must not assume
    /// unstage works on an unborn branch.
    /// </summary>
    [Fact]
    public async Task UnstageBeforeFirstCommitIsTypedFailureAndLeavesIndexUnchanged()
    {
        using var repo = TestGitRepository.Create($"scm06-unborn-{Guid.NewGuid():N}");
        repo.WriteFile("docs/first.md", "first");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/first.md"));

        (GitStageService service, _) = CreateService();
        GitStageResult result = await service.UnstageAsync(repo.RootPath, "docs/first.md");

        Assert.Equal(GitStageOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Failure);
        Assert.Equal(
            "A  docs/first.md",
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "status", "--porcelain").Trim());
    }

    // ---------------------------------------------------------------------
    // Source-of-truth boundaries (spec 13 §4.2/§10/§24)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion "Document IDE does not maintain a proprietary
    /// staging area": staging is visible to a completely independent
    /// service/runner instance because the real Git index is the only staging
    /// state.
    /// </summary>
    [Fact]
    public async Task StagingStateLivesInTheGitIndexNotInServiceMemory()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");

        (GitStageService first, _) = CreateService();
        Assert.Equal(GitStageOutcome.Succeeded, (await first.StageAsync(repo.RootPath, "docs/a.md")).Outcome);

        var independentRepositories = new GitRepositoryService(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitStatusResult independent = await independentRepositories.GetStatusAsync(repo.RootPath);

        Assert.True(independent.IsAvailable);
        GitStatusEntry staged = Assert.Single(independent.Snapshot!.Entries.Where(entry => entry.HasStagedChange));
        Assert.Equal("docs/a.md", staged.Path);
        Assert.Equal(GitChangeKind.Modified, staged.IndexChange);
    }

    /// <summary>
    /// Acceptance criterion "Staging never widens scope beyond selected
    /// repository paths": staging inside a linked worktree mutates that
    /// worktree's index only — the main worktree stays untouched.
    /// </summary>
    [Fact]
    public async Task StageIsScopedToTheActiveWorktreeOnly()
    {
        string[] files = { "docs/a.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/a.md", "main worktree edit");
        string linkedRoot = Path.Combine(Path.GetTempPath(), "scm01", $"scm06-wt-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "worktree", "add", "-q", linkedRoot, "HEAD"));

        try
        {
            File.WriteAllText(Path.Combine(linkedRoot, "docs", "a.md"), "linked worktree edit");
            (GitStageService service, _) = CreateService();
            GitStageResult result = await service.StageAsync(linkedRoot, "docs/a.md");

            Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
            Assert.Equal(
                Path.GetFullPath(linkedRoot),
                Assert.IsType<GitStatusResult>(result.Status).Snapshot!.WorktreeRoot,
                StringComparer.OrdinalIgnoreCase);

            // The linked worktree staged its own change; the main worktree's
            // working tree and index are untouched.
            Assert.Equal(new[] { "docs/a.md" }, StagedNames(linkedRoot).ToArray());
            Assert.Empty(StagedNames(repo));
            Assert.Equal(new[] { "docs/a.md" }, UnstagedNames(repo).ToArray());
            Assert.Equal(
                "main worktree edit",
                File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        }
        finally
        {
            TestGitRepository.RunGit(repo.RootPath, "worktree", "remove", "--force", linkedRoot);
            DeleteDirectoryBestEffort(linkedRoot);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds the real SCM-01 runner (with its per-worktree mutation queue),
    /// the SCM-02 repository service, and the service under test.
    /// </summary>
    private static (GitStageService Service, GitWorktreeMutationQueue Queue) CreateService()
    {
        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var repositories = new GitRepositoryService(runner);
        return (new GitStageService(runner, repositories), queue);
    }

    /// <summary>
    /// Creates a repository with branch <c>main</c> and one baseline commit
    /// containing <paramref name="baselineFiles"/>, so later edits to those
    /// files are reported as unstaged modifications.
    /// </summary>
    private static TestGitRepository CreateRepositoryWithBaselineCommit(
        string[] baselineFiles,
        string? directoryName = null)
    {
        var repo = TestGitRepository.Create(directoryName ?? $"scm06-repo-{Guid.NewGuid():N}");
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

    private static string[] StagedNames(TestGitRepository repo) => StagedNames(repo.RootPath);

    private static string[] StagedNames(string rootPath) => SplitNames(
        TestGitRepository.RunGitCaptureUtf8(rootPath, "diff", "--cached", "--name-only", "-z"));

    private static string[] UnstagedNames(TestGitRepository repo) => UnstagedNames(repo.RootPath);

    private static string[] UnstagedNames(string rootPath) => SplitNames(
        TestGitRepository.RunGitCaptureUtf8(rootPath, "diff", "--name-only", "-z"));

    private static string[] UntrackedNames(TestGitRepository repo) => SplitNames(
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "ls-files", "--others", "--exclude-standard", "-z"));

    /// <summary>
    /// Removes a scratch directory even when Git left read-only files behind;
    /// failures are tolerated because the directory lives under the OS temp dir.
    /// </summary>
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
    /// Removes a Git worktree directory completely: Git pack/object files are
    /// read-only, so a plain recursive delete on Windows leaves a partial
    /// directory behind. Tests that simulate "the worktree disappeared" must
    /// clear attributes first and must fail loudly when removal is impossible.
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

    /// <summary>Splits NUL-delimited Git name output; paths stay verbatim (spaces/Unicode).</summary>
    private static string[] SplitNames(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

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
/// SCM-06 — privileged API half of the Git Stage &amp; Unstage suite
/// (spec 13 §18/§20). The four stage/unstage routes must sit behind the
/// localhost session-token guard, reject non-contained paths typed before Git
/// runs (400, nothing staged), surface typed Git states for missing
/// repositories/Git, and return the re-read status payload on success — the
/// pane never keeps a proprietary staging cache.
/// </summary>
public sealed class GitStageApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GitStageApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------
    // Happy paths — real Git index, refreshed status payload in the response
    // ---------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion "Every stage action re-reads status": the stage
    /// response carries a full refreshed status payload and the real Git index
    /// is the only staging state.
    /// </summary>
    [Fact]
    public async Task StageEndpointStagesOneFileAndReturnsRefreshedStatus()
    {
        string[] files = { "docs/a.md", "docs/b.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/a.md", "a v2");
        repo.WriteFile("docs/b.md", "b v2");

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/stage",
            new SourceControlStageRequest(repo.RootPath, "docs/a.md"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlStageResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlStageResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Outcome);
        Assert.True(body.IsSuccess);
        Assert.Null(body.ErrorCode);
        Assert.Null(body.Message);
        Assert.Null(body.Failure);

        Assert.NotNull(body.Status);
        Assert.Equal("Available", body.Status!.Status);
        Assert.True(body.Status.HasRepository);
        Assert.Equal(Path.GetFullPath(repo.RootPath), body.Status.WorktreeRoot, StringComparer.OrdinalIgnoreCase);

        var staged = Assert.Single(body.Status.Sections.Staged);
        Assert.Equal("docs/a.md", staged.Path);
        Assert.Equal("Modified", staged.IndexChange);
        Assert.Equal("Unmodified", staged.WorkingTreeChange);
        Assert.Equal("M.", staged.StatusCharacters);
        Assert.Equal(new[] { "docs/b.md" }, body.Status.Sections.Unstaged.Select(entry => entry.Path).ToArray());
        Assert.Empty(body.Status.Sections.Untracked);
        Assert.Empty(body.Status.Sections.Conflicts);

        // The real Git index is the only staging state (spec 13 §10/§24).
        Assert.Equal(
            "docs/a.md",
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    /// <summary>Required test 2 through the API: "Unstage preserves working-tree edits."</summary>
    [Fact]
    public async Task UnstageEndpointRestoresIndexSideAndKeepsWorkingTreeEdit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/unstage",
            new SourceControlStageRequest(repo.RootPath, "docs/a.md"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlStageResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlStageResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Outcome);
        Assert.Empty(body.Status!.Sections.Staged);
        var unstaged = Assert.Single(body.Status.Sections.Unstaged);
        Assert.Equal("docs/a.md", unstaged.Path);
        Assert.Equal("Modified", unstaged.WorkingTreeChange);

        Assert.Equal("a v2", File.ReadAllText(aPath));
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    /// <summary>Required test 3 through the API: "Stage All and Unstage All match expected Git state."</summary>
    [Fact]
    public async Task StageAllAndUnstageAllEndpointsMatchExpectedGitState()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/tracked.md" });
        repo.WriteFile("docs/tracked.md", "tracked v2");
        repo.WriteFile("docs/untracked.md", "untracked");

        using var client = await CreateAuthorizedClientAsync();

        using var stageAll = await client.PostAsJsonAsync(
            "/api/source-control/stage-all",
            new SourceControlStageAllRequest(repo.RootPath));
        Assert.Equal(HttpStatusCode.OK, stageAll.StatusCode);
        SourceControlStageResponse? stagedBody =
            await stageAll.Content.ReadFromJsonAsync<SourceControlStageResponse>();
        Assert.Equal("Succeeded", stagedBody!.Outcome);
        Assert.Equal(
            new[] { "docs/tracked.md", "docs/untracked.md" },
            stagedBody.Status!.Sections.Staged.Select(entry => entry.Path).OrderBy(path => path).ToArray());
        Assert.Empty(stagedBody.Status.Sections.Unstaged);
        Assert.Empty(stagedBody.Status.Sections.Untracked);

        using var unstageAll = await client.PostAsJsonAsync(
            "/api/source-control/unstage-all",
            new SourceControlStageAllRequest(repo.RootPath));
        Assert.Equal(HttpStatusCode.OK, unstageAll.StatusCode);
        SourceControlStageResponse? unstagedBody =
            await unstageAll.Content.ReadFromJsonAsync<SourceControlStageResponse>();
        Assert.Equal("Succeeded", unstagedBody!.Outcome);
        Assert.Empty(unstagedBody.Status!.Sections.Staged);
        Assert.Equal(new[] { "docs/tracked.md" }, unstagedBody.Status.Sections.Unstaged.Select(entry => entry.Path).ToArray());
        Assert.Equal(new[] { "docs/untracked.md" }, unstagedBody.Status.Sections.Untracked.Select(entry => entry.Path).ToArray());
    }

    /// <summary>
    /// Required test 4 through the API: "Path names with spaces and Unicode
    /// remain correct" — the JSON round-trip must carry the status-reported
    /// path verbatim.
    /// </summary>
    [Fact]
    public async Task StageEndpointCarriesSpacesAndUnicodePathsVerbatim()
    {
        string spaced = "docs/my notes \u00e9\u00e0\u00fc.md";
        using var repo = CreateRepositoryWithBaselineCommit(
            new[] { spaced },
            $"scm06 api repo \u00fc {Guid.NewGuid():N}");
        repo.WriteFile(spaced, "edited");

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/stage",
            new SourceControlStageRequest(repo.RootPath, spaced));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlStageResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlStageResponse>();

        Assert.Equal("Succeeded", body!.Outcome);
        Assert.Equal(
            new[] { spaced },
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only", "-z")
                .Split('\0', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(
            new[] { spaced },
            body.Status!.Sections.Staged.Select(entry => entry.Path).ToArray());
    }

    // ---------------------------------------------------------------------
    // Typed rejections and failures (never silent, never widened)
    // ---------------------------------------------------------------------

    /// <summary>
    /// The path containment rule is enforced before Git runs: an escaping
    /// request is a 400 with a typed error code, nothing is staged and the
    /// outside file is untouched.
    /// </summary>
    [Fact]
    public async Task StageRejectsEscapingPathWithTypedBadRequestAndStagesNothing()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        string outsidePath = Path.GetFullPath(Path.Combine(repo.RootPath, "..", "outside.md"));
        File.WriteAllText(outsidePath, "outside content");

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/stage",
            new SourceControlStageRequest(repo.RootPath, "../outside.md"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument rejection = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PathNotNormalized", rejection.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(rejection.RootElement.GetProperty("error").GetString()));

        // Nothing was staged and nothing outside the worktree was touched.
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
        Assert.Equal("outside content", File.ReadAllText(outsidePath));
        Assert.Equal("a v2", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
    }

    [Fact]
    public async Task StageRejectsAbsolutePathEvenWhenItPointsInsideTheWorktree()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        string absoluteInside = Path.Combine(repo.RootPath, "docs", "a.md");

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/stage",
            new SourceControlStageRequest(repo.RootPath, absoluteInside));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument rejection = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AbsolutePathNotAllowed", rejection.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    [Fact]
    public async Task StageRoutesRejectBlankPathOrBlankChangePathWithTypedBadRequest()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        using var client = await CreateAuthorizedClientAsync();

        // Missing request body values.
        using var missingEverything = await client.PostAsJsonAsync(
            "/api/source-control/stage",
            new SourceControlStageRequest(null, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingEverything.StatusCode);
        using JsonDocument missingAll = JsonDocument.Parse(await missingEverything.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", missingAll.RootElement.GetProperty("errorCode").GetString());

        // Blank change path for the single-file action.
        using var blankChangePath = await client.PostAsJsonAsync(
            "/api/source-control/unstage",
            new SourceControlStageRequest(repo.RootPath, "   "));
        Assert.Equal(HttpStatusCode.BadRequest, blankChangePath.StatusCode);
        using JsonDocument blankPath = JsonDocument.Parse(await blankChangePath.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", blankPath.RootElement.GetProperty("errorCode").GetString());

        // Blank discovery path for the whole-worktree action.
        using var blankStageAll = await client.PostAsJsonAsync(
            "/api/source-control/stage-all",
            new SourceControlStageAllRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, blankStageAll.StatusCode);

        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    /// <summary>
    /// A path outside Git stays a valid source with a typed state (HTTP 200),
    /// never a silent success and never an exception-shaped 500.
    /// </summary>
    [Fact]
    public async Task StageInNonGitFolderReturnsTypedNoRepositoryState()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm06", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            using var client = await CreateAuthorizedClientAsync();
            using var response = await client.PostAsJsonAsync(
                "/api/source-control/stage",
                new SourceControlStageRequest(folder, "a.md"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SourceControlStageResponse? body =
                await response.Content.ReadFromJsonAsync<SourceControlStageResponse>();

            Assert.NotNull(body);
            Assert.Equal("NoRepository", body!.Outcome);
            Assert.False(body.IsSuccess);
            Assert.Null(body.Status);
            Assert.NotNull(body.Failure);
            Assert.Equal("GitRepositoryNotFound", body.Failure!.ErrorKind);
            Assert.False(string.IsNullOrWhiteSpace(body.Message));
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    // ---------------------------------------------------------------------
    // Privileged API security — every stage/unstage route is guarded
    // ---------------------------------------------------------------------

    [Fact]
    public async Task StageRoutesRejectMissingInvalidTokenAndHostileOrigin()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });

        using (var anonymous = _factory.CreateClient())
        {
            using var content = JsonContent.Create(new SourceControlStageRequest(repo.RootPath, "docs/a.md"));
            using var response = await anonymous.PostAsync("/api/source-control/stage", content);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

            using var hostileOrigin = _factory.CreateClient();
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
            using var hostileContent = JsonContent.Create(new SourceControlStageRequest(repo.RootPath, "docs/a.md"));
            using var hostileResponse = await hostileOrigin.PostAsync("/api/source-control/stage", hostileContent);
            Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

            using var wrongToken = _factory.CreateClient();
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            using var wrongContent = JsonContent.Create(new SourceControlStageAllRequest(repo.RootPath));
            using var wrongResponse = await wrongToken.PostAsync("/api/source-control/unstage-all", wrongContent);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        }

        // No unauthenticated request reached Git: nothing staged.
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCapture(repo.RootPath, "diff", "--cached", "--name-only").Trim());
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates a repository with branch <c>main</c> and one baseline commit
    /// containing <paramref name="baselineFiles"/>, so later edits to those
    /// files are reported as unstaged modifications.
    /// </summary>
    private static TestGitRepository CreateRepositoryWithBaselineCommit(
        string[] baselineFiles,
        string? directoryName = null)
    {
        var repo = TestGitRepository.Create(directoryName ?? $"scm06-api-repo-{Guid.NewGuid():N}");
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

    private Task<HttpClient> CreateAuthorizedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return Task.FromResult(client);
    }

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
}
