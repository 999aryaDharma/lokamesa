using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;

namespace DocumentIDE.Infrastructure.Filesystem;

/// <summary>
/// Transactional single-file replace used by the batch apply engine.
/// Copies the original to a same-volume temp file, mutates/validates the temp,
/// then atomically replaces the original. If anything fails before commit,
/// the temp is rolled back and the original remains unchanged.
/// BAT-04: Transactional Safe Apply Engine.
/// </summary>
public sealed class AtomicFileReplacer : IAtomicFileReplacer
{
    public async Task<PreparedFileReplacement> PrepareAsync(
        string sourcePath,
        Func<string, CancellationToken, Task> mutate,
        Func<string, CancellationToken, Task>? validate,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(mutate);

        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource))
            throw new FileNotFoundException($"Source file not found: {fullSource}", fullSource);

        var directory = Path.GetDirectoryName(fullSource);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException($"Cannot determine directory for '{fullSource}'.");

        // Same directory ⇒ same volume, so File.Replace is a true atomic rename.
        // The '~' prefix keeps the temp file ignored by the filesystem watcher's
        // temporary/lock-file filter while it exists.
        var tempPath = Path.Combine(directory, $"~apply-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(fullSource, tempPath, overwrite: false);
            await mutate(tempPath, cancellationToken);
            if (validate != null)
                await validate(tempPath, cancellationToken);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        return new PreparedFileReplacement(fullSource, tempPath);
    }

    public Task CommitAsync(PreparedFileReplacement replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        cancellationToken.ThrowIfCancellationRequested();
        ReplaceAtomically(replacement.TempPath, replacement.SourcePath);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(PreparedFileReplacement replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        TryDelete(replacement.TempPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Strict atomic replace. Deliberately no non-atomic fallback: if the target is
    /// locked or unavailable, the commit fails and the original remains unchanged.
    /// </summary>
    public static void ReplaceAtomically(string tempFilePath, string targetFilePath)
    {
        File.Replace(tempFilePath, targetFilePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the temp file carries no user data.
        }
    }
}
