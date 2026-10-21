namespace DocumentIDE.Api.Lifecycle;

/// <summary>Runtime facts about this process, reported by <c>GET /api/app/status</c>.</summary>
public sealed class AppLifecycleState
{
    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
}
