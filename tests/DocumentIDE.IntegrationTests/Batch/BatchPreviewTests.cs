using System.Security.Cryptography;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using System.Text;
using System.Text.Json;
using DocumentIDE.IntegrationTests.Docx;
using System.Net;
using System.Net.Http.Json;
using DocumentIDE.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIDE.IntegrationTests.Batch;

public sealed class BatchPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "batch-preview-" + Guid.NewGuid().ToString("N"));
    private readonly AppDb _db;
    private readonly BatchPlanRepository _plans;

    public BatchPreviewTests()
    {
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _plans = new BatchPlanRepository(_db);
        new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "preview" }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Preview_LargePersistedSelectionMapsOrdinalIdsAndFileSelection()
    {
        var (plan, _, _) = await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target")));
        var occurrences = Enumerable.Range(0, 10000).Select(i => new BatchPreviewOccurrence($"occ:{i}", "Body", "target")).ToArray();
        plan.ScopeJson = JsonSerializer.Serialize(new BatchPreviewState(new BatchPreviewScope { WorkspaceId = "ws", Type = BatchScopeType.Selection }, null));
        plan.Status = BatchPlanStatus.Previewed;
        plan.Files.Add(new BatchOperationPlanFile {
            DocumentId = "doc", Selected = false, MatchCount = occurrences.Length,
            SelectedChangeIds = occurrences.Where((_, i) => i % 2 == 0).Select(o => o.Id).Append("OCC:1").ToList(),
            PlanPayloadJson = JsonSerializer.Serialize(new BatchPreviewFile("doc", "notes.md", true, "Unknown", null, occurrences)) });
        await _plans.SavePlanAsync(plan);
        using var scheduler = new DocumentIDE.Infrastructure.Jobs.LocalJobScheduler();
        var result = (await new BatchPreviewCoordinator(_db, scheduler, new BatchPreviewService(_plans)).GetAsync(plan.Id))!;
        var file = Assert.Single(result.Files);
        Assert.False(file.Selected);
        Assert.Equal(10000, file.Occurrences.Count);
        for (var i = 0; i < file.Occurrences.Count; i++) Assert.Equal(i % 2 == 0, file.Occurrences[i].Selected);
    }

    [Fact]
    public async Task Preview_ValidDocxExceedingOccurrenceBudgetHasTypedFailureWithoutMutation()
    {
        var bytes = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>" + string.Concat(Enumerable.Repeat("target ", 10001)) + "</w:t></w:r></w:p></w:body></w:document>");
        var (plan, request, scope) = await Fixture(("doc", "large.docx", bytes));
        await new BatchPreviewService(_plans).BuildAsync(plan.Id, request, scope, CancellationToken.None);
        var file = Assert.Single((await _plans.GetByIdAsync(plan.Id))!.Files);
        Assert.Equal("ResourceLimit", JsonSerializer.Deserialize<BatchPreviewFile>(file.PlanPayloadJson!)!.FailureCode);
        Assert.Equal(0, file.MatchCount);
        Assert.Empty(file.SelectedChangeIds);
        Assert.False(file.Selected);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "large.docx")));
    }

    [Fact]
    public async Task Preview_PerformsNoSourceMutation_AndPersistsFingerprintAndExactOccurrences()
    {
        var path = Path.Combine(_root, "notes.md");
        await File.WriteAllTextAsync(path, "target TARGET target");
        var before = await File.ReadAllBytesAsync(path);
        var document = new Document { Id = "doc", CurrentPath = path, Filename = "notes.md", Extension = ".md" };
        await new DocumentRepository(_db).CreateAsync(document);
        var request = new BatchPreviewRequest(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = new[] { "doc" } }, "target", "new");
        var scope = new BatchScopeResolver().Resolve(request.Scope, new[] { document }, new[] { new FolderSource { WorkspaceId = "ws", CanonicalPath = _root } });
        var plan = new BatchOperationPlan { WorkspaceId = "ws", ScopeJson = "{}", ParametersJson = "{}" };
        await _plans.SavePlanAsync(plan);

        await new BatchPreviewService(_plans).BuildAsync(plan.Id, request, scope, CancellationToken.None);

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var stored = await _plans.GetByIdAsync(plan.Id);
        Assert.Equal(BatchPlanStatus.Previewed, stored!.Status);
        var file = Assert.Single(stored.Files);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(before)), file.ExpectedFingerprint);
        Assert.Equal(2, file.MatchCount);
        Assert.Equal(2, file.SelectedChangeIds.Count);
        Assert.NotEmpty(file.PlanPayloadJson!);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    private async Task<(BatchOperationPlan Plan, BatchPreviewRequest Request, BatchScopeResult Scope)> Fixture(params (string Id, string Name, byte[] Bytes)[] files)
    {
        var documents = new List<Document>();
        foreach (var file in files)
        {
            var path = Path.Combine(_root, file.Name);
            await File.WriteAllBytesAsync(path, file.Bytes);
            var document = new Document { Id = file.Id, CurrentPath = path, Filename = file.Name, Extension = Path.GetExtension(file.Name) };
            await new DocumentRepository(_db).CreateAsync(document);
            documents.Add(document);
        }
        var request = new BatchPreviewRequest(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = documents.Select(d => d.Id).ToArray() }, "target", "new");
        var scope = new BatchScopeResolver().Resolve(request.Scope, documents, new[] { new FolderSource { WorkspaceId = "ws", CanonicalPath = _root } });
        var plan = new BatchOperationPlan { WorkspaceId = "ws", ScopeJson = "{}", ParametersJson = "{}" };
        await _plans.SavePlanAsync(plan);
        return (plan, request, scope);
    }

    [Fact]
    public async Task Preview_DocxCorpus_CrossRunsAndAllOrigins_AndInvalidPackageRemainInspectable()
    {
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
            <w:p><w:r><w:t>tar</w:t></w:r><w:r><w:t>get</w:t></w:r></w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:t>target</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            </w:body></w:document>
            """, extraEntries: new[] {
                ("word/header1.xml", Encoding.UTF8.GetBytes("<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>target</w:t></w:r></w:p></w:hdr>")),
                ("word/footer1.xml", Encoding.UTF8.GetBytes("<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>target</w:t></w:r></w:p></w:ftr>")) });
        var (plan, request, scope) = await Fixture(("good", "good.docx", package), ("bad", "bad.docx", Encoding.UTF8.GetBytes("not a package")), ("none", "none.md", Encoding.UTF8.GetBytes("TARGET")));
        await new BatchPreviewService(_plans).BuildAsync(plan.Id, request, scope, CancellationToken.None);
        var stored = (await _plans.GetByIdAsync(plan.Id))!;
        Assert.Equal(3, stored.Files.Count);
        var good = stored.Files.Single(f => f.DocumentId == "good");
        var payload = JsonSerializer.Deserialize<BatchPreviewFile>(good.PlanPayloadJson!)!;
        Assert.Equal(new[] { "Body", "Table", "Header", "Footer" }, payload.Occurrences.Select(o => o.Origin));
        Assert.Equal(4, good.MatchCount);
        Assert.NotNull(JsonSerializer.Deserialize<BatchPreviewFile>(stored.Files.Single(f => f.DocumentId == "bad").PlanPayloadJson!)!.FailureCode);
        Assert.Equal(0, stored.Files.Single(f => f.DocumentId == "none").MatchCount);
        Assert.Equal(package, await File.ReadAllBytesAsync(Path.Combine(_root, "good.docx")));
    }

    [Fact]
    public async Task Preview_CancelledTokenLeavesSourceUntouchedAndPlanUnreviewed()
    {
        var bytes = Encoding.UTF8.GetBytes("target");
        var (plan, request, scope) = await Fixture(("doc", "notes.md", bytes));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BatchPreviewService(_plans).BuildAsync(plan.Id, request, scope, cts.Token));
        Assert.Equal(BatchPlanStatus.Draft, (await _plans.GetByIdAsync(plan.Id))!.Status);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(_root, "notes.md")));
    }

    [Fact]
    public async Task Preview_SelectionPersists_UnknownOccurrencesAndStaleExpiredPlansAreRejected()
    {
        var (plan, request, scope) = await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target target")));
        var service = new BatchPreviewService(_plans);
        await service.BuildAsync(plan.Id, request, scope, CancellationToken.None);
        var stored = (await _plans.GetByIdAsync(plan.Id))!;
        var id = stored.Files[0].SelectedChangeIds[0];
        await service.SaveSelectionAsync(plan.Id, new(new[] { new BatchPreviewFileSelection("doc", true, new[] { id }) }), scope);
        Assert.Equal(new[] { id }, (await _plans.GetByIdAsync(plan.Id))!.Files[0].SelectedChangeIds);
        await Assert.ThrowsAsync<BatchPreviewConflictException>(() => service.SaveSelectionAsync(plan.Id, new(new[] { new BatchPreviewFileSelection("doc", true, new[] { "unknown" }) }), scope));
        await Assert.ThrowsAsync<BatchPreviewConflictException>(() => service.SaveSelectionAsync(plan.Id, new(new[] { new BatchPreviewFileSelection("other", true, Array.Empty<string>()) }), scope));
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "changed");
        var stale = await Assert.ThrowsAsync<BatchPreviewConflictException>(() => service.SaveSelectionAsync(plan.Id, new(new[] { new BatchPreviewFileSelection("doc", false, Array.Empty<string>()) }), scope));
        Assert.Equal("StalePlan", stale.Code);
        stored.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await _plans.SavePlanAsync(stored);
        var expired = await Assert.ThrowsAsync<BatchPreviewConflictException>(() => service.SaveSelectionAsync(plan.Id, new(Array.Empty<BatchPreviewFileSelection>()), scope));
        Assert.Equal("ExpiredPlan", expired.Code);
    }

    [Fact]
    public async Task Preview_ApiGuard_QueuedProgressAndSelectionContract()
    {
        var (_, request, _) = await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target target")));
        await new FolderSourceRepository(_db).CreateAsync(new FolderSource { Id = "source", WorkspaceId = "ws", CanonicalPath = _root });
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<AppDb>(); services.AddSingleton(_db); }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/batch-operations/preview", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", factory.Services.GetRequiredService<ISessionTokenProvider>().SessionToken);
        var workspaces = await client.GetFromJsonAsync<JsonElement>("/api/batch-operations/workspaces");
        Assert.Equal("ws", workspaces[0].GetProperty("id").GetString());
        var response = await client.PostAsJsonAsync("/api/batch-operations/preview", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var started = await response.Content.ReadFromJsonAsync<JsonElement>();
        var planId = started.GetProperty("planId").GetString();
        Assert.False(string.IsNullOrEmpty(started.GetProperty("jobId").GetString()));
        JsonElement preview = default;
        for (var i = 0; i < 100; i++)
        {
            preview = await client.GetFromJsonAsync<JsonElement>($"/api/batch-operations/{planId}");
            Assert.InRange(preview.GetProperty("progress").GetDouble(), 0, 100);
            if (preview.GetProperty("status").GetString() == "Completed") break;
            await Task.Delay(20);
        }
        Assert.Equal("Completed", preview.GetProperty("status").GetString());
        Assert.Equal(2, preview.GetProperty("occurrenceCount").GetInt32());
        Assert.Equal("Body", preview.GetProperty("files")[0].GetProperty("occurrences")[0].GetProperty("origin").GetString());
        var selection = new BatchPreviewSelection(new[] { new BatchPreviewFileSelection("doc", false, Array.Empty<string>()) });
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/batch-operations/{planId}/selection", selection)).StatusCode);
        Assert.False((await _plans.GetByIdAsync(planId!))!.Files[0].Selected);
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.md"), "changed after preview");
        var stale = await client.PutAsJsonAsync($"/api/batch-operations/{planId}/selection", selection);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("StalePlan", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/batch-operations/{planId}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Preview_CollectionSchemaHasForeignKeysAndTrustedWorkspaceMembership()
    {
        await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target")));
        using var connection = _db.CreateConnection(); await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO collections(id,workspace_id,name,created_at) VALUES ('col','ws','review','now'); INSERT INTO collection_members(collection_id,document_id,sort_order) VALUES ('col','doc',0);";
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = "INSERT INTO collection_members(collection_id,document_id,sort_order) VALUES ('col','unknown',1);";
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => cmd.ExecuteNonQueryAsync());
        var trusted = await new BatchCollectionReader(_db).GetByWorkspaceAsync("ws");
        Assert.Equal(new[] { "doc" }, Assert.Single(trusted).DocumentIds);
        Assert.Empty(await new BatchCollectionReader(_db).GetByWorkspaceAsync("other"));
    }

    [Fact]
    public async Task Preview_UnrelatedPlanCannotBeReadOrReviewedThroughPreviewApi()
    {
        var (plan, _, _) = await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target")));
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<AppDb>(); services.AddSingleton(_db); }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", factory.Services.GetRequiredService<ISessionTokenProvider>().SessionToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/batch-operations/{plan.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/batch-operations/{plan.Id}/selection", new BatchPreviewSelection(Array.Empty<BatchPreviewFileSelection>()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/batch-operations/{plan.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Preview_QueuedCancellationAndPendingReviewCannotAlterSourceOrBroadenMembership()
    {
        var (_, request, _) = await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target")));
        await new FolderSourceRepository(_db).CreateAsync(new FolderSource { Id = "source", WorkspaceId = "ws", CanonicalPath = _root });
        using var scheduler = new DocumentIDE.Infrastructure.Jobs.LocalJobScheduler(maxDegreeOfParallelism: 1);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await scheduler.EnqueueJobAsync("blocker", "blocker", null, new DocumentIDE.Core.Jobs.DelegateJob(async context => { entered.SetResult(); await release.Task; }));
        await entered.Task;
        var coordinator = new BatchPreviewCoordinator(_db, scheduler, new BatchPreviewService(_plans));
        var started = await coordinator.StartAsync(request);
        var pending = (await coordinator.GetAsync(started.PlanId))!;
        Assert.Equal("Pending", pending.Status); Assert.Equal(0, pending.Progress);
        var conflict = await Assert.ThrowsAsync<BatchPreviewConflictException>(() => coordinator.SaveSelectionAsync(started.PlanId, new(Array.Empty<BatchPreviewFileSelection>())));
        Assert.Equal("PreviewIncomplete", conflict.Code);
        Assert.True(await coordinator.CancelAsync(started.PlanId));
        Assert.Equal("Cancelled", (await coordinator.GetAsync(started.PlanId))!.Status);
        release.SetResult();
        Assert.Empty((await _plans.GetByIdAsync(started.PlanId))!.Files);
        Assert.Equal("target", await File.ReadAllTextAsync(Path.Combine(_root, "notes.md")));
    }

    [Fact]
    public async Task Preview_FolderTreeIncludesEmptyFoldersAndExcludesBranches_WithExpiredReviewConflict()
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty", "nested")); Directory.CreateDirectory(Path.Combine(_root, "excluded"));
        await new FolderSourceRepository(_db).CreateAsync(new FolderSource { Id = "source", WorkspaceId = "ws", CanonicalPath = _root });
        using var scheduler = new DocumentIDE.Infrastructure.Jobs.LocalJobScheduler();
        var coordinator = new BatchPreviewCoordinator(_db, scheduler, new BatchPreviewService(_plans));
        var started = await coordinator.StartAsync(new(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root, ExcludedBranches = new[] { "excluded" } }, "target", "new"));
        var preview = (await coordinator.GetAsync(started.PlanId))!;
        Assert.Equal(2, preview.Scope.SubfolderCount);
        Assert.DoesNotContain(Path.Combine(_root, "excluded"), preview.Scope.IncludedSubfolders);
        for (var i = 0; i < 100 && preview.Status != "Completed"; i++) { await Task.Delay(10); preview = (await coordinator.GetAsync(started.PlanId))!; }
        Assert.Equal("Completed", preview.Status);
        var plan = (await _plans.GetByIdAsync(started.PlanId))!; plan.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await _plans.SavePlanAsync(plan);
        Assert.Equal("Expired", (await coordinator.GetAsync(started.PlanId))!.Status);
        var conflict = await Assert.ThrowsAsync<BatchPreviewConflictException>(() => coordinator.SaveSelectionAsync(started.PlanId, new(Array.Empty<BatchPreviewFileSelection>())));
        Assert.Equal("ExpiredPlan", conflict.Code);
    }

    [Fact]
    public async Task Preview_ScopeOptionsExposeTrustedNamesAndWorkspaceCollectionChoicesOnly()
    {
        await Fixture(("doc", "notes.md", Encoding.UTF8.GetBytes("target")));
        await new FolderSourceRepository(_db).CreateAsync(new FolderSource { Id = "source", WorkspaceId = "ws", CanonicalPath = _root });
        using var connection = _db.CreateConnection(); await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO collections(id,workspace_id,name,created_at) VALUES ('col','ws','Review notes','now');"; await cmd.ExecuteNonQueryAsync();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<AppDb>(); services.AddSingleton(_db); }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", factory.Services.GetRequiredService<ISessionTokenProvider>().SessionToken);
        var response = await client.GetAsync("/api/batch-operations/workspaces/ws/scope-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("doc", body.GetProperty("documents")[0].GetProperty("id").GetString());
        Assert.Equal("notes.md", body.GetProperty("documents")[0].GetProperty("fileName").GetString());
        Assert.Equal("Review notes", body.GetProperty("collections")[0].GetProperty("name").GetString());
        Assert.DoesNotContain(_root, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/batch-operations/workspaces/unknown/scope-options")).StatusCode);
    }

    [Fact]
    public async Task Preview_InputAndPackageBudgets_InvalidEncodingAndDtdHaveTypedFailuresWithoutMutation()
    {
        var dtd = DocxTestFixtureBuilder.CreateDtdDocxPackage();
        var (plan, request, scope) = await Fixture(("oversize", "large.md", Array.Empty<byte>()), ("encoding", "invalid.md", new byte[] { 255 }), ("dtd", "unsafe.docx", dtd));
        var path = Path.Combine(_root, "large.md");
        using (var stream = File.OpenWrite(path)) stream.SetLength(10 * 1024 * 1024 + 1);
        await new BatchPreviewService(_plans).BuildAsync(plan.Id, request, scope, CancellationToken.None);
        var stored = (await _plans.GetByIdAsync(plan.Id))!;
        var files = stored.Files.ToDictionary(f => f.DocumentId, f => JsonSerializer.Deserialize<BatchPreviewFile>(f.PlanPayloadJson!)!);
        Assert.Equal("ResourceLimit", files["oversize"].FailureCode);
        Assert.Equal("InvalidEncoding", files["encoding"].FailureCode);
        Assert.Equal("DtdProhibited", files["dtd"].FailureCode);
        Assert.All(stored.Files, file => { Assert.False(file.Selected); Assert.Empty(file.SelectedChangeIds); });
        Assert.Equal(10 * 1024 * 1024 + 1, new FileInfo(path).Length);
        Assert.Equal(dtd, await File.ReadAllBytesAsync(Path.Combine(_root, "unsafe.docx")));
    }
}
