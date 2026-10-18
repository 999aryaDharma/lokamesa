using System.Net;
using System.Net.Http;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// UI-03 reachability — the shipped desktop host must serve the Source Control
/// controller the app shell mounts, while still refusing a controller that is
/// not on the host allow-list.
///
/// A Source Control request that reaches the controller answers with a typed
/// HTTP 200 state (Git unavailable / no repository / real status); a 404 here
/// means the desktop WebView cannot reach screen 05/06/07 at all.
/// </summary>
[Collection("HostFixtures")]
public sealed class SourceControlHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "source-control-host-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SourceControlRoute_IsReachable_WhileNonAllowedControllerStays404()
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var status = await client.GetAsync(
            $"/api/source-control/status?path={Uri.EscapeDataString(_root)}");
        Assert.NotEqual(HttpStatusCode.NotFound, status.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, status.StatusCode);
        Assert.True(
            status.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"Unexpected Source Control status: {(int)status.StatusCode} {status.StatusCode}");

        // DiagnosticsController exists in the API assembly but is deliberately
        // not on the desktop host's allow-list. If this route is served, the
        // allow-list is not actually being enforced.
        using var diagnostics = await client.GetAsync("/api/diagnostics/latest");
        Assert.Equal(HttpStatusCode.NotFound, diagnostics.StatusCode);
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
