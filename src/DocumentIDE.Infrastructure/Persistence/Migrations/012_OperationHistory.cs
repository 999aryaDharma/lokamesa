using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

/// <summary>
/// HIST-04 — persisted Document IDE operation history (spec 02 §18
/// OperationHistory, spec 00 §25.1). One row per applied operation; the undo
/// write path updates that same row to <c>Undone</c> and stores the per-file
/// undo review, so the History screen lists one entry per operation.
///
/// Per-file apply outcomes and the undo review are stored as bounded JSON
/// documents. The table is derived metadata only: dropping it never affects a
/// user file, a recovery snapshot, or Word.
/// </summary>
public sealed class Migration_012_OperationHistory : IMigration
{
    public int Version => 12;
    public string Name => "012_OperationHistory";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE operation_history (
                id TEXT PRIMARY KEY,
                workspace_id TEXT,
                plan_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                outcome TEXT NOT NULL,
                snapshot_id TEXT,
                started_at TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                affected_file_count INTEGER NOT NULL,
                files_json TEXT NOT NULL,
                undo_json TEXT,
                recorded_at TEXT NOT NULL);
            CREATE INDEX idx_operation_history_started ON operation_history(started_at DESC);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
