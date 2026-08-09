using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Infrastructure.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence;

public class AppDb
{
    public string DatabasePath { get; }
    public string ConnectionString => $"Data Source={DatabasePath}";

    public AppDb(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDir = Path.Combine(localAppData, "DocumentIDE");
            DatabasePath = Path.Combine(appDir, "app.db");
        }
        else
        {
            DatabasePath = databasePath;
        }
    }

    public SqliteConnection CreateConnection()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connection = new SqliteConnection(ConnectionString);
        connection.StateChange += (sender, args) =>
        {
            if (args.CurrentState == System.Data.ConnectionState.Open && sender is SqliteConnection conn)
            {
                ApplyPragmas(conn);
            }
        };

        return connection;
    }

    public async Task InitializeAsync()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var connection = CreateConnection();
        await connection.OpenAsync();

        var migrationRunner = new MigrationRunner(this);
        await migrationRunner.RunMigrationsAsync();
    }

    private static void ApplyPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = NORMAL;
        ";
        cmd.ExecuteNonQuery();
    }
}
