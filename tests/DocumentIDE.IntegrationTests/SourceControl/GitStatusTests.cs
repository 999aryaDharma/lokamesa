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
/// SCM-02 — Git Repository, Worktree &amp; Status Domain.
///
/// Required first failing test: "Repository/worktree discovery fixtures pass."
/// The discovery fixtures below are the RED stage for repository/worktree
/// discovery (spec 13 §5): discovery is derived through Git itself
/// (<c>git rev-parse</c> family) and a non-Git folder yields a typed
/// no-repository state rather than an exception or silent fallback.
/// </summary>
public sealed class GitStatusTests
{
    // ---------------------------------------------------------------------
    // Step 1 — repository/worktree discovery
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Repository_Worktree_Discovery_Fixtures_Pass()
    {
        using var repo = TestGitRepository.Create("scm02 repo \u00fc (v1)");
        EnsureBranch(repo, "main");
        repo.WriteFile("docs/notes.md", "# notes");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/notes.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "initial"));

        var service = new GitRepositoryService(new GitProcessRunner());

        // Discovery must work from the worktree root, from a nested directory,
        // and from a file path (the active-document case, spec 13 §5).
        foreach (string startPath in new[]
                 {
                     repo.RootPath,
                     Path.Combine(repo.RootPath, "docs"),
                     Path.Combine(repo.RootPath, "docs", "notes.md"),
                 })
        {
            var context = await service.DiscoverAsync(startPath);

            Assert.Equal(GitRepositoryStatus.Available, context.Status);
            Assert.True(context.HasRepository);
            Assert.Equal(Path.GetFullPath(repo.RootPath), context.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(repo.RootPath), context.RepositoryRoot, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(
                Path.Combine(Path.GetFullPath(repo.RootPath), ".git"),
                context.GitDirReference,
                StringComparer.OrdinalIgnoreCase);
            Assert.Equal("main", context.BranchName);
            Assert.False(context.IsDetached);
            Assert.False(string.IsNullOrWhiteSpace(context.HeadCommit));
            Assert.Null(context.UpstreamReference);
            Assert.Null(context.Failure);
        }
    }

    [Fact]
    public async Task NonGitFolder_Discovery_Returns_TypedNoRepositoryState()
    {
        string folder = CreatePlainFolder();

        try
        {
            var service = new GitRepositoryService(new GitProcessRunner());

            var context = await service.DiscoverAsync(folder);

            Assert.Equal(GitRepositoryStatus.NoRepository, context.Status);
            Assert.False(context.HasRepository);
            Assert.Null(context.WorktreeRoot);
            Assert.NotNull(context.Failure);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, context.Failure!.ErrorKind);
            Assert.False(string.IsNullOrWhiteSpace(context.Failure.ErrorMessage));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Discovery_MissingGitExecutable_YieldsTypedGitUnavailableState()
    {
        string folder = CreatePlainFolder();

        try
        {
            var options = new GitProcessRunnerOptions { ExecutablePath = "scm02-definitely-missing-git-executable" };
            var service = new GitRepositoryService(new GitProcessRunner(options));

            var context = await service.DiscoverAsync(folder);

            Assert.Equal(GitRepositoryStatus.GitUnavailable, context.Status);
            Assert.NotNull(context.Failure);
            Assert.Equal(GitProcessErrorKind.GitNotInstalled, context.Failure!.ErrorKind);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Discovery_MissingDirectory_YieldsTypedFailure_InsteadOfException()
    {
        string missing = Path.Combine(
            Path.GetTempPath(), "scm02", $"missing-{Guid.NewGuid():N}");

        var service = new GitRepositoryService(new GitProcessRunner());

        var context = await service.DiscoverAsync(missing);

        Assert.Equal(GitRepositoryStatus.Failed, context.Status);
        Assert.NotNull(context.Failure);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, context.Failure!.ErrorKind);
    }

    [Fact]
    public async Task Discovery_LinkedWorktree_ResolvesRepositoryRootAndWorktreeRootSeparately()
    {
        using var repo = TestGitRepository.Create("scm02-main \u00fc");
        EnsureBranch(repo, "main");
        repo.WriteFile("a.md", "a");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "a.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "init"));

        string linked = Path.Combine(
            Path.GetTempPath(), "scm02-linked", $"wt-{Guid.NewGuid():N}");

        try
        {
            Assert.Equal(
                0,
                TestGitRepository.RunGit(
                    repo.RootPath, "worktree", "add", "-q", linked, "-b", "scm02-linked-branch"));

            var service = new GitRepositoryService(new GitProcessRunner());

            var context = await service.DiscoverAsync(linked);

            Assert.Equal(GitRepositoryStatus.Available, context.Status);
            Assert.Equal(Path.GetFullPath(linked), context.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(repo.RootPath), context.RepositoryRoot, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("scm02-linked-branch", context.BranchName);
        }
        finally
        {
            TestGitRepository.RunGit(repo.RootPath, "worktree", "remove", "--force", linked);
            if (Directory.Exists(linked))
            {
                Directory.Delete(linked, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Steps 2/3 — porcelain v2 status parsing into typed domain records
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Status_PorcelainV2_Covers_Staged_Unstaged_Untracked_And_Conflict_States()
    {
        using var repo = TestGitRepository.Create("scm02 status \u00fc (v1)");
        EnsureBranch(repo, "main");
        repo.WriteFile("clean.md", "clean");
        repo.WriteFile("unstaged.md", "v1");
        repo.WriteFile("both.md", "v1");
        repo.WriteFile("rename-source.md", "rename me");
        repo.WriteFile("conflict.md", "base");
        Assert.Equal(
            0,
            TestGitRepository.RunGit(
                repo.RootPath, "add", "--",
                "clean.md", "unstaged.md", "both.md", "rename-source.md", "conflict.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "base"));

        // Unmerged (conflict) state — established while the index still
        // matches HEAD (merge requires that; later states are created after).
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "side"));
        repo.WriteFile("conflict.md", "side-change");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "conflict.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "side"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        repo.WriteFile("conflict.md", "main-change");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "conflict.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "main"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));

        // Unstaged modification of a clean tracked file.
        repo.WriteFile("unstaged.md", "v2");
        // Staged addition of a new file.
        repo.WriteFile("staged-new.md", "new");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "staged-new.md"));
        // Staged and then modified again: index and working tree diverge.
        repo.WriteFile("both.md", "v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "both.md"));
        repo.WriteFile("both.md", "v3");
        // Untracked file.
        repo.WriteFile("untracked-new.md", "u");
        // Staged rename.
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "mv", "rename-source.md", "renamed.md"));

        var service = new GitRepositoryService(new GitProcessRunner());

        var result = await service.GetStatusAsync(repo.RootPath);

        Assert.Equal(GitRepositoryStatus.Available, result.Status);
        Assert.True(result.IsAvailable);
        GitStatusSnapshot snapshot = result.Snapshot!;
        Assert.Equal(Path.GetFullPath(repo.RootPath), snapshot.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("main", snapshot.BranchName);
        Assert.False(snapshot.IsDetached);
        Assert.Equal(HeadCommit(repo), snapshot.HeadCommit);
        Assert.False(snapshot.HasUpstream);

        // Unmodified tracked files are not reported.
        Assert.DoesNotContain(snapshot.Entries, e => e.Path == "clean.md");

        GitStatusEntry unstaged = SingleEntry(snapshot, "unstaged.md");
        Assert.Equal(GitStatusEntryType.Ordinary, unstaged.EntryType);
        Assert.Equal(GitChangeKind.Unmodified, unstaged.IndexChange);
        Assert.Equal(GitChangeKind.Modified, unstaged.WorkingTreeChange);
        Assert.False(unstaged.HasStagedChange);
        Assert.True(unstaged.HasWorkingTreeChange);
        Assert.Equal(GitFileState.Modified, unstaged.State);

        GitStatusEntry staged = SingleEntry(snapshot, "staged-new.md");
        Assert.Equal(GitChangeKind.Added, staged.IndexChange);
        Assert.Equal(GitChangeKind.Unmodified, staged.WorkingTreeChange);
        Assert.True(staged.HasStagedChange);
        Assert.False(staged.HasWorkingTreeChange);
        Assert.Equal(GitFileState.Added, staged.State);

        GitStatusEntry both = SingleEntry(snapshot, "both.md");
        Assert.Equal(GitChangeKind.Modified, both.IndexChange);
        Assert.Equal(GitChangeKind.Modified, both.WorkingTreeChange);
        Assert.True(both.HasStagedChange);
        Assert.True(both.HasWorkingTreeChange);

        GitStatusEntry untracked = SingleEntry(snapshot, "untracked-new.md");
        Assert.True(untracked.IsUntracked);
        Assert.Equal(GitFileState.Untracked, untracked.State);
        Assert.False(untracked.HasStagedChange);
        Assert.False(untracked.HasWorkingTreeChange);

        GitStatusEntry renamed = SingleEntry(snapshot, "renamed.md");
        Assert.Equal(GitStatusEntryType.RenamedOrCopied, renamed.EntryType);
        Assert.Equal("rename-source.md", renamed.OriginalPath);
        Assert.Equal(GitChangeKind.Renamed, renamed.IndexChange);
        Assert.True(renamed.HasStagedChange);
        Assert.Equal(GitFileState.Renamed, renamed.State);

        GitStatusEntry conflict = SingleEntry(snapshot, "conflict.md");
        Assert.True(conflict.IsUnmerged);
        Assert.True(conflict.IsConflicted);
        Assert.Equal(GitConflictKind.BothModified, conflict.ConflictKind);
        Assert.Equal(GitFileState.Conflicted, conflict.State);
        Assert.Equal("UU", conflict.StatusCharacters);
        // A conflicted file is neither a normal staged nor a working-tree change.
        Assert.False(conflict.HasStagedChange);
        Assert.False(conflict.HasWorkingTreeChange);
    }

    [Fact]
    public async Task Status_Distinguishes_IndexChanges_From_WorkingTreeChanges_ForTheSameFile()
    {
        using var repo = TestGitRepository.Create();
        EnsureBranch(repo, "main");
        repo.WriteFile("report.md", "v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "report.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "base"));

        // Stage v2, then write v3 on top: the index holds v2, the working tree
        // holds v3. Status must expose both sides independently (spec 13 §8/§10).
        repo.WriteFile("report.md", "v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "report.md"));
        repo.WriteFile("report.md", "v3");

        var service = new GitRepositoryService(new GitProcessRunner());
        var snapshot = (await service.GetStatusAsync(repo.RootPath)).Snapshot!;

        GitStatusEntry entry = SingleEntry(snapshot, "report.md");
        Assert.Equal("MM", entry.StatusCharacters);
        Assert.Equal(GitChangeKind.Modified, entry.IndexChange);
        Assert.Equal(GitChangeKind.Modified, entry.WorkingTreeChange);
        Assert.True(entry.HasStagedChange);
        Assert.True(entry.HasWorkingTreeChange);
    }

    [Fact]
    public void Parser_PorcelainV2Z_Covers_TypedStates_For_All_EntryRecordTypes()
    {
        string output = Porcelain(
            "# branch.oid 1111111111111111111111111111111111111111",
            "# branch.head feature/x",
            "# branch.upstream origin/feature/x",
            "# branch.ab +2 -3",
            "# stash 2",
            "1 M. N... 100644 100644 100644 aaaa bbbb staged.txt",
            "1 .M N... 100644 100644 100644 aaaa bbbb unstaged.txt",
            "1 MM N... 100644 100644 100644 aaaa bbbb both sides.txt",
            "1 .T N... 100644 100644 100644 aaaa bbbb type \u00fc changed.md",
            "2 R. N... 100644 100644 100644 aaaa bbbb R100 renamed file.md",
            "old name.md",
            "2 C. N... 100644 100644 100644 aaaa bbbb C75 copied file.md",
            "copy source.md",
            "? untracked with spaces \u65e5\u672c\u8a9e.md",
            "! ignored.md");

        GitStatusSnapshot snapshot = GitPorcelainV2Parser.Parse(@"C:\work trees\repo \u00fc", output);

        Assert.Equal(@"C:\work trees\repo \u00fc", snapshot.WorktreeRoot);
        Assert.Equal("feature/x", snapshot.BranchName);
        Assert.False(snapshot.IsDetached);
        Assert.Equal("1111111111111111111111111111111111111111", snapshot.HeadCommit);
        Assert.Equal("origin/feature/x", snapshot.UpstreamReference);
        Assert.True(snapshot.HasUpstream);
        Assert.Equal(2, snapshot.Ahead);
        Assert.Equal(3, snapshot.Behind);
        Assert.Equal(8, snapshot.Entries.Count);

        GitStatusEntry staged = SingleEntry(snapshot, "staged.txt");
        Assert.Equal(GitChangeKind.Modified, staged.IndexChange);
        Assert.Equal(GitChangeKind.Unmodified, staged.WorkingTreeChange);
        Assert.Equal("M.", staged.StatusCharacters);

        GitStatusEntry unstaged = SingleEntry(snapshot, "unstaged.txt");
        Assert.Equal(GitChangeKind.Unmodified, unstaged.IndexChange);
        Assert.Equal(GitChangeKind.Modified, unstaged.WorkingTreeChange);

        GitStatusEntry both = SingleEntry(snapshot, "both sides.txt");
        Assert.True(both.HasStagedChange);
        Assert.True(both.HasWorkingTreeChange);

        GitStatusEntry typeChanged = SingleEntry(snapshot, "type \u00fc changed.md");
        Assert.Equal(GitChangeKind.TypeChanged, typeChanged.WorkingTreeChange);
        Assert.Equal(GitFileState.TypeChanged, typeChanged.State);

        GitStatusEntry renamed = SingleEntry(snapshot, "renamed file.md");
        Assert.Equal(GitStatusEntryType.RenamedOrCopied, renamed.EntryType);
        Assert.Equal("old name.md", renamed.OriginalPath);
        Assert.Equal(GitChangeKind.Renamed, renamed.IndexChange);
        Assert.Equal(GitFileState.Renamed, renamed.State);

        GitStatusEntry copied = SingleEntry(snapshot, "copied file.md");
        Assert.Equal(GitChangeKind.Copied, copied.IndexChange);
        Assert.Equal("copy source.md", copied.OriginalPath);
        Assert.Equal(GitFileState.Copied, copied.State);

        GitStatusEntry untracked = SingleEntry(snapshot, "untracked with spaces \u65e5\u672c\u8a9e.md");
        Assert.True(untracked.IsUntracked);
        Assert.Equal(GitFileState.Untracked, untracked.State);
        Assert.Equal("??", untracked.StatusCharacters);

        GitStatusEntry ignored = SingleEntry(snapshot, "ignored.md");
        Assert.True(ignored.IsIgnored);
        Assert.Equal(GitFileState.Ignored, ignored.State);
        Assert.Equal("!!", ignored.StatusCharacters);
        Assert.False(ignored.HasStagedChange);
        Assert.False(ignored.HasWorkingTreeChange);
    }

    [Theory]
    [InlineData("DD", GitConflictKind.BothDeleted)]
    [InlineData("AU", GitConflictKind.AddedByUs)]
    [InlineData("UD", GitConflictKind.DeletedByThem)]
    [InlineData("UA", GitConflictKind.AddedByThem)]
    [InlineData("DU", GitConflictKind.DeletedByUs)]
    [InlineData("AA", GitConflictKind.BothAdded)]
    [InlineData("UU", GitConflictKind.BothModified)]
    public void Parser_UnmergedEntries_MapEveryConflictCombination(string statusCharacters, GitConflictKind expected)
    {
        string output = Porcelain(
            "# branch.oid 1111111111111111111111111111111111111111",
            "# branch.head main",
            $"u {statusCharacters} N... 100644 100644 100644 100644 aaaa bbbb cccc conflicted.md");

        GitStatusSnapshot snapshot = GitPorcelainV2Parser.Parse(@"C:\repo", output);

        GitStatusEntry entry = Assert.Single(snapshot.Entries);
        Assert.True(entry.IsUnmerged);
        Assert.Equal(expected, entry.ConflictKind);
        Assert.Equal(GitFileState.Conflicted, entry.State);
        Assert.Equal(statusCharacters, entry.StatusCharacters);
    }

    [Fact]
    public void Parser_UnknownUnmergedCombination_StaysConflictedWithUnclassifiedKind()
    {
        string output = Porcelain(
            "# branch.oid 1111111111111111111111111111111111111111",
            "# branch.head main",
            "u XY N... 100644 100644 100644 100644 aaaa bbbb cccc conflicted.md");

        GitStatusSnapshot snapshot = GitPorcelainV2Parser.Parse(@"C:\repo", output);

        GitStatusEntry entry = Assert.Single(snapshot.Entries);
        Assert.True(entry.IsUnmerged);
        Assert.Equal(GitConflictKind.None, entry.ConflictKind);
        Assert.Equal(GitFileState.Conflicted, entry.State);
    }

    [Fact]
    public void Parser_DetachedAndUnbornBranchHeaders_ProduceTypedBranchState()
    {
        GitStatusSnapshot detached = GitPorcelainV2Parser.Parse(
            @"C:\repo",
            Porcelain(
                "# branch.oid 2222222222222222222222222222222222222222",
                "# branch.head (detached)"));

        Assert.True(detached.IsDetached);
        Assert.Null(detached.BranchName);
        Assert.Equal("2222222222222222222222222222222222222222", detached.HeadCommit);
        Assert.Empty(detached.Entries);

        GitStatusSnapshot unborn = GitPorcelainV2Parser.Parse(
            @"C:\repo",
            Porcelain(
                "# branch.oid (initial)",
                "# branch.head main"));

        Assert.False(unborn.IsDetached);
        Assert.Equal("main", unborn.BranchName);
        Assert.Null(unborn.HeadCommit);
        Assert.False(unborn.HasUpstream);
        Assert.Null(unborn.Ahead);
        Assert.Null(unborn.Behind);
    }

    [Fact]
    public void Parser_MalformedRecord_YieldsTypedFormatFailure()
    {
        string output = Porcelain(
            "# branch.oid 1111111111111111111111111111111111111111",
            "# branch.head main",
            "1 M. broken");

        Assert.Throws<FormatException>(() => GitPorcelainV2Parser.Parse(@"C:\repo", output));
    }

    [Fact]
    public async Task Status_NonGitFolder_Returns_TypedNoRepositoryState()
    {
        string folder = CreatePlainFolder();

        try
        {
            var service = new GitRepositoryService(new GitProcessRunner());

            var result = await service.GetStatusAsync(folder);

            Assert.Equal(GitRepositoryStatus.NoRepository, result.Status);
            Assert.False(result.IsAvailable);
            Assert.Null(result.Snapshot);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.Failure!.ErrorKind);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Status_MissingGitExecutable_YieldsTypedGitUnavailableState()
    {
        string folder = CreatePlainFolder();

        try
        {
            var options = new GitProcessRunnerOptions { ExecutablePath = "scm02-definitely-missing-git-executable" };
            var service = new GitRepositoryService(new GitProcessRunner(options));

            var result = await service.GetStatusAsync(folder);

            Assert.Equal(GitRepositoryStatus.GitUnavailable, result.Status);
            Assert.NotNull(result.Failure);
            Assert.Equal(GitProcessErrorKind.GitNotInstalled, result.Failure!.ErrorKind);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Status_PathsWithSpacesAndUnicode_ArePreservedExactly()
    {
        using var repo = TestGitRepository.Create("scm02 paths \u00fc (v1)");
        EnsureBranch(repo, "main");
        const string spacesPath = "docs dir/notes with spaces.md";
        const string unicodePath = "docs dir/nested/\u30d5\u30a1\u30a4\u30eb \u00e9.md";
        repo.WriteFile(spacesPath, "v1");
        repo.WriteFile(unicodePath, "v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", spacesPath, unicodePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "base"));

        repo.WriteFile(spacesPath, "v2");
        repo.WriteFile(unicodePath, "v2");

        var service = new GitRepositoryService(new GitProcessRunner());
        var snapshot = (await service.GetStatusAsync(repo.RootPath)).Snapshot!;

        GitStatusEntry spaces = SingleEntry(snapshot, spacesPath);
        Assert.Equal(GitChangeKind.Modified, spaces.WorkingTreeChange);
        GitStatusEntry unicode = SingleEntry(snapshot, unicodePath);
        Assert.Equal(GitChangeKind.Modified, unicode.WorkingTreeChange);
    }

    [Fact]
    public async Task Status_RepositoryDisappearsBeforeRefresh_YieldsTypedFailure()
    {
        using var repo = TestGitRepository.Create();
        EnsureBranch(repo, "main");
        repo.WriteFile("a.md", "a");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "a.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "init"));

        var service = new GitRepositoryService(new GitProcessRunner());
        var before = await service.GetStatusAsync(repo.RootPath);
        Assert.Equal(GitRepositoryStatus.Available, before.Status);

        // The repository disappears while the worktree directory remains.
        ForceDeleteDirectory(Path.Combine(repo.RootPath, ".git"));

        var after = await service.GetStatusAsync(repo.RootPath);

        Assert.Equal(GitRepositoryStatus.NoRepository, after.Status);
        Assert.Null(after.Snapshot);
        Assert.NotNull(after.Failure);
        Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, after.Failure!.ErrorKind);
        Assert.False(string.IsNullOrWhiteSpace(after.Failure.ErrorMessage));
    }

    // ---------------------------------------------------------------------
    // Step 4 — debounced status refresh integrated with the FS-03 watcher
    // ---------------------------------------------------------------------

    [Fact]
    public async Task FilesystemEvent_Triggers_DebouncedStatusRefresh_OutsideTheWatcherCallback()
    {
        using var repo = TestGitRepository.Create();
        EnsureBranch(repo, "main");
        repo.WriteFile("notes.md", "v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "notes.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "base"));

        var watcher = new MockSourceWatcher();
        using var coordinator = CreateCoordinator(watcher);
        using var service = new GitRepositoryService(
            new GitProcessRunner(),
            coordinator,
            statusDebounceDelay: TimeSpan.FromMilliseconds(150));

        var refreshEvents = new List<GitStatusResult>();
        service.OnStatusChanged += (_, result) =>
        {
            lock (refreshEvents)
            {
                refreshEvents.Add(result);
            }
        };

        service.StartWatching(repo.RootPath);

        // Act: one Word-like save burst (multiple raw watcher events) after a
        // physical modification of a tracked document.
        repo.WriteFile("notes.md", "v2");
        for (int i = 0; i < 5; i++)
        {
            watcher.RaiseRawEvent(WatcherChangeTypes.Changed, Path.Combine(repo.RootPath, "notes.md"), null, "source-1");
        }

        // The watcher callback is a hint only: no Git status ran synchronously
        // inside the callback path (spec 13 §17).
        Assert.Null(service.CurrentStatus);

        GitStatusResult? refreshed = await WaitForRefreshAsync(service, TimeSpan.FromSeconds(10));
        Assert.NotNull(refreshed);

        // The burst coalesced into a single debounced refresh.
        await Task.Delay(400);
        lock (refreshEvents)
        {
            Assert.Single(refreshEvents);
        }

        GitStatusSnapshot snapshot = service.CurrentStatus!.Snapshot!;
        Assert.True(snapshot.HasUpstream == false);
        GitStatusEntry entry = SingleEntry(snapshot, "notes.md");
        Assert.True(entry.HasWorkingTreeChange);
        Assert.Equal(GitChangeKind.Modified, entry.WorkingTreeChange);
    }

    [Fact]
    public async Task StopWatching_Stops_DebouncedStatusRefresh()
    {
        using var repo = TestGitRepository.Create();
        EnsureBranch(repo, "main");
        repo.WriteFile("notes.md", "v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "notes.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "base"));

        var watcher = new MockSourceWatcher();
        using var coordinator = CreateCoordinator(watcher);
        using var service = new GitRepositoryService(
            new GitProcessRunner(),
            coordinator,
            statusDebounceDelay: TimeSpan.FromMilliseconds(100));
        int refreshCount = 0;
        service.OnStatusChanged += (_, _) => Interlocked.Increment(ref refreshCount);

        service.StartWatching(repo.RootPath);
        repo.WriteFile("notes.md", "v2");
        watcher.RaiseRawEvent(WatcherChangeTypes.Changed, Path.Combine(repo.RootPath, "notes.md"), null, "source-1");
        await WaitForCountAsync(() => refreshCount, 1, TimeSpan.FromSeconds(10));

        // Act: stop watching, then produce another hint.
        service.StopWatching();
        repo.WriteFile("notes.md", "v3");
        watcher.RaiseRawEvent(WatcherChangeTypes.Changed, Path.Combine(repo.RootPath, "notes.md"), null, "source-1");

        await Task.Delay(500);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task RefreshAsync_RefreshesWatchedWorktree_WithoutFilesystemEvent()
    {
        using var repo = TestGitRepository.Create();
        EnsureBranch(repo, "main");
        repo.WriteFile("report.md", "v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "report.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "base"));

        var watcher = new MockSourceWatcher();
        using var coordinator = CreateCoordinator(watcher);
        using var service = new GitRepositoryService(new GitProcessRunner(), coordinator);
        service.StartWatching(repo.RootPath);

        // A change made outside the watcher (e.g. another tool) can be pulled
        // in explicitly; the result is still derived from Git.
        repo.WriteFile("report.md", "v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "report.md"));

        GitStatusResult? result = await service.RefreshAsync();

        Assert.NotNull(result);
        Assert.True(result!.IsAvailable);
        GitStatusEntry entry = SingleEntry(result.Snapshot!, "report.md");
        Assert.True(entry.HasStagedChange);
        Assert.Equal(GitChangeKind.Modified, entry.IndexChange);
        Assert.Same(result, service.CurrentStatus);
    }

    [Fact]
    public async Task RefreshAsync_WithoutWatchedWorktree_ReturnsNull_InsteadOfGuessing()
    {
        var watcher = new MockSourceWatcher();
        using var coordinator = CreateCoordinator(watcher);
        using var service = new GitRepositoryService(new GitProcessRunner(), coordinator);

        Assert.Null(await service.RefreshAsync());
        Assert.Null(service.CurrentStatus);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static GitStatusEntry SingleEntry(GitStatusSnapshot snapshot, string path) =>
        Assert.Single(snapshot.Entries, e => e.Path == path);

    private static string HeadCommit(TestGitRepository repo) =>
        TestGitRepository.RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim();

    private static string Porcelain(params string[] records) =>
        string.Join('\0', records) + '\0';

    private static void ForceDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    private static void EnsureBranch(TestGitRepository repo, string branch)
    {
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", branch));
    }

    private static string CreatePlainFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm02", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Builds a real FS-03 coordinator wired to a throw-away in-memory document
    /// store, mirroring how the application composes the watcher pipeline.
    /// </summary>
    private static FileChangeCoordinator CreateCoordinator(MockSourceWatcher watcher)
    {
        var documentRepo = new FakeDocumentRepository();
        return new FileChangeCoordinator(
            watcher,
            new DocumentIdentityService(documentRepo, new FakeFileIdentityProvider()),
            documentRepo,
            debounceDelay: TimeSpan.FromMilliseconds(50));
    }

    private static async Task<GitStatusResult?> WaitForRefreshAsync(
        GitRepositoryService service, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            GitStatusResult? current = service.CurrentStatus;
            if (current != null)
            {
                return current;
            }

            await Task.Delay(25);
        }

        return service.CurrentStatus;
    }

    private static async Task WaitForCountAsync(Func<int> count, int expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (count() >= expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(count() >= expected, $"Expected at least {expected} refreshes within {timeout}, saw {count()}.");
    }

    private sealed class MockSourceWatcher : ISourceWatcher
    {
        private readonly HashSet<string> _watchedFolderIds = new();

        public event EventHandler<RawWatcherEventArgs>? OnRawEvent;
        public event EventHandler<string>? OnWatcherOverflow;

        public void StartWatching(FolderSource source) => _watchedFolderIds.Add(source.Id);
        public void StopWatching(string folderSourceId) => _watchedFolderIds.Remove(folderSourceId);
        public bool IsWatching(string folderSourceId) => _watchedFolderIds.Contains(folderSourceId);

        public void RaiseRawEvent(
            WatcherChangeTypes changeType, string fullPath, string? oldFullPath, string folderSourceId)
        {
            OnRawEvent?.Invoke(this, new RawWatcherEventArgs(changeType, fullPath, oldFullPath, folderSourceId));
        }

        public void RaiseOverflow(string folderSourceId) => OnWatcherOverflow?.Invoke(this, folderSourceId);

        public void Dispose() => _watchedFolderIds.Clear();
    }

    private sealed class FakeFileIdentityProvider : IFileIdentityProvider
    {
        public FileIdentity? GetFileIdentity(string path) => null;
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        private readonly Dictionary<string, Document> _byId = new(StringComparer.Ordinal);

        public Task<Document?> GetByIdAsync(string id)
        {
            _byId.TryGetValue(id, out var document);
            return Task.FromResult(document);
        }

        public Task<Document?> GetByPathAsync(string currentPath) => Task.FromResult<Document?>(null);

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) =>
            Task.FromResult<Document?>(null);

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) =>
            Task.FromResult(new List<Document>());

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) =>
            Task.FromResult<IReadOnlyList<Document>>(new List<Document>());

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId) =>
            Task.FromResult<IReadOnlyList<Document>>(new List<Document>());

        public Task CreateAsync(Document document)
        {
            _byId[document.Id] = document;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Document document)
        {
            _byId[document.Id] = document;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            _byId.Remove(id);
            return Task.CompletedTask;
        }
    }
}
