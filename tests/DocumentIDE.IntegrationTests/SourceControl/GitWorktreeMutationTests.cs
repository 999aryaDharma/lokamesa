using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-16 — Safe Git Worktree Create &amp; Remove (spec 13 §16.3/§16.4/§16.5).
///
/// Red stage: the first test in this file
/// (<see cref="CreateFromExistingBranchSucceeds"/>) must FAIL before the
/// SCM-16 mutation service types exist. Fixtures use raw Git process
/// invocations (SCM-01 <see cref="TestGitRepository"/>), never the code under
/// test, so setup stays independent of the service being verified.
/// </summary>
public sealed class GitWorktreeMutationTests
{
    // -------------------------------------------------------------------------
    // Required test (FIRST FAILING): create from an existing branch succeeds.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateFromExistingBranchSucceeds()
    {
        using MutationFixture fixture = MutationFixture.Create("create-branch");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string targetPath = fixture.NewTargetPath("wt-branch");

        string branchesBefore = RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/");
        string featureHead = RunGitCapture(fixture.Repo.RootPath, "rev-parse", "refs/heads/feature-x").Trim();

        GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "feature-x"));

        Assert.Equal(GitWorktreeCreateStatus.Succeeded, result.Status);
        Assert.True(Directory.Exists(targetPath), "The worktree directory must exist after a successful create.");

        // The created worktree carries the read-only Git metadata of the ref.
        Assert.NotNull(result.Worktree);
        Assert.Equal("refs/heads/feature-x", result.Worktree!.BranchRef);
        Assert.False(result.Worktree.IsDetached);
        Assert.Equal(featureHead, result.Worktree.HeadCommit);

        // Spec 13 §16.3 / acceptance: no branch is created implicitly.
        Assert.Equal(
            branchesBefore,
            RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/"));

        // Git itself registers the new worktree at the requested location.
        string porcelain = RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain");
        Assert.Contains(PathEquals(targetPath, fixture.Repo.RootPath) ? string.Empty : targetPath.Replace('\\', '/'), porcelain);
    }

    // -------------------------------------------------------------------------
    // Required test: clean worktree removal succeeds.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RemoveCleanWorktreeSucceeds()
    {
        using MutationFixture fixture = MutationFixture.Create("remove-clean");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string targetPath = fixture.NewTargetPath("wt-clean");
        AddDetachedWorktree(fixture, targetPath);

        GitWorktreeRemoveResult result = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, targetPath));

        Assert.Equal(GitWorktreeRemoveStatus.Succeeded, result.Status);
        Assert.False(Directory.Exists(targetPath), "The worktree directory must be gone after a successful removal.");

        // Git itself no longer registers the removed worktree.
        Assert.DoesNotContain(NormalizedPath(targetPath), RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain"));

        Assert.NotNull(result.Worktree);
        AssertSamePath(targetPath, result.Worktree!.Path);
        Assert.NotNull(result.ListResult);
        Assert.DoesNotContain(
            result.ListResult!.Worktrees,
            worktree => PathEquals(worktree.Path, targetPath));
    }

    // -------------------------------------------------------------------------
    // Required test: dirty worktree removal is blocked (typed change list).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RemoveDirtyWorktreeIsBlockedWithTypedChangeList()
    {
        using MutationFixture fixture = MutationFixture.Create("remove-dirty");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());

        // One committed file so the worktree can carry both a tracked
        // modification and an untracked file.
        File.WriteAllText(Path.Combine(fixture.Repo.RootPath, "tracked.txt"), "hello");
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "add", "tracked.txt"));
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "commit", "-m", "tracked"));

        string targetPath = fixture.NewTargetPath("wt-dirty");
        AddDetachedWorktree(fixture, targetPath);
        File.WriteAllText(Path.Combine(targetPath, "tracked.txt"), "changed");
        File.WriteAllText(Path.Combine(targetPath, "untracked.txt"), "new");

        GitWorktreeRemoveResult result = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, targetPath));

        Assert.Equal(GitWorktreeRemoveStatus.DirtyWorktree, result.Status);
        Assert.NotNull(result.Changes);
        Assert.Contains(result.Changes!, change => change.Path == "tracked.txt" && change.HasWorkingTreeChange);
        Assert.Contains(result.Changes!, change => change.Path == "untracked.txt" && change.IsUntracked);
        Assert.NotNull(result.ErrorMessage);

        // Nothing was removed: directory, registration, and Git state survive.
        Assert.True(Directory.Exists(targetPath), "A blocked removal must not delete the worktree directory.");
        Assert.Contains(NormalizedPath(targetPath), RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain"));
        Assert.Contains(
            result.ListResult!.Worktrees,
            worktree => PathEquals(worktree.Path, targetPath));
    }

    // -------------------------------------------------------------------------
    // Edge: the caller may be inside the worktree that is being removed. Git
    // cannot delete its own current directory on Windows ("Permission denied"),
    // so removal must run from the main working tree instead.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RemoveWorktreeWhenCallerIsInsideItSucceeds()
    {
        using MutationFixture fixture = MutationFixture.Create("remove-current");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string targetPath = fixture.NewTargetPath("wt-current");
        AddDetachedWorktree(fixture, targetPath);

        GitWorktreeRemoveResult result = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(targetPath, targetPath));

        Assert.Equal(GitWorktreeRemoveStatus.Succeeded, result.Status);
        Assert.False(Directory.Exists(targetPath), "The worktree directory must be gone after a successful removal.");
    }

    // -------------------------------------------------------------------------
    // Safe refresh/prune: stale metadata of missing unlocked worktrees is
    // pruned; valid and locked worktrees survive (spec 13 §16.5).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RefreshPrunesMissingWorktreeMetadataAndKeepsValidOnes()
    {
        using MutationFixture fixture = MutationFixture.Create("refresh");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string validPath = fixture.NewTargetPath("wt-valid");
        string missingPath = fixture.NewTargetPath("wt-missing");
        string lockedPath = fixture.NewTargetPath("wt-locked");

        AddDetachedWorktree(fixture, validPath);
        AddDetachedWorktree(fixture, missingPath);
        AddDetachedWorktree(fixture, lockedPath);

        Directory.Delete(missingPath, recursive: true);
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "worktree", "lock", lockedPath));
        Directory.Delete(lockedPath, recursive: true);

        GitWorktreeRefreshResult result = await service.RefreshWorktreesAsync(fixture.Repo.RootPath);

        Assert.Equal(GitWorktreeRefreshStatus.Succeeded, result.Status);
        Assert.NotNull(result.ListResult);
        Assert.True(result.ListResult!.IsSuccess, "The refreshed registry must be a successful listing.");

        Assert.Contains(result.ListResult.Worktrees, worktree => PathEquals(worktree.Path, validPath));
        Assert.DoesNotContain(result.ListResult.Worktrees, worktree => PathEquals(worktree.Path, missingPath));
        Assert.Contains(
            result.ListResult.Worktrees,
            worktree => PathEquals(worktree.Path, lockedPath) && worktree.IsLocked);

        // Git itself agrees with the returned registry.
        string porcelain = RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain");
        Assert.Contains(NormalizedPath(validPath), porcelain);
        Assert.DoesNotContain(NormalizedPath(missingPath), porcelain);
        Assert.Contains(NormalizedPath(lockedPath), porcelain);
    }

    [Fact]
    public async Task RefreshRejectsMissingWorkingDirectory()
    {
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());

        await Assert.ThrowsAsync<ArgumentException>(() => service.RefreshWorktreesAsync(" "));
    }

    // -------------------------------------------------------------------------
    // Required test: path normalization/containment is safe on Windows.
    // Every unsafe form is rejected typed BEFORE any Git invocation.
    // -------------------------------------------------------------------------
    public static IEnumerable<object[]> UnsafePaths
    {
        get
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "scm16");
            yield return new object[] { "relative", "scm16-relative-target", false };
            yield return new object[] { "whitespace only", "   ", true };
            yield return new object[] { "parent traversal", Path.Combine(tempRoot, "..", "scm16-escape"), false };
            yield return new object[] { "nested traversal", Path.Combine(tempRoot, "a", "..", "b"), false };
            yield return new object[] { "git metadata segment", Path.Combine(tempRoot, ".git", "wt"), false };
            yield return new object[] { "alternate data stream", Path.Combine(tempRoot, "wt:stream"), false };
            yield return new object[] { "reserved device name", Path.Combine(tempRoot, "CON"), false };
            yield return new object[] { "reserved device name with extension", Path.Combine(tempRoot, "NUL.txt"), false };
            yield return new object[] { "trailing space", Path.Combine(tempRoot, "wt "), false };
            yield return new object[] { "trailing dot", Path.Combine(tempRoot, "wt."), false };
            yield return new object[] { "invalid character", Path.Combine(tempRoot, "wt<bad>"), false };
            yield return new object[] { "device path prefix", @"\\?\C:\scm16-wt", false };
            yield return new object[] { "drive root", Path.GetPathRoot(Path.GetTempPath())!, false };
        }
    }

    [Theory]
    [MemberData(nameof(UnsafePaths))]
    public async Task CreateRejectsUnsafeTargetPathsWithoutInvokingGit(string label, string rawPath, bool isMalformedRequest)
    {
        var runner = new ScriptedGitProcessRunner();
        GitWorktreeMutationService service = CreateService(runner);

        GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(Path.GetTempPath(), rawPath, "feature-x"));

        GitWorktreeCreateStatus expected = isMalformedRequest
            ? GitWorktreeCreateStatus.InvalidRequest
            : GitWorktreeCreateStatus.InvalidTargetPath;
        Assert.Equal(expected, result.Status);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [MemberData(nameof(UnsafePaths))]
    public async Task RemoveRejectsUnsafeWorktreePathsWithoutInvokingGit(string label, string rawPath, bool isMalformedRequest)
    {
        var runner = new ScriptedGitProcessRunner();
        GitWorktreeMutationService service = CreateService(runner);

        GitWorktreeRemoveResult result = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(Path.GetTempPath(), rawPath));

        GitWorktreeRemoveStatus expected = isMalformedRequest
            ? GitWorktreeRemoveStatus.InvalidRequest
            : GitWorktreeRemoveStatus.InvalidTargetPath;
        Assert.Equal(expected, result.Status);
        Assert.Empty(runner.Requests);
    }

    // -------------------------------------------------------------------------
    // Create from tag/commit/remote-tracking ref: detached, never a branch.
    // Spec 13 §16.3 / acceptance: no branch is created implicitly.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateFromTagCommitAndRemoteRefUsesDetachedHeadWithoutCreatingBranches()
    {
        using MutationFixture fixture = MutationFixture.Create("detached-refs");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "tag", "v1.0"));
        Assert.Equal(
            0,
            TestGitRepository.RunGit(fixture.Repo.RootPath, "update-ref", "refs/remotes/origin/topic", fixture.HeadCommit));

        string branchesBefore = RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/");

        (string Ref, string Label)[] cases =
        {
            ("v1.0", "tag"),
            ("refs/tags/v1.0", "qualified-tag"),
            (fixture.HeadCommit, "commit"),
            ("refs/remotes/origin/topic", "remote-tracking"),
        };

        foreach ((string refToken, string label) in cases)
        {
            string targetPath = fixture.NewTargetPath($"wt-{label}");
            GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
                new GitWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, refToken));

            Assert.Equal(GitWorktreeCreateStatus.Succeeded, result.Status);
            Assert.NotNull(result.Worktree);
            Assert.True(result.Worktree!.IsDetached, $"Ref '{refToken}' must produce a detached worktree.");
            Assert.Null(result.Worktree.BranchRef);
            Assert.Equal(fixture.HeadCommit, result.Worktree.HeadCommit);
        }

        Assert.Equal(
            branchesBefore,
            RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/"));
    }

    // -------------------------------------------------------------------------
    // A token that names an existing local branch checks the branch out — even
    // fully qualified, and even when a tag shares the short name (the
    // gitrevisions disambiguation order deterministically prefers the branch).
    // Checking out an EXISTING branch is not creating one.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateFromQualifiedHeadsRefAndAmbiguousShortNameCheckOutTheExistingBranch()
    {
        using MutationFixture fixture = MutationFixture.Create("heads-ref");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "branch", "amb-x"));
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "tag", "amb-x"));
        string ambHead = RunGitCapture(fixture.Repo.RootPath, "rev-parse", "refs/heads/amb-x").Trim();
        string featureHead = RunGitCapture(fixture.Repo.RootPath, "rev-parse", "refs/heads/feature-x").Trim();

        string qualifiedTarget = fixture.NewTargetPath("wt-qualified");
        GitWorktreeCreateResult qualified = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, qualifiedTarget, "refs/heads/feature-x"));

        Assert.Equal(GitWorktreeCreateStatus.Succeeded, qualified.Status);
        Assert.NotNull(qualified.Worktree);
        Assert.Equal("refs/heads/feature-x", qualified.Worktree!.BranchRef);
        Assert.False(qualified.Worktree.IsDetached);
        Assert.Equal(featureHead, qualified.Worktree.HeadCommit);

        string ambiguousTarget = fixture.NewTargetPath("wt-ambiguous");
        GitWorktreeCreateResult ambiguous = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, ambiguousTarget, "amb-x"));

        Assert.Equal(GitWorktreeCreateStatus.Succeeded, ambiguous.Status);
        Assert.NotNull(ambiguous.Worktree);
        Assert.Equal("refs/heads/amb-x", ambiguous.Worktree!.BranchRef);
        Assert.False(ambiguous.Worktree.IsDetached);
        Assert.Equal(ambHead, ambiguous.Worktree.HeadCommit);
    }

    [Fact]
    public async Task CreateIsBlockedWhenBranchIsAlreadyCheckedOutElsewhere()
    {
        using MutationFixture fixture = MutationFixture.Create("branch-in-use");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string firstTarget = fixture.NewTargetPath("wt-first");
        string secondTarget = fixture.NewTargetPath("wt-second");

        GitWorktreeCreateResult first = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, firstTarget, "feature-x"));
        Assert.Equal(GitWorktreeCreateStatus.Succeeded, first.Status);

        GitWorktreeCreateResult second = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, secondTarget, "feature-x"));

        Assert.Equal(GitWorktreeCreateStatus.BranchAlreadyCheckedOut, second.Status);
        Assert.False(Directory.Exists(secondTarget), "A blocked create must not leave a worktree behind.");
        Assert.NotNull(second.ErrorMessage);
        Assert.Contains(firstTarget, second.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateFromMissingRefIsTypedRefNotFound()
    {
        using MutationFixture fixture = MutationFixture.Create("ref-missing");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());

        foreach (string refToken in new[] { "no-such-ref", "origin/remote-only", "refs/tags/absent" })
        {
            string targetPath = fixture.NewTargetPath("wt-noref");
            GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
                new GitWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, refToken));

            Assert.Equal(GitWorktreeCreateStatus.RefNotFound, result.Status);
            Assert.False(Directory.Exists(targetPath), $"Ref '{refToken}' must not create anything.");
        }
    }

    [Fact]
    public async Task CreateRejectsOccupiedTargetsAndSucceedsOnExistingEmptyDirectory()
    {
        using MutationFixture fixture = MutationFixture.Create("occupied");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());

        string filePath = fixture.NewTargetPath("wt-file");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, "occupied");
        GitWorktreeCreateResult fileResult = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, filePath, "feature-x"));
        Assert.Equal(GitWorktreeCreateStatus.TargetAlreadyExists, fileResult.Status);
        Assert.True(File.Exists(filePath), "A blocked create must never delete the occupant.");

        string nonEmptyPath = fixture.NewTargetPath("wt-nonempty");
        Directory.CreateDirectory(nonEmptyPath);
        File.WriteAllText(Path.Combine(nonEmptyPath, "keep.txt"), "keep");
        GitWorktreeCreateResult nonEmptyResult = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, nonEmptyPath, "feature-x"));
        Assert.Equal(GitWorktreeCreateStatus.TargetAlreadyExists, nonEmptyResult.Status);
        Assert.True(File.Exists(Path.Combine(nonEmptyPath, "keep.txt")));

        string registeredPath = fixture.NewTargetPath("wt-registered");
        AddDetachedWorktree(fixture, registeredPath);
        GitWorktreeCreateResult registeredResult = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, registeredPath, "feature-x"));
        Assert.Equal(GitWorktreeCreateStatus.TargetAlreadyExists, registeredResult.Status);

        // Git accepts an existing empty directory as a checkout location.
        string emptyPath = fixture.NewTargetPath("wt-empty");
        Directory.CreateDirectory(emptyPath);
        GitWorktreeCreateResult emptyResult = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, emptyPath, "feature-x"));
        Assert.Equal(GitWorktreeCreateStatus.Succeeded, emptyResult.Status);
        Assert.True(Directory.Exists(emptyPath));
        Assert.NotNull(emptyResult.Worktree);
    }

    // -------------------------------------------------------------------------
    // A failing post-checkout hook makes `git worktree add` exit non-zero while
    // the worktree still exists: the typed failure must not hide that partial
    // state, which stays inspectable through the refreshed registry.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateReportsTypedFailureWhenPostCheckoutHookFails()
    {
        using MutationFixture fixture = MutationFixture.Create("hook-fail");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string targetPath = fixture.NewTargetPath("wt-hook");

        string hookPath = Path.Combine(fixture.Repo.RootPath, ".git", "hooks", "post-checkout");
        File.WriteAllText(hookPath, "#!/bin/sh\nexit 1\n");

        GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "feature-x"));

        Assert.Equal(GitWorktreeCreateStatus.CreateFailed, result.Status);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotNull(result.ProcessResult);
        Assert.Equal(1, result.ProcessResult!.ExitCode);

        // Git's partial state is real and inspectable, not silently erased.
        Assert.True(Directory.Exists(targetPath), "Git leaves the worktree in place when the hook fails.");
        Assert.NotNull(result.ListResult);
        Assert.Contains(
            result.ListResult!.Worktrees,
            worktree => PathEquals(worktree.Path, targetPath));
    }

    [Fact]
    public async Task CreateIntoTargetPathWithSpacesAndUnicodeSucceeds()
    {
        using MutationFixture fixture = MutationFixture.Create("unicode");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string targetPath = fixture.NewTargetPath("wörk trée ünïcode");

        GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "feature-x"));

        Assert.Equal(GitWorktreeCreateStatus.Succeeded, result.Status);
        Assert.True(Directory.Exists(targetPath));
        Assert.NotNull(result.Worktree);
        Assert.Contains(
            result.ListResult!.Worktrees,
            worktree => PathEquals(worktree.Path, targetPath));
    }

    // -------------------------------------------------------------------------
    // Removal refuses main/locked/missing/unregistered worktrees typed.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RemoveRefusesMainLockedMissingAndUnregisteredWorktrees()
    {
        using MutationFixture fixture = MutationFixture.Create("remove-refusals");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());

        GitWorktreeRemoveResult main = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, fixture.Repo.RootPath));
        Assert.Equal(GitWorktreeRemoveStatus.IsMainWorktree, main.Status);
        Assert.True(Directory.Exists(fixture.Repo.RootPath), "The main working tree must survive.");

        string lockedPath = fixture.NewTargetPath("wt-locked");
        AddDetachedWorktree(fixture, lockedPath);
        Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "worktree", "lock", lockedPath));
        GitWorktreeRemoveResult locked = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, lockedPath));
        Assert.Equal(GitWorktreeRemoveStatus.WorktreeLocked, locked.Status);
        Assert.True(Directory.Exists(lockedPath), "A locked worktree must survive V1 removal.");

        string missingPath = fixture.NewTargetPath("wt-missing");
        AddDetachedWorktree(fixture, missingPath);
        Directory.Delete(missingPath, recursive: true);
        GitWorktreeRemoveResult missing = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, missingPath));
        Assert.Equal(GitWorktreeRemoveStatus.Unavailable, missing.Status);
        Assert.Contains(
            NormalizedPath(missingPath),
            RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain"));

        string unregisteredPath = fixture.NewTargetPath("wt-unregistered");
        GitWorktreeRemoveResult unregistered = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, unregisteredPath));
        Assert.Equal(GitWorktreeRemoveStatus.NotRegistered, unregistered.Status);
    }

    // -------------------------------------------------------------------------
    // Typed operational failures: not a repository, cancellation, Git missing.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OperationsReportNotARepositoryOutsideGitRepositories()
    {
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        string directory = Path.Combine(Path.GetTempPath(), "scm16", $"not-a-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            GitWorktreeCreateResult create = await service.CreateWorktreeAsync(
                new GitWorktreeCreateRequest(directory, Path.Combine(directory, "wt"), "feature-x"));
            Assert.Equal(GitWorktreeCreateStatus.NotARepository, create.Status);

            GitWorktreeRemoveResult remove = await service.RemoveWorktreeAsync(
                new GitWorktreeRemoveRequest(directory, directory));
            Assert.Equal(GitWorktreeRemoveStatus.NotARepository, remove.Status);

            GitWorktreeRefreshResult refresh = await service.RefreshWorktreesAsync(directory);
            Assert.Equal(GitWorktreeRefreshStatus.NotARepository, refresh.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledOperationsReturnTypedCancelledResults()
    {
        using MutationFixture fixture = MutationFixture.Create("cancelled");
        GitWorktreeMutationService service = CreateService(new GitProcessRunner());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        string targetPath = fixture.NewTargetPath("wt-cancelled");

        GitWorktreeCreateResult create = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "feature-x"),
            cancelled.Token);
        Assert.Equal(GitWorktreeCreateStatus.Cancelled, create.Status);
        Assert.False(Directory.Exists(targetPath));

        GitWorktreeRemoveResult remove = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(fixture.Repo.RootPath, targetPath),
            cancelled.Token);
        Assert.Equal(GitWorktreeRemoveStatus.Cancelled, remove.Status);

        GitWorktreeRefreshResult refresh = await service.RefreshWorktreesAsync(fixture.Repo.RootPath, cancelled.Token);
        Assert.Equal(GitWorktreeRefreshStatus.Cancelled, refresh.Status);
    }

    [Fact]
    public async Task OperationsReportGitUnavailableWhenGitCannotStart()
    {
        var runner = new ScriptedGitProcessRunner(_ => new GitProcessResult(
            GitProcessStatus.Unavailable,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitNotInstalled,
            "git is not installed"));
        GitWorktreeMutationService service = CreateService(runner);
        string targetPath = Path.Combine(Path.GetTempPath(), "scm16", $"wt-unavailable-{Guid.NewGuid():N}");

        GitWorktreeCreateResult create = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(Path.GetTempPath(), targetPath, "feature-x"));
        Assert.Equal(GitWorktreeCreateStatus.GitUnavailable, create.Status);

        GitWorktreeRemoveResult remove = await service.RemoveWorktreeAsync(
            new GitWorktreeRemoveRequest(Path.GetTempPath(), targetPath));
        Assert.Equal(GitWorktreeRemoveStatus.GitUnavailable, remove.Status);

        GitWorktreeRefreshResult refresh = await service.RefreshWorktreesAsync(Path.GetTempPath());
        Assert.Equal(GitWorktreeRefreshStatus.GitUnavailable, refresh.Status);
    }

    // -------------------------------------------------------------------------
    // Exact argument-vector proofs (scripted runner): structured arguments only,
    // the resolved worktree root as the SCM-01 queue key, the registered path
    // (never the raw caller string) passed to Git, and no branch/force flags.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task CreateInvokesGitWithExactArgumentVectorFromTheResolvedRoot()
    {
        string resolvedRoot = Path.Combine(Path.GetTempPath(), "scm16-scripted", "repo");
        string userDirectory = Path.Combine(resolvedRoot, "sub", "dir");
        string targetPath = Path.Combine(Path.GetTempPath(), "scm16-scripted", $"wt-new-{Guid.NewGuid():N}");
        const string headCommit = "0123456789abcdef0123456789abcdef01234567";

        string mainEntry = $"worktree {NormalizedPath(resolvedRoot)}\nHEAD {headCommit}\nbranch refs/heads/master\n";
        string createdEntry = $"worktree {NormalizedPath(targetPath)}\nHEAD {headCommit}\nbranch refs/heads/feature-x\n";
        int listCalls = 0;

        var runner = new ScriptedGitProcessRunner(request =>
        {
            if (Matches(request, "rev-parse", "--show-toplevel"))
            {
                return ScriptedSucceeded(resolvedRoot + "\n");
            }

            if (Matches(request, "worktree", "list"))
            {
                listCalls++;
                return ScriptedSucceeded(
                    listCalls == 1
                        ? mainEntry + "\n"
                        : mainEntry + "\n" + createdEntry + "\n");
            }

            if (Matches(request, "rev-parse", "--verify"))
            {
                return ScriptedSucceeded(headCommit + "\n");
            }

            if (Matches(request, "worktree", "add"))
            {
                return ScriptedSucceeded();
            }

            throw new InvalidOperationException(
                $"Unexpected Git invocation: {string.Join(' ', request.Arguments)} (cwd '{request.WorkingDirectory}')");
        });
        GitWorktreeMutationService service = CreateService(runner);

        GitWorktreeCreateResult result = await service.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(userDirectory, targetPath, "feature-x"));

        Assert.Equal(GitWorktreeCreateStatus.Succeeded, result.Status);
        Assert.NotNull(result.Worktree);
        Assert.Equal("refs/heads/feature-x", result.Worktree!.BranchRef);

        string[][] expectedArguments =
        {
            new[] { "rev-parse", "--show-toplevel" },
            new[] { "worktree", "list", "--porcelain" },
            new[] { "rev-parse", "--verify", "--quiet", "feature-x^{commit}" },
            new[] { "rev-parse", "--verify", "--quiet", "refs/heads/feature-x" },
            new[] { "worktree", "add", targetPath, "feature-x" },
            new[] { "worktree", "list", "--porcelain" },
        };

        Assert.Equal(expectedArguments.Length, runner.Requests.Count);
        for (int i = 0; i < expectedArguments.Length; i++)
        {
            Assert.Equal(expectedArguments[i], runner.Requests[i].Arguments);
        }

        // Queue-key compliance: the resolution runs where the caller was, and
        // every later invocation runs from the resolved worktree root.
        Assert.Equal(userDirectory, runner.Requests[0].WorkingDirectory);
        Assert.All(
            runner.Requests.Skip(1),
            request => Assert.Equal(resolvedRoot, request.WorkingDirectory));

        // No branch-management or force flags may ever reach Git.
        Assert.DoesNotContain(
            runner.Requests.SelectMany(request => request.Arguments),
            argument => argument is "-b" or "-B" or "--force" or "-f");
    }

    [Fact]
    public async Task RemoveInvokesGitStatusAndRemovalWithExactArgumentsFromTheResolvedRoot()
    {
        string resolvedRoot = Path.Combine(Path.GetTempPath(), "scm16-scripted-remove", "repo");
        string registeredPath = Path.Combine(Path.GetTempPath(), $"scm16-scripted-remove-{Guid.NewGuid():N}");
        const string headCommit = "0123456789abcdef0123456789abcdef01234567";
        Directory.CreateDirectory(registeredPath);

        try
        {
            string listPorcelain =
                $"worktree {NormalizedPath(resolvedRoot)}\nHEAD {headCommit}\nbranch refs/heads/master\n\n" +
                $"worktree {NormalizedPath(registeredPath)}\nHEAD {headCommit}\ndetached\n\n";

            var runner = new ScriptedGitProcessRunner(request =>
            {
                if (Matches(request, "rev-parse", "--show-toplevel"))
                {
                    return ScriptedSucceeded(resolvedRoot + "\n");
                }

                if (Matches(request, "worktree", "list"))
                {
                    return ScriptedSucceeded(listPorcelain);
                }

                if (request.Arguments.Contains("status"))
                {
                    return ScriptedSucceeded($"# branch.oid {headCommit}\0# branch.head (detached)\0");
                }

                if (Matches(request, "worktree", "remove"))
                {
                    return ScriptedSucceeded();
                }

                throw new InvalidOperationException(
                    $"Unexpected Git invocation: {string.Join(' ', request.Arguments)} (cwd '{request.WorkingDirectory}')");
            });
            GitWorktreeMutationService service = CreateService(runner);

            // The caller supplies a differently-cased path; Git's registered
            // canonical location is what must reach Git.
            GitWorktreeRemoveResult result = await service.RemoveWorktreeAsync(
                new GitWorktreeRemoveRequest(resolvedRoot, registeredPath.ToUpperInvariant()));

            Assert.Equal(GitWorktreeRemoveStatus.Succeeded, result.Status);

            string[][] expectedArguments =
            {
                new[] { "rev-parse", "--show-toplevel" },
                new[] { "worktree", "list", "--porcelain" },
                new[] { "--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch" },
                new[] { "worktree", "remove", registeredPath },
                new[] { "worktree", "list", "--porcelain" },
            };

            Assert.Equal(expectedArguments.Length, runner.Requests.Count);
            for (int i = 0; i < expectedArguments.Length; i++)
            {
                Assert.Equal(expectedArguments[i], runner.Requests[i].Arguments);
            }

            // The dirty preflight runs inside the worktree being removed; the
            // registry mutations run from the resolved root (queue key).
            Assert.Equal(registeredPath, runner.Requests[2].WorkingDirectory);
            Assert.Equal(resolvedRoot, runner.Requests[3].WorkingDirectory);
            Assert.Equal(resolvedRoot, runner.Requests[4].WorkingDirectory);

            // Removal is never forced.
            Assert.DoesNotContain(
                runner.Requests.SelectMany(request => request.Arguments),
                argument => argument is "--force" or "-f");
        }
        finally
        {
            if (Directory.Exists(registeredPath))
            {
                Directory.Delete(registeredPath, recursive: true);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AddDetachedWorktree(MutationFixture fixture, string targetPath)
    {
        Assert.Equal(
            0,
            TestGitRepository.RunGit(fixture.Repo.RootPath, "worktree", "add", "--detach", targetPath));
    }

    private static string NormalizedPath(string path) => path.Replace('\\', '/');

    private static bool Matches(GitProcessRequest request, params string[] prefix) =>
        request.Arguments.Count >= prefix.Length
        && prefix.Select((token, index) => request.Arguments[index] == token).All(match => match);

    private static GitProcessResult ScriptedSucceeded(string output = "") =>
        new(GitProcessStatus.Succeeded, 0, output, string.Empty);

    private static GitWorktreeMutationService CreateService(IGitProcessRunner runner) =>
        new(runner, new GitWorktreeService(runner, new FolderSourceService(new InMemoryFolderSourceRepository())));

    private static void AssertSamePath(string expected, string actual) =>
        Assert.True(
            PathEquals(expected, actual),
            $"Expected path '{expected}' but got '{actual}'.");

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string RunGitCapture(string workingDirectory, params string[] arguments) =>
        TestGitRepository.RunGitCapture(workingDirectory, arguments);

    /// <summary>
    /// A disposable Git repository with one commit and a dedicated
    /// <c>feature-x</c> branch; tracks generated target paths so each test's
    /// worktree directories are cleaned up even when the test fails.
    /// </summary>
    private sealed class MutationFixture : IDisposable
    {
        private readonly List<string> _targetPaths = new();

        private MutationFixture(TestGitRepository repo, string headCommit)
        {
            Repo = repo;
            HeadCommit = headCommit;
        }

        public TestGitRepository Repo { get; }

        public string HeadCommit { get; }

        public static MutationFixture Create(string name)
        {
            TestGitRepository repo = TestGitRepository.Create($"scm16-{name}-{Guid.NewGuid():N}");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "--allow-empty", "-m", "initial"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "branch", "feature-x"));

            string headCommit = RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim();
            return new MutationFixture(repo, headCommit);
        }

        /// <summary>Generates (and registers for cleanup) a fresh sibling target path.</summary>
        public string NewTargetPath(string label)
        {
            string path = Path.Combine(Path.GetTempPath(), "scm16", $"{label}-{Guid.NewGuid():N}");
            _targetPaths.Add(path);
            return path;
        }

        public void Dispose()
        {
            foreach (string targetPath in _targetPaths)
            {
                try
                {
                    if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // Best effort: a locked worktree file must not fail the test run.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best effort (same as above).
                }
            }

            Repo.Dispose();
        }
    }

    /// <summary>
    /// Scripted <see cref="IGitProcessRunner"/>: records every request and
    /// answers from an optional responder. With no responder, any Git
    /// invocation fails the test loudly — used to prove that pure validation
    /// never spawns Git.
    /// </summary>
    private sealed class ScriptedGitProcessRunner : IGitProcessRunner
    {
        private readonly Func<GitProcessRequest, GitProcessResult>? _responder;

        public ScriptedGitProcessRunner(Func<GitProcessRequest, GitProcessResult>? responder = null)
        {
            _responder = responder;
        }

        public List<GitProcessRequest> Requests { get; } = new();

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SCM-16 never probes Git capability.");

        public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (_responder is null)
            {
                throw new InvalidOperationException(
                    $"Unexpected Git invocation: git {string.Join(' ', request.Arguments)} (cwd '{request.WorkingDirectory}')");
            }

            return Task.FromResult(_responder(request));
        }
    }

    /// <summary>In-memory <see cref="IFolderSourceRepository"/> so the FS-01 service logic runs without SQLite.</summary>
    private sealed class InMemoryFolderSourceRepository : IFolderSourceRepository
    {
        private readonly List<FolderSource> _sources = new();

        public Task CreateAsync(FolderSource folderSource)
        {
            _sources.Add(folderSource);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FolderSource>> GetByWorkspaceAsync(string workspaceId) =>
            Task.FromResult<IReadOnlyList<FolderSource>>(
                _sources.Where(source => string.Equals(source.WorkspaceId, workspaceId, StringComparison.Ordinal)).ToList());

        public Task UpdateAvailabilityAsync(string id, string availabilityStatus)
        {
            FolderSource? source = _sources.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
            if (source is not null)
            {
                source.AvailabilityStatus = availabilityStatus;
            }

            return Task.CompletedTask;
        }

        public Task UpdateLastReconciledAtAsync(string id, string lastReconciledAt) => Task.CompletedTask;

        public Task DeleteAsync(string id)
        {
            _sources.RemoveAll(source => string.Equals(source.Id, id, StringComparison.Ordinal));
            return Task.CompletedTask;
        }
    }
}
