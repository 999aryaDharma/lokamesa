using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-04 — Markdown Git Diff.
///
/// Required first failing test: "HEAD↔working-tree diff matches fixture."
/// The first test below was the RED stage for that behavior: the diff is
/// materialized read-only from Git (HEAD blob + working-tree file, index blob
/// where the target requires it) and rendered as a deterministic line-based
/// unified diff (spec 13 §9.1/§9.4) without ever writing to the repository.
/// </summary>
public sealed class MarkdownGitDiffTests
{
    // ---------------------------------------------------------------------
    // Step 1/2 — HEAD ↔ working tree materialization + textual rendering
    // ---------------------------------------------------------------------

    [Fact]
    public async Task WorkingTree_HeadToWorkingTreeDiff_MatchesFixture()
    {
        using var repo = TestGitRepository.Create("scm04 plan fixture");
        repo.WriteFile("docs/plan.md", "# Plan\n\nline one\nline two\nline three\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/plan.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "initial"));
        repo.WriteFile("docs/plan.md", "# Plan\n\nline one\nline 2\nline three\nline four\n");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, result.Status);
        Assert.True(result.IsAvailable);
        Assert.Null(result.Failure);

        GitDiff diff = Assert.IsType<GitDiff>(result.Diff);
        Assert.Equal(GitDiffTarget.WorkingTree, diff.Target);
        Assert.Equal("docs/plan.md", diff.Path);
        Assert.Equal("HEAD", diff.LeftLabel);
        Assert.Equal("working tree", diff.RightLabel);
        Assert.True(diff.LeftExists);
        Assert.True(diff.RightExists);
        Assert.False(diff.IsBinary);
        Assert.True(diff.HasChanges);

        Assert.Equal(
            "--- HEAD:docs/plan.md\n" +
            "+++ working tree:docs/plan.md\n" +
            "@@ -1,5 +1,6 @@\n" +
            " # Plan\n" +
            " \n" +
            " line one\n" +
            "-line two\n" +
            "+line 2\n" +
            " line three\n" +
            "+line four\n",
            diff.UnifiedText);

        GitDiffHunk hunk = Assert.Single(diff.Hunks);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(5, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(6, hunk.NewCount);
        Assert.Equal("@@ -1,5 +1,6 @@", hunk.Header);
    }

    // ---------------------------------------------------------------------
    // Step 3 — staged vs working-tree targets
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Staged_HeadToIndexDiff_MatchesStagedFixture_AndUnstagedIsEmptyWhenInSync()
    {
        using var repo = TestGitRepository.Create("scm04 staged fixture");
        repo.WriteFile("docs/plan.md", "# Plan\n\nline one\nline two\nline three\n");
        CommitAll(repo, "initial");
        repo.WriteFile("docs/plan.md", "# Plan\n\nline one\nline 2\nline three\nline four\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/plan.md"));

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult staged = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.Staged);

        Assert.Equal(GitDiffStatus.Available, staged.Status);
        Assert.Equal("HEAD", staged.Diff!.LeftLabel);
        Assert.Equal("index", staged.Diff.RightLabel);
        Assert.True(staged.Diff.LeftExists);
        Assert.True(staged.Diff.RightExists);
        Assert.Equal(
            "--- HEAD:docs/plan.md\n" +
            "+++ index:docs/plan.md\n" +
            "@@ -1,5 +1,6 @@\n" +
            " # Plan\n" +
            " \n" +
            " line one\n" +
            "-line two\n" +
            "+line 2\n" +
            " line three\n" +
            "+line four\n",
            staged.Diff.UnifiedText);

