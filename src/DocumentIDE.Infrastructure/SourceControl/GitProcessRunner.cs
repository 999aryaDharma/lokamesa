using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-01 — controlled boundary for invoking the system Git executable.
///
/// Git runs as a direct child process of this application (never through a
/// shell) with a controlled working directory and environment:
/// <list type="bullet">
/// <item>arguments travel as a structured argument list (spec 13 §18/§23),</item>
/// <item>stdout/stderr are captured and the exit code is inspectable,</item>
/// <item>cancellation and timeouts terminate the whole process tree, so no
/// orphaned Git (hook/helper) processes survive,</item>
/// <item>failures become typed <see cref="GitProcessResult"/> values — callers
/// never need to catch exceptions for operational outcomes.</item>
/// </list>
/// </summary>
public sealed class GitProcessRunner : IGitProcessRunner
{
    private static readonly Regex VersionPattern = new(
        @"git version\s+(?<version>\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly TimeSpan ProcessTreeReapGrace = TimeSpan.FromSeconds(10);

    private readonly GitProcessRunnerOptions _options;
    private readonly GitWorktreeMutationQueue? _mutationQueue;
    private int _activeProcessCount;

    public GitProcessRunner()
        : this(GitProcessRunnerOptions.Default)
    {
    }

    public GitProcessRunner(
        GitProcessRunnerOptions options,
        GitWorktreeMutationQueue? mutationQueue = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mutationQueue = mutationQueue;
    }

    /// <summary>
    /// Number of Git child processes this runner currently has alive.
    /// Diagnostics/verification aid: after a cancelled or timed-out call
    /// returns, this must be back to 0.
    /// </summary>
    public int ActiveProcessCount => Volatile.Read(ref _activeProcessCount);

    /// <summary>
    /// Probes the configured Git executable with `git --version`.
    /// Always returns a typed capability state; never throws for
    /// missing/unusable Git.
    /// </summary>
    public async Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default)
    {
        string executable = ResolveExecutable();
        var probeRequest = new GitProcessRequest(
            new[] { "--version" },
            Environment.CurrentDirectory,
            _options.ProbeTimeout);

        GitProcessResult probe = await RunCoreAsync(probeRequest, executable, cancellationToken);

        if (probe.Status != GitProcessStatus.Succeeded)
        {
            return new GitCapabilityInfo(GitCapabilityStatus.Unavailable, executable, probe);
        }

        Match match = VersionPattern.Match(probe.StandardOutput);
        if (!match.Success)
        {
            GitProcessResult unrecognized = probe with
            {
                Status = GitProcessStatus.Unavailable,
                ErrorKind = GitProcessErrorKind.GitNotInstalled,
                ErrorMessage = $"`git --version` did not report a recognizable version: '{Truncate(probe.StandardOutput)}'",
            };
            return new GitCapabilityInfo(GitCapabilityStatus.Unavailable, executable, unrecognized);
        }

        return new GitCapabilityInfo(
            GitCapabilityStatus.Available,
            executable,
            probe,
            match.Groups["version"].Value);
    }

