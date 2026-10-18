using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class Migration_007_BatchOperationPlans : IMigration
{
    public int Version => 7;
    public string Name => "007_BatchOperationPlans";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS batch_operation_plans (
                id TEXT PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                scope_json TEXT NOT NULL,
                parameters_json TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                idempotency_key TEXT,
                FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_batch_plans_workspace ON batch_operation_plans(workspace_id);
            CREATE INDEX IF NOT EXISTS idx_batch_plans_status ON batch_operation_plans(status);
            CREATE INDEX IF NOT EXISTS idx_batch_plans_idempotency ON batch_operation_plans(idempotency_key);

            CREATE TABLE IF NOT EXISTS batch_operation_plan_files (
                plan_id TEXT NOT NULL,
                document_id TEXT NOT NULL,
                expected_fingerprint TEXT NOT NULL,
                match_count INTEGER NOT NULL DEFAULT 0,
                selected INTEGER NOT NULL DEFAULT 1,
                selected_change_ids_json TEXT,
                plan_payload_json TEXT,
                PRIMARY KEY (plan_id, document_id),
                FOREIGN KEY (plan_id) REFERENCES batch_operation_plans(id) ON DELETE CASCADE,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_batch_plan_files_plan ON batch_operation_plan_files(plan_id);
            CREATE INDEX IF NOT EXISTS idx_batch_plan_files_doc ON batch_operation_plan_files(document_id);
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
