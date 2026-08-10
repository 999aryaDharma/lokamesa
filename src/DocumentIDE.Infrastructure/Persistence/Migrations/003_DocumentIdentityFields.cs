using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

/// <summary>
/// FS-02: Adds size_bytes and modified_at columns to documents.
/// </summary>
public class Migration_003_DocumentIdentityFields : IMigration
{
    public int Version => 3;
    public string Name => "003_DocumentIdentityFields";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            ALTER TABLE documents ADD COLUMN size_bytes INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE documents ADD COLUMN modified_at TEXT;
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
