using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;

namespace DocumentIDE.Infrastructure.Filesystem;

/// <summary>
/// Probes whether a document is likely open in an external editor (Microsoft Word).
/// Signals: the Word lock-file convention (~$&lt;basename&gt;) and an exclusive-open probe.
/// No single heuristic is absolute: when certainty is unavailable the probe reports
/// <see cref="ExternalEditState.Unknown"/> so the caller can fail safe.
/// </summary>
public sealed class WordEditStateProbe : IExternalEditStateProbe
{
    public Task<ExternalEditState> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
                return Task.FromResult(ExternalEditState.Unknown);

            // Signal 1: Word lock/temp file convention.
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                var lockFile = Path.Combine(directory, "~$" + Path.GetFileName(fullPath));
                if (File.Exists(lockFile))
                    return Task.FromResult(ExternalEditState.Locked);
            }

            // Signal 2: exclusive open. If another process holds the file, this fails.
            using (new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return Task.FromResult(ExternalEditState.NotLocked);
            }
        }
        catch (IOException)
        {
            // Exclusive open failed → another process holds the file.
            return Task.FromResult(ExternalEditState.Locked);
        }
        catch (Exception)
        {
            // Certainty unavailable (e.g. directory inaccessible, unexpected error).
            return Task.FromResult(ExternalEditState.Unknown);
        }
    }
}
