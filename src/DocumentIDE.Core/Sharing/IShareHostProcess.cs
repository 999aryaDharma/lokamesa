using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Sharing;

/// <summary>
/// B5 — owner-side lifecycle of the separate <c>DocumentIDE.ShareHost</c>
/// process. The privileged app owns the public viewer host: it launches it on
/// loopback and hands the transport a base URL to tunnel. ShareHost itself never
/// references this abstraction (it is the child process, not its owner), so the
/// SHR-02 dependency boundary is unchanged.
/// </summary>
public interface IShareHostProcess
{
    /// <summary>
    /// The loopback base URL of the running ShareHost
    /// (e.g. <c>http://127.0.0.1:53123</c>), or <c>null</c> before it starts.
    /// </summary>
    string? LocalBaseUrl { get; }

    /// <summary>
    /// Starts ShareHost if it is not already running and returns its loopback
    /// base URL. Throws (typed) when the host cannot be launched so the caller
    /// can surface a real failure instead of a silent fallback.
    /// </summary>
    Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the ShareHost process if this owner started it.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
