using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Activity;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIDE.IntegrationTests.Api;

/// <summary>
/// DEV-UPD — every mutation the auto-updater must not interrupt holds an
/// <see cref="ActivityGate"/> lease for its whole action. The blocking observer
/// pauses the action inside <c>Enter</c>, so the assertions are deterministic
/// and need no slow fixtures.
/// </summary>
public sealed class ActivityGateWiringTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "activity-gate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public Task Batch_apply_holds_the_gate() =>
        AssertActionHoldsGateAsync(
            "/api/batch-operations/missing/apply",
            "batch-apply",
            content: null);

    [Fact]
    public Task Batch_undo_holds_the_gate() =>
        AssertActionHoldsGateAsync(
            "/api/batch-operations/missing/undo",
            "batch-undo",
            content: null);

    [Fact]
    public Task Word_open_holds_the_gate() =>
        AssertActionHoldsGateAsync(
            "/api/word/open",
            "word-open",
            JsonContent.Create(new { documentId = "missing" }));

    [Fact]
    public Task Reconcile_holds_the_gate() =>
        AssertActionHoldsGateAsync(
            "/api/workspaces/missing/reconcile",
            "reconcile",
            content: null);

    [Fact]
    public Task Git_stage_holds_the_gate() =>
        AssertActionHoldsGateAsync(
            "/api/source-control/stage",
            "git",
            JsonContent.Create(new { path = (string?)null, changePath = (string?)null }));

    [Fact]
    public async Task Read_only_review_does_not_hold_the_gate()
    {
        var db = await NewDbAsync();
        var observer = new BlockingObserver();
        var gate = new ActivityGate(observer);
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton(gate));
        using var client = Authed(host);

        using var response = await client.PostAsJsonAsync(
            "/api/source-control/discard/review",
            new { path = (string?)null, changePath = (string?)null });

        Assert.False(observer.EnteredEvent.IsSet,
            "a read-only discard review must not be reported as an in-flight mutation");
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/app/status"))
            .GetProperty("busy").GetBoolean());
    }

    private async Task AssertActionHoldsGateAsync(string path, string reason, HttpContent? content)
    {
        var db = await NewDbAsync();
        var observer = new BlockingObserver();
        var gate = new ActivityGate(observer);
        await using var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services => services.AddSingleton(gate));
        using var client = Authed(host);

        Task<HttpResponseMessage> action;
        try
        {
            action = client.PostAsync(path, content);
            Assert.True(observer.EnteredEvent.Wait(TimeSpan.FromSeconds(10)),
                $"the mutation at {path} never entered the activity gate");

            var busy = await client.GetFromJsonAsync<JsonElement>("/api/app/status");
            Assert.True(busy.GetProperty("busy").GetBoolean());
            Assert.Contains(reason,
                busy.GetProperty("reasons").EnumerateArray().Select(entry => entry.GetString()));
        }
        finally
        {
            observer.Release.Set();
        }

        using var response = await action;
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/app/status"))
            .GetProperty("busy").GetBoolean());
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

    private sealed class BlockingObserver : IActivityObserver
    {
        public ManualResetEventSlim EnteredEvent { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public void Entered(string reason)
        {
            EnteredEvent.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
        }
    }
}
