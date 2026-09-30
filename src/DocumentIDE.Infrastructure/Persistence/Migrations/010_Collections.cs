using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public sealed class Migration_010_Collections : IMigration
{
    public int Version => 10;
    public string Name => "010_Collections";
    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE collections (
                id TEXT PRIMARY KEY, workspace_id TEXT NOT NULL, name TEXT NOT NULL,
                description TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL DEFAULT '',
                FOREIGN KEY(workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE);
            CREATE INDEX idx_collections_workspace ON collections(workspace_id);
            CREATE TABLE collection_members (
                collection_id TEXT NOT NULL, document_id TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0,
                added_at TEXT NOT NULL DEFAULT '', PRIMARY KEY(collection_id, document_id),
                FOREIGN KEY(collection_id) REFERENCES collections(id) ON DELETE CASCADE,
                FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
