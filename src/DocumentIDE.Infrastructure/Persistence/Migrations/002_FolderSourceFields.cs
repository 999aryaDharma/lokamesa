using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

/// <summary>
/// FS-01: Adds display_name and last_reconciled_at columns to folder_sources.
/// </summary>
public class Migration_002_FolderSourceFields : IMigration
{
    public int Version => 2;
    public string Name => "002_FolderSourceFields";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            ALTER TABLE folder_sources ADD COLUMN display_name TEXT NOT NULL DEFAULT '';
            ALTER TABLE folder_sources ADD COLUMN last_reconciled_at TEXT;
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
