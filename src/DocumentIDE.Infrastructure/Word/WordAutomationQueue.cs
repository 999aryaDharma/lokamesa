using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Infrastructure.Word;

/// <summary>
/// WORD-03 — Dedicated STA Word Automation Queue.
///
/// All Word COM automation (ExportPdf, RenderPreview, document-list probes)
/// is serialized through one dedicated single-threaded-apartment worker with
/// concurrency exactly 1. Indexing/OpenXML work never touches this queue and
/// stays independently concurrent.
///
/// Cancellation applies between documents (a queued item whose token is
/// cancelled before execution starts never runs); an already-running COM
/// operation always runs to completion so a write is never aborted mid-call.
/// COM proxies are released deterministically via <see cref="TryReleaseComObject"/>.
/// </summary>
public sealed class WordAutomationQueue : IDisposable
{
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>());
    private readonly Thread _worker;
    private readonly CancellationTokenSource _shutdown = new();
    private int _activeCount;
    private int _maxObservedConcurrency;
    private int _disposed;

    public WordAutomationQueue()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "WordAutomationSTA"
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>Apartment of the dedicated worker. Must be STA for Word COM.</summary>
    public ApartmentState WorkerApartmentState => _worker.GetApartmentState();

    public int WorkerThreadId => _worker.ManagedThreadId;

    public bool IsWorkerAlive => _worker.IsAlive;

    /// <summary>Highest number of concurrently executing COM operations observed. Must stay 1.</summary>
    public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

    public int PendingCount => _queue.Count;

    /// <summary>
    /// Enqueues a Word COM operation to run on the dedicated STA worker.
    /// If <paramref name="cancellationToken"/> is cancelled before execution
    /// starts, the operation never runs and the task is cancelled.
    /// Exceptions thrown by the operation (Word busy/modal/unavailable, COM
    /// failure) propagate to the caller; the queue stays usable.
    /// </summary>
    public Task<T> EnqueueAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(WordAutomationQueue));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken))
            : default;

        _queue.Add(new WorkItem(
            () =>
            {
                registration.Dispose();
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                int active = Interlocked.Increment(ref _activeCount);
                try
                {
                    int prev;
                    do
                    {
                        prev = Volatile.Read(ref _maxObservedConcurrency);
                        if (active <= prev) break;
                    } while (Interlocked.CompareExchange(ref _maxObservedConcurrency, active, prev) != prev);

                    T result = operation();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeCount);
                }
            },
            () =>
            {
                registration.Dispose();
                tcs.TrySetCanceled(cancellationToken);
            }));

        return tcs.Task;
    }

    /// <summary>Non-generic overload for COM operations without a result.</summary>
    public Task EnqueueAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnqueueAsync<object?>(() =>
        {
            operation().GetAwaiter().GetResult();
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// Deterministically releases a COM proxy (loops ReleaseComObject until the
    /// reference count reaches zero). Safe to call with non-COM or null inputs.
    /// Returns the number of releases performed (0 for non-COM/null).
    /// </summary>
    public static int TryReleaseComObject(object? comObject)
    {
        if (comObject == null || !Marshal.IsComObject(comObject))
        {
            return 0;
        }

        int releases = 0;
        try
        {
            while (Marshal.ReleaseComObject(comObject) > 0)
            {
                releases++;
                if (releases > 1024)
                {
                    break;
                }
            }
            releases++;
        }
        catch
        {
            // Best effort: a release failure must not crash the STA worker.
        }
        return releases;
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable(_shutdown.Token))
            {
                try
                {
                    item.Execute();
                }
                catch
                {
                    // Work items report via their own TaskCompletionSource;
                    // never let the worker die on an item fault.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown path.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _queue.CompleteAdding();
        _shutdown.Cancel();
        if (Thread.CurrentThread.ManagedThreadId != _worker.ManagedThreadId)
        {
            _worker.Join(TimeSpan.FromSeconds(10));
        }

        // Drain: cancel anything that never ran so awaiters do not hang.
        while (_queue.TryTake(out var pending))
        {
            try
            {
                pending.Cancel();
            }
            catch
            {
                // Best effort on shutdown.
            }
        }

        _queue.Dispose();
        _shutdown.Dispose();
    }

    private sealed class WorkItem
    {
        private readonly Action _execute;
        private readonly Action _cancel;

        public WorkItem(Action execute, Action cancel)
        {
            _execute = execute;
            _cancel = cancel;
        }

        public void Execute() => _execute();
        public void Cancel() => _cancel();
    }
}
