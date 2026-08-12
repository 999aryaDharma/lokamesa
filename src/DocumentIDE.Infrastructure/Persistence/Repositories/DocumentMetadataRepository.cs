using System;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

public class DocumentMetadataRepository : IDocumentMetadataRepository
{
    private readonly AppDb _appDb;

    public DocumentMetadataRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task<DocumentMetadata?> GetDocxMetadataAsync(string documentId)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT document_id, page_count, section_count, table_count, image_count,
                   paper_summary, orientation_summary, margin_summary, primary_font,
                   primary_font_size, header_present, footer_present, headings_json,
                   extracted_at, parser_version
            FROM document_metadata
            WHERE document_id = @documentId;
        ";
        cmd.Parameters.AddWithValue("@documentId", documentId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new DocumentMetadata
            {
                DocumentId = reader.GetString(0),
                PageCount = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                SectionCount = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                TableCount = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ImageCount = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                PaperSummary = reader.IsDBNull(5) ? null : reader.GetString(5),
                OrientationSummary = reader.IsDBNull(6) ? null : reader.GetString(6),
                MarginSummary = reader.IsDBNull(7) ? null : reader.GetString(7),
                PrimaryFont = reader.IsDBNull(8) ? null : reader.GetString(8),
                PrimaryFontSize = reader.IsDBNull(9) ? null : reader.GetDouble(9),
                HeaderPresent = reader.IsDBNull(10) ? null : reader.GetInt32(10) != 0,
                FooterPresent = reader.IsDBNull(11) ? null : reader.GetInt32(11) != 0,
                HeadingsJson = reader.IsDBNull(12) ? null : reader.GetString(12),
                ExtractedAt = reader.GetString(13),
                ParserVersion = reader.GetString(14)
            };
        }

