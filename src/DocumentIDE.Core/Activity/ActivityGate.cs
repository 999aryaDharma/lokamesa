using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DocumentIDE.Core.Activity;

/// <summary>
/// Observer notified when a mutation lease is acquired. Used by the desktop
/// host/tests to observe the gate without polling; production registrations
/// leave it null.
/// </summary>
public interface IActivityObserver
{
    void Entered(string reason);
}

/// <summary>
/// In-memory record of backend mutations currently in flight. The dev
/// auto-updater reads this through <c>/api/app/status</c> and defers a restart
/// while any lease is held, so an install swap never interrupts a batch
/// apply/undo, Word automation, a git mutation, or reconciliation.
///
/// The gate is observational only: it never blocks or cancels the mutation.
/// </summary>
public sealed class ActivityGate
{
    private readonly object _sync = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly IActivityObserver? _observer;

    public ActivityGate(IActivityObserver? observer = null)
    {
        _observer = observer;
    }

    public int ActiveCount
    {
        get
        {
            lock (_sync)
            {
                return _counts.Values.Sum();
            }
        }
    }

    public IReadOnlyList<string> ActiveReasons
    {
        get
        {
            lock (_sync)
            {
                return _counts
                    .Where(pair => pair.Value > 0)
                    .Select(pair => pair.Key)
                    .ToArray();
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _counts.Count > 0;
            }
        }
    }

    /// <summary>
    /// Marks a mutation as in flight. Dispose the returned lease when the
    /// mutation finishes; disposal is idempotent and safe from any thread.
    /// </summary>
    public IDisposable Enter(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_sync)
        {
            _counts[reason] = _counts.TryGetValue(reason, out int count) ? count + 1 : 1;
        }

        // Notified outside the lock so an observer may block or call back into
        // the gate (tests use this to pause an action deterministically).
        _observer?.Entered(reason);

        return new Lease(this, reason);
    }

    private void Exit(string reason)
    {
        lock (_sync)
        {
            if (!_counts.TryGetValue(reason, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                _counts.Remove(reason);
            }
            else
            {
                _counts[reason] = count - 1;
            }
        }
    }

    private sealed class Lease(ActivityGate gate, string reason) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            gate.Exit(reason);
        }
    }
}
