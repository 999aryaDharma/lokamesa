using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class Migration_005_MetadataPersistence : IMigration
{
    public int Version => 5;
    public string Name => "005_MetadataPersistence";

    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS document_metadata (
                document_id TEXT PRIMARY KEY,
                page_count INTEGER,
                section_count INTEGER,
                table_count INTEGER,
                image_count INTEGER,
                paper_summary TEXT,
                orientation_summary TEXT,
                margin_summary TEXT,
                primary_font TEXT,
                primary_font_size REAL,
                header_present INTEGER,
                footer_present INTEGER,
                headings_json TEXT,
                extracted_at TEXT NOT NULL,
                parser_version TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS markdown_metadata (
                document_id TEXT PRIMARY KEY,
                frontmatter_json TEXT,
                headings_json TEXT,
                outbound_links_json TEXT,
                wikilinks_json TEXT,
                code_block_count INTEGER,
                updated_at TEXT NOT NULL,
                parser_version TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_document_metadata_document_id ON document_metadata(document_id);
            CREATE INDEX IF NOT EXISTS idx_markdown_metadata_document_id ON markdown_metadata(document_id);
        ";

        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
