using System.Text.Json;
using DocumentIDE.Api.Lifecycle;
using DocumentIDE.Desktop.WebView;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// DEV-UPD — the WebView flush bridge. <c>ExecuteScriptAsync</c> returns the JSON
/// value of the script (a Promise serializes as <c>{}</c>), so the page stores its
/// result in a global and the host polls it; the parser below is the seam tested
/// without a live WebView2 control.
/// </summary>
public sealed class RestartCoordinatorTests
{
    [Fact]
    public async Task Coordinator_returns_ok_when_no_bridge_is_attached()
    {
        var coordinator = new RestartCoordinator();

        var result = await coordinator.PrepareRestartAsync(CancellationToken.None);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Coordinator_propagates_the_bridge_failure()
    {
        var coordinator = new RestartCoordinator
        {
            FlushAsync = _ => Task.FromResult(new RestartFlushResult(false, "locked")),
        };

        var result = await coordinator.PrepareRestartAsync(CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("locked", result.Reason);
    }

    [Fact]
    public void Poll_result_is_not_ready_until_the_page_reports()
    {
        Assert.Null(RestartCoordinator.ParsePollResult("null"));
    }

    [Fact]
    public void Poll_result_parses_success()
    {
        string raw = JsonSerializer.Serialize("""{"ok":true}""");

        var result = RestartCoordinator.ParsePollResult(raw);

        Assert.NotNull(result);
        Assert.True(result!.Ok);
    }

    [Fact]
    public void Poll_result_parses_a_failure_reason()
    {
        string raw = JsonSerializer.Serialize("""{"ok":false,"reason":"disk full"}""");

        var result = RestartCoordinator.ParsePollResult(raw);

        Assert.NotNull(result);
        Assert.False(result!.Ok);
        Assert.Equal("disk full", result.Reason);
    }

    [Fact]
    public void Poll_result_treats_a_malformed_payload_as_failure()
    {
        var result = RestartCoordinator.ParsePollResult("\"not json\"");

        Assert.NotNull(result);
        Assert.False(result!.Ok);
        Assert.Equal("invalid-flush-result", result.Reason);
    }
}
