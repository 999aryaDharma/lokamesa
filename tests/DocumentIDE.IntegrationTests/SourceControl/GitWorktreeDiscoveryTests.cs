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
using DocumentIDE.Core.Workspaces;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-15 — Git Worktree Discovery &amp; Open Existing.
///
/// Red stage: the first test in this file
/// (<see cref="MainAndLinkedWorktreesAreListed"/>) must FAIL before
/// <c>GitWorktreeService</c> exists. The fixture uses raw Git process
/// invocations (SCM-01 <see cref="TestGitRepository"/>), never the code
/// under test, so setup stays independent of the service being verified.
/// </summary>
public sealed class GitWorktreeDiscoveryTests
{
    // -------------------------------------------------------------------------
    // Required test 1 (FIRST FAILING): Main and linked worktrees are listed.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task MainAndLinkedWorktreesAreListed()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("main-linked");
        GitWorktreeService service = CreateService();

        GitWorktreeListResult result = await service.ListWorktreesAsync(fixture.Repo.RootPath);

        Assert.True(result.IsSuccess, $"Expected a successful list, got {result.Status}: {result.ErrorMessage}");
        Assert.Equal(2, result.Worktrees.Count);

        // Git documents the main working tree as listed first (spec 13 §16.1).
        GitWorktreeInfo main = result.Worktrees[0];
        Assert.True(main.IsMainWorktree, "The main worktree must be flagged and listed first.");
        AssertSamePath(fixture.Repo.RootPath, main.Path);
        Assert.False(main.IsDetached);
        Assert.False(main.IsLocked);
        Assert.False(main.IsPrunable);
        Assert.False(string.IsNullOrWhiteSpace(main.HeadCommit));

