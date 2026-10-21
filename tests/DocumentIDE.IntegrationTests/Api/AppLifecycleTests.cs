using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Api.Lifecycle;
using DocumentIDE.Core.Activity;
using DocumentIDE.Core.Diagnostics;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIDE.IntegrationTests.Api;

/// <summary>
/// DEV-UPD — the loopback control surface the local auto-updater uses to read
/// build identity, wait for idle, flush unsaved editor buffers, and quit. The
/// control token is accepted only here; document routes keep the session rule.
/// </summary>
public sealed class AppLifecycleTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "app-lifecycle-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // Best effort cleanup of the isolated test database.
        }
    }

    [Fact]
    public async Task Status_reports_build_identity_and_is_idle()
    {
        var db = await NewDbAsync();
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton(new BuildInfo("1.0.2.0", "abc123")));
        using var client = Authed(host);

        var json = await client.GetFromJsonAsync<JsonElement>("/api/app/status");

        Assert.Equal("1.0.2.0", json.GetProperty("version").GetString());
        Assert.Equal("abc123", json.GetProperty("sha").GetString());
        Assert.False(json.GetProperty("busy").GetBoolean());
        Assert.Equal(0, json.GetProperty("reasons").GetArrayLength());
    }

    [Fact]
    public async Task Status_is_busy_while_the_gate_is_held()
    {
        var db = await NewDbAsync();
        var gate = new ActivityGate();
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton(gate));
        using var client = Authed(host);

        using (gate.Enter("batch-apply"))
        {
            var json = await client.GetFromJsonAsync<JsonElement>("/api/app/status");

            Assert.True(json.GetProperty("busy").GetBoolean());
            Assert.Contains("batch-apply",
                json.GetProperty("reasons").EnumerateArray().Select(entry => entry.GetString()));
        }

        var idle = await client.GetFromJsonAsync<JsonElement>("/api/app/status");
        Assert.False(idle.GetProperty("busy").GetBoolean());
    }

    [Fact]
    public async Task Prepare_restart_returns_ok_when_the_flush_succeeds()
    {
        var db = await NewDbAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = Authed(host);

        var response = await client.PostAsync("/api/app/prepare-restart", content: null);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Prepare_restart_propagates_a_flush_failure()
    {
        var db = await NewDbAsync();
        var bridge = new FakeBridge(new RestartFlushResult(false, "read-only file"));
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton<IRestartFlushBridge>(bridge));
        using var client = Authed(host);

        var response = await client.PostAsync("/api/app/prepare-restart", content: null);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal("read-only file", json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Prepare_restart_refuses_while_a_mutation_is_in_flight()
    {
        var db = await NewDbAsync();
        var gate = new ActivityGate();
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton(gate));
        using var client = Authed(host);

        using (gate.Enter("git"))
        {
            var response = await client.PostAsync("/api/app/prepare-restart", content: null);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.False(json.GetProperty("ok").GetBoolean());
            Assert.Equal("busy", json.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task Quit_refuses_while_a_mutation_is_in_flight()
    {
        var db = await NewDbAsync();
        var gate = new ActivityGate();
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton(gate));
        using var client = Authed(host);

        using (gate.Enter("batch-apply"))
        {
            var response = await client.PostAsync("/api/app/quit", content: null);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.False(json.GetProperty("ok").GetBoolean());
            Assert.Equal("busy", json.GetProperty("reason").GetString());
        }

        // The refused quit must not stop the host after the gate is released.
        await Task.Delay(300);
        var status = await client.GetAsync("/api/app/status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    [Fact]
    public async Task Quit_requires_authorization()
    {
        var db = await NewDbAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var anonymous = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };

        var response = await anonymous.PostAsync("/api/app/quit", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Control_token_works_on_the_control_surface_only()
    {
        var db = await NewDbAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };

        var status = new HttpRequestMessage(HttpMethod.Get, "/api/app/status");
        status.Headers.Add("X-DocumentIDE-Control-Token", host.ControlToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(status)).StatusCode);

        var documents = new HttpRequestMessage(HttpMethod.Get, "/api/documents/x/markdown");
        documents.Headers.Add("X-DocumentIDE-Control-Token", host.ControlToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(documents)).StatusCode);

        // The control token never authorizes a mutation, even on the same
        // document surface the SPA uses.
        var mutation = new HttpRequestMessage(HttpMethod.Put, "/api/documents/x/markdown")
        {
            Content = JsonContent.Create(new { source = "# changed" }),
        };
        mutation.Headers.Add("X-DocumentIDE-Control-Token", host.ControlToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(mutation)).StatusCode);
    }

    private async Task<AppDb> NewDbAsync()
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        return db;
    }

    private static HttpClient Authed(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        return client;
    }

    private sealed class FakeBridge(RestartFlushResult result) : IRestartFlushBridge
    {
        public Task<RestartFlushResult> PrepareRestartAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
