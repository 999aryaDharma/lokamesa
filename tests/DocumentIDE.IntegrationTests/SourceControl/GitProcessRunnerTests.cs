using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-01 — Git Capability &amp; Controlled Process Runner.
/// Red stage: this test must FAIL before GitProcessRunner exists.
/// Acceptance: capability/version detection for the system Git executable
/// succeeds on a machine with Git, and a machine without discoverable Git
/// yields a typed unavailable state instead of an exception or silent fallback.
/// </summary>
public sealed class GitProcessRunnerTests
{
    [Fact]
    public async Task GitVersionCapabilityDetection_SucceedsOrYieldsTypedUnavailableState()
    {
        var runner = new GitProcessRunner();

        var capability = await runner.GetCapabilityAsync();

        Assert.True(
            capability.Status is GitCapabilityStatus.Available or GitCapabilityStatus.Unavailable,
            $"Unexpected capability status: {capability.Status}");

        if (capability.Status == GitCapabilityStatus.Available)
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.Version));
            Assert.Matches(@"^\d+\.\d+", capability.Version!);
            Assert.Equal(GitProcessStatus.Succeeded, capability.ProbeResult.Status);
            Assert.Equal(0, capability.ProbeResult.ExitCode);
        }
        else
        {
            // Typed unavailable state: inspectable, never an exception or silence.
            Assert.Equal(GitProcessStatus.Unavailable, capability.ProbeResult.Status);
            Assert.Equal(GitProcessErrorKind.GitNotInstalled, capability.ProbeResult.ErrorKind);
            Assert.False(string.IsNullOrWhiteSpace(capability.ProbeResult.ErrorMessage));
        }
    }

    [Fact]
    public async Task CapabilityProbe_MissingExecutable_YieldsTypedUnavailableState_InsteadOfException()
    {
        var options = new GitProcessRunnerOptions { ExecutablePath = "scm01-definitely-missing-git-executable" };
        var runner = new GitProcessRunner(options);

        var capability = await runner.GetCapabilityAsync();

        Assert.Equal(GitCapabilityStatus.Unavailable, capability.Status);
        Assert.Equal(GitProcessStatus.Unavailable, capability.ProbeResult.Status);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, capability.ProbeResult.ErrorKind);
        Assert.False(string.IsNullOrWhiteSpace(capability.ProbeResult.ErrorMessage));
    }

    [Fact]
    public async Task RunAsync_StructuredArguments_NoShell_ResolveRootOfRepositoryInPathWithSpacesAndUnicode()
    {
        using var repo = TestGitRepository.Create("scm01 repo \u00fc test (v1)");
        IGitProcessRunner runner = new GitProcessRunner();

        var result = await runner.RunAsync(new GitProcessRequest(
            new[] { "rev-parse", "--show-toplevel" },
            repo.RootPath));

        Assert.True(result.IsSuccess, $"Expected success, got {result.Status}: {result.ErrorMessage}");
        Assert.Equal(0, result.ExitCode);
        string reported = result.StandardOutput.Trim().Replace('/', Path.DirectorySeparatorChar);
        Assert.Equal(
            Path.GetFullPath(repo.RootPath),
            Path.GetFullPath(reported),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_OutsideRepository_YieldsTypedFailure_WithCapturedStreamsAndExitCode()
    {
        string emptyDirectory = Path.Combine(Path.GetTempPath(), "scm01", $"empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDirectory);
        try
        {
            var runner = new GitProcessRunner();

            var result = await runner.RunAsync(new GitProcessRequest(
                new[] { "status", "--porcelain=v2", "-z", "--branch" },
                emptyDirectory));

            Assert.False(result.IsSuccess);
            Assert.Equal(GitProcessStatus.Failed, result.Status);
            Assert.Equal(GitProcessErrorKind.GitRepositoryNotFound, result.ErrorKind);
            Assert.NotNull(result.ExitCode);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("not a git repository", result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
        finally
        {
            Directory.Delete(emptyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MissingWorkingDirectory_YieldsTypedWorktreeInvalid_InsteadOfException()
    {
        string missingDirectory = Path.Combine(Path.GetTempPath(), "scm01", $"missing-{Guid.NewGuid():N}");
        var runner = new GitProcessRunner();

        var result = await runner.RunAsync(new GitProcessRequest(
            new[] { "status", "--porcelain=v2", "-z", "--branch" },
            missingDirectory));

        Assert.Equal(GitProcessStatus.Failed, result.Status);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, result.ErrorKind);
        Assert.Null(result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_YieldsTypedUnavailable_GitNotInstalled()
    {
        var options = new GitProcessRunnerOptions { ExecutablePath = "scm01-definitely-missing-git-executable" };
        var runner = new GitProcessRunner(options);

        var result = await runner.RunAsync(new GitProcessRequest(
            new[] { "status" },
            Environment.CurrentDirectory));

        Assert.Equal(GitProcessStatus.Unavailable, result.Status);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, result.ErrorKind);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_RepositoryOperation_ExposesZeroExitCodeAndMachineReadableStdout()
    {
        using var repo = TestGitRepository.Create();
        var runner = new GitProcessRunner();

        var result = await runner.RunAsync(new GitProcessRequest(
            new[] { "status", "--porcelain=v2", "-z", "--branch" },
            repo.RootPath));

        Assert.True(result.IsSuccess, $"Expected success, got {result.Status}: {result.ErrorMessage}");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("# branch.head", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_EnvironmentOverrides_ReachTheChildProcess()
    {
        using var repo = TestGitRepository.Create();
        var runner = new GitProcessRunner();

        var request = new GitProcessRequest(
            new[] { "config", "--get", "scm01.testkey" },
            repo.RootPath,
            EnvironmentOverrides: new Dictionary<string, string>
            {
                ["GIT_CONFIG_COUNT"] = "1",
                ["GIT_CONFIG_KEY_0"] = "scm01.testkey",
                ["GIT_CONFIG_VALUE_0"] = "injected-value",
            });

        var result = await runner.RunAsync(request);

        Assert.True(result.IsSuccess, $"Expected success, got {result.Status}: {result.ErrorMessage}");
        Assert.Equal("injected-value", result.StandardOutput.Trim());
    }

    [Fact]
    public void ResolveEnvironmentVariables_ProtectedPolicyCannotBeOverridden()
    {
        var effective = GitProcessRunnerOptions.ResolveEnvironmentVariables(
            new Dictionary<string, string>
            {
                ["GIT_TERMINAL_PROMPT"] = "1",
                ["SCM01_CUSTOM"] = "x",
            });

        Assert.Equal("0", effective["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("x", effective["SCM01_CUSTOM"]);
    }

    [Fact]
    public async Task RunAsync_AlreadyCancelledToken_YieldsTypedCancelled_WithoutLaunchingProcess()
    {
        using var repo = TestGitRepository.Create();
        var runner = new GitProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await runner.RunAsync(
            new GitProcessRequest(new[] { "status", "--porcelain=v2", "-z", "--branch" }, repo.RootPath),
            cts.Token);

        Assert.Equal(GitProcessStatus.Cancelled, result.Status);
        Assert.Equal(0, runner.ActiveProcessCount);
    }

    [Fact]
    public async Task RunAsync_Cancellation_TerminatesGitProcessTree_WithoutOrphanedProcesses()
    {
        using var repo = TestGitRepository.Create();
        const int hookSleepSeconds = 3;
        HangingCommitHook.Write(repo, hookSleepSeconds);
        repo.WriteFile("notes.md", "content");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "notes.md"));

        var runner = new GitProcessRunner();
        using var cts = new CancellationTokenSource();

        Task<GitProcessResult> commitTask = runner.RunAsync(
            new GitProcessRequest(new[] { "commit", "-m", "scm01-cancel-probe" }, repo.RootPath),
            cts.Token);

        // The hook writes its `started` marker from inside the Git process tree
        // (git.exe -> sh -> sleep); observing it proves the tree is live.
        await WaitForFileAsync(HangingCommitHook.StartedMarker(repo), TimeSpan.FromSeconds(30));

        cts.Cancel();
        GitProcessResult result = await commitTask;

        Assert.Equal(GitProcessStatus.Cancelled, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, runner.ActiveProcessCount);

        // If the process tree were not killed, `sh` would survive its parent,
        // finish sleeping, and write the `finished` marker.
        await Task.Delay(TimeSpan.FromSeconds(hookSleepSeconds + 1));
        Assert.False(
            File.Exists(HangingCommitHook.FinishedMarker(repo)),
            "A hook descendant survived cancellation: the Git process tree was not fully terminated.");
    }

    [Fact]
    public async Task RunAsync_Timeout_TerminatesGitProcessTree_AndYieldsTypedTimedOut()
    {
        using var repo = TestGitRepository.Create();
        const int hookSleepSeconds = 3;
        HangingCommitHook.Write(repo, hookSleepSeconds);
        repo.WriteFile("notes.md", "content");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "notes.md"));

        var runner = new GitProcessRunner();

        Task<GitProcessResult> commitTask = runner.RunAsync(new GitProcessRequest(
            new[] { "commit", "-m", "scm01-timeout-probe" },
            repo.RootPath,
            Timeout: TimeSpan.FromSeconds(1)));

        await WaitForFileAsync(HangingCommitHook.StartedMarker(repo), TimeSpan.FromSeconds(30));

        GitProcessResult result = await commitTask;

        Assert.Equal(GitProcessStatus.TimedOut, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, runner.ActiveProcessCount);

        await Task.Delay(TimeSpan.FromSeconds(hookSleepSeconds + 1));
        Assert.False(
            File.Exists(HangingCommitHook.FinishedMarker(repo)),
            "A hook descendant survived the timeout: the Git process tree was not fully terminated.");
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Expected marker file '{path}' was not created within {timeout}.");
    }
}
