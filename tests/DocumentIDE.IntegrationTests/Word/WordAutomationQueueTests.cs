using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Infrastructure.Word;
using Xunit;

namespace DocumentIDE.IntegrationTests.Word;

/// <summary>
/// WORD-03 — Dedicated STA Word Automation Queue.
/// Red stage: this test must FAIL before WordAutomationQueue exists.
/// Acceptance: max Word COM concurrency is exactly 1 on a dedicated STA worker;
/// indexing/OpenXML work stays independently concurrent.
/// </summary>
public sealed class WordAutomationQueueTests
{
    [Fact]
    public async Task ConcurrentCallers_AreSerialized_MaxObservedConcurrencyIsOne()
    {
        using var queue = new WordAutomationQueue();
        const int callers = 8;
        int active = 0;
        int maxObserved = 0;

        var tasks = Enumerable.Range(0, callers).Select(_ => queue.EnqueueAsync(() =>
        {
            int now = Interlocked.Increment(ref active);
            try
            {
                int prev;
                do
                {
                    prev = maxObserved;
                    if (now <= prev) break;
                } while (Interlocked.CompareExchange(ref maxObserved, now, prev) != prev);
                Thread.Sleep(50);
                return now;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        })).ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(callers, results.Length);
        Assert.Equal(1, maxObserved);
        Assert.Equal(1, queue.MaxObservedConcurrency);
    }

    [Fact]
    public async Task WorkerThread_IsSta()
    {
        using var queue = new WordAutomationQueue();

        Assert.Equal(ApartmentState.STA, queue.WorkerApartmentState);

        var observed = await queue.EnqueueAsync(() => Thread.CurrentThread.GetApartmentState());
        Assert.Equal(ApartmentState.STA, observed);
    }

    [Fact]
    public async Task AllOperations_RunOnSameDedicatedWorkerThread()
    {
        using var queue = new WordAutomationQueue();

        var ids = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => queue.EnqueueAsync(() => Thread.CurrentThread.ManagedThreadId)));

        Assert.All(ids, id => Assert.Equal(queue.WorkerThreadId, id));
        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task CancelledQueuedItem_DoesNotExecute()
    {
        using var queue = new WordAutomationQueue();
        using var gate = new ManualResetEventSlim(false);
        bool executed = false;

        var blocking = queue.EnqueueAsync(() =>
        {
            gate.Wait(TimeSpan.FromSeconds(10));
            return 1;
        });

        using var cts = new CancellationTokenSource();
        var cancelled = queue.EnqueueAsync(() =>
        {
            executed = true;
            return 2;
        }, cts.Token);

        // Cancel while the first operation still occupies the worker:
        // cancellation applies between documents, never mid-unsafe-COM-write.
        cts.Cancel();
        gate.Set();

        Assert.Equal(1, await blocking);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(executed);
    }

    [Fact]
    public async Task FailedOperation_DoesNotBreakQueue()
    {
        using var queue = new WordAutomationQueue();

        var failing = queue.EnqueueAsync<int>(() => throw new InvalidOperationException("Word is busy"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);

        var next = await queue.EnqueueAsync(() => 42);
        Assert.Equal(42, next);
    }

    [Fact]
    public async Task NonQueuedWork_RemainsConcurrentWhileQueueIsBusy()
    {
        using var queue = new WordAutomationQueue();
        int active = 0;
        int maxObserved = 0;

        var slow = queue.EnqueueAsync(() =>
        {
            Thread.Sleep(300);
            return 1;
        });

        var background = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            int now = Interlocked.Increment(ref active);
            try
            {
                int prev;
                do
                {
                    prev = maxObserved;
                    if (now <= prev) break;
                } while (Interlocked.CompareExchange(ref maxObserved, now, prev) != prev);
                Thread.Sleep(50);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        })).ToArray();

        await Task.WhenAll(background);
        Assert.Equal(1, await slow);

        // Indexing/OpenXML-style work outside the COM queue must overlap freely.
        Assert.True(maxObserved > 1, $"Expected concurrent background work, observed max {maxObserved}.");
    }

    [Fact]
    public void Dispose_TerminatesWorkerWithoutOrphanThread()
    {
        var queue = new WordAutomationQueue();
        int workerId = queue.WorkerThreadId;
        Assert.True(workerId > 0);

        queue.Dispose();

        Assert.False(queue.IsWorkerAlive);
    }

    [Fact]
    public void ReleaseComObject_NonComObject_DoesNotThrow()
    {
        var result = WordAutomationQueue.TryReleaseComObject(new object());
        Assert.Equal(0, result);
        Assert.Equal(0, WordAutomationQueue.TryReleaseComObject(null));
    }
}
