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
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-11 — Git Remote State &amp; Fetch (spec 13 §14.1/§14.2/§18/§19/§20/§23).
///
/// These tests drive <see cref="GitRemoteService"/> against real Git
/// repositories and real <b>local bare remotes</b> (no network access) through
/// the SCM-01 controlled process boundary and the SCM-01 per-worktree mutation
/// queue:
/// <list type="bullet">
/// <item>the configured upstream/tracking info is read from Git and parsed
/// correctly,</item>
/// <item>a repository with <b>no configured upstream</b> is a first-class typed
/// state — never an error and never a silent empty/0-0 result (spec 13
/// §14.2),</item>
/// <item>ahead/behind counts match the fixture remote,</item>
/// <item><c>Fetch</c> (<c>git fetch</c>, spec 13 §14.1) updates remote-tracking
/// state and never changes document or working-tree contents, and never touches
/// the index,</item>
/// <item>remote failures are typed (no upstream, no remote reachable, host
/// unreachable, authentication required, rejected, permission denied, not a
/// repository, Git missing) and authentication never hangs
/// (<c>GIT_TERMINAL_PROMPT=0</c> plus a typed outcome),</item>
/// <item>fetch runs through the per-worktree serialization queue with the true
/// worktree root as working directory (spec 13 §19), and</item>
/// <item>fetch never carries <c>pull</c>/<c>push</c>/<c>merge</c>/<c>rebase</c>
/// or scope-broadening options (spec 13 §14.4/§23).</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitFetchTests.cs</c>;
/// the frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/RemoteStatus.test.tsx</c>
/// (CAN-05 batch-B ruling).
/// </summary>
public sealed class GitFetchTests
{
    // ---------------------------------------------------------------------
    // Step 1 — Read upstream/tracking info (mandatory first failing test)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Configured upstream is parsed
    /// correctly." A repository whose current branch tracks a local bare remote
    /// reports the configured upstream reference, the remote name and zero
    /// ahead/behind (spec 13 §14.1/§14.2).
    /// </summary>
    [Fact]
    public async Task ConfiguredUpstreamIsParsedCorrectly()
    {
        using var repo = CreateRepositoryWithUpstream(out _);

        GitRemoteService service = CreateService();
        GitRemoteStateResult result = await service.GetRemoteStateAsync(repo.RootPath);

        Assert.Equal(GitRemoteOutcome.Available, result.Outcome);
        Assert.True(result.IsAvailable);
        Assert.Null(result.Failure);

        GitRemoteState state = Assert.IsType<GitRemoteState>(result.State);
        Assert.Equal(Path.GetFullPath(repo.RootPath), state.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("main", state.BranchName);
        Assert.False(state.IsDetached);
        Assert.Equal(Head(repo), state.HeadCommit);
        Assert.True(state.HasUpstream);
        Assert.Equal("origin/main", state.UpstreamReference);
        Assert.Equal("origin", state.RemoteName);
        Assert.Equal(0, state.Ahead);
        Assert.Equal(0, state.Behind);
    }

    // ---------------------------------------------------------------------
    // Step 1/2 — No-upstream state, ahead/behind counts
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 2: a repository with <b>no configured upstream</b> is a
    /// typed, first-class state — not an error and not a silent empty/0-0
    /// result (spec 13 §14.2). The state is present with ahead/behind
    /// <c>null</c>, never <c>0</c>.
    /// </summary>
    [Fact]
    public async Task NoUpstreamIsATypedFirstClassState()
    {
        using var repo = CreateRepositoryWithoutUpstream();

        GitRemoteService service = CreateService();
        GitRemoteStateResult result = await service.GetRemoteStateAsync(repo.RootPath);

        Assert.Equal(GitRemoteOutcome.NoUpstream, result.Outcome);
        // A resolved state, not a failure.
        Assert.True(result.IsAvailable);
        Assert.False(result.HasUpstream);
        Assert.Null(result.Failure);

        GitRemoteState state = Assert.IsType<GitRemoteState>(result.State);
        Assert.Equal("main", state.BranchName);
        Assert.Null(state.UpstreamReference);
        Assert.Null(state.RemoteName);
        Assert.False(state.HasUpstream);
        // Never a bogus 0/0: the counters are unknown without an upstream.
        Assert.Null(state.Ahead);
        Assert.Null(state.Behind);
    }

    /// <summary>
    /// Required test 3: ahead/behind counts match the fixture remote after the
    /// remote-tracking state is refreshed (spec 13 §14.2). Two local commits not
    /// on the remote and one remote commit not local are reported as
    /// <c>ahead=2, behind=1</c>.
    /// </summary>
    [Fact]
    public async Task AheadBehindCountsMatchFixtureRemote()
    {
        using var repo = CreateRepositoryWithUpstream(out string remotePath);

        // A separate clone advances the bare remote by one commit (behind).
        string clone = CloneRemote(remotePath);
        try
        {
            CommitFile(clone, "docs/remote.md", "remote v2", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
        }
        finally
        {
            DeleteDirectoryBestEffort(clone);
        }

        // The working repository advances by two commits (ahead).
        CommitFile(repo.RootPath, "docs/local1.md", "local 1", "local 1");
        CommitFile(repo.RootPath, "docs/local2.md", "local 2", "local 2");

        // Refresh the remote-tracking refs from the fixture remote (raw Git, so
        // this cycle pins count derivation independently of FetchAsync).
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "fetch", "-q", "origin"));

        GitRemoteService service = CreateService();
        GitRemoteStateResult result = await service.GetRemoteStateAsync(repo.RootPath);

        Assert.Equal(GitRemoteOutcome.Available, result.Outcome);
        GitRemoteState state = Assert.IsType<GitRemoteState>(result.State);
        Assert.True(state.HasUpstream);
        Assert.Equal("origin/main", state.UpstreamReference);
        Assert.Equal(2, state.Ahead);
        Assert.Equal(1, state.Behind);
    }

