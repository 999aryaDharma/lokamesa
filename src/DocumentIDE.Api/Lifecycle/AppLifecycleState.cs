namespace DocumentIDE.Api.Lifecycle;

/// <summary>Runtime facts about this process, reported by <c>GET /api/app/status</c>.</summary>
public sealed class AppLifecycleState
{
    // Process start, not the first DI resolution (which is the first status
    // request). The watcher only uses this for logs, but it should be honest.
    public DateTimeOffset StartedAtUtc { get; } = ProcessStartUtc();

    private static DateTimeOffset ProcessStartUtc()
    {
        try
        {
            return new DateTimeOffset(
                System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime());
        }
        catch
        {
            return DateTimeOffset.UtcNow;
        }
    }
}