        // The working tree still matches the index, so the unstaged pair has
        // no difference — the staged diff is not a HEAD↔worktree diff.
        GitDiffResult unstaged = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.Unstaged);

        Assert.Equal(GitDiffStatus.Available, unstaged.Status);
        Assert.False(unstaged.Diff!.HasChanges);
        Assert.Equal(string.Empty, unstaged.Diff.UnifiedText);
        Assert.Equal("index", unstaged.Diff.LeftLabel);
        Assert.Equal("working tree", unstaged.Diff.RightLabel);
    }

    [Fact]
    public async Task UnstagedAndWorkingTreeTargets_ShowOnlyTheirOwnVersionPair()
    {
        using var repo = TestGitRepository.Create("scm04 three way");
        repo.WriteFile("docs/plan.md", "one\ntwo\n");
        CommitAll(repo, "initial");

        repo.WriteFile("docs/plan.md", "one\ntwo-staged\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/plan.md"));
        repo.WriteFile("docs/plan.md", "one\ntwo-staged\nthree\n");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult staged = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.Staged);
        Assert.Equal(
            "--- HEAD:docs/plan.md\n" +
            "+++ index:docs/plan.md\n" +
            "@@ -1,2 +1,2 @@\n" +
            " one\n" +
            "-two\n" +
            "+two-staged\n",
            staged.Diff!.UnifiedText);

        GitDiffResult unstaged = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.Unstaged);
        Assert.Equal("index", unstaged.Diff!.LeftLabel);
        Assert.Equal("working tree", unstaged.Diff.RightLabel);
        Assert.Equal(
            "--- index:docs/plan.md\n" +
            "+++ working tree:docs/plan.md\n" +
            "@@ -1,2 +1,3 @@\n" +
            " one\n" +
            " two-staged\n" +
            "+three\n",
            unstaged.Diff.UnifiedText);

        GitDiffResult workingTree = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.WorkingTree);
        Assert.Equal(
            "--- HEAD:docs/plan.md\n" +
            "+++ working tree:docs/plan.md\n" +
            "@@ -1,2 +1,3 @@\n" +
            " one\n" +
            "-two\n" +
            "+two-staged\n" +
            "+three\n",
            workingTree.Diff!.UnifiedText);
    }

    // ---------------------------------------------------------------------
    // Step 4 — edge-case fixtures
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UntrackedFile_DiffsAsCompleteAddition_ForWorkingTreeAndUnstagedTargets()
    {
        using var repo = TestGitRepository.Create("scm04 untracked");
        repo.WriteFile("docs/tracked.md", "tracked\n");
        CommitAll(repo, "initial");
        repo.WriteFile("docs/new.md", "# New\nline\n");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult workingTree = await service.GetFileDiffAsync(
            repo.RootPath, "docs/new.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, workingTree.Status);
        Assert.False(workingTree.Diff!.LeftExists);
        Assert.True(workingTree.Diff.RightExists);
        Assert.Equal(
            "--- /dev/null\n" +
            "+++ working tree:docs/new.md\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+# New\n" +
            "+line\n",
            workingTree.Diff.UnifiedText);

        GitDiffResult unstaged = await service.GetFileDiffAsync(
            repo.RootPath, "docs/new.md", GitDiffTarget.Unstaged);

        Assert.Equal(GitDiffStatus.Available, unstaged.Status);
        Assert.False(unstaged.Diff!.LeftExists);
        Assert.Equal(
            "--- /dev/null\n" +
            "+++ working tree:docs/new.md\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+# New\n" +
            "+line\n",
            unstaged.Diff.UnifiedText);
    }

    [Fact]
    public async Task DeletedWorkingTreeFile_DiffsAsCompleteRemoval()
    {
        using var repo = TestGitRepository.Create("scm04 deleted");
        repo.WriteFile("docs/gone.md", "keep\nremove me\n");
        CommitAll(repo, "initial");
        File.Delete(Path.Combine(repo.RootPath, "docs", "gone.md"));

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, "docs/gone.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, result.Status);
        Assert.True(result.Diff!.LeftExists);
        Assert.False(result.Diff.RightExists);
        Assert.Equal(
            "--- HEAD:docs/gone.md\n" +
            "+++ /dev/null\n" +
            "@@ -1,2 +0,0 @@\n" +
            "-keep\n" +
            "-remove me\n",
            result.Diff.UnifiedText);
    }

    [Fact]
    public async Task NoTrailingNewline_IsMarked_AfterBothChangedSides()
    {
        using var repo = TestGitRepository.Create("scm04 no newline");
        repo.WriteFile("docs/nl.md", "aaa\nbbb");
        CommitAll(repo, "initial");
        repo.WriteFile("docs/nl.md", "aaa\nccc");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, "docs/nl.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, result.Status);
        Assert.Equal(
            "--- HEAD:docs/nl.md\n" +
            "+++ working tree:docs/nl.md\n" +
            "@@ -1,2 +1,2 @@\n" +
            " aaa\n" +
            "-bbb\n" +
            "\\ No newline at end of file\n" +
            "+ccc\n" +
            "\\ No newline at end of file\n",
            result.Diff!.UnifiedText);

        Assert.True(result.Diff.Hunks[0].Lines[1].NoNewlineAtEndOfFile);
        Assert.True(result.Diff.Hunks[0].Lines[2].NoNewlineAtEndOfFile);
        Assert.False(result.Diff.Hunks[0].Lines[0].NoNewlineAtEndOfFile);
    }

    [Fact]
    public async Task UnicodeAndSpecialCharacterPathAndContent_AreDiffedWithoutMangling()
    {
        const string path = "docs/notes caf\u00e9 \u65e5\u672c\u8a9e (v2) & more.md";
        using var repo = TestGitRepository.Create("scm04 unicode");
        repo.WriteFile(path, "# \u00dcbersicht\n\n- caf\u00e9\n- na\u00efve\n");
        CommitAll(repo, "initial");
        repo.WriteFile(path, "# \u00dcbersicht\n\n- caf\u00e9\n- na\u00efve\n- \u65e5\u672c\u8a9e\u306e\u30e1\u30e2\n");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, path, GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, result.Status);
        Assert.Equal(path, result.Diff!.Path);
        // The appended line is within the trailing context window: the hunk
        // covers the last three old lines (2..4) plus the addition.
        Assert.Equal(
            $"--- HEAD:{path}\n" +
            $"+++ working tree:{path}\n" +
            "@@ -2,3 +2,4 @@\n" +
            " \n" +
            " - caf\u00e9\n" +
            " - na\u00efve\n" +
            "+- \u65e5\u672c\u8a9e\u306e\u30e1\u30e2\n",
            result.Diff.UnifiedText);
    }

    [Fact]
    public async Task LeadingDashFilename_IsTreatedAsAPath_NeverAsAnOption()
    {
        using var repo = TestGitRepository.Create("scm04 dash");
        repo.WriteFile("-dash-note.md", "v1\n");
        CommitAll(repo, "initial");
        repo.WriteFile("-dash-note.md", "v2\n");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, "-dash-note.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, result.Status);
        Assert.Equal(
            "--- HEAD:-dash-note.md\n" +
            "+++ working tree:-dash-note.md\n" +
            "@@ -1 +1 @@\n" +
            "-v1\n" +
            "+v2\n",
            result.Diff!.UnifiedText);
    }

    [Fact]
    public async Task BinaryContent_IsReportedAsBinary_InsteadOfPretendingATextDiff()
    {
        using var repo = TestGitRepository.Create("scm04 binary");
        string assetPath = Path.Combine(repo.RootPath, "docs", "asset.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllBytes(assetPath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02 });
        CommitAll(repo, "initial");
        File.WriteAllBytes(assetPath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x99, 0x9A });

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, "docs/asset.bin", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Available, result.Status);
        Assert.True(result.Diff!.IsBinary);
        Assert.True(result.Diff.LeftExists);
        Assert.True(result.Diff.RightExists);
        Assert.Empty(result.Diff.Hunks);
        Assert.Equal(string.Empty, result.Diff.UnifiedText);
        Assert.False(result.Diff.HasChanges);
    }

    [Fact]
    public async Task RenamedFile_StagedDiff_UsesOriginalPathForTheHeadSide()
    {
        using var repo = TestGitRepository.Create("scm04 rename");
        repo.WriteFile("docs/old.md", "# Doc\nkeep\nchange me\n");
        CommitAll(repo, "initial");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "mv", "docs/old.md", "docs/new.md"));
        repo.WriteFile("docs/new.md", "# Doc\nkeep\nchanged!\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/new.md"));

        var service = new GitDiffService(new GitProcessRunner());

        // Without the original path the new path simply does not exist in HEAD.
        GitDiffResult withoutOriginal = await service.GetFileDiffAsync(
            repo.RootPath, "docs/new.md", GitDiffTarget.Staged);
        Assert.Equal(GitDiffStatus.Available, withoutOriginal.Status);
        Assert.False(withoutOriginal.Diff!.LeftExists);

        // With the original path from Git status, only the content change shows.
        GitDiffResult withOriginal = await service.GetFileDiffAsync(
            repo.RootPath, "docs/new.md", GitDiffTarget.Staged, originalPath: "docs/old.md");

        Assert.Equal(GitDiffStatus.Available, withOriginal.Status);
        Assert.True(withOriginal.Diff!.LeftExists);
        Assert.Equal(
            "--- HEAD:docs/new.md\n" +
            "+++ index:docs/new.md\n" +
            "@@ -1,3 +1,3 @@\n" +
            " # Doc\n" +
            " keep\n" +
            "-change me\n" +
            "+changed!\n",
            withOriginal.Diff.UnifiedText);
    }

    [Fact]
    public async Task UnmergedFile_StagedDiff_YieldsTypedConflictFailure_ButWorkingTreeStaysInspectable()
    {
        using var repo = TestGitRepository.Create("scm04 conflict");
        repo.WriteFile("docs/c.md", "base\n");
        CommitAll(repo, "initial");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "-b", "side"));
        repo.WriteFile("docs/c.md", "side\n");
        CommitAll(repo, "side");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "-"));
        repo.WriteFile("docs/c.md", "main\n");
        CommitAll(repo, "main");

        // A conflicted merge exits non-zero by design; that is the fixture state.
        TestGitRepository.RunGit(repo.RootPath, "merge", "--no-edit", "side");

        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult staged = await service.GetFileDiffAsync(
            repo.RootPath, "docs/c.md", GitDiffTarget.Staged);

        Assert.Equal(GitDiffStatus.Failed, staged.Status);
        Assert.Null(staged.Diff);
        Assert.NotNull(staged.Failure);
        Assert.Equal(GitProcessErrorKind.GitConflict, staged.Failure!.ErrorKind);
        Assert.Contains("unmerged", staged.Failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Spec 13 §21: available versions stay inspectable during a conflict.
        GitDiffResult workingTree = await service.GetFileDiffAsync(
            repo.RootPath, "docs/c.md", GitDiffTarget.WorkingTree);
        Assert.Equal(GitDiffStatus.Available, workingTree.Status);
        Assert.Contains("<<<<<<<", workingTree.Diff!.UnifiedText);
    }

    // ---------------------------------------------------------------------
    // Typed failures and path safety
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EscapingOrRootedPaths_AreRejected_BeforeAnyGitOrFileAccess()
    {
        using var repo = TestGitRepository.Create("scm04 escapes");
        repo.WriteFile("docs/plan.md", "content\n");
        CommitAll(repo, "initial");

        var service = new GitDiffService(new GitProcessRunner());

        string[] attacks =
        {
            "../outside.md",
            "..\\outside.md",
            "docs/../secrets.md",
            "/etc/passwd",
            "C:/Windows/win.ini",
            "C:\\Windows\\win.ini",
            ".",
            "..",
            "docs//plan.md",
            "docs/plan.md:stream",
        };

        foreach (string attack in attacks)
        {
            GitDiffResult result = await service.GetFileDiffAsync(
                repo.RootPath, attack, GitDiffTarget.WorkingTree);

            Assert.Equal(GitDiffStatus.PathRejected, result.Status);
            Assert.Null(result.Diff);
            Assert.NotNull(result.Failure);
            Assert.False(string.IsNullOrWhiteSpace(result.Failure!.ErrorMessage));
        }

        GitDiffResult originalEscape = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.Staged, originalPath: "..\\outside.md");
        Assert.Equal(GitDiffStatus.PathRejected, originalEscape.Status);
    }

    [Fact]
    public async Task MissingGitExecutable_YieldsTypedGitUnavailableState()
    {
        using var repo = TestGitRepository.Create("scm04 missing git");
        repo.WriteFile("docs/plan.md", "content\n");
        CommitAll(repo, "initial");

        var options = new GitProcessRunnerOptions
        {
            ExecutablePath = "scm04-definitely-missing-git-executable",
        };
        var service = new GitDiffService(new GitProcessRunner(options));

        GitDiffResult result = await service.GetFileDiffAsync(
            repo.RootPath, "docs/plan.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.GitUnavailable, result.Status);
        Assert.Null(result.Diff);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, result.Failure!.ErrorKind);
    }

    [Fact]
    public async Task NonGitFolder_YieldsTypedNoRepositoryState()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm04", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var service = new GitDiffService(new GitProcessRunner());

            GitDiffResult result = await service.GetFileDiffAsync(
                folder, "notes.md", GitDiffTarget.WorkingTree);

            Assert.Equal(GitDiffStatus.NoRepository, result.Status);
            Assert.Null(result.Diff);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.Failure!.ErrorKind);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task DisappearedWorktree_YieldsTypedFailure_InsteadOfException()
    {
        string missing = Path.Combine(Path.GetTempPath(), "scm04", $"missing-{Guid.NewGuid():N}");
        var service = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await service.GetFileDiffAsync(
            missing, "notes.md", GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.Failed, result.Status);
        Assert.Null(result.Diff);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, result.Failure!.ErrorKind);
    }

    [Fact]
    public async Task Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists()
    {
        using var repo = TestGitRepository.Create("scm04 readonly");
        repo.WriteFile("docs/plan.md", "# Plan\nline\n");
        CommitAll(repo, "initial");
        repo.WriteFile("docs/plan.md", "# Plan\nline changed\n");
        repo.WriteFile("docs/second.md", "staged\n");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/second.md"));

        // Capture repository state after the fixture's own Git writes settled.
        string indexFile = Path.Combine(repo.RootPath, ".git", "index");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "status", "--porcelain=v2", "-z"));
        byte[] indexBefore = File.ReadAllBytes(indexFile);
        byte[] worktreeBefore = File.ReadAllBytes(Path.Combine(repo.RootPath, "docs", "plan.md"));
        string statusBefore = TestGitRepository.RunGitCapture(repo.RootPath, "status", "--porcelain=v2");

        // A foreign Git process holds the index lock: the read-only diff must
        // not need it (spec 13 §18 "no-optional-locks" / §20 lock contention).
        string indexLock = indexFile + ".lock";
        File.WriteAllText(indexLock, "held by another git process");

        try
        {
            var service = new GitDiffService(new GitProcessRunner());

            GitDiffResult workingTree = await service.GetFileDiffAsync(
                repo.RootPath, "docs/plan.md", GitDiffTarget.WorkingTree);
            GitDiffResult staged = await service.GetFileDiffAsync(
                repo.RootPath, "docs/second.md", GitDiffTarget.Staged);
            GitDiffResult unstaged = await service.GetFileDiffAsync(
                repo.RootPath, "docs/plan.md", GitDiffTarget.Unstaged);

            Assert.Equal(GitDiffStatus.Available, workingTree.Status);
            Assert.Equal(GitDiffStatus.Available, staged.Status);
            Assert.Equal(GitDiffStatus.Available, unstaged.Status);
            Assert.True(workingTree.Diff!.HasChanges);
            Assert.True(staged.Diff!.HasChanges);
        }
        finally
        {
            File.Delete(indexLock);
        }

        // Not one byte of the repository changed: index, worktree file and
        // Git status are exactly as they were before the diff calls.
        Assert.Equal(indexBefore, File.ReadAllBytes(indexFile));
        Assert.Equal(worktreeBefore, File.ReadAllBytes(Path.Combine(repo.RootPath, "docs", "plan.md")));
        Assert.Equal(statusBefore, TestGitRepository.RunGitCapture(repo.RootPath, "status", "--porcelain=v2"));
        Assert.False(File.Exists(indexLock));
    }

    // ---------------------------------------------------------------------
    // Pure formatter contracts (Git conventions pinned without a repository)
    // ---------------------------------------------------------------------

    [Fact]
    public void Formatter_EmptyVersions_UseGitConventionHunkHeaders()
    {
        IReadOnlyList<GitDiffHunk> additions = GitUnifiedDiffFormatter.ComputeHunks(
            Array.Empty<string>(), false, new[] { "a", "b" }, true);

        GitDiffHunk addition = Assert.Single(additions);
        Assert.Equal("@@ -0,0 +1,2 @@", addition.Header);
        Assert.Equal(0, addition.OldStart);
        Assert.Equal(0, addition.OldCount);
        Assert.Equal(1, addition.NewStart);
        Assert.Equal(2, addition.NewCount);
        Assert.All(addition.Lines, line => Assert.Equal(GitDiffLineKind.Added, line.Kind));

        IReadOnlyList<GitDiffHunk> removals = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "a", "b" }, true, Array.Empty<string>(), false);

        GitDiffHunk removal = Assert.Single(removals);
        Assert.Equal("@@ -1,2 +0,0 @@", removal.Header);
        Assert.Equal(0, removal.NewStart);
        Assert.Equal(0, removal.NewCount);
        Assert.All(removal.Lines, line => Assert.Equal(GitDiffLineKind.Removed, line.Kind));
    }

    [Fact]
    public void Formatter_ElidesSingleLineCounts_InHunkHeaders()
    {
        IReadOnlyList<GitDiffHunk> hunks = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "v1" }, false, new[] { "v2" }, false);

        GitDiffHunk hunk = Assert.Single(hunks);
        Assert.Equal("@@ -1 +1 @@", hunk.Header);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(1, hunk.NewCount);
    }

    [Fact]
    public void Formatter_MergesAndSplitsHunks_AtTheTwiceContextBoundary()
    {
        string[] lines = new string[20];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = (i + 1).ToString();
        }

        // Changes at 1-based lines 1 and 8: six unchanged lines between them
        // (2..7) — the contexts touch and merge into one hunk.
        string[] merged = (string[])lines.Clone();
        merged[0] = "X1";
        merged[7] = "X8";
        IReadOnlyList<GitDiffHunk> mergedHunks = GitUnifiedDiffFormatter.ComputeHunks(
            lines, true, merged, true);
        GitDiffHunk singleHunk = Assert.Single(mergedHunks);
        Assert.Equal("@@ -1,11 +1,11 @@", singleHunk.Header);

        // Changes at lines 1 and 9: seven unchanged lines between them — two
        // separate hunks, exactly as Git reports them.
        string[] split = (string[])lines.Clone();
        split[0] = "X1";
        split[8] = "X9";
        IReadOnlyList<GitDiffHunk> splitHunks = GitUnifiedDiffFormatter.ComputeHunks(
            lines, true, split, true);
        Assert.Equal(2, splitHunks.Count);
        Assert.Equal("@@ -1,4 +1,4 @@", splitHunks[0].Header);
        Assert.Equal("@@ -6,7 +6,7 @@", splitHunks[1].Header);
    }

    [Fact]
    public void Formatter_TrailingNewlineChanges_AreVisible()
    {
        // "a\n" -> "a": the newline removal is shown by git convention.
        IReadOnlyList<GitDiffHunk> removing = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "a" }, true, new[] { "a" }, false);
        Assert.Equal(
            "--- HEAD:n.md\n" +
            "+++ working tree:n.md\n" +
            "@@ -1 +1 @@\n" +
            "-a\n" +
            "+a\n" +
            "\\ No newline at end of file\n",
            GitUnifiedDiffFormatter.FormatUnifiedText(
                "n.md", "HEAD", "working tree", true, true, removing));

        // "a" -> "a\n": the newline addition is shown symmetrically.
        IReadOnlyList<GitDiffHunk> adding = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "a" }, false, new[] { "a" }, true);
        Assert.Equal(
            "--- HEAD:n.md\n" +
            "+++ working tree:n.md\n" +
            "@@ -1 +1 @@\n" +
            "-a\n" +
            "\\ No newline at end of file\n" +
            "+a\n",
            GitUnifiedDiffFormatter.FormatUnifiedText(
                "n.md", "HEAD", "working tree", true, true, adding));
    }

    [Fact]
    public void Formatter_MarksContextLine_WhenBothVersionsEndWithoutNewline()
    {
        IReadOnlyList<GitDiffHunk> hunks = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "x", "a" }, false, new[] { "y", "a" }, false);

        Assert.Equal(
            "--- HEAD:n.md\n" +
            "+++ working tree:n.md\n" +
            "@@ -1,2 +1,2 @@\n" +
            "-x\n" +
            "+y\n" +
            " a\n" +
            "\\ No newline at end of file\n",
            GitUnifiedDiffFormatter.FormatUnifiedText(
                "n.md", "HEAD", "working tree", true, true, hunks));
    }

    [Fact]
    public void Formatter_TraceBudgetGuard_EmitsCorrectCoarseReplacement()
    {
        IReadOnlyList<GitDiffHunk> hunks = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "a", "b", "c" },
            true,
            new[] { "x", "y", "z" },
            true,
            maxTraceCells: 5);

        GitDiffHunk hunk = Assert.Single(hunks);
        Assert.Equal("@@ -1,3 +1,3 @@", hunk.Header);
        Assert.Equal(
            "--- HEAD:n.md\n" +
            "+++ working tree:n.md\n" +
            "@@ -1,3 +1,3 @@\n" +
            "-a\n" +
            "-b\n" +
            "-c\n" +
            "+x\n" +
            "+y\n" +
            "+z\n",
            GitUnifiedDiffFormatter.FormatUnifiedText(
                "n.md", "HEAD", "working tree", true, true, hunks));
    }

    [Fact]
    public void Formatter_EqualVersions_ProduceNoHunksAndNoText()
    {
        IReadOnlyList<GitDiffHunk> hunks = GitUnifiedDiffFormatter.ComputeHunks(
            new[] { "same", "" }, true, new[] { "same", "" }, true);

        Assert.Empty(hunks);
        Assert.Equal(
            string.Empty,
            GitUnifiedDiffFormatter.FormatUnifiedText("n.md", "HEAD", "working tree", true, true, hunks));
    }

    private static void CommitAll(TestGitRepository repo, string message)
    {
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", message));
    }
}