    /// <inheritdoc />
    public async Task<GitProcessResult> RunAsync(GitProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string executable = ResolveExecutable();

        // Spec 13 §19: operations that mutate shared repository state are
        // serialized per worktree. The classifier is deterministic and based
        // only on the Git vocabulary; read-only commands stay unserialized.
        if (_mutationQueue is not null
            && request.Arguments is not null
            && GitCommandClassifier.MutatesRepositoryState(request.Arguments))
        {
            try
            {
                return await _mutationQueue.EnqueueAsync(
                    request.WorkingDirectory,
                    innerToken => RunCoreAsync(request, executable, innerToken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new GitProcessResult(
                    GitProcessStatus.Cancelled,
                    null,
                    string.Empty,
                    string.Empty,
                    GitProcessErrorKind.None,
                    "Git operation was cancelled while waiting for the worktree mutation queue and never executed.");
            }
        }

        return await RunCoreAsync(request, executable, cancellationToken);
    }

    private string ResolveExecutable() =>
        string.IsNullOrWhiteSpace(_options.ExecutablePath) ? "git" : _options.ExecutablePath!;

    private async Task<GitProcessResult> RunCoreAsync(
        GitProcessRequest request,
        string executable,
        CancellationToken cancellationToken)
    {
        if (request.Arguments is null)
        {
            throw new ArgumentException("Git arguments are required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            throw new ArgumentException("A working directory is required.", nameof(request));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CancelledBeforeLaunch(request);
        }

        string workingDirectory;
        try
        {
            workingDirectory = Path.GetFullPath(request.WorkingDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or System.Security.SecurityException)
        {
            return WorktreeInvalid(request.WorkingDirectory, exception.Message);
        }

        if (!Directory.Exists(workingDirectory))
        {
            return WorktreeInvalid(workingDirectory, "The working directory does not exist.");
        }

        ProcessStartInfo startInfo = BuildStartInfo(executable, workingDirectory, request);
        TimeSpan timeout = request.Timeout ?? _options.DefaultTimeout;

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return Unavailable(executable, $"Failed to start '{executable}'.");
            }

            Interlocked.Increment(ref _activeProcessCount);
            try
            {
                return await WaitForProcessAsync(process, request, executable, timeout, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeProcessCount);
            }
        }
        catch (Win32Exception exception)
        {
            return Unavailable(executable, $"Git executable '{executable}' could not be started: {exception.Message}");
        }
    }

    private static async Task<GitProcessResult> WaitForProcessAsync(
        Process process,
        GitProcessRequest request,
        string executable,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        CloseChildStdin(process);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            bool timedOut = !cancellationToken.IsCancellationRequested;
            TryKillProcessTree(process);
            await ReapProcessBestEffortAsync(process);

            string killedStdout = await ReadStreamBestEffortAsync(stdoutTask);
            string killedStderr = await ReadStreamBestEffortAsync(stderrTask);

            return timedOut
                ? new GitProcessResult(
                    GitProcessStatus.TimedOut,
                    null,
                    killedStdout,
                    killedStderr,
                    GitProcessErrorKind.None,
                    $"`{executable} {SummarizeArguments(request.Arguments)}` exceeded the timeout of {timeout} and its process tree was terminated.")
                : new GitProcessResult(
                    GitProcessStatus.Cancelled,
                    null,
                    killedStdout,
                    killedStderr,
                    GitProcessErrorKind.None,
                    "Git operation was cancelled and its process tree was terminated.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        int exitCode = process.ExitCode;

        if (exitCode == 0)
        {
            return new GitProcessResult(GitProcessStatus.Succeeded, exitCode, stdout, stderr);
        }

        return new GitProcessResult(
            GitProcessStatus.Failed,
            exitCode,
            stdout,
            stderr,
            GitFailureClassifier.Classify(exitCode, stdout, stderr),
            $"`{executable} {SummarizeArguments(request.Arguments)}` exited with code {exitCode}.");
    }

    private static ProcessStartInfo BuildStartInfo(
        string executable,
        string workingDirectory,
        GitProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (string argument in request.Arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException(
                    "Git arguments must not contain null entries; pass each argument as a separate string.",
                    nameof(request));
            }

            startInfo.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> variable in
            GitProcessRunnerOptions.ResolveEnvironmentVariables(request.EnvironmentOverrides))
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        return startInfo;
    }

    private static GitProcessResult CancelledBeforeLaunch(GitProcessRequest request) =>
        new(
            GitProcessStatus.Cancelled,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.None,
            $"Git operation was cancelled before launch: `git {SummarizeArguments(request.Arguments)}`.");

    private static GitProcessResult WorktreeInvalid(string workingDirectory, string reason) =>
        new(
            GitProcessStatus.Failed,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitWorktreeInvalid,
            $"Working directory '{workingDirectory}' is unavailable: {reason}");

    private static GitProcessResult Unavailable(string executable, string message) =>
        new(
            GitProcessStatus.Unavailable,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitNotInstalled,
            message);

    private static void CloseChildStdin(Process process)
    {
        // Git must never wait on inherited stdin; closing it makes an
        // accidental interactive prompt fail fast instead of hanging.
        try
        {
            process.StandardInput.Close();
        }
        catch (Exception)
        {
            // Stdin already closed/terminated; nothing to release.
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        // SCM-01: cancellation/timeout must not leave orphaned Git processes,
        // including descendants spawned by hooks or `git-remote-*` helpers.
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Best effort: the process may have exited between the check and
            // the kill; the caller still receives a typed outcome.
        }
    }

    private static async Task ReapProcessBestEffortAsync(Process process)
    {
        // After a tree kill the child must be reaped before this call returns,
        // otherwise a caller could observe a live process after Cancelled/TimedOut.
        try
        {
            using var grace = new CancellationTokenSource(ProcessTreeReapGrace);
            await process.WaitForExitAsync(grace.Token);
        }
        catch (Exception)
        {
            // Bounded: a locked/killed process that cannot be reaped in time
            // must not block the typed result forever.
        }
    }

    private static async Task<string> ReadStreamBestEffortAsync(Task<string> readTask)
    {
        try
        {
            return await readTask;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string SummarizeArguments(IReadOnlyList<string> arguments) =>
        arguments.Count == 0 ? string.Empty : string.Join(' ', arguments);

    private static string Truncate(string value) =>
        value.Length <= 200 ? value.Trim() : value[..200].Trim() + "...";
}