        GitWorktreeInfo linked = Assert.Single(result.Worktrees, w => !w.IsMainWorktree);
        AssertSamePath(fixture.LinkedPath, linked.Path);
        Assert.False(linked.IsDetached);
        Assert.Equal("wt-branch", linked.BranchName);
        Assert.Equal("refs/heads/wt-branch", linked.BranchRef);
        Assert.Equal(fixture.HeadCommit, linked.HeadCommit);
        Assert.Equal(fixture.HeadCommit, main.HeadCommit);
    }

    // -------------------------------------------------------------------------
    // Step 2: worktree paths resolve safely (spaces/Unicode, canonical form).
    // -------------------------------------------------------------------------
    [Fact]
    public async Task WorktreePathsWithSpacesAndUnicodeAreCanonicalized()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("spaces unicode \u00e9 \u00fcn\u00efc\u00f8de (v1)");
        GitWorktreeService service = CreateService();

        Assert.Contains(' ', fixture.Repo.RootPath);
        Assert.Contains(' ', fixture.LinkedPath);

        GitWorktreeListResult result = await service.ListWorktreesAsync(fixture.Repo.RootPath);

        Assert.True(result.IsSuccess, $"Expected a successful list, got {result.Status}: {result.ErrorMessage}");
        Assert.Equal(2, result.Worktrees.Count);
        AssertSamePath(fixture.Repo.RootPath, result.Worktrees[0].Path);

        GitWorktreeInfo linked = Assert.Single(result.Worktrees, w => !w.IsMainWorktree);
        AssertSamePath(fixture.LinkedPath, linked.Path);

        // Canonical form: fully qualified, no forward-slash Git styling, no trailing separator.
        Assert.True(Path.IsPathFullyQualified(linked.Path));
        Assert.DoesNotContain('/', linked.Path);
        Assert.False(linked.Path.EndsWith(Path.DirectorySeparatorChar));
    }

    // -------------------------------------------------------------------------
    // Step 1/2: locked, detached, and prunable state is exposed read-only.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task DetachedLockedAndPrunableWorktreesExposeTypedMetadata()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("metadata");
        string detachedPath = Path.Combine(Path.GetTempPath(), "scm15", $"detached-{Guid.NewGuid():N}");
        try
        {
            Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "worktree", "add", "-q", "--detach", detachedPath));
            Assert.Equal(0, TestGitRepository.RunGit(fixture.Repo.RootPath, "worktree", "lock", "--reason", "held for tests", detachedPath));

            // Deleting a linked worktree directory keeps its registration, and
            // Git reports it as prunable instead of silently forgetting it.
            Directory.Delete(fixture.LinkedPath, recursive: true);

            GitWorktreeListResult result = await CreateService().ListWorktreesAsync(fixture.Repo.RootPath);

            Assert.True(result.IsSuccess, $"Expected a successful list, got {result.Status}: {result.ErrorMessage}");
            Assert.Equal(3, result.Worktrees.Count);

            GitWorktreeInfo detached = Assert.Single(result.Worktrees, w => PathEquals(w.Path, detachedPath));
            Assert.True(detached.IsDetached);
            Assert.Null(detached.BranchRef);
            Assert.Null(detached.BranchName);
            Assert.True(detached.IsLocked);
            Assert.Equal("held for tests", detached.LockReason);

            GitWorktreeInfo prunable = Assert.Single(result.Worktrees, w => PathEquals(w.Path, fixture.LinkedPath));
            Assert.True(prunable.IsPrunable);
            Assert.False(string.IsNullOrWhiteSpace(prunable.PruneReason));
        }
        finally
        {
            if (Directory.Exists(detachedPath))
            {
                // A locked worktree needs the forced removal spelled twice.
                TestGitRepository.RunGit(fixture.Repo.RootPath, "worktree", "remove", "--force", "--force", detachedPath);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Failure handling: Git unavailable / not a repository are typed states.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task NonRepositoryDirectoryYieldsTypedNotARepositoryState()
    {
        string emptyDirectory = Path.Combine(Path.GetTempPath(), "scm15", $"empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            GitWorktreeListResult result = await CreateService().ListWorktreesAsync(emptyDirectory);

            Assert.Equal(GitWorktreeListStatus.NotARepository, result.Status);
            Assert.Empty(result.Worktrees);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.ProcessResult.ErrorKind);
        }
        finally
        {
            Directory.Delete(emptyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GitUnavailableYieldsTypedUnavailableState()
    {
        var runner = new FakeGitProcessRunner(new GitProcessResult(
            GitProcessStatus.Unavailable,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitNotInstalled,
            "git executable not found"));
        var service = new GitWorktreeService(runner, new FolderSourceService(new InMemoryFolderSourceRepository()));

        GitWorktreeListResult result = await service.ListWorktreesAsync(@"C:\anywhere");

        Assert.Equal(GitWorktreeListStatus.GitUnavailable, result.Status);
        Assert.Empty(result.Worktrees);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    // -------------------------------------------------------------------------
    // Failure handling: malformed porcelain output never yields a partial list.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task MalformedPorcelainOutputYieldsTypedParseErrorAndNoPartialList()
    {
        var runner = new FakeGitProcessRunner(new GitProcessResult(
            GitProcessStatus.Succeeded,
            0,
            "worktree C:/repo\nHEAD 1111111111111111111111111111111111111111\nbranch refs/heads/main\n\nnotworktree C:/other\nHEAD 2222\n",
            string.Empty));
        var service = new GitWorktreeService(runner, new FolderSourceService(new InMemoryFolderSourceRepository()));

        GitWorktreeListResult result = await service.ListWorktreesAsync(@"C:\repo");

        Assert.Equal(GitWorktreeListStatus.ParseError, result.Status);
        Assert.Empty(result.Worktrees);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

        // The only Git invocation is the read-only porcelain listing.
        GitProcessRequest request = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "worktree", "list", "--porcelain" }, request.Arguments);
    }

    [Fact]
    public async Task RelativeWorktreePathInPorcelainOutputIsRejectedAsParseError()
    {
        var runner = new FakeGitProcessRunner(new GitProcessResult(
            GitProcessStatus.Succeeded,
            0,
            "worktree relative/escape\nHEAD 1111111111111111111111111111111111111111\nbranch refs/heads/main\n",
            string.Empty));
        var service = new GitWorktreeService(runner, new FolderSourceService(new InMemoryFolderSourceRepository()));

        GitWorktreeListResult result = await service.ListWorktreesAsync(@"C:\repo");

        Assert.Equal(GitWorktreeListStatus.ParseError, result.Status);
        Assert.Empty(result.Worktrees);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    // -------------------------------------------------------------------------
    // Required test 2: Open existing worktree updates the active filesystem
    // context, reusing the FS-01 workspace/source services.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OpenExistingWorktreeUpdatesActiveFilesystemContext()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-context");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.Repo.RootPath);
        await folderSources.AttachFolderAsync(workspaceId, fixture.LinkedPath);
        var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

        Assert.Null(service.ActiveWorktree);

        GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, fixture.LinkedPath));

        Assert.Equal(GitWorktreeOpenStatus.Opened, result.Status);
        Assert.True(result.IsAttachedToWorkspace);
        Assert.NotNull(result.Worktree);
        AssertSamePath(fixture.LinkedPath, result.Worktree!.Path);
        Assert.Equal("wt-branch", result.Worktree.BranchName);
        Assert.False(string.IsNullOrWhiteSpace(result.Worktree.HeadCommit));

        Assert.NotNull(service.ActiveWorktree);
        AssertSamePath(fixture.LinkedPath, service.ActiveWorktree!.Path);
        Assert.Equal("wt-branch", service.ActiveWorktree.BranchName);

        // Opening never detaches or re-registers sources: both stay attached.
        IReadOnlyList<FolderSource> sources = await folderSources.GetFolderSourcesAsync(workspaceId);
        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, source => PathEquals(source.CanonicalPath, fixture.Repo.RootPath));
        Assert.Contains(sources, source => PathEquals(source.CanonicalPath, fixture.LinkedPath));
    }

    [Fact]
    public async Task OpeningUnattachedWorktreeRequiresExplicitAttachmentFlow()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-unattached");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.Repo.RootPath);
        var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

        GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, fixture.LinkedPath));

        // Spec 13 §16.2: an unattached worktree must go through the explicit
        // FS-01 attachment flow; opening never attaches (or detaches) silently.
        Assert.Equal(GitWorktreeOpenStatus.RequiresAttachmentFlow, result.Status);
        Assert.False(result.IsAttachedToWorkspace);
        Assert.NotNull(result.Worktree);
        AssertSamePath(fixture.LinkedPath, result.Worktree!.Path);
        Assert.Null(service.ActiveWorktree);

        IReadOnlyList<FolderSource> sources = await folderSources.GetFolderSourcesAsync(workspaceId);
        FolderSource onlySource = Assert.Single(sources);
        AssertSamePath(fixture.Repo.RootPath, onlySource.CanonicalPath);
    }

    // -------------------------------------------------------------------------
    // Required test 3: No branch switch (or branch creation, or history
    // mutation) occurs when opening a worktree.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OpeningWorktreeDoesNotSwitchBranchesCreateBranchesOrAlterHistory()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-no-switch");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.Repo.RootPath);
        await folderSources.AttachFolderAsync(workspaceId, fixture.LinkedPath);
        var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

        string mainSymbolicHeadBefore = RunGitCapture(fixture.Repo.RootPath, "symbolic-ref", "HEAD").Trim();
        string linkedSymbolicHeadBefore = RunGitCapture(fixture.LinkedPath, "symbolic-ref", "HEAD").Trim();
        string mainHeadBefore = RunGitCapture(fixture.Repo.RootPath, "rev-parse", "HEAD").Trim();
        string linkedHeadBefore = RunGitCapture(fixture.LinkedPath, "rev-parse", "HEAD").Trim();
        string branchesBefore = RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/");

        GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, fixture.LinkedPath));

        Assert.Equal(GitWorktreeOpenStatus.Opened, result.Status);

        // HEAD symbolic refs (and commits) in both worktrees are untouched.
        Assert.Equal(mainSymbolicHeadBefore, RunGitCapture(fixture.Repo.RootPath, "symbolic-ref", "HEAD").Trim());
        Assert.Equal(linkedSymbolicHeadBefore, RunGitCapture(fixture.LinkedPath, "symbolic-ref", "HEAD").Trim());
        Assert.Equal(mainHeadBefore, RunGitCapture(fixture.Repo.RootPath, "rev-parse", "HEAD").Trim());
        Assert.Equal(linkedHeadBefore, RunGitCapture(fixture.LinkedPath, "rev-parse", "HEAD").Trim());

        // No branch was created or deleted.
        Assert.Equal(branchesBefore, RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/"));
    }

    [Fact]
    public async Task OpeningWorktreeIssuesOnlyTheReadOnlyWorktreeListInvocation()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-invocations");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.LinkedPath);

        string mainBranch = RunGitCapture(fixture.Repo.RootPath, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        string porcelain =
            $"worktree {fixture.Repo.RootPath.Replace('\\', '/')}\n" +
            $"HEAD {fixture.HeadCommit}\n" +
            $"branch refs/heads/{mainBranch}\n\n" +
            $"worktree {fixture.LinkedPath.Replace('\\', '/')}\n" +
            $"HEAD {fixture.HeadCommit}\n" +
            "branch refs/heads/wt-branch\n";
        var runner = new FakeGitProcessRunner(new GitProcessResult(GitProcessStatus.Succeeded, 0, porcelain, string.Empty));
        var service = new GitWorktreeService(runner, folderSources);

        GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, fixture.LinkedPath));

        Assert.Equal(GitWorktreeOpenStatus.Opened, result.Status);

        // The entire open flow is one read-only porcelain listing: no checkout,
        // no branch creation, no mutation of any kind reaches Git.
        GitProcessRequest request = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "worktree", "list", "--porcelain" }, request.Arguments);
        AssertSamePath(fixture.Repo.RootPath, request.WorkingDirectory);
    }

    // -------------------------------------------------------------------------
    // Step 2 containment: only locations Git registered as worktrees can be
    // opened; arbitrary or escaping paths are rejected typed.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task UnregisteredPathIsRejectedAndActiveContextIsUnchanged()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-unregistered");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.Repo.RootPath);
        await folderSources.AttachFolderAsync(workspaceId, fixture.LinkedPath);
        var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

        string intruderPath = Path.Combine(Path.GetTempPath(), "scm15", $"intruder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(intruderPath);
        try
        {
            GitWorktreeOpenResult intruder = await service.OpenWorktreeAsync(
                new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, intruderPath));

            Assert.Equal(GitWorktreeOpenStatus.NotRegistered, intruder.Status);
            Assert.False(intruder.IsAttachedToWorkspace);
            Assert.Null(service.ActiveWorktree);

            // A traversal form that resolves outside every registered worktree
            // is rejected the same way — no repository escape is possible.
            string escapingPath = Path.Combine(fixture.Repo.RootPath, "..", $"escape-{Guid.NewGuid():N}");
            GitWorktreeOpenResult escape = await service.OpenWorktreeAsync(
                new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, escapingPath));

            Assert.Equal(GitWorktreeOpenStatus.NotRegistered, escape.Status);
            Assert.Null(service.ActiveWorktree);
        }
        finally
        {
            Directory.Delete(intruderPath, recursive: true);
        }
    }

    [Fact]
    public async Task DisappearingWorktreeIsReportedAsTypedUnavailable()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-disappeared");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.Repo.RootPath);
        await folderSources.AttachFolderAsync(workspaceId, fixture.LinkedPath);
        var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

        // The directory disappears while its registration remains (prunable).
        Directory.Delete(fixture.LinkedPath, recursive: true);

        GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, fixture.LinkedPath));

        Assert.Equal(GitWorktreeOpenStatus.Unavailable, result.Status);
        Assert.Null(service.ActiveWorktree);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task OpenWithInvalidArgumentsYieldsTypedInvalidRequest()
    {
        var service = new GitWorktreeService(
            new GitProcessRunner(),
            new FolderSourceService(new InMemoryFolderSourceRepository()));

        GitWorktreeOpenResult emptyWorkspace = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(string.Empty, @"C:\repo", @"C:\repo"));
        Assert.Equal(GitWorktreeOpenStatus.InvalidRequest, emptyWorkspace.Status);

        GitWorktreeOpenResult relativePath = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest("ws", @"C:\repo", @"relative\worktree"));
        Assert.Equal(GitWorktreeOpenStatus.InvalidRequest, relativePath.Status);

        Assert.Null(service.ActiveWorktree);
    }

    [Fact]
    public async Task OpenFromNonRepositoryWorkingDirectoryYieldsTypedListFailure()
    {
        string emptyDirectory = Path.Combine(Path.GetTempPath(), "scm15", $"empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            const string workspaceId = "scm15-workspace";
            var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
            var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

            GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
                new GitWorktreeOpenRequest(workspaceId, emptyDirectory, emptyDirectory));

            Assert.Equal(GitWorktreeOpenStatus.ListFailed, result.Status);
            Assert.NotNull(result.ListResult);
            Assert.Equal(GitWorktreeListStatus.NotARepository, result.ListResult!.Status);
            Assert.Null(service.ActiveWorktree);
        }
        finally
        {
            Directory.Delete(emptyDirectory, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Step 4: canvas spatial memory survives a worktree context change.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task OpeningWorktreePreservesCanvasSpatialMemory()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open-canvas");
        const string workspaceId = "scm15-workspace";
        var folderSources = new FolderSourceService(new InMemoryFolderSourceRepository());
        await folderSources.AttachFolderAsync(workspaceId, fixture.Repo.RootPath);
        await folderSources.AttachFolderAsync(workspaceId, fixture.LinkedPath);
        var service = new GitWorktreeService(new GitProcessRunner(), folderSources);

        var canvas = new CanvasPositionService();
        canvas.UpsertPosition(workspaceId, "doc-a", 120, 240, CanvasPositionService.Manual);
        canvas.UpsertPosition(workspaceId, "doc-b", 300, 400, CanvasPositionService.Auto);
        canvas.Pin(workspaceId, "doc-b");
        string before = SnapshotCanvas(canvas.ExportSnapshot());

        GitWorktreeOpenResult result = await service.OpenWorktreeAsync(
            new GitWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, fixture.LinkedPath));

        Assert.Equal(GitWorktreeOpenStatus.Opened, result.Status);

        // The context switch writes nothing to canvas/workspace stores:
        // positions, modes, and pinning are byte-identical afterwards.
        Assert.Equal(before, SnapshotCanvas(canvas.ExportSnapshot()));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static GitWorktreeService CreateService() =>
        new(new GitProcessRunner(), new FolderSourceService(new InMemoryFolderSourceRepository()));

    private static void AssertSamePath(string expected, string actual) =>
        Assert.True(
            PathEquals(expected, actual),
            $"Expected path '{expected}' but got '{actual}'.");

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string SnapshotCanvas(IReadOnlyList<WorkspaceDocument> documents) =>
        string.Join(
            "\n",
            documents
                .OrderBy(document => document.DocumentId, StringComparer.Ordinal)
                .Select(document =>
                    $"{document.WorkspaceId}|{document.DocumentId}|{document.CanvasX}|{document.CanvasY}|{document.PositionMode}|{document.Pinned}"));

    /// <summary>
    /// A disposable Git repository with one commit and one linked worktree on
    /// a dedicated branch (<c>wt-branch</c>) outside the main worktree.
    /// </summary>
    private sealed class WorktreeFixture : IDisposable
    {
        private WorktreeFixture(TestGitRepository repo, string linkedPath, string headCommit)
        {
            Repo = repo;
            LinkedPath = linkedPath;
            HeadCommit = headCommit;
        }

        public TestGitRepository Repo { get; }

        public string LinkedPath { get; }

        public string HeadCommit { get; }

        public static WorktreeFixture Create(string name)
        {
            TestGitRepository repo = TestGitRepository.Create($"scm15-{name}-{Guid.NewGuid():N}");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "--allow-empty", "-m", "initial"));
            string headCommit = RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim();

            string linkedPath = Path.Combine(Path.GetTempPath(), "scm15", $"linked-{name}-{Guid.NewGuid():N}");
            Assert.Equal(
                0,
                TestGitRepository.RunGit(repo.RootPath, "worktree", "add", "-q", linkedPath, "-b", "wt-branch"));

            return new WorktreeFixture(repo, linkedPath, headCommit);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(LinkedPath))
                {
                    Directory.Delete(LinkedPath, recursive: true);
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

            Repo.Dispose();
        }
    }

    /// <summary>Runs Git for fixture setup/verification and captures stdout.</summary>
    private static string RunGitCapture(string workingDirectory, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return stdout;
    }

    /// <summary>In-memory <see cref="IFolderSourceRepository"/> so the real FS-01 service logic is exercised without SQLite.</summary>
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

    /// <summary>
    /// Test double for the SCM-01 <see cref="IGitProcessRunner"/> boundary:
    /// records every request so tests can prove which Git invocations the
    /// service is allowed to issue, and returns one canned typed result.
    /// </summary>
    private sealed class FakeGitProcessRunner : IGitProcessRunner
    {
        private readonly GitProcessResult _result;

        public FakeGitProcessRunner(GitProcessResult result)
        {
            _result = result;
        }

        public List<GitProcessRequest> Requests { get; } = new();

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCapabilityInfo(GitCapabilityStatus.Available, "git", _result, "2.51.0"));

        public Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }
}
