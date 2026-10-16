using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-01 — per-worktree mutation queue (spec 13 §19).
/// Red stage: these tests must FAIL before GitWorktreeMutationQueue exists.
/// Acceptance: index/repository-mutating Git operations against one worktree
/// are serialized (concurrency exactly 1); different worktrees stay
/// independent; the runner routes mutating commands through the queue and
/// leaves read-only commands unserialized.
/// </summary>
public sealed class GitWorktreeMutationQueueTests
{
    [Fact]
    public async Task EnqueueAsync_SerializesMutationOperations_ForTheSameWorktree()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        const int callers = 6;
        int active = 0;
        int maxObserved = 0;

        // Same worktree spelled three ways (case + trailing separator) must map
        // to one queue slot.
        string[] spellings =
        {
            repo.RootPath,
            repo.RootPath.ToUpperInvariant(),
            repo.RootPath + Path.DirectorySeparatorChar,
        };

        Task<int>[] tasks = Enumerable.Range(0, callers).Select(index =>
            queue.EnqueueAsync(spellings[index % spellings.Length], async () =>
            {
                int now = Interlocked.Increment(ref active);
                try
                {
                    int previous;
                    do
                    {
                        previous = maxObserved;
                        if (now <= previous)
                        {
                            break;
                        }
                    }
                    while (Interlocked.CompareExchange(ref maxObserved, now, previous) != previous);

                    await Task.Delay(50);
                    return now;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            })).ToArray();

        int[] results = await Task.WhenAll(tasks);

        Assert.Equal(callers, results.Length);
        Assert.Equal(1, maxObserved);
        Assert.Equal(1, queue.MaxObservedConcurrency);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotSerialize_AcrossDifferentWorktrees()
    {
        using var repoA = TestGitRepository.Create();
        using var repoB = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        using var bothStarted = new CountdownEvent(2);

        Task<int>[] tasks =
        {
            queue.EnqueueAsync(repoA.RootPath, async () =>
            {
                await SignalAndWaitAsync(bothStarted, TimeSpan.FromSeconds(10));
                return 1;
            }),
            queue.EnqueueAsync(repoB.RootPath, async () =>
            {
                await SignalAndWaitAsync(bothStarted, TimeSpan.FromSeconds(10));
                return 2;
            }),
        };

        int[] results = await Task.WhenAll(tasks);

        Assert.Equal(new[] { 1, 2 }, results);
        Assert.True(queue.MaxObservedConcurrency >= 2);
    }

    [Fact]
    public async Task EnqueueAsync_CancelledWhileQueued_OperationNeverRuns()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        using var gate = new ManualResetEventSlim(false);
        bool executed = false;

        Task<int> blocking = queue.EnqueueAsync(repo.RootPath, async () =>
        {
            await Task.Run(() => gate.Wait(TimeSpan.FromSeconds(20)));
            return 1;
        });

        using var cts = new CancellationTokenSource();
        Task<int> cancelled = queue.EnqueueAsync(
            repo.RootPath,
            () =>
            {
                executed = true;
                return Task.FromResult(2);
            },
            cts.Token);

        cts.Cancel();
        gate.Set();

        Assert.Equal(1, await blocking);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(executed);
    }

    [Fact]
    public async Task EnqueueAsync_FaultedOperation_DoesNotBreakQueue()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();

        Task<int> failing = queue.EnqueueAsync<int>(repo.RootPath, () =>
            throw new InvalidOperationException("index is locked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);

        int next = await queue.EnqueueAsync(repo.RootPath, () => Task.FromResult(42));
        Assert.Equal(42, next);
    }

    [Fact]
    public async Task GitProcessRunner_RoutesIndexMutatingCommands_ThroughTheWorktreeQueue()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        repo.WriteFile("a.md", "a");
        repo.WriteFile("b.md", "b");

        Task<GitProcessResult> addA = runner.RunAsync(
            new GitProcessRequest(new[] { "add", "--", "a.md" }, repo.RootPath));
        Task<GitProcessResult> addB = runner.RunAsync(
            new GitProcessRequest(new[] { "add", "--", "b.md" }, repo.RootPath));

        GitProcessResult[] results = await Task.WhenAll(addA, addB);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.ErrorMessage));
        Assert.Equal(1, queue.MaxObservedConcurrency);

        GitProcessResult staged = await runner.RunAsync(
            new GitProcessRequest(new[] { "diff", "--cached", "--name-only", "-z" }, repo.RootPath));
        Assert.True(staged.IsSuccess, staged.ErrorMessage);
        Assert.Contains("a.md", staged.StandardOutput);
        Assert.Contains("b.md", staged.StandardOutput);
    }

    [Fact]
    public async Task GitProcessRunner_DoesNotRouteReadOnlyCommands_ThroughTheWorktreeQueue()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);

        Task<GitProcessResult> statusA = runner.RunAsync(
            new GitProcessRequest(new[] { "status", "--porcelain=v2", "-z", "--branch" }, repo.RootPath));
        Task<GitProcessResult> statusB = runner.RunAsync(
            new GitProcessRequest(new[] { "status", "--porcelain=v2", "-z", "--branch" }, repo.RootPath));

        GitProcessResult[] results = await Task.WhenAll(statusA, statusB);

        Assert.All(results, result => Assert.True(result.IsSuccess, result.ErrorMessage));
        Assert.Equal(0, queue.MaxObservedConcurrency);
    }

    [Fact]
    public async Task GitProcessRunner_CancelledWhileWaitingForWorktreeQueue_YieldsTypedCancelled()
    {
        using var repo = TestGitRepository.Create();
        repo.WriteFile("a.md", "a");
        var queue = new GitWorktreeMutationQueue();
        using var gate = new ManualResetEventSlim(false);

        Task<int> blocker = queue.EnqueueAsync(repo.RootPath, async () =>
        {
            await Task.Run(() => gate.Wait(TimeSpan.FromSeconds(20)));
            return 0;
        });

        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        using var cts = new CancellationTokenSource();

        Task<GitProcessResult> pending = runner.RunAsync(
            new GitProcessRequest(new[] { "add", "--", "a.md" }, repo.RootPath),
            cts.Token);

        cts.Cancel();
        GitProcessResult result = await pending;

        Assert.Equal(GitProcessStatus.Cancelled, result.Status);
        Assert.Equal(0, runner.ActiveProcessCount);

        gate.Set();
        Assert.Equal(0, await blocker);
    }

    private static async Task SignalAndWaitAsync(CountdownEvent barrier, TimeSpan timeout)
    {
        barrier.Signal();
        bool observed = await Task.Run(() => barrier.Wait(timeout));
        if (!observed)
        {
            throw new TimeoutException("Operations on different worktrees were serialized; they must stay independent.");
        }
    }
}
