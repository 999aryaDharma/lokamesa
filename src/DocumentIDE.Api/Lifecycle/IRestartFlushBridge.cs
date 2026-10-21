namespace DocumentIDE.Api.Lifecycle;

public sealed record RestartFlushResult(bool Ok, string? Reason);

/// <summary>
/// Host-specific bridge that asks the UI to save dirty editor buffers before
/// the auto-updater stops the process. The API host has no UI and uses
/// <see cref="NoopRestartFlushBridge"/>; the desktop host supplies a bridge
/// backed by WebView2.
/// </summary>
public interface IRestartFlushBridge
{
    Task<RestartFlushResult> PrepareRestartAsync(CancellationToken cancellationToken);
}

public sealed class NoopRestartFlushBridge : IRestartFlushBridge
{
    public Task<RestartFlushResult> PrepareRestartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new RestartFlushResult(true, null));
}
