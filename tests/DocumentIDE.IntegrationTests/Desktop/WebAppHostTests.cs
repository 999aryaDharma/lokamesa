using System;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

[Collection("HostFixtures")]
public class WebAppHostTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "desktop-host-" + Guid.NewGuid().ToString("N"));
    private AppDb CreateDatabase() => new(System.IO.Path.Combine(_root, "state.db"));

    [Fact]
    public async Task Production_Build_Opens_Without_Vite_Dev_Server()
    {
        await using var appHost = await WebAppHost.StartAsync(database: CreateDatabase());

        Assert.True(appHost.IsRunning);
        Assert.True(appHost.Port > 0);
        Assert.False(string.IsNullOrEmpty(appHost.SessionToken));

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", appHost.SessionToken);

        var response = await client.GetAsync($"{appHost.ApiUrl}/health");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void Foreign_Navigation_Loses_Privileged_Bridge_Capability()
    {
        var bridge = new LocalSessionBridge("test_token_123");

        var localTarget = new Uri("http://127.0.0.1:5000/api/documents");
        var foreignTarget = new Uri("https://external-website.com/api/steal");

        Assert.True(bridge.ShouldInjectHeader(localTarget, 5000));
        Assert.False(bridge.ShouldInjectHeader(foreignTarget, 5000));
    }

    [Fact]
    public async Task Closing_Desktop_Stops_Privileged_Local_Host_Cleanly()
    {
        var appHost = await WebAppHost.StartAsync(database: CreateDatabase());
        var apiUrl = appHost.ApiUrl;
        var token = appHost.SessionToken;

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", token);
            var resBefore = await client.GetAsync($"{apiUrl}/health");
            Assert.True(resBefore.IsSuccessStatusCode);
        }

        await appHost.DisposeAsync();

        Assert.False(appHost.IsRunning);

        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(2);
            await Assert.ThrowsAnyAsync<Exception>(async () => await client.GetAsync($"{apiUrl}/health"));
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, true);
    }
}
