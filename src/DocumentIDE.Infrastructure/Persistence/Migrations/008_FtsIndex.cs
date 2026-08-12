using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class Migration_008_FtsIndex : IMigration
{
    public int Version => 8;
    public string Name => "008_FtsIndex";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS search_content USING fts5(
                document_id UNINDEXED,
                filename,
                body,
                headings,
                tables,
                headers,
                footers,
                frontmatter
            );
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
