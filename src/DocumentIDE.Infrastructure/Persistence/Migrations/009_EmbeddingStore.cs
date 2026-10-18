using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class Migration_009_EmbeddingStore : IMigration
{
    public int Version => 9;
    public string Name => "009_EmbeddingStore";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS document_embeddings (
                document_id TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                content_fingerprint TEXT NOT NULL,
                vector_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_document_embeddings_document_id ON document_embeddings(document_id);
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