    /// <summary>
    /// A workspace outside any Git worktree reports the typed
    /// <c>NoRepository</c> state (spec 13 §5), never an exception.
    /// </summary>
    [Fact]
    public async Task RemoteStateOutsideRepositoryIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"scm11-norepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            GitRemoteService service = CreateService();
            GitRemoteStateResult result = await service.GetRemoteStateAsync(folder);

            Assert.Equal(GitRemoteOutcome.NoRepository, result.Outcome);
            Assert.False(result.IsAvailable);
            Assert.Null(result.State);
            Assert.NotNull(result.Failure);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    /// <summary>
    /// A machine without discoverable Git yields the typed
    /// <c>GitUnavailable</c> state (spec 13 §5/§20), never an exception.
    /// </summary>
    [Fact]
    public async Task RemoteStateWhenGitUnavailableIsTypedGitUnavailable()
    {
        using var repo = CreateRepositoryWithUpstream(out _);

        var runner = new UnavailableGitProcessRunner();
        GitRemoteService service = CreateService(runner, new GitRepositoryService(runner));
        GitRemoteStateResult result = await service.GetRemoteStateAsync(repo.RootPath);

        Assert.Equal(GitRemoteOutcome.GitUnavailable, result.Outcome);
        Assert.False(result.IsAvailable);
        Assert.Null(result.State);
    }

