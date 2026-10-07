using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Process-wide per-file operation locks shared by every component that mutates a
/// document through the safe-write discipline (batch apply, conflict-aware undo).
/// Serializing the whole check→commit sequence per path closes the in-process race
/// between two operations targeting the same file; cross-process writers remain
/// out of scope and are mitigated by fingerprint checks and the atomic replace.
/// </summary>
internal static class FileOperationLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim GetOrAdd(string path) => FileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

    public static void EvictIfIdle(string path, SemaphoreSlim fileLock)
    {
        // Removal is conditional on identity, so a semaphore a concurrent operation
        // already replaced is never evicted. The check is inherently racy; content
        // safety remains guaranteed independently by the fingerprint checks and the
        // atomic replace.
        if (fileLock.CurrentCount == 1)
            ((ICollection<KeyValuePair<string, SemaphoreSlim>>)FileLocks)
                .Remove(new KeyValuePair<string, SemaphoreSlim>(path, fileLock));
    }
}
