using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class InitialMigration_001 : IMigration
{
    public int Version => 1;
    public string Name => "001_Initial";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS workspaces (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                active_view TEXT NOT NULL DEFAULT 'canvas',
                canvas_viewport_json TEXT,
                list_state_json TEXT,
                graph_viewport_json TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS folder_sources (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                canonical_path TEXT NOT NULL,
                filesystem_volume_id TEXT,
                filesystem_object_id TEXT,
                availability_status TEXT NOT NULL DEFAULT 'available',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_folder_sources_workspace_id ON folder_sources(workspace_id);

            CREATE TABLE IF NOT EXISTS documents (
                id TEXT PRIMARY KEY,
                filesystem_volume_id TEXT,
                filesystem_object_id TEXT,
                current_path TEXT NOT NULL,
                last_known_path TEXT,
                filename TEXT NOT NULL,
                extension TEXT NOT NULL,
                document_type TEXT NOT NULL,
                content_fingerprint TEXT,
                availability_status TEXT NOT NULL DEFAULT 'available',
                index_status TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_documents_current_path ON documents(current_path);
            CREATE INDEX IF NOT EXISTS idx_documents_volume_object ON documents(filesystem_volume_id, filesystem_object_id);

            CREATE TABLE IF NOT EXISTS workspace_documents (
                workspace_id TEXT NOT NULL,
                document_id TEXT NOT NULL,
                canvas_x REAL NOT NULL DEFAULT 0,
                canvas_y REAL NOT NULL DEFAULT 0,
                position_mode TEXT NOT NULL DEFAULT 'auto',
                pinned INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                PRIMARY KEY (workspace_id, document_id),
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_workspace_documents_workspace_id ON workspace_documents(workspace_id);
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