        return null;
    }

    public async Task SaveDocxMetadataAsync(DocumentMetadata metadata)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO document_metadata (
                    document_id, page_count, section_count, table_count, image_count,
                    paper_summary, orientation_summary, margin_summary, primary_font,
                    primary_font_size, header_present, footer_present, headings_json,
                    extracted_at, parser_version
                )
                VALUES (
                    @documentId, @pageCount, @sectionCount, @tableCount, @imageCount,
                    @paperSummary, @orientationSummary, @marginSummary, @primaryFont,
                    @primaryFontSize, @headerPresent, @footerPresent, @headingsJson,
                    @extractedAt, @parserVersion
                )
                ON CONFLICT(document_id) DO UPDATE SET
                    page_count = excluded.page_count,
                    section_count = excluded.section_count,
                    table_count = excluded.table_count,
                    image_count = excluded.image_count,
                    paper_summary = excluded.paper_summary,
                    orientation_summary = excluded.orientation_summary,
                    margin_summary = excluded.margin_summary,
                    primary_font = excluded.primary_font,
                    primary_font_size = excluded.primary_font_size,
                    header_present = excluded.header_present,
                    footer_present = excluded.footer_present,
                    headings_json = excluded.headings_json,
                    extracted_at = excluded.extracted_at,
                    parser_version = excluded.parser_version;
            ";

            cmd.Parameters.AddWithValue("@documentId", metadata.DocumentId);
            cmd.Parameters.AddWithValue("@pageCount", (object?)metadata.PageCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sectionCount", (object?)metadata.SectionCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tableCount", (object?)metadata.TableCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@imageCount", (object?)metadata.ImageCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@paperSummary", (object?)metadata.PaperSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@orientationSummary", (object?)metadata.OrientationSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@marginSummary", (object?)metadata.MarginSummary ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@primaryFont", (object?)metadata.PrimaryFont ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@primaryFontSize", (object?)metadata.PrimaryFontSize ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@headerPresent", metadata.HeaderPresent.HasValue ? (metadata.HeaderPresent.Value ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("@footerPresent", metadata.FooterPresent.HasValue ? (metadata.FooterPresent.Value ? 1 : 0) : DBNull.Value);
            cmd.Parameters.AddWithValue("@headingsJson", (object?)metadata.HeadingsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@extractedAt", metadata.ExtractedAt);
            cmd.Parameters.AddWithValue("@parserVersion", metadata.ParserVersion);

            await cmd.ExecuteNonQueryAsync();

            // Mark document index status ready
            using var statusCmd = conn.CreateCommand();
            statusCmd.Transaction = tx;
            statusCmd.CommandText = "UPDATE documents SET index_status = 'ready', updated_at = @updatedAt WHERE id = @documentId;";
            statusCmd.Parameters.AddWithValue("@documentId", metadata.DocumentId);
            statusCmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            await statusCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<MarkdownDocumentMetadata?> GetMarkdownMetadataAsync(string documentId)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT document_id, frontmatter_json, headings_json, outbound_links_json,
                   wikilinks_json, code_block_count, updated_at, parser_version
            FROM markdown_metadata
            WHERE document_id = @documentId;
        ";
        cmd.Parameters.AddWithValue("@documentId", documentId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new MarkdownDocumentMetadata
            {
                DocumentId = reader.GetString(0),
                FrontmatterJson = reader.IsDBNull(1) ? null : reader.GetString(1),
                HeadingsJson = reader.IsDBNull(2) ? null : reader.GetString(2),
                OutboundLinksJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                WikilinksJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                CodeBlockCount = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                UpdatedAt = reader.GetString(6),
                ParserVersion = reader.GetString(7)
            };
        }

        return null;
    }

    public async Task SaveMarkdownMetadataAsync(MarkdownDocumentMetadata metadata)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO markdown_metadata (
                    document_id, frontmatter_json, headings_json, outbound_links_json,
                    wikilinks_json, code_block_count, updated_at, parser_version
                )
                VALUES (
                    @documentId, @frontmatterJson, @headingsJson, @outboundLinksJson,
                    @wikilinksJson, @codeBlockCount, @updatedAt, @parserVersion
                )
                ON CONFLICT(document_id) DO UPDATE SET
                    frontmatter_json = excluded.frontmatter_json,
                    headings_json = excluded.headings_json,
                    outbound_links_json = excluded.outbound_links_json,
                    wikilinks_json = excluded.wikilinks_json,
                    code_block_count = excluded.code_block_count,
                    updated_at = excluded.updated_at,
                    parser_version = excluded.parser_version;
            ";

            cmd.Parameters.AddWithValue("@documentId", metadata.DocumentId);
            cmd.Parameters.AddWithValue("@frontmatterJson", (object?)metadata.FrontmatterJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@headingsJson", (object?)metadata.HeadingsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@outboundLinksJson", (object?)metadata.OutboundLinksJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@wikilinksJson", (object?)metadata.WikilinksJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@codeBlockCount", (object?)metadata.CodeBlockCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@updatedAt", metadata.UpdatedAt);
            cmd.Parameters.AddWithValue("@parserVersion", metadata.ParserVersion);

            await cmd.ExecuteNonQueryAsync();

            // Mark document index status ready
            using var statusCmd = conn.CreateCommand();
            statusCmd.Transaction = tx;
            statusCmd.CommandText = "UPDATE documents SET index_status = 'ready', updated_at = @updatedAt WHERE id = @documentId;";
            statusCmd.Parameters.AddWithValue("@documentId", metadata.DocumentId);
            statusCmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            await statusCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteMetadataAsync(string documentId)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd1 = conn.CreateCommand();
            cmd1.Transaction = tx;
            cmd1.CommandText = "DELETE FROM document_metadata WHERE document_id = @documentId;";
            cmd1.Parameters.AddWithValue("@documentId", documentId);
            await cmd1.ExecuteNonQueryAsync();

            using var cmd2 = conn.CreateCommand();
            cmd2.Transaction = tx;
            cmd2.CommandText = "DELETE FROM markdown_metadata WHERE document_id = @documentId;";
            cmd2.Parameters.AddWithValue("@documentId", documentId);
            await cmd2.ExecuteNonQueryAsync();

            using var statusCmd = conn.CreateCommand();
            statusCmd.Transaction = tx;
            statusCmd.CommandText = "UPDATE documents SET index_status = 'stale', updated_at = @updatedAt WHERE id = @documentId;";
            statusCmd.Parameters.AddWithValue("@documentId", documentId);
            statusCmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            await statusCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAllMetadataAsync()
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd1 = conn.CreateCommand();
            cmd1.Transaction = tx;
            cmd1.CommandText = "DELETE FROM document_metadata;";
            await cmd1.ExecuteNonQueryAsync();

            using var cmd2 = conn.CreateCommand();
            cmd2.Transaction = tx;
            cmd2.CommandText = "DELETE FROM markdown_metadata;";
            await cmd2.ExecuteNonQueryAsync();

            using var statusCmd = conn.CreateCommand();
            statusCmd.Transaction = tx;
            statusCmd.CommandText = "UPDATE documents SET index_status = 'stale', updated_at = @updatedAt;";
            statusCmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
            await statusCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task MarkStatusAsync(string documentId, string status, string? lastError = null)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET index_status = @status, updated_at = @updatedAt WHERE id = @documentId;";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@documentId", documentId);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<DocumentMetadataDto?> GetMetadataDtoAsync(string documentId, string currentParserVersion = "1.0.0")
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        string? documentType = null;
        string? indexStatus = null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT document_type, index_status FROM documents WHERE id = @documentId;";
            cmd.Parameters.AddWithValue("@documentId", documentId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                documentType = reader.GetString(0);
                indexStatus = reader.GetString(1);
            }
        }

        if (documentType == null)
        {
            return null;
        }

        var docxMeta = await GetDocxMetadataAsync(documentId);
        var mdMeta = await GetMarkdownMetadataAsync(documentId);

        bool isRebuildable = false;
        if (indexStatus == "stale" || indexStatus == "error")
        {
            isRebuildable = true;
        }

        if (docxMeta != null && !string.Equals(docxMeta.ParserVersion, currentParserVersion, StringComparison.OrdinalIgnoreCase))
        {
            isRebuildable = true;
            indexStatus = "stale";
            await MarkStatusAsync(documentId, "stale");
        }

        if (mdMeta != null && !string.Equals(mdMeta.ParserVersion, currentParserVersion, StringComparison.OrdinalIgnoreCase))
        {
            isRebuildable = true;
            indexStatus = "stale";
            await MarkStatusAsync(documentId, "stale");
        }

        if (docxMeta == null && mdMeta == null)
        {
            isRebuildable = true;
        }

        return new DocumentMetadataDto
        {
            DocumentId = documentId,
            DocumentType = documentType,
            IndexStatus = indexStatus ?? "pending",
            DocxMetadata = docxMeta,
            MarkdownMetadata = mdMeta,
            IsRebuildable = isRebuildable
        };
    }
}
