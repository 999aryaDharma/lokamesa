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
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-07 — Git Commit &amp; Commit All (spec 13 §9.4/§11/§19/§20/§23).
///
/// These tests drive <see cref="GitCommitService"/> against real Git
/// repositories through the SCM-01 controlled process boundary and the SCM-01
/// per-worktree mutation queue:
/// <list type="bullet">
/// <item>Commit records the staged state only — unstaged working-tree changes
/// are never silently included and there is no <c>-a</c>/<c>--all</c> on the
/// plain path (spec 13 §9.4/§11.1/§342),</item>
/// <item>Commit All is an explicitly distinct flow that stages eligible changes
/// first (reusing the SCM-06 <see cref="GitStageService"/>) and reports the flow
/// it ran (spec 13 §11.2),</item>
/// <item>the commit message is validated and passed as a structured
/// <c>-m</c> argument, never a shell string (spec 13 §18/§23),</item>
/// <item>hooks stay enabled: a failing pre-commit hook is a typed commit
/// failure with no commit created (spec 13 §11.1/§23),</item>
/// <item>failures are typed and inspectable (nothing staged, no repository,
/// missing identity, hook rejection, index lock), and</item>
/// <item>the commit path never talks to a remote — no automatic push
/// (spec 13 §14.4/§23).</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitCommitTests.cs</c>
/// (no <c>DocumentIDE.UnitTests</c> project exists in this repository); the
/// frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/CommitBox.test.tsx</c>
/// (CAN-05 batch-B ruling).
/// </summary>
public sealed class GitCommitTests
{
    // ---------------------------------------------------------------------
    // Step 2 — Staged-only commit (first failing test)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Commit contains staged changes
    /// only." The new commit contains exactly the staged path; a modified but
    /// unstaged file is left untouched in the working tree and is not part of
    /// the commit (spec 13 §9.4/§11.1 — no <c>-a</c>/<c>--all</c>).
    /// </summary>
    [Fact]
    public async Task CommitContainsStagedChangesOnly()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md", "docs/b.md" });
        repo.WriteFile("docs/a.md", "a v2");
        repo.WriteFile("docs/b.md", "b v2");
        string headBefore = Head(repo);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "commit staged only");

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        Assert.Equal(GitCommitFlow.Commit, result.Flow);
        Assert.True(result.IsSuccess);

        GitCommitInfo info = Assert.IsType<GitCommitInfo>(result.Commit);
        Assert.NotEqual(headBefore, info.CommitId);
        Assert.Equal(Head(repo), info.CommitId);
        Assert.StartsWith(info.ShortId, info.CommitId, StringComparison.Ordinal);
        Assert.Equal("commit staged only", info.Summary);

        // The created commit contains exactly the staged file.
        Assert.Equal(new[] { "docs/a.md" }, CommitFiles(repo, info.CommitId));

        // The unstaged change survived untouched and was never committed.
        Assert.Equal(new[] { "docs/b.md" }, UnstagedNames(repo).ToArray());
        Assert.Equal("b v2", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "b.md")));

        // The response carries the status re-read from Git after the commit.
        GitStatusResult refreshed = Assert.IsType<GitStatusResult>(result.Status);
        Assert.True(refreshed.IsAvailable);
        Assert.Equal(Path.GetFullPath(repo.RootPath), refreshed.Snapshot!.WorktreeRoot, ignoreCase: true);
        Assert.Empty(refreshed.Snapshot.Entries.Where(entry => entry.HasStagedChange));
    }

    // ---------------------------------------------------------------------
    // Step 1 — Commit message validation
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 2: an empty or whitespace-only commit message is rejected
    /// typed before any Git invocation — no commit is created and the staged
    /// state is untouched (spec 13 §11.1 "the commit message is required").
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n \r\n")]
    public async Task BlankOrWhitespaceOnlyCommitMessageIsRejectedTypedWithoutRunningGit(string? message)
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, message);

        Assert.Equal(GitCommitOutcome.MessageRequired, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Commit);
        Assert.Null(result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        // No commit, no index change, nothing touched.
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(countBefore, CommitCount(repo));
        Assert.Equal(new[] { "docs/a.md" }, StagedNames(repo.RootPath).ToArray());
    }

    /// <summary>
    /// A message that cannot be passed as a process argument (a NUL character)
    /// is rejected typed before any Git invocation instead of escaping the
    /// structured argument boundary.
    /// </summary>
    [Fact]
    public async Task CommitMessageContainingNulIsRejectedTypedWithoutRunningGit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string headBefore = Head(repo);

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitCommitService service, _, _, _) = CreateService(recording);

        GitCommitResult result = await service.CommitAsync(repo.RootPath, "subject\0injected");

        Assert.Equal(GitCommitOutcome.MessageInvalid, result.Outcome);
        Assert.Null(result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.Equal(headBefore, Head(repo));

        // A rejected message never reaches Git at all.
        Assert.Empty(recording.Invocations);
    }

    /// <summary>
    /// The commit message is data, never a shell string (spec 13 §18/§23): it
    /// travels as a single structured <c>-m</c> argument, so metacharacters
    /// stay literal and no redirection/command substitution happens.
    /// </summary>
    [Fact]
    public async Task CommitMessageIsPassedAsAStructuredArgumentAndNeverShellInterpreted()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        const string message = "feat: \"quoted\" $HOME `id` ; && | > < * ? [x] 'single' %s %n";
        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitCommitService service, _, _, _) = CreateService(recording);

        GitCommitResult result = await service.CommitAsync(repo.RootPath, message);

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        Assert.Equal(message, result.Commit!.Summary);
        Assert.Equal(
            message,
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "-1", "--format=%s").Trim());

        // The message became one argument of the commit invocation.
        IReadOnlyList<string> commitInvocation = Assert.Single(
            recording.Invocations.Where(arguments => arguments[0] == "commit"));
        Assert.Equal(new[] { "commit", "-m", message }, commitInvocation);

        // No shell interpretation: the metacharacters created no file.
        Assert.Equal(
            new[] { "docs/a.md" },
            CommitFiles(repo, Head(repo)));
        Assert.Empty(UntrackedNames(repo));
    }

    // ---------------------------------------------------------------------
    // Step 2/3 — Nothing staged is never an empty commit
    // ---------------------------------------------------------------------

    /// <summary>
    /// Acceptance criterion "Commit never silently includes unstaged-only
    /// changes": with only working-tree edits the commit reports a typed
    /// <see cref="GitCommitOutcome.NothingStaged"/>, no commit is attempted and
    /// the edit survives untouched (spec 13 §9.4/§11.3).
    /// </summary>
    [Fact]
    public async Task UnstagedOnlyChangesAreNeverCommittedAndReportNothingStaged()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string aPath = repo.WriteFile("docs/a.md", "a v2 (unstaged only)");
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitCommitService service, _, _, _) = CreateService(recording);

        GitCommitResult result = await service.CommitAsync(repo.RootPath, "should not commit");

        Assert.Equal(GitCommitOutcome.NothingStaged, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Commit);
        Assert.Null(result.Failure);
        Assert.Equal(GitCommitFlow.Commit, result.Flow);

        // No commit was created and no `git commit` was even attempted.
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(countBefore, CommitCount(repo));
        Assert.DoesNotContain(recording.Invocations, arguments => arguments[0] == "commit");

        // The unstaged edit is untouched.
        Assert.Equal("a v2 (unstaged only)", File.ReadAllText(aPath));
        Assert.Equal(new[] { "docs/a.md" }, UnstagedNames(repo).ToArray());
    }

    /// <summary>
    /// A clean working tree reports the same typed outcome — the UI must never
    /// create an empty commit (spec 13 §11.3).
    /// </summary>
    [Fact]
    public async Task CleanWorktreeReportsNothingStagedWithoutCreatingAnEmptyCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "nothing to do");

        Assert.Equal(GitCommitOutcome.NothingStaged, result.Outcome);
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(countBefore, CommitCount(repo));
    }

    /// <summary>
    /// Failure/edge case "Word has changed a DOCX between status/diff/stage
    /// operations": the commit records the staged version, and the newer
    /// working-tree save remains an unstaged change (spec 13 §10/§11.1) — the
    /// commit never silently absorbs the newer content.
    /// </summary>
    [Fact]
    public async Task DocumentSavedAgainAfterStagingIsNotPartOfTheCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/treaty.docx" });
        repo.WriteFile("docs/treaty.docx", "docx v2 (staged)");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/treaty.docx"));

        // Word saves again after the file was staged.
        repo.WriteFile("docs/treaty.docx", "docx v3 (saved after staging)");

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "commit staged docx");

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        string commitId = result.Commit!.CommitId;
        Assert.Equal(
            "docx v2 (staged)",
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "show", $"{commitId}:docs/treaty.docx").Trim());

        Assert.Equal(
            "docx v3 (saved after staging)",
            File.ReadAllText(Path.Combine(repo.RootPath, "docs", "treaty.docx")));
        Assert.Equal(new[] { "docs/treaty.docx" }, UnstagedNames(repo).ToArray());
    }

    // ---------------------------------------------------------------------
    // Step 3 — Commit All intentionally stages eligible changes and commits
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 3: "Commit All intentionally stages and commits eligible
    /// changes." The flow re-reads status, stages the eligible working-tree
    /// changes through the SCM-06 stage service, re-reads status, commits, and
    /// reports both the flow it ran and the paths it staged (spec 13 §11.2).
    /// Ignored files are never silently included.
    /// </summary>
    [Fact]
    public async Task CommitAllStagesEligibleChangesThenCommitsAndReportsItsFlow()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/modified.md", "docs/deleted.md" });
        repo.WriteFile(".gitignore", "docs/ignored.md\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", ".gitignore"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "ignore"));

        repo.WriteFile("docs/modified.md", "modified v2");
        File.Delete(Path.Combine(repo.RootPath, "docs", "deleted.md"));
        repo.WriteFile("docs/untracked \u00e9.md", "untracked");
        repo.WriteFile("docs/ignored.md", "ignored");

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitCommitService service, _, _, _) = CreateService(recording);

        GitCommitResult result = await service.CommitAllAsync(repo.RootPath, "commit all eligible");

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        Assert.Equal(GitCommitFlow.CommitAll, result.Flow);
        Assert.True(result.IsSuccess);

        string[] eligible =
        {
            "docs/deleted.md", "docs/modified.md", "docs/untracked \u00e9.md",
        };
        Assert.Equal(
            eligible.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            CommitFiles(repo, result.Commit!.CommitId).OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // The response reports exactly which paths the Commit All flow staged.
        Assert.NotNull(result.StagedPaths);
        Assert.Equal(
            eligible.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            result.StagedPaths!.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // The flow reused the SCM-06 stage-all pipeline before committing.
        Assert.Contains(
            recording.Invocations,
            arguments => arguments.SequenceEqual(new[] { "add", "--all", "--", "." }));

        // Ignored files were never silently included.
        Assert.Equal(
            string.Empty,
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "ls-files", "--cached", "--", "docs/ignored.md").Trim());
        Assert.Equal("ignored", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "ignored.md")));

        // Everything eligible was committed: the tree is clean afterwards.
        Assert.Empty(StagedNames(repo.RootPath));
        Assert.Empty(UnstagedNames(repo));
        Assert.Empty(UntrackedNames(repo));
    }

    /// <summary>
    /// Commit and Commit All stay visibly distinct flows: the plain Commit
    /// never stages anything, Commit All does (spec 13 §9.4/§11.2).
    /// </summary>
    [Fact]
    public async Task CommitAndCommitAllRunDistinctFlows()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitCommitService service, _, _, _) = CreateService(recording);

        GitCommitResult plain = await service.CommitAsync(repo.RootPath, "plain commit");
        Assert.Equal(GitCommitFlow.Commit, plain.Flow);
        Assert.Equal(GitCommitOutcome.NothingStaged, plain.Outcome);
        Assert.Null(plain.StagedPaths);
        Assert.DoesNotContain(
            recording.Invocations,
            arguments => arguments.SequenceEqual(new[] { "add", "--all", "--", "." }));

        GitCommitResult all = await service.CommitAllAsync(repo.RootPath, "commit all");
        Assert.Equal(GitCommitFlow.CommitAll, all.Flow);
        Assert.Equal(GitCommitOutcome.Succeeded, all.Outcome);
        Assert.NotNull(all.StagedPaths);
    }

    // ---------------------------------------------------------------------
    // Step 2/5 — First commit on an unborn HEAD is legitimate
    // ---------------------------------------------------------------------

    /// <summary>
    /// SCM-06 documented that <c>git restore --staged</c> fails before the
    /// first commit; a first commit itself is legitimate. Commit works on an
    /// unborn HEAD and returns the new commit identifier (spec 13 §11.1).
    /// </summary>
    [Fact]
    public async Task CommitOnUnbornHeadCreatesTheFirstCommit()
    {
        using var repo = CreateRepositoryOnMainWithoutCommit();
        repo.WriteFile("docs/first.md", "first");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/first.md"));

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "first commit");

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, CommitCount(repo));
        Assert.Equal(Head(repo), result.Commit!.CommitId);
        Assert.Equal("first commit", result.Commit.Summary);
        Assert.Equal("main", result.Commit.BranchName);
        Assert.Equal(new[] { "docs/first.md" }, CommitFiles(repo, result.Commit.CommitId));
    }

    /// <summary>
    /// Commit All also works on an unborn HEAD: it stages the eligible changes
    /// and creates the repository's first commit (spec 13 §11.2).
    /// </summary>
    [Fact]
    public async Task CommitAllOnUnbornHeadCreatesTheFirstCommit()
    {
        using var repo = CreateRepositoryOnMainWithoutCommit();
        repo.WriteFile("docs/one.md", "one");
        repo.WriteFile("docs/two \u00e9.md", "two");

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAllAsync(repo.RootPath, "first commit via commit all");

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        Assert.Equal(GitCommitFlow.CommitAll, result.Flow);
        Assert.Equal(1, CommitCount(repo));
        Assert.Equal(
            new[] { "docs/one.md", "docs/two \u00e9.md" },
            CommitFiles(repo, result.Commit!.CommitId).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal("main", result.Commit.BranchName);
    }

    // ---------------------------------------------------------------------
    // Step 4 — Repository hooks stay enabled and are never bypassed
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 4 (part 1): "a failing pre-commit hook produces a typed
    /// failure and no commit." Hooks are part of normal Git semantics and are
    /// never bypassed (spec 13 §11.1/§23): Git runs the hook, the hook fails,
    /// and the application reports a typed <see cref="GitCommitOutcome.HookRejected"/>
    /// with HEAD, the commit count and the index all unchanged.
    /// </summary>
    [Fact]
    public async Task FailingPreCommitHookIsATypedFailureAndCreatesNoCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        WriteCommitHook(repo, "pre-commit", "echo 'blocked by hook' >&2\nexit 1\n");
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "must be rejected by hook");

        Assert.Equal(GitCommitOutcome.HookRejected, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Commit);

        // The originating Git failure stays inspectable.
        GitProcessResult failure = Assert.IsType<GitProcessResult>(result.Failure);
        Assert.Equal(1, failure.ExitCode);

        // No commit was created and the staged state is untouched.
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(countBefore, CommitCount(repo));
        Assert.Equal(new[] { "docs/a.md" }, StagedNames(repo.RootPath).ToArray());
        Assert.Equal("a v2", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
    }

    /// <summary>
    /// The hook's own output is surfaced on the typed failure instead of being
    /// swallowed, so the user can see why the commit was rejected.
    /// </summary>
    [Fact]
    public async Task FailingHookOutputIsSurfacedOnTheTypedFailure()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        WriteCommitHook(repo, "pre-commit", "echo 'policy violation: ticket id missing' >&2\nexit 1\n");

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "no ticket id");

        Assert.Equal(GitCommitOutcome.HookRejected, result.Outcome);
        GitProcessResult failure = Assert.IsType<GitProcessResult>(result.Failure);
        Assert.Contains("policy violation", failure.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rejecting <c>commit-msg</c> hook is a typed hook failure too: the
    /// message a repository hook rewrote/refused is never committed silently.
    /// </summary>
    [Fact]
    public async Task FailingCommitMsgHookIsATypedFailureAndCreatesNoCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        WriteCommitHook(repo, "commit-msg", "echo 'commit message rejected' >&2\nexit 1\n");
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "message the hook will reject");

        Assert.Equal(GitCommitOutcome.HookRejected, result.Outcome);
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(countBefore, CommitCount(repo));
        Assert.Equal(new[] { "docs/a.md" }, StagedNames(repo.RootPath).ToArray());
    }

    /// <summary>
    /// Required test 4 (part 2): "a successful hook runs and the commit
    /// succeeds." The hook writes a marker file, so the commit can only exist
    /// if Git actually executed the hook — proving the hook was not skipped
    /// (spec 13 §11.1).
    /// </summary>
    [Fact]
    public async Task SuccessfulPreCommitHookRunsAndTheCommitSucceeds()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        WriteCommitHook(repo, "pre-commit", "echo ran > .git/hook-ran.marker\nexit 0\n");

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "hook allows this");

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        Assert.Equal(Head(repo), result.Commit!.CommitId);
        Assert.True(
            File.Exists(Path.Combine(repo.RootPath, ".git", "hook-ran.marker")),
            "the pre-commit hook must have run: its marker file was not created.");
    }

    /// <summary>
    /// The commit path never bypasses repository hooks: no invocation carries
    /// <c>--no-verify</c>/<c>-n</c> (spec 13 §11.1/§23). Verified on the plain
    /// Commit and on Commit All.
    /// </summary>
    [Fact]
    public async Task CommitNeverBypassesHooksWithNoVerify()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitCommitService service, _, _, _) = CreateService(recording);

        Assert.Equal(GitCommitOutcome.Succeeded, (await service.CommitAsync(repo.RootPath, "plain")).Outcome);
        repo.WriteFile("docs/a.md", "a v3");
        Assert.Equal(GitCommitOutcome.Succeeded, (await service.CommitAllAsync(repo.RootPath, "all")).Outcome);

        Assert.DoesNotContain(recording.Invocations, args => args.Contains("--no-verify"));
        Assert.DoesNotContain(recording.Invocations, args => args.Contains("-n"));
    }

    // ---------------------------------------------------------------------
    // Step 4 — Typed failures: index lock, identity, race, unmerged index
    // ---------------------------------------------------------------------

    /// <summary>
    /// Failure/edge case "Git index locked by another process": the commit is
    /// reported typed as <see cref="GitCommitOutcome.IndexLocked"/>, no commit is
    /// created and the staged state is left exactly as it was (spec 13 §19/§20).
    /// </summary>
    [Fact]
    public async Task LockedIndexIsATypedFailureAndCreatesNoCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        string lockPath = Path.Combine(repo.RootPath, ".git", "index.lock");
        File.WriteAllText(lockPath, string.Empty);
        try
        {
            (GitCommitService service, _, _, _) = CreateService();
            GitCommitResult result = await service.CommitAsync(repo.RootPath, "blocked by the lock");

            Assert.Equal(GitCommitOutcome.IndexLocked, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Commit);
            Assert.IsType<GitProcessResult>(result.Failure);

            Assert.Equal(headBefore, Head(repo));
            Assert.Equal(countBefore, CommitCount(repo));
            Assert.Equal(new[] { "docs/a.md" }, StagedNames(repo.RootPath).ToArray());
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    /// <summary>
    /// Commit All reports the same typed lock outcome: its staging step fails
    /// against the locked index, so it never proceeds to commit and nothing is
    /// staged or committed (spec 13 §11.2/§19).
    /// </summary>
    [Fact]
    public async Task LockedIndexDuringCommitAllIsATypedFailureAndCreatesNoCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        string lockPath = Path.Combine(repo.RootPath, ".git", "index.lock");
        File.WriteAllText(lockPath, string.Empty);
        try
        {
            (GitCommitService service, _, _, _) = CreateService();
            GitCommitResult result = await service.CommitAllAsync(repo.RootPath, "blocked by the lock");

            Assert.Equal(GitCommitOutcome.IndexLocked, result.Outcome);
            Assert.Equal(GitCommitFlow.CommitAll, result.Flow);
            Assert.Null(result.Commit);

            Assert.Equal(headBefore, Head(repo));
            Assert.Equal(countBefore, CommitCount(repo));

            // Staging failed, so the edit is still unstaged and untouched.
            Assert.Empty(StagedNames(repo.RootPath));
            Assert.Equal(new[] { "docs/a.md" }, UnstagedNames(repo).ToArray());
            Assert.Equal("a v2", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    /// <summary>
    /// Failure/edge case "commit fails because Git identity is not configured":
    /// Git's identity errors are reported typed as
    /// <see cref="GitCommitOutcome.MissingIdentity"/> with no commit created
    /// (spec 13 §11/§20). The canned runner keeps the test independent of the
    /// host's own global Git identity.
    /// </summary>
    [Theory]
    [InlineData("fatal: empty ident name (for <>) not allowed\n")]
    [InlineData("fatal: unable to auto-detect email address (got 'user@host.(none)')\n")]
    [InlineData(
        "Author identity unknown\n\n*** Please tell me who you are.\n\n" +
        "Run\n\n  git config --global user.email \"you@example.com\"\n\n" +
        "fatal: unable to auto-detect email address\n")]
    public async Task MissingGitIdentityIsATypedFailureAndCreatesNoCommit(string stderr)
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string headBefore = Head(repo);
        int countBefore = CommitCount(repo);

        var scripted = new ScriptedGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()),
            ("commit", new GitProcessResult(
                GitProcessStatus.Failed,
                128,
                string.Empty,
                stderr,
                GitProcessErrorKind.Unknown,
                "Git commit failed.")));
        (GitCommitService service, _, _, _) = CreateService(scripted);

        GitCommitResult result = await service.CommitAsync(repo.RootPath, "no identity");

        Assert.Equal(GitCommitOutcome.MissingIdentity, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Commit);
        Assert.Equal(128, Assert.IsType<GitProcessResult>(result.Failure).ExitCode);

        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(countBefore, CommitCount(repo));
        Assert.Equal(new[] { "docs/a.md" }, StagedNames(repo.RootPath).ToArray());
    }

    /// <summary>
    /// Race safety: if the index changed between the status pre-check and the
    /// commit, Git's own "nothing to commit" answer is surfaced as the typed
    /// <see cref="GitCommitOutcome.NothingStaged"/> instead of an anonymous
    /// failure or an empty commit (spec 13 §11.3/§19).
    /// </summary>
    [Theory]
    [InlineData("nothing to commit, working tree clean\n", "")]
    [InlineData("", "nothing added to commit but untracked files present (use \"git add\" to track)\n")]
    public async Task IndexChangedBetweenPreCheckAndCommitIsReportedAsNothingStaged(
        string stdout,
        string stderr)
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string headBefore = Head(repo);

        var scripted = new ScriptedGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()),
            ("commit", new GitProcessResult(
                GitProcessStatus.Failed,
                1,
                stdout,
                stderr,
                GitProcessErrorKind.Unknown,
                "Git commit failed.")));
        (GitCommitService service, _, _, _) = CreateService(scripted);

        GitCommitResult result = await service.CommitAsync(repo.RootPath, "raced away");

        Assert.Equal(GitCommitOutcome.NothingStaged, result.Outcome);
        Assert.Null(result.Commit);
        Assert.Equal("NothingStaged", result.ErrorCode);
        Assert.Equal(headBefore, Head(repo));
    }

    /// <summary>
    /// Failure/edge case "commit blocked by an unresolved merge conflict": with
    /// unmerged entries the pre-check deliberately lets Git be the reporting
    /// authority, and the typed failure carries
    /// <see cref="GitProcessErrorKind.GitConflict"/> (spec 13 §11/§20). HEAD is
    /// unchanged because no commit was recorded.
    /// </summary>
    [Fact]
    public async Task UnresolvedConflictIsATypedCommitFailure()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "main side");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "main side"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "-b", "feature", "HEAD~1"));
        repo.WriteFile("docs/a.md", "feature side");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "feature side"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "--no-edit", "feature"));
        string headBefore = Head(repo);

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(repo.RootPath, "must not commit a conflict");

        Assert.Equal(GitCommitOutcome.Failed, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Commit);

        GitProcessResult failure = Assert.IsType<GitProcessResult>(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitConflict, failure.ErrorKind);
        Assert.Equal(headBefore, Head(repo));
    }

    // ---------------------------------------------------------------------
    // Step 4 — Discovery failures stay typed and never touch Git state
    // ---------------------------------------------------------------------

    /// <summary>
    /// A workspace outside any Git worktree is a valid Document IDE source with
    /// no Git state: the commit reports the typed
    /// <see cref="GitCommitOutcome.NoRepository"/> and no <c>git commit</c> is
    /// ever attempted (spec 13 §5/§11).
    /// </summary>
    [Fact]
    public async Task CommitOutsideAnyRepositoryIsTypedNoRepositoryWithoutRunningGit()
    {
        string scratch = Path.Combine(Path.GetTempPath(), $"scm07-norepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            var recording = new RecordingGitProcessRunner(
                new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
            (GitCommitService service, _, _, _) = CreateService(recording);

            GitCommitResult result = await service.CommitAsync(scratch, "nowhere to commit");

            Assert.Equal(GitCommitOutcome.NoRepository, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Commit);
            Assert.DoesNotContain(recording.Invocations, arguments => arguments[0] == "commit");
            Assert.DoesNotContain(recording.Invocations, arguments => arguments[0] == "add");
        }
        finally
        {
            DeleteDirectoryBestEffort(scratch);
        }
    }

    /// <summary>
    /// Git being missing/unusable is a typed
    /// <see cref="GitCommitOutcome.GitUnavailable"/> for both flows — never an
    /// exception and never a silent no-op (spec 13 §20).
    /// </summary>
    [Fact]
    public async Task CommitWhenGitIsUnavailableIsTypedGitUnavailable()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        (GitCommitService service, _, _, _) = CreateService(new UnavailableGitProcessRunner());

        GitCommitResult plain = await service.CommitAsync(repo.RootPath, "no git to run");
        GitCommitResult all = await service.CommitAllAsync(repo.RootPath, "no git to run either");

        Assert.Equal(GitCommitOutcome.GitUnavailable, plain.Outcome);
        Assert.Equal(GitCommitOutcome.GitUnavailable, all.Outcome);
        Assert.Null(plain.Commit);
        Assert.Null(all.Commit);
    }

    /// <summary>
    /// Failure/edge case "the worktree disappears mid-request": a repository
    /// path that no longer exists yields the typed
    /// <see cref="GitCommitOutcome.Failed"/> with the runner's
    /// <see cref="GitProcessErrorKind.GitWorktreeInvalid"/> failure, and no
    /// commit is created (spec 13 §19/§20).
    /// </summary>
    [Fact]
    public async Task WorktreeThatDisappearedIsATypedFailureAndCreatesNoCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string root = repo.RootPath;
        DeleteDirectoryForcefully(root);
        Assert.False(Directory.Exists(root));

        (GitCommitService service, _, _, _) = CreateService();
        GitCommitResult result = await service.CommitAsync(root, "the worktree is gone");

        Assert.Equal(GitCommitOutcome.Failed, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Commit);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, Assert.IsType<GitProcessResult>(result.Failure).ErrorKind);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds the real SCM-01 runner (with its per-worktree mutation queue), the
    /// SCM-02 repository service, the SCM-06 stage service and the service under
    /// test — the same composition the API host registers.
    /// </summary>
    private static (GitCommitService Service, GitStageService Stages, GitRepositoryService Repositories, GitWorktreeMutationQueue Queue)
        CreateService(IGitProcessRunner? runner = null)
    {
        var queue = new GitWorktreeMutationQueue();
        IGitProcessRunner effective = runner ?? new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var repositories = new GitRepositoryService(effective);
        var stages = new GitStageService(effective, repositories);
        return (new GitCommitService(effective, repositories, stages), stages, repositories, queue);
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
        var repo = TestGitRepository.Create(directoryName ?? $"scm07-repo-{Guid.NewGuid():N}");
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

    /// <summary>Creates an initialized repository with no commit yet (unborn HEAD).</summary>
    private static TestGitRepository CreateRepositoryWithoutCommit() =>
        TestGitRepository.Create($"scm07-unborn-{Guid.NewGuid():N}");

    /// <summary>
    /// Creates an initialized repository whose unborn branch is named
    /// <c>main</c>, so first-commit assertions stay deterministic regardless of
    /// the host's default branch name.
    /// </summary>
    private static TestGitRepository CreateRepositoryOnMainWithoutCommit()
    {
        var repo = CreateRepositoryWithoutCommit();
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        return repo;
    }

    private static string Head(TestGitRepository repo) => Head(repo.RootPath);

    private static string Head(string rootPath) =>
        TestGitRepository.RunGitCaptureUtf8(rootPath, "rev-parse", "HEAD").Trim();

    private static int CommitCount(TestGitRepository repo) =>
        int.Parse(TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-list", "--count", "HEAD").Trim());

    /// <summary>
    /// The paths recorded in one commit. <c>core.quotepath=false</c> keeps Git
    /// from C-quoting non-ASCII paths, so the names match the worktree-relative
    /// paths the service reports verbatim.
    /// </summary>
    private static string[] CommitFiles(TestGitRepository repo, string commitId) =>
        TestGitRepository
            .RunGitCaptureUtf8(repo.RootPath, "-c", "core.quotepath=false", "show", "--name-only", "--format=", commitId)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

    private static string[] StagedNames(string rootPath) => SplitNames(
        TestGitRepository.RunGitCaptureUtf8(rootPath, "diff", "--cached", "--name-only", "-z"));

    private static string[] UnstagedNames(TestGitRepository repo) => SplitNames(
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--name-only", "-z"));

    private static string[] UntrackedNames(TestGitRepository repo) => SplitNames(
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "ls-files", "--others", "--exclude-standard", "-z"));

    private static string[] SplitNames(string output) =>
        output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Writes a Git commit hook (spec 13 §11.1/§23): hooks stay part of normal
    /// Git semantics and must never be bypassed by the application.
    /// </summary>
    private static void WriteCommitHook(TestGitRepository repo, string hookName, string shellScript)
    {
        string hooksDirectory = Path.Combine(repo.RootPath, ".git", "hooks");
        Directory.CreateDirectory(hooksDirectory);
        string hookPath = Path.Combine(hooksDirectory, hookName);
        File.WriteAllText(hookPath, "#!/bin/sh\n" + shellScript);
        File.SetAttributes(hookPath, FileAttributes.Normal);
    }

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
    /// directory behind.
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
    /// Records every Git invocation while delegating to a real runner. Used to
    /// prove which arguments the commit path used (no shell string, no
    /// <c>-a</c>/<c>--all</c>, no <c>--no-verify</c>) and that it never touched
    /// a remote.
    /// </summary>
    private sealed class RecordingGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;

        public RecordingGitProcessRunner(IGitProcessRunner inner)
        {
            _inner = inner;
        }

        public List<IReadOnlyList<string>> Invocations { get; } = new();

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public async Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (Invocations)
            {
                Invocations.Add(request.Arguments);
            }

            return await _inner.RunAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Delegates to a real runner but substitutes a canned
    /// <see cref="GitProcessResult"/> for one subcommand (keyed by the first
    /// argument). Used to pin failure signatures that depend on machine
    /// configuration — e.g. a missing Git identity, which cannot be reproduced
    /// reliably against a host that has a global <c>user.name</c>/<c>user.email</c>.
    /// </summary>
    private sealed class ScriptedGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;
        private readonly Dictionary<string, GitProcessResult> _overrides;

        public ScriptedGitProcessRunner(
            IGitProcessRunner inner,
            params (string Subcommand, GitProcessResult Result)[] overrides)
        {
            _inner = inner;
            _overrides = overrides.ToDictionary(
                entry => entry.Subcommand,
                entry => entry.Result,
                StringComparer.Ordinal);
        }

        public List<IReadOnlyList<string>> Invocations { get; } = new();

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public async Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (Invocations)
            {
                Invocations.Add(request.Arguments);
            }

            if (request.Arguments.Count > 0
                && _overrides.TryGetValue(request.Arguments[0], out GitProcessResult? canned))
            {
                return canned;
            }

            return await _inner.RunAsync(request, cancellationToken);
        }
    }

    /// <summary>Git is not installed/discoverable: typed unavailable, never an exception.</summary>
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
