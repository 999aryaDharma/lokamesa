using System.Security.Cryptography;
using System.Text;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIDE.IntegrationTests.Filesystem;

/// <summary>
/// QA-07 — Step 5 must actually run at product startup. These tests boot both real
/// hosts (desktop <see cref="WebAppHost"/> and the API's <c>Program</c>) against a
/// database seeded with the exact state a crash leaves behind, and assert the repair
/// happened on start.
/// </summary>
[Collection("HostFixtures")]
public sealed class StartupRecoveryHostTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDir;
    private readonly AppDb _db;

    public StartupRecoveryHostTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qa07-host-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "source");
        Directory.CreateDirectory(_sourceDir);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public async Task DesktopHost_StartAsync_RepairsCrashState_AndLeavesSourceUntouched()
    {
        var seed = await SeedCrashedOperationAsync();

        await using var host = await WebAppHost.StartAsync(preferredPort: 0, database: _db);

        Assert.True(host.IsRunning);
        await AssertCrashStateRepairedAsync(seed);
    }

    [Fact]
    public async Task ApiHost_StartupScope_RepairsCrashState_AndLeavesSourceUntouched()
    {
        var seed = await SeedCrashedOperationAsync();

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AppDb>();
                services.AddSingleton(_db);
            }));
        using var client = factory.CreateClient();

        await AssertCrashStateRepairedAsync(seed);
    }

    [Fact]
    public async Task DesktopHost_EmptyDatabase_StartsAsSafeNoOp()
    {
        await using var host = await WebAppHost.StartAsync(preferredPort: 0, database: _db);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public async Task ApiHost_EmptyDatabase_StartsAsSafeNoOp()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AppDb>();
                services.AddSingleton(_db);
            }));
        using var client = factory.CreateClient();

        // Startup recovery found nothing to repair and did not fail the host.
        Assert.Empty(await new WorkspaceRepository(_db).GetAllAsync());
    }

    private async Task<CrashedOperation> SeedCrashedOperationAsync()
    {
        await new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws-host", Name = "Host recovery" });
        await new FolderSourceRepository(_db).CreateAsync(new FolderSource
        {
            Id = "src-host",
            WorkspaceId = "ws-host",
            CanonicalPath = _sourceDir,
            DisplayName = "source"
        });

        var path = Path.Combine(_sourceDir, "notes.md");
        var bytes = Encoding.UTF8.GetBytes("source must stay valid");
        await File.WriteAllBytesAsync(path, bytes);
        await new DocumentRepository(_db).CreateAsync(new Document
        {
            Id = "doc-host",
            CurrentPath = path,
            Filename = "notes.md",
            Extension = ".md",
            DocumentType = "markdown"
        });

        var plan = new BatchOperationPlan
        {
            WorkspaceId = "ws-host",
            Status = BatchPlanStatus.Executing,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            Files =
            {
                new BatchOperationPlanFile
                {
                    DocumentId = "doc-host",
                    ExpectedFingerprint = Convert.ToHexString(SHA256.HashData(bytes)),
                    Selected = true
                }
            }
        };
        await new BatchPlanRepository(_db).SavePlanAsync(plan);

        var tempPath = Path.Combine(_sourceDir, $"~apply-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, "half-written apply temp");

        return new CrashedOperation(plan.Id, path, bytes, tempPath);
    }

    private async Task AssertCrashStateRepairedAsync(CrashedOperation seed)
    {
        // The interrupted plan is reconciled on startup — Failed, never Completed.
        var plan = await new BatchPlanRepository(_db).GetByIdAsync(seed.PlanId);
        Assert.NotNull(plan);
        Assert.Equal(BatchPlanStatus.Failed, plan!.Status);

        // The orphan apply temp is cleaned on startup.
        Assert.False(File.Exists(seed.TempPath));

        // The source document is byte-identical before and after.
        Assert.Equal(seed.Bytes, await File.ReadAllBytesAsync(seed.Path));
    }

    private sealed record CrashedOperation(string PlanId, string Path, byte[] Bytes, string TempPath);
}
