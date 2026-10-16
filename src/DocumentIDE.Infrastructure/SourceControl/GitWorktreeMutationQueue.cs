using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-01 — per-worktree mutation queue (spec 13 §19).
///
/// Index/repository-mutating Git operations against one worktree are serialized
/// (concurrency exactly 1 per worktree), while different worktrees stay fully
/// independent. This queue is distinct from the Word COM STA queue; read-only
/// history/status operations are not routed through it.
///
/// The queue key is the canonical working directory resolved by the caller;
/// callers must pass the worktree/repository root (SCM-02+ resolve it with
/// <c>git rev-parse</c>) so operations against the same index share one slot.
/// </summary>
public sealed class GitWorktreeMutationQueue
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _worktreeGates;
    private int _activeCount;
    private int _maxObservedConcurrency;

    public GitWorktreeMutationQueue()
    {
        _worktreeGates = new ConcurrentDictionary<string, SemaphoreSlim>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>
    /// Highest number of concurrently executing queued operations observed
    /// across all worktrees. Must stay 1 when only one worktree is used.
    /// </summary>
    public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

    /// <summary>Enqueues a mutation for a worktree. See the token overload.</summary>
    public Task<T> EnqueueAsync<T>(
        string worktreePath,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnqueueAsync(worktreePath, _ => operation(), cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> once the worktree slot is free.
    /// If <paramref name="cancellationToken"/> fires while queued, the task is
    /// cancelled and the operation never runs. Exceptions from the operation
    /// propagate; the queue stays usable.
    /// </summary>
    public async Task<T> EnqueueAsync<T>(
        string worktreePath,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentNullException.ThrowIfNull(operation);

        SemaphoreSlim gate = _worktreeGates.GetOrAdd(
            NormalizeWorktreeKey(worktreePath),
            static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            int active = Interlocked.Increment(ref _activeCount);
            try
            {
                int previous;
                do
                {
                    previous = Volatile.Read(ref _maxObservedConcurrency);
                    if (active <= previous)
                    {
                        break;
                    }
                }
                while (Interlocked.CompareExchange(ref _maxObservedConcurrency, active, previous) != previous);

                return await operation(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string NormalizeWorktreeKey(string worktreePath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(worktreePath));
}
