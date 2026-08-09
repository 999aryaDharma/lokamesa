using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Persistence;

public class SqliteConcurrencyTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
        {
            try { File.Delete(walPath); } catch { }
        }
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
        {
            try { File.Delete(shmPath); } catch { }
        }
    }

    [Fact]
    public async Task Migrations_Apply_On_Empty_Database()
    {
        var appDb = new AppDb(_dbPath);
        await appDb.InitializeAsync();

        using var conn = appDb.CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('workspaces', 'folder_sources', 'documents', 'workspace_documents', 'schema_migrations');";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        Assert.Equal(5, count);

        using var pragmaCmd = conn.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode;";
        var journalMode = (string?)await pragmaCmd.ExecuteScalarAsync();
        Assert.Equal("wal", journalMode, ignoreCase: true);
    }

    [Fact]
    public async Task Foreign_Key_Violations_Fail()
    {
        var appDb = new AppDb(_dbPath);
        await appDb.InitializeAsync();

        using var conn = appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO folder_sources (id, workspace_id, canonical_path, created_at, updated_at) VALUES ('fs1', 'nonexistent_ws', '/path', '2026-08-09T00:00:00Z', '2026-08-09T00:00:00Z');";

        await Assert.ThrowsAsync<SqliteException>(async () => await cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task FTS_Like_Read_Can_Continue_During_Unrelated_Short_Write_Transaction()
    {
        var appDb = new AppDb(_dbPath);
        await appDb.InitializeAsync();

        var wsRepo = new WorkspaceRepository(appDb);
        var ws = new Workspace { Name = "Concurrent Test WS" };
        await wsRepo.CreateAsync(ws);

        using var connWriter = appDb.CreateConnection();
        await connWriter.OpenAsync();

        using var tx = connWriter.BeginTransaction();
        using var cmdWrite = connWriter.CreateCommand();
        cmdWrite.Transaction = tx;
        cmdWrite.CommandText = "INSERT INTO workspaces (id, name, created_at, updated_at) VALUES ('ws2', 'Write Tx WS', '2026-08-09T00:00:00Z', '2026-08-09T00:00:00Z');";
        await cmdWrite.ExecuteNonQueryAsync();

        using var connReader = appDb.CreateConnection();
        await connReader.OpenAsync();
        using var cmdRead = connReader.CreateCommand();
        cmdRead.CommandText = "SELECT count(*) FROM workspaces;";
        var readCount = Convert.ToInt32(await cmdRead.ExecuteScalarAsync());

        Assert.Equal(1, readCount);

        await tx.CommitAsync();
    }
}