    // ---------------------------------------------------------------------
    // Step 3 — Explicit fetch (spec 13 §14.1/§19)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 4: <c>Fetch</c> updates remote-tracking state (the behind
    /// count moves from 0 to 1) without changing working-tree document contents,
    /// file mtimes, or the index (spec 13 §14.1).
    /// </summary>
    [Fact]
    public async Task FetchUpdatesTrackingStateWithoutChangingWorkingTreeContent()
    {
        using var repo = CreateRepositoryWithUpstream(out string remotePath);

        string clone = CloneRemote(remotePath);
        try
        {
            CommitFile(clone, "docs/remote.md", "remote v2", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
        }
        finally
        {
            DeleteDirectoryBestEffort(clone);
        }

        GitRemoteService service = CreateService();
        GitRemoteStateResult before = await service.GetRemoteStateAsync(repo.RootPath);
        Assert.Equal(0, Assert.IsType<GitRemoteState>(before.State).Behind);

        IReadOnlyList<string> treeBefore = SnapshotWorkingTree(repo.RootPath);
        string statusBefore = WorkingTreeStatus(repo.RootPath);
        string stagedBefore = StagedNames(repo.RootPath);

        GitFetchResult result = await service.FetchAsync(repo.RootPath);

        Assert.Equal(GitFetchOutcome.Succeeded, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);

        GitRemoteState after = Assert.IsType<GitRemoteState>(result.Remote);
        Assert.True(after.HasUpstream);
        Assert.Equal(1, after.Behind);
        Assert.Equal(0, after.Ahead);

        // The working tree (contents + mtimes) and the index are untouched.
        Assert.Equal(treeBefore, SnapshotWorkingTree(repo.RootPath));
        Assert.Equal(statusBefore, WorkingTreeStatus(repo.RootPath));
        Assert.Equal(stagedBefore, StagedNames(repo.RootPath));
    }

    /// <summary>
    /// A branch with no configured upstream reports the typed
    /// <c>NoUpstream</c> state and runs <b>no</b> fetch at all (spec 13 §14.2).
    /// </summary>
    [Fact]
    public async Task FetchWithNoUpstreamIsTypedAndRunsNoGit()
    {
        using var repo = CreateRepositoryWithoutUpstream();

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitRemoteService service = CreateService(recording);

        GitFetchResult result = await service.FetchAsync(repo.RootPath);

        Assert.Equal(GitFetchOutcome.NoUpstream, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Remote);
        Assert.False(result.Remote!.HasUpstream);
        Assert.Null(result.Remote.Ahead);
        Assert.Null(result.Remote.Behind);

        // No fetch subcommand ever ran.
        Assert.DoesNotContain(
            recording.Invocations,
            arguments => arguments.Count > 0 && arguments[0] == "fetch");
    }

    /// <summary>
    /// Fetch resolves and runs against the true worktree root even when the
    /// caller passes a subdirectory — a subdirectory would defeat per-worktree
    /// serialization if it were used as the working directory (spec 13 §19).
    /// </summary>
    [Fact]
    public async Task FetchUsesTheTrueWorktreeRootEvenFromASubdirectory()
    {
        using var repo = CreateRepositoryWithUpstream(out _);
        string nested = Path.Combine(repo.RootPath, "docs");

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitRemoteService service = CreateService(recording);

        GitFetchResult result = await service.FetchAsync(nested);

        Assert.Equal(GitFetchOutcome.Succeeded, result.Outcome);
        GitProcessRequest fetchRequest = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "fetch");
        Assert.Equal(
            Path.GetFullPath(repo.RootPath),
            Path.GetFullPath(fetchRequest.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The fetch invocation is exactly <c>["fetch"]</c>: structured arguments
    /// (never a shell string) and no scope-broadening option. Fetch never pulls,
    /// pushes, merges, rebases or prunes (spec 13 §14.4/§23).
    /// </summary>
    [Fact]
    public async Task FetchCarriesOnlyStructuredFetchArguments()
    {
        using var repo = CreateRepositoryWithUpstream(out _);

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitRemoteService service = CreateService(recording);

        GitFetchResult result = await service.FetchAsync(repo.RootPath);
        Assert.Equal(GitFetchOutcome.Succeeded, result.Outcome);

        GitProcessRequest fetchRequest = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "fetch");
        Assert.Equal(new[] { "fetch" }, fetchRequest.Arguments.ToArray());

        string[] forbidden = { "pull", "push", "merge", "rebase", "--prune", "--all", "--tags", "--force", "--prune-tags" };
        foreach (IReadOnlyList<string> invocation in recording.Invocations)
        {
            foreach (string token in invocation)
            {
                Assert.DoesNotContain(token, forbidden);
            }
        }
    }

    /// <summary>
    /// Fetch runs through the SCM-01 per-worktree mutation queue, so concurrent
    /// fetches against one worktree serialize (spec 13 §19).
    /// </summary>
    [Fact]
    public async Task FetchRunsThroughThePerWorktreeMutationQueue()
    {
        using var repo = CreateRepositoryWithUpstream(out _);

        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var service = new GitRemoteService(runner, new GitRepositoryService(runner));

        GitFetchResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 3).Select(_ => service.FetchAsync(repo.RootPath)));

        Assert.All(results, result => Assert.Equal(GitFetchOutcome.Succeeded, result.Outcome));
        Assert.Equal(1, queue.MaxObservedConcurrency);
    }

