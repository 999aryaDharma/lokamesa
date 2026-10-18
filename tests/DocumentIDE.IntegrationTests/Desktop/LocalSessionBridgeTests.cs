using System;
using System.Threading.Tasks;
using DocumentIDE.Desktop.WebView;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// The packaged app navigates to <c>https://appassets.html2sav/index.html</c>,
/// which has no <c>/api</c> route. The only way the SPA can reach the loopback
/// API is the pre-load script that sets <c>__DOCUMENTIDE_API_URL__</c>; if the
/// WebView navigates before that registration completes, every fetch resolves
/// against the asset origin and the UI reports "Failed to fetch".
/// </summary>
public sealed class LocalSessionBridgeTests
{
    [Fact]
    public async Task PrepareAsync_CompletesOnlyAfterTheScriptRegistrationCompletes()
    {
        var bridge = new LocalSessionBridge("session-token-abc", 5311);
        var registration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? scriptSeen = null;

        Task prepare = bridge.PrepareAsync(
            async script =>
            {
                scriptSeen = script;
                await registration.Task.ConfigureAwait(false);
            },
            "http://127.0.0.1:5311");

        Assert.False(prepare.IsCompleted);
        Assert.NotNull(scriptSeen);
        Assert.Contains("window.__DOCUMENTIDE_API_URL__ = 'http://127.0.0.1:5311'", scriptSeen);
        Assert.Contains("window.__DOCUMENTIDE_SESSION_TOKEN__ = 'session-token-abc'", scriptSeen);
        Assert.Contains("window.__DOCUMENTIDE_WORKSPACE_ID__ = 'default'", scriptSeen);

        registration.SetResult();
        await prepare.ConfigureAwait(false);

        Assert.True(prepare.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task PrepareAsync_PropagatesARegistrationFailure()
    {
        var bridge = new LocalSessionBridge("session-token-abc", 5311);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bridge.PrepareAsync(
                _ => Task.FromException(new InvalidOperationException("registration failed")),
                "http://127.0.0.1:5311"));
    }
}
