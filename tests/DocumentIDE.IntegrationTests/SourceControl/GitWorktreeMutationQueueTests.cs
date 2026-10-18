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

    /// <summary>
    /// SCM-17 (append-only): an already-cancelled token never runs the
    /// operation, even when the worktree gate is free. This is the cheap,
    /// non-racy half of the documented "cancelled while queued = never runs"
    /// contract; the concurrent release/cancel race below is the half that was
    /// genuinely unguarded.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_AlreadyCancelledToken_OperationNeverRuns()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        bool executed = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.EnqueueAsync(
                repo.RootPath,
                () =>
                {
                    executed = true;
                    return Task.FromResult(1);
                },
                new CancellationToken(canceled: true)));

        Assert.False(executed);
        Assert.Equal(0, queue.MaxObservedConcurrency);
    }

    /// <summary>
    /// SCM-17 — the narrowed-window safety gap the sprint fixes.
    ///
    /// <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/> checks the token
    /// once on entry. If the gate is released at the same moment the token is
    /// cancelled, the release can win and the acquire succeeds even though the
    /// token is already cancelled when the operation would start. The queue's
    /// documented contract is that such an operation never runs.
    ///
    /// Determinism: the queue's continuations run on a single-threaded
    /// <see cref="SynchronizationContext"/> owned by this test, so the gate can
    /// be released and the token cancelled before the queued continuation is
    /// pumped. Without the acquire-time guard this test fails because the
    /// operation runs; with it, the call is cancelled and the flag stays false.
    /// </summary>
    [Fact]
    public void EnqueueAsync_CancelledConcurrentlyWithRelease_OperationNeverRuns()
    {
        using var repo = TestGitRepository.Create();
        var queue = new GitWorktreeMutationQueue();
        var context = new SingleThreadedSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            using var blockerStarted = new ManualResetEventSlim(false);
            using var releaseBlocker = new ManualResetEventSlim(false);

            Task<int> blocker = queue.EnqueueAsync(repo.RootPath, async () =>
            {
                blockerStarted.Set();
                await Task.Run(() => releaseBlocker.Wait(TimeSpan.FromSeconds(20)));
                return 1;
            });

            Assert.True(
                blockerStarted.Wait(TimeSpan.FromSeconds(10)),
                "The blocking operation never started, so the gate was never held.");

            using var cts = new CancellationTokenSource();
            bool executed = false;
            Task<int> queued = queue.EnqueueAsync(repo.RootPath, () =>
            {
                executed = true;
                return Task.FromResult(2);
            }, cts.Token);

            Assert.False(queued.IsCompleted);

            // Release the gate. The blocker's continuation (and the queue's
            // gate release) is posted to this test's context; the queued
            // continuation is posted only after the release, and is not pumped
            // yet. Cancelling now reproduces release-vs-cancel with the release
            // already won.
            releaseBlocker.Set();
            context.PumpUntil(() => blocker.IsCompleted);
#pragma warning disable xUnit1031 // The context is pumped manually; the task is already complete.
            Assert.Equal(1, blocker.GetAwaiter().GetResult());
#pragma warning restore xUnit1031
            Assert.False(queued.IsCompleted, "The queued continuation ran before the token was cancelled.");

            cts.Cancel();
            context.PumpUntil(() => queued.IsCompleted);

            Assert.True(
                queued.IsCanceled,
                "A token cancelled while queued must cancel the call even when the acquire raced with a release.");
            Assert.False(executed, "The cancelled operation must never run.");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
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

    /// <summary>
    /// SCM-17 test seam: a single-threaded context whose work items the test
    /// pumps explicitly, so a release/cancel interleaving is deterministic
    /// instead of timing-dependent.
    /// </summary>
    private sealed class SingleThreadedSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void PumpUntil(Func<bool> predicate, TimeSpan? timeout = null)
        {
            DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
            while (!predicate())
            {
                if (_queue.TryTake(out (SendOrPostCallback Callback, object? State) work, TimeSpan.FromMilliseconds(50)))
                {
                    work.Callback(work.State);
                    continue;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("The queued synchronization-context work item never arrived.");
                }
            }
        }
    }
}