    // ---------------------------------------------------------------------
    // Step 3/4 — Typed remote failures & edge cases (spec 13 §14.1/§20)
    // ---------------------------------------------------------------------

    /// <summary>
    /// A remote that cannot be reached yields the typed <c>HostUnreachable</c>
    /// outcome — never an exception and never a silent success (spec 13 §20).
    /// Uses a local path that is not a repository: no network access.
    /// </summary>
    [Fact]
    public async Task FetchHostUnreachableIsTyped()
    {
        using var repo = CreateRepositoryWithUpstream(out _);
        string missingRemote = Path.Combine(Path.GetTempPath(), "scm11", $"missing-{Guid.NewGuid():N}.git");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "remote", "set-url", "origin", missingRemote));

        GitRemoteService service = CreateService();
        GitFetchResult result = await service.FetchAsync(repo.RootPath);

        Assert.Equal(GitFetchOutcome.HostUnreachable, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("HostUnreachable", result.ErrorCode);
        Assert.NotNull(result.Failure);
    }

    /// <summary>
    /// A remote that requires authentication yields the typed
    /// <c>AuthenticationRequired</c> outcome and returns promptly instead of
    /// hanging (spec 13 §14.4/§18): the SCM-01 runner pins
    /// <c>GIT_TERMINAL_PROMPT=0</c> and closes child stdin, and the service maps
    /// the failure typed. Driven with a scripted runner so no network is needed
    /// and the signature is deterministic.
    /// </summary>
    [Fact]
    public async Task FetchAuthenticationRequiredIsTypedWithoutHanging()
    {
        using var repo = CreateRepositoryWithUpstream(out _);

        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var scripted = new ScriptedGitProcessRunner(
            inner,
            ("fetch", new GitProcessResult(
                GitProcessStatus.Failed,
                128,
                string.Empty,
                "fatal: could not read Username for 'https://example.invalid': terminal prompts disabled",
                GitProcessErrorKind.GitAuthRequired,
                "`git fetch` exited with code 128.")));
        GitRemoteService service = CreateService(scripted);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        GitFetchResult result = await service.FetchAsync(repo.RootPath);
        stopwatch.Stop();

        Assert.Equal(GitFetchOutcome.AuthenticationRequired, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("AuthenticationRequired", result.ErrorCode);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitAuthRequired, result.Failure!.ErrorKind);

        // The operation failed fast — it never blocked on an interactive prompt.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Fetch blocked for {stopwatch.Elapsed}; an auth failure must not hang.");

        // No credential/secret material is echoed into the surfaced message.
        Assert.DoesNotContain("Username", result.Message ?? string.Empty);
        Assert.DoesNotContain("https://", result.Message ?? string.Empty);
    }

    /// <summary>
    /// Paths containing spaces, Unicode and special characters are handled
    /// verbatim (no shell, structured arguments) and a fetch from a nested
    /// subdirectory still resolves the true worktree root (spec 13 §18/§23).
    /// </summary>
    [Fact]
    public async Task FetchHandlesPathsWithSpacesUnicodeAndSpecialCharacters()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using var repo = CreateRepositoryWithUpstream(
            out _,
            directoryName: $"scm11 repo \u00fc \u6771 & (# {suffix}",
            remoteDirectoryName: $"remote \u00fc \u6771 & (# {suffix}.git");

        IReadOnlyList<string> treeBefore = SnapshotWorkingTree(repo.RootPath);

        GitRemoteService service = CreateService();
        GitFetchResult result = await service.FetchAsync(Path.Combine(repo.RootPath, "docs"));

        Assert.Equal(GitFetchOutcome.Succeeded, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Equal("origin/main", result.Remote!.UpstreamReference);
        Assert.Equal(treeBefore, SnapshotWorkingTree(repo.RootPath));
    }

    /// <summary>
    /// A workspace outside any Git worktree reports the typed
    /// <c>NoRepository</c> outcome; nothing runs (spec 13 §5).
    /// </summary>
    [Fact]
    public async Task FetchOutsideRepositoryIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"scm11-fetch-norepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            GitRemoteService service = CreateService();
            GitFetchResult result = await service.FetchAsync(folder);

            Assert.Equal(GitFetchOutcome.NoRepository, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Remote);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    /// <summary>
    /// A machine without discoverable Git yields the typed
    /// <c>GitUnavailable</c> outcome, never an exception (spec 13 §14.1/§20).
    /// </summary>
    [Fact]
    public async Task FetchWhenGitUnavailableIsTypedGitUnavailable()
    {
        using var repo = CreateRepositoryWithUpstream(out _);

        var runner = new UnavailableGitProcessRunner();
        GitRemoteService service = CreateService(runner, new GitRepositoryService(runner));
        GitFetchResult result = await service.FetchAsync(repo.RootPath);

        Assert.Equal(GitFetchOutcome.GitUnavailable, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Remote);
    }

    /// <summary>
    /// A repository/worktree that disappears before the operation is a typed
    /// non-success — never an exception (spec 13 §20 failure/edge cases).
    /// </summary>
    [Fact]
    public async Task FetchOnDisappearedWorktreeIsTyped()
    {
        TestGitRepository repo = CreateRepositoryWithUpstream(out _);
        string root = repo.RootPath;
        repo.Dispose();

        GitRemoteService service = CreateService();
        GitFetchResult result = await service.FetchAsync(root);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Outcome,
            new[] { GitFetchOutcome.Failed, GitFetchOutcome.NoRepository });
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    internal static GitRemoteService CreateService(
        IGitProcessRunner? runner = null,
        GitRepositoryService? repositories = null)
    {
        var queue = new GitWorktreeMutationQueue();
        IGitProcessRunner effective = runner ?? new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        GitRepositoryService repos = repositories ?? new GitRepositoryService(effective);
        return new GitRemoteService(effective, repos);
    }

    /// <summary>
    /// Creates a working repository on branch <c>main</c> with one baseline
    /// commit pushed to a fresh local <b>bare</b> remote named <c>origin</c>,
    /// with the branch's upstream configured (<c>push -u</c>). No network is
    /// used: the remote is a local filesystem path.
    /// </summary>
    internal static TestGitRepository CreateRepositoryWithUpstream(
        out string remotePath,
        string? directoryName = null,
        string? remoteDirectoryName = null)
    {
        remotePath = CreateBareRemote(remoteDirectoryName);

        var repo = TestGitRepository.Create(directoryName ?? $"scm11-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        repo.WriteFile("docs/baseline.md", "baseline");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "remote", "add", "origin", remotePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "push", "-q", "-u", "origin", "main"));
        // Point the bare remote's HEAD at main so a later clone checks it out.
        Assert.Equal(0, TestGitRepository.RunGit(remotePath, "symbolic-ref", "HEAD", "refs/heads/main"));
        return repo;
    }

    /// <summary>
    /// Creates an initialized repository on branch <c>main</c> with one commit
    /// and <b>no</b> remote/upstream configured.
    /// </summary>
    internal static TestGitRepository CreateRepositoryWithoutUpstream()
    {
        var repo = TestGitRepository.Create($"scm11-noupstream-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        repo.WriteFile("docs/a.md", "a");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    /// <summary>Clones the bare remote into a fresh working copy (no network).</summary>
    internal static string CloneRemote(string remotePath)
    {
        string parent = Path.Combine(Path.GetTempPath(), "scm11");
        Directory.CreateDirectory(parent);
        string clone = Path.Combine(parent, $"clone-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(parent, "clone", "-q", remotePath, clone));
        Assert.Equal(0, TestGitRepository.RunGit(clone, "config", "user.email", "scm11-tests@example.invalid"));
        Assert.Equal(0, TestGitRepository.RunGit(clone, "config", "user.name", "SCM-11 Tests"));
        return clone;
    }

    /// <summary>Writes a file and commits it in <paramref name="repositoryPath"/>.</summary>
    internal static void CommitFile(
        string repositoryPath,
        string relativePath,
        string content,
        string message)
    {
        string full = Path.Combine(repositoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Assert.Equal(0, TestGitRepository.RunGit(repositoryPath, "add", "--", relativePath));
        Assert.Equal(0, TestGitRepository.RunGit(repositoryPath, "commit", "-qm", message));
    }

    internal static void DeleteDirectoryBestEffort(string path)
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

    /// <summary>
    /// Snapshots every tracked-file-side working-tree entry outside <c>.git</c>
    /// (relative path, length, last-write time and content hash) so a fetch can
    /// be proven not to have changed document contents or mtimes
    /// (spec 13 §14.1).
    /// </summary>
    internal static IReadOnlyList<string> SnapshotWorkingTree(string root)
    {
        var entries = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file);
            if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var info = new FileInfo(file);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            entries.Add($"{relative}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{hash}");
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    /// <summary>Working-tree/index change report with no branch headers (so only real changes differ).</summary>
    internal static string WorkingTreeStatus(string root) =>
        TestGitRepository.RunGitCaptureUtf8(root, "status", "--porcelain=v2");

    internal static string StagedNames(string root) =>
        TestGitRepository.RunGitCaptureUtf8(root, "diff", "--cached", "--name-only");

    /// <summary>Creates a fresh local bare repository and returns its path.</summary>
    private static string CreateBareRemote(string? directoryName = null)
    {
        string parent = Path.Combine(Path.GetTempPath(), "scm11");
        Directory.CreateDirectory(parent);
        string remotePath = Path.Combine(parent, directoryName ?? $"{Guid.NewGuid():N}.git");
        Assert.Equal(0, TestGitRepository.RunGit(parent, "init", "--bare", "-q", remotePath));
        return remotePath;
    }

    internal static string Head(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-parse", "HEAD").Trim();
}

/// <summary>
/// A runner that reports Git as missing/unusable for every invocation, pinning
/// the typed <c>GitUnavailable</c> path without uninstalling Git.
/// </summary>
internal sealed class UnavailableGitProcessRunner : IGitProcessRunner
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

/// <summary>
/// Delegates to a real runner but substitutes a canned
/// <see cref="GitProcessResult"/> for one subcommand (keyed by the first
/// argument), pinning signatures that require a remote/credentials and cannot
/// be reproduced deterministically without network access.
/// </summary>
internal sealed class ScriptedGitProcessRunner : IGitProcessRunner
{
    private readonly IGitProcessRunner _inner;
    private readonly Dictionary<string, GitProcessResult> _overrides;

    public ScriptedGitProcessRunner(
        IGitProcessRunner inner,
        params (string Subcommand, GitProcessResult Result)[] overrides)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
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

/// <summary>
/// Records every Git invocation while delegating to a real runner, so tests can
/// pin which arguments the fetch path used and which working directory it
/// resolved (spec 13 §19/§23).
/// </summary>
internal sealed class RecordingGitProcessRunner : IGitProcessRunner
{
    private readonly IGitProcessRunner _inner;

    public RecordingGitProcessRunner(IGitProcessRunner inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public List<IReadOnlyList<string>> Invocations { get; } = new();

    public List<GitProcessRequest> Requests { get; } = new();

    public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
        _inner.GetCapabilityAsync(cancellationToken);

    public async Task<GitProcessResult> RunAsync(
        GitProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (Invocations)
        {
            Invocations.Add(request.Arguments);
            Requests.Add(request);
        }

        return await _inner.RunAsync(request, cancellationToken);
    }
}

/// <summary>
/// SCM-11 — privileged Source Control API surface for remote state and fetch
/// (spec 13 §14.1/§14.2/§18/§19/§20).
///
/// The remote-state route reports the configured upstream plus ahead/behind and
/// represents a repository with <b>no configured upstream</b> as the explicit
/// typed <c>NoUpstream</c> status (never a fake <c>0/0</c>); the fetch route runs
/// one explicit <c>git fetch</c> and returns the refreshed state, and neither
/// route ever touches document contents or the index. Every route inherits the
/// localhost session-token guard; a missing path is a typed 400 and nothing runs.
/// </summary>
public sealed class GitFetchApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GitFetchApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The remote-state route reports the configured upstream reference, remote
    /// name and ahead/behind counts for the resolved worktree (spec 13 §14.1).
    /// </summary>
    [Fact]
    public async Task RemoteEndpointReturnsConfiguredUpstreamAndAheadBehind()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync(
            $"/api/source-control/remote?path={Uri.EscapeDataString(repo.RootPath)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlRemoteResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlRemoteResponse>();

        Assert.NotNull(body);
        Assert.Equal("Available", body!.Status);
        Assert.True(body.HasRepository);
        Assert.Equal(Path.GetFullPath(repo.RootPath), body.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("main", body.BranchName);
        Assert.False(body.IsDetached);
        Assert.True(body.HasUpstream);
        Assert.Equal("origin/main", body.UpstreamReference);
        Assert.Equal("origin", body.RemoteName);
        Assert.Equal(0, body.Ahead);
        Assert.Equal(0, body.Behind);
        Assert.Null(body.Failure);
    }

    /// <summary>
    /// A repository with no configured upstream is the explicit typed
    /// <c>NoUpstream</c> status with null counters — never an error and never a
    /// fake <c>0/0</c> (spec 13 §14.2).
    /// </summary>
    [Fact]
    public async Task RemoteEndpointReportsNoUpstreamTypedly()
    {
        using var repo = GitFetchTests.CreateRepositoryWithoutUpstream();

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync(
            $"/api/source-control/remote?path={Uri.EscapeDataString(repo.RootPath)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlRemoteResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlRemoteResponse>();

        Assert.NotNull(body);
        Assert.Equal("NoUpstream", body!.Status);
        Assert.True(body.HasRepository);
        Assert.False(body.HasUpstream);
        Assert.Null(body.UpstreamReference);
        Assert.Null(body.RemoteName);
        Assert.Null(body.Ahead);
        Assert.Null(body.Behind);
        Assert.Null(body.Failure);
    }

    /// <summary>A missing <c>path</c> is a typed 400 and no Git runs (spec 13 §18).</summary>
    [Fact]
    public async Task RemoteEndpointRequiresPath()
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync("/api/source-control/remote");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", payload.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// The fetch route runs one explicit <c>git fetch</c> and returns the
    /// refreshed tracking state (behind moves 0 → 1) without changing
    /// working-tree document contents or the index (spec 13 §14.1).
    /// </summary>
    [Fact]
    public async Task FetchEndpointFetchesAndReturnsUpdatedState()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);

        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote.md", "remote v2", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }

        IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);
        string statusBefore = GitFetchTests.WorkingTreeStatus(repo.RootPath);
        string stagedBefore = GitFetchTests.StagedNames(repo.RootPath);

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/fetch",
            new SourceControlFetchRequest(repo.RootPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlFetchResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlFetchResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Outcome);
        Assert.True(body.IsSuccess);
        Assert.Null(body.ErrorCode);
        Assert.Null(body.Failure);
        Assert.NotNull(body.Remote);
        Assert.True(body.Remote!.HasUpstream);
        Assert.Equal("origin/main", body.Remote.UpstreamReference);
        Assert.Equal(1, body.Remote.Behind);
        Assert.Equal(0, body.Remote.Ahead);

        // The fetch did not touch working-tree contents or the index.
        Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));
        Assert.Equal(statusBefore, GitFetchTests.WorkingTreeStatus(repo.RootPath));
        Assert.Equal(stagedBefore, GitFetchTests.StagedNames(repo.RootPath));
    }

    /// <summary>A missing request body/path is a typed 400 and no Git runs (spec 13 §18).</summary>
    [Fact]
    public async Task FetchEndpointRequiresPath()
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/fetch",
            new SourceControlFetchRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", payload.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// Both remote routes are guarded exactly like the other privileged Source
    /// Control routes: a missing token is 401, a hostile origin is 403 and a
    /// wrong token is 401, and no unauthenticated request reaches Git
    /// (spec 13 §18).
    /// </summary>
    [Fact]
    public async Task RemoteRoutesRejectMissingInvalidTokenAndHostileOrigin()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        string headBefore = GitFetchTests.Head(repo);

        using (var anonymous = _factory.CreateClient())
        {
            using var response = await anonymous.GetAsync(
                $"/api/source-control/remote?path={Uri.EscapeDataString(repo.RootPath)}");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

            using var hostileOrigin = _factory.CreateClient();
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
            using var hostileResponse = await hostileOrigin.PostAsJsonAsync(
                "/api/source-control/fetch",
                new SourceControlFetchRequest(repo.RootPath));
            Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

            using var wrongToken = _factory.CreateClient();
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            using var wrongResponse = await wrongToken.PostAsJsonAsync(
                "/api/source-control/fetch",
                new SourceControlFetchRequest(repo.RootPath));
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        }

        // No unauthenticated request reached Git: HEAD is unchanged.
        Assert.Equal(headBefore, GitFetchTests.Head(repo));
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
