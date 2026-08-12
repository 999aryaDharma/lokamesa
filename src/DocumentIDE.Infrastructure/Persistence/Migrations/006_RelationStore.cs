using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class Migration_006_RelationStore : IMigration
{
    public int Version => 6;
    public string Name => "006_RelationStore";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS relations (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                source_document_id TEXT NOT NULL,
                target_document_id TEXT NOT NULL,
                relation_type TEXT NOT NULL,
                relation_origin TEXT NOT NULL,
                confidence REAL NOT NULL DEFAULT 1.0,
                metadata_json TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
                FOREIGN KEY (source_document_id) REFERENCES documents(id) ON DELETE CASCADE,
                FOREIGN KEY (target_document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_relations_workspace_id ON relations(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_relations_source_doc ON relations(workspace_id, source_document_id);
            CREATE INDEX IF NOT EXISTS idx_relations_target_doc ON relations(workspace_id, target_document_id);
            CREATE INDEX IF NOT EXISTS idx_relations_origin ON relations(relation_origin);
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
