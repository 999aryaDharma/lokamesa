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

    private static string Head(TestGitRepository repo) => Head(repo.RootPath);

    private static string Head(string rootPath) =>
        TestGitRepository.RunGitCaptureUtf8(rootPath, "rev-parse", "HEAD").Trim();

    private static int CommitCount(TestGitRepository repo) =>
        int.Parse(TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-list", "--count", "HEAD").Trim());

    private static string[] CommitFiles(TestGitRepository repo, string commitId) =>
        TestGitRepository
            .RunGitCaptureUtf8(repo.RootPath, "show", "--name-only", "--format=", commitId)
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
