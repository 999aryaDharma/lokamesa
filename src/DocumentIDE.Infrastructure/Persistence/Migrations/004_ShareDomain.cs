using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

/// <summary>
/// SHR-01: Creates shares, share_members, and share_snapshot_artifacts tables for Share Domain.
/// </summary>
public class Migration_004_ShareDomain : IMigration
{
    public int Version => 4;
    public string Name => "004_ShareDomain";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS shares (
                id TEXT PRIMARY KEY,
                mode TEXT NOT NULL,
                access_mode TEXT NOT NULL,
                token_hash TEXT NOT NULL,
                allow_original_download INTEGER NOT NULL DEFAULT 0,
                allow_pdf_download INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                expires_at TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_shares_token_hash ON shares(token_hash);

            CREATE TABLE IF NOT EXISTS share_members (
                id TEXT PRIMARY KEY,
                share_id TEXT NOT NULL,
                document_id TEXT NOT NULL,
                display_path TEXT NOT NULL,
                added_at TEXT NOT NULL,
                FOREIGN KEY (share_id) REFERENCES shares(id) ON DELETE CASCADE,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_share_members_share_id ON share_members(share_id);
            CREATE INDEX IF NOT EXISTS idx_share_members_document_id ON share_members(document_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_share_members_share_doc ON share_members(share_id, document_id);

            CREATE TABLE IF NOT EXISTS share_snapshot_artifacts (
                id TEXT PRIMARY KEY,
                share_id TEXT NOT NULL,
                document_id TEXT NOT NULL,
                artifact_type TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                size_bytes INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                FOREIGN KEY (share_id) REFERENCES shares(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_share_snapshot_artifacts_share_id ON share_snapshot_artifacts(share_id);
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
