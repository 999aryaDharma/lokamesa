using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.History;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.History;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIDE.IntegrationTests.History;

/// <summary>
/// HIST-04 — Operation History Listing API.
///
/// The Operations tab is only honest if it reads real persisted operations. These
/// tests prove the apply/undo write paths persist exactly one operation record,
/// that the read routes page and clamp, that storage usage comes from the real
/// history storage, and that every failure stays typed (empty is never conflated
/// with unavailable).
/// </summary>
public sealed class OperationHistoryListingTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private const long CapacityBytes = 1024L * 1024 * 1024;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hist04-" + Guid.NewGuid().ToString("N"));
    private readonly List<WebAppHost> _hosts = new();

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            try { host.Dispose(); } catch { /* best effort */ }
        }

        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AppliedOperation_IsListedWithAppliedOutcomeAndFileCount()
    {
        var (db, storage, path) = await SeedAsync("target TARGET target");
        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);

        var planId = await PreviewAsync(client);
        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        }
        Assert.Equal("new TARGET new", await File.ReadAllTextAsync(path));

        using var response = await client.GetAsync("/api/history/operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("Available", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        Assert.False(json.RootElement.GetProperty("hasMore").GetBoolean());

        var entry = Assert.Single(json.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal(planId, entry.GetProperty("planId").GetString());
        Assert.Equal(planId, entry.GetProperty("operationId").GetString());
        Assert.Equal("Search & Replace", entry.GetProperty("operationType").GetString());
        Assert.Equal("Applied", entry.GetProperty("status").GetString());
        Assert.Equal(1, entry.GetProperty("affectedFileCount").GetInt32());
        Assert.NotEqual(default, entry.GetProperty("startedUtc").GetDateTimeOffset());
        Assert.NotEqual(default, entry.GetProperty("completedUtc").GetDateTimeOffset());
        Assert.NotEqual(default, entry.GetProperty("recordedUtc").GetDateTimeOffset());

        var file = Assert.Single(entry.GetProperty("files").EnumerateArray());
        Assert.Equal("doc", file.GetProperty("documentId").GetString());
        Assert.Equal("Success", file.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task UndoneOperation_UpdatesTheSameRecordToUndone()
    {
        var (db, storage, path) = await SeedAsync("target content");
        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);

        var planId = await PreviewAsync(client);
        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        }

        string appliedCompletedUtc;
        using (var beforeUndo = await client.GetAsync("/api/history/operations"))
        using (var beforeJson = JsonDocument.Parse(await beforeUndo.Content.ReadAsStringAsync()))
        {
            appliedCompletedUtc = Assert.Single(beforeJson.RootElement.GetProperty("entries").EnumerateArray())
                .GetProperty("completedUtc").GetString()!;
        }

        using (var undo = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/undo", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        }
        Assert.Equal("target content", await File.ReadAllTextAsync(path));

        using var response = await client.GetAsync("/api/history/operations");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
        var entry = Assert.Single(json.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal("Undone", entry.GetProperty("status").GetString());
        Assert.Equal(1, entry.GetProperty("affectedFileCount").GetInt32());
        // The operation's own completed timestamp is not rewritten by the undo.
        Assert.Equal(appliedCompletedUtc, entry.GetProperty("completedUtc").GetString());

        var undoView = entry.GetProperty("undo");
        Assert.Equal("Completed", undoView.GetProperty("status").GetString());
        var undoFile = Assert.Single(undoView.GetProperty("files").EnumerateArray());
        Assert.Equal("doc", undoFile.GetProperty("documentId").GetString());
        Assert.Equal("Restored", undoFile.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task FailedApply_IsListedAsFailedWithItsPerFileOutcomes()
    {
        var (db, storage, path) = await SeedAsync("target content");
        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);

        var planId = await PreviewAsync(client);
        // The file changes after the reviewed preview: apply skips it as stale and
        // nothing is mutated, so the operation outcome is Failed, not Applied.
        await File.WriteAllTextAsync(path, "changed after preview");

        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        }
        Assert.Equal("changed after preview", await File.ReadAllTextAsync(path));

        using var response = await client.GetAsync("/api/history/operations");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var entry = Assert.Single(json.RootElement.GetProperty("entries").EnumerateArray());
        Assert.Equal("Failed", entry.GetProperty("status").GetString());
        var file = Assert.Single(entry.GetProperty("files").EnumerateArray());
        Assert.Equal("Skipped", file.GetProperty("outcome").GetString());
        Assert.Equal("Stale", file.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task Pagination_ClampsLimit_HonoursSkip_AndKeepsANewestFirstOrder()
    {
        var (db, storage, _) = await SeedAsync("target content");
        var repository = new OperationHistoryRepository(db);
        for (var index = 0; index < 3; index++)
        {
            await repository.RecordAppliedAsync(Record(
                operationId: $"op-{index}",
                startedUtc: new DateTimeOffset(2026, 10, 18, 12, index, 0, TimeSpan.Zero)));
        }

        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);

        using (var clamped = await client.GetAsync("/api/history/operations?limit=1000"))
        using (var json = JsonDocument.Parse(await clamped.Content.ReadAsStringAsync()))
        {
            Assert.Equal(3, json.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(OperationHistoryQuery.MaxLimit, json.RootElement.GetProperty("limit").GetInt32());
            Assert.Equal(3, json.RootElement.GetProperty("entries").GetArrayLength());
        }

        using (var skipped = await client.GetAsync("/api/history/operations?skip=1&limit=1"))
        using (var json = JsonDocument.Parse(await skipped.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("skip").GetInt32());
            Assert.Equal(1, json.RootElement.GetProperty("limit").GetInt32());
            var entry = Assert.Single(json.RootElement.GetProperty("entries").EnumerateArray());
            Assert.Equal("op-1", entry.GetProperty("operationId").GetString());
            Assert.True(json.RootElement.GetProperty("hasMore").GetBoolean());
            Assert.Equal(2, json.RootElement.GetProperty("nextSkip").GetInt32());
        }

        using (var invalid = await client.GetAsync("/api/history/operations?skip=-1"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task EmptyHistory_ReturnsAnEmptyAvailablePage_NotUnavailable()
    {
        var (db, storage, _) = await SeedAsync("target content");
        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);

        using (var response = await client.GetAsync("/api/history/operations"))
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Available", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, json.RootElement.GetProperty("entries").GetArrayLength());
            Assert.Equal(0, json.RootElement.GetProperty("totalCount").GetInt32());
            Assert.False(json.RootElement.GetProperty("hasMore").GetBoolean());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextSkip").ValueKind);
        }

        using var storageResponse = await client.GetAsync("/api/history/storage");
        using var storageJson = JsonDocument.Parse(await storageResponse.Content.ReadAsStringAsync());
        Assert.Equal("Available", storageJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, storageJson.RootElement.GetProperty("usedBytes").GetInt64());
        Assert.Equal(CapacityBytes, storageJson.RootElement.GetProperty("capacityBytes").GetInt64());
    }

    [Fact]
    public async Task StorageUsage_ReflectsTheRecordedSnapshotBytesAndRealCapacity()
    {
        var (db, storage, path) = await SeedAsync("target content");
        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);

        var planId = await PreviewAsync(client);
        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        }

        var expectedUsedBytes = await storage.GetTotalSizeAsync();
        Assert.True(expectedUsedBytes >= new FileInfo(path).Length, "the recovery snapshot must be counted");

        using var response = await client.GetAsync("/api/history/storage");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Available", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(expectedUsedBytes, json.RootElement.GetProperty("usedBytes").GetInt64());
        Assert.Equal(CapacityBytes, json.RootElement.GetProperty("capacityBytes").GetInt64());
    }

    [Fact]
    public async Task RouteFailure_StaysTypedUnavailable_InsteadOfAnEmptyPage()
    {
        var (db, storage, _) = await SeedAsync("target content");
        await using var host = await StartAsync(
            db,
            storage,
            services => services.AddScoped<IOperationHistoryRepository>(_ => new ThrowingOperationHistoryRepository()));
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/history/operations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Unavailable", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("HistoryUnavailable", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task StorageRouteFailure_StaysTypedUnavailable()
    {
        var (db, storage, _) = await SeedAsync("target content");
        await using var host = await StartAsync(
            db,
            storage,
            services => services.AddSingleton<IHistoryStorage>(new ThrowingHistoryStorage(storage)));
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/history/storage");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Unavailable", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("HistoryStorageUnavailable", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ReadRoutes_RequireThePrivilegedSessionToken_AndRejectHostileOrigins()
    {
        var (db, storage, _) = await SeedAsync("target content");
        await using var host = await StartAsync(db, storage);
        using var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/history/operations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/history/storage")).StatusCode);

        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-startup-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/history/operations")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-DocumentIDE-Session-Token");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);

        foreach (var origin in new[] { "https://attacker.example", "null" })
        {
            using var hostileOrigin = new HttpRequestMessage(HttpMethod.Get, "/api/history/operations");
            hostileOrigin.Headers.Add("Origin", origin);
            hostileOrigin.Headers.Add("Sec-Fetch-Site", "cross-site");
            using var rejected = await client.SendAsync(hostileOrigin);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        using (var hostileHost = new HttpRequestMessage(HttpMethod.Get, "/api/history/operations"))
        {
            hostileHost.Headers.Host = "attacker.example";
            hostileHost.Headers.Add("Origin", AllowedOrigin);
            hostileHost.Headers.Add("Sec-Fetch-Site", "cross-site");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(hostileHost)).StatusCode);
        }

        using var accepted = new HttpRequestMessage(HttpMethod.Get, "/api/history/operations");
        accepted.Headers.Add("Origin", AllowedOrigin);
        accepted.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var allowed = await client.SendAsync(accepted);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task OneApply_RecordsExactlyOneOperation_AndARetryCannotDuplicateIt()
    {
        var (db, storage, _) = await SeedAsync("target content");
        await using var host = await StartAsync(db, storage);
        using var client = AuthorizedClient(host);
        var repository = new OperationHistoryRepository(db);

        var planId = await PreviewAsync(client);
        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        }

        var page = await repository.GetPageAsync(0, OperationHistoryQuery.MaxLimit);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(planId, Assert.Single(page.Entries).OperationId);

        // The reviewed plan is completed, so a retry is rejected before it can
        // record a second operation (spec 07 §25 idempotency).
        using (var retry = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        }

        page = await repository.GetPageAsync(0, OperationHistoryQuery.MaxLimit);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task OperationRecordingFailure_IsSurfaced_AndNoRowIsFabricated()
    {
        var (db, storage, path) = await SeedAsync("target content");
        await using var host = await StartAsync(
            db,
            storage,
            services => services.AddScoped<IOperationHistoryRecorder>(_ => new ThrowingOperationHistoryRecorder()));
        using var client = AuthorizedClient(host);

        var planId = await PreviewAsync(client);
        using var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { });

        // The mutation completed, but a failed history write is surfaced through the
        // existing typed apply failure path — never swallowed. The operation is not
        // silently listed as if the database write had succeeded.
        Assert.Equal(HttpStatusCode.Conflict, apply.StatusCode);
        using (var json = JsonDocument.Parse(await apply.Content.ReadAsStringAsync()))
        {
            Assert.Equal("PlanNotApplyable", json.RootElement.GetProperty("errorCode").GetString());
            Assert.Contains(
                "Operation history record failed",
                json.RootElement.GetProperty("detail").GetString(),
                StringComparison.Ordinal);
        }
        Assert.Equal("new content", await File.ReadAllTextAsync(path));

        var page = await new OperationHistoryRepository(db).GetPageAsync(0, OperationHistoryQuery.MaxLimit);
        Assert.Equal(0, page.TotalCount);
    }

    private static OperationHistoryRecord Record(string operationId, DateTimeOffset startedUtc) =>
        new(
            OperationId: operationId,
            Kind: "Search & Replace",
            PlanId: operationId,
            StartedUtc: startedUtc,
            CompletedUtc: startedUtc.AddSeconds(1),
            Outcome: OperationOutcome.Applied,
            AffectedFileCount: 1,
            Files: new[] { new OperationHistoryFileOutcome("doc", BatchApplyFileOutcome.Success, null, null) },
            Undo: null,
            SnapshotId: null);

    private async Task<(AppDb Db, HistoryStorage Storage, string Path)> SeedAsync(string documentBody)
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "HIST-04" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource
        {
            Id = "folder",
            WorkspaceId = "ws",
            CanonicalPath = _root,
            DisplayName = "Source",
        });
        var path = Path.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(path, documentBody);
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = "doc",
            CurrentPath = path,
            Filename = "notes.md",
            Extension = ".md",
            DocumentType = "markdown",
        });
        var storage = new HistoryStorage(
            capacityBytes: CapacityBytes,
            clock: null,
            root: Path.Combine(_root, "history-storage"));
        return (db, storage, path);
    }

    private async Task<WebAppHost> StartAsync(AppDb db, HistoryStorage storage, Action<IServiceCollection>? configure = null)
    {
        var host = await WebAppHost.StartAsync(
            database: db,
            configureServices: services =>
            {
                // Isolate the real history stores from the developer's LocalAppData.
                services.AddSingleton<IHistoryStorage>(storage);
                services.AddSingleton(new JsonlHistoryStore(Path.Combine(_root, "jsonl-history")));
                configure?.Invoke(services);
            });
        _hosts.Add(host);
        return host;
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    private static async Task<string> PreviewAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/batch-operations/preview", new
        {
            scope = new
            {
                workspaceId = "ws",
                type = 0,
                documentIds = new[] { "doc" },
                includeSubfolders = true,
                excludedBranches = Array.Empty<string>(),
            },
            find = "target",
            replacement = "new",
        });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        using var creationJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var planId = creationJson.RootElement.GetProperty("planId").GetString()!;

        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var response = await client.GetAsync($"/api/batch-operations/{planId}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var status = json.RootElement.GetProperty("status").GetString();
            if (status is not ("Pending" or "Running")) return planId;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Preview did not complete in time.");
    }

    private sealed class ThrowingOperationHistoryRepository : IOperationHistoryRepository
    {
        public Task<OperationHistoryPage> GetPageAsync(int skip, int limit, CancellationToken cancellationToken = default) =>
            throw new IOException("operation history database unavailable");
    }

    private sealed class ThrowingOperationHistoryRecorder : IOperationHistoryRecorder
    {
        public Task RecordAppliedAsync(OperationHistoryRecord record, CancellationToken cancellationToken = default) =>
            throw new IOException("operation history database unavailable");

        public Task RecordUndoneAsync(OperationUndoRecord record, CancellationToken cancellationToken = default) =>
            throw new IOException("operation history database unavailable");
    }

    private sealed class ThrowingHistoryStorage : IHistoryStorage
    {
        private readonly IHistoryStorage _inner;

        public ThrowingHistoryStorage(IHistoryStorage inner) => _inner = inner;

        public string GetSnapshotRootPath(string snapshotId) => _inner.GetSnapshotRootPath(snapshotId);
        public Task SaveMetadataAsync(SnapshotMetadata metadata) => _inner.SaveMetadataAsync(metadata);
        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() => _inner.GetAllMetadataAsync();
        public Task DeleteSnapshotAsync(string snapshotId) => _inner.DeleteSnapshotAsync(snapshotId);
        public Task<long> GetTotalSizeAsync() => throw new IOException("history storage unavailable");
        public Task<long> GetCapacityBytesAsync() => _inner.GetCapacityBytesAsync();
    }
}
