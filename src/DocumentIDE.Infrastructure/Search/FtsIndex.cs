using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Search;

public class FtsIndex : IFtsIndex, IIndexedTextReader
{
    private const string IndexVersionValue = "1.0.0";
    private readonly AppDb _appDb;

    public FtsIndex(AppDb appDb)
    {
        _appDb = appDb ?? throw new ArgumentNullException(nameof(appDb));
    }

    /// <summary>
    /// UI-05 — the indexed searchable body (the DOCX parser's
    /// <c>SearchableBody</c> when this row was produced by a DOCX parse).
    /// Read-only; <c>null</c> means the index has no text for the document.
    /// </summary>
    public async Task<string?> GetIndexedBodyAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return null;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT body FROM search_content WHERE document_id = @documentId LIMIT 1;";
        cmd.Parameters.AddWithValue("@documentId", documentId);

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : value as string;
    }

    public async Task IndexDocumentAsync(FtsIndexEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.DocumentId))
            throw new ArgumentException("DocumentId cannot be empty.", nameof(entry));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        await UpsertSingleEntryAsync(connection, transaction, entry, now);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry != null && !string.IsNullOrWhiteSpace(entry.DocumentId))
            {
                await UpsertSingleEntryAsync(connection, transaction, entry, now);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) return;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM search_content WHERE document_id = @documentId;";
            deleteCmd.Parameters.AddWithValue("@documentId", documentId);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var updateDocCmd = connection.CreateCommand())
        {
            updateDocCmd.Transaction = transaction;
            updateDocCmd.CommandText = "UPDATE documents SET index_status = 'stale', updated_at = @updatedAt WHERE id = @documentId;";
            updateDocCmd.Parameters.AddWithValue("@documentId", documentId);
            updateDocCmd.Parameters.AddWithValue("@updatedAt", now);
            await updateDocCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearIndexAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        using (var clearCmd = connection.CreateCommand())
        {
            clearCmd.Transaction = transaction;
            clearCmd.CommandText = "DELETE FROM search_content;";
            await clearCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var updateDocsCmd = connection.CreateCommand())
        {
            updateDocsCmd.Transaction = transaction;
            updateDocsCmd.CommandText = "UPDATE documents SET index_status = 'stale', updated_at = @updatedAt;";
            updateDocsCmd.Parameters.AddWithValue("@updatedAt", now);
            await updateDocsCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");

        using (var clearCmd = connection.CreateCommand())
        {
            clearCmd.Transaction = transaction;
            clearCmd.CommandText = "DELETE FROM search_content;";
            await clearCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var updateDocsCmd = connection.CreateCommand())
        {
            updateDocsCmd.Transaction = transaction;
            updateDocsCmd.CommandText = "UPDATE documents SET index_status = 'stale', updated_at = @updatedAt;";
            updateDocsCmd.Parameters.AddWithValue("@updatedAt", now);
            await updateDocsCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry != null && !string.IsNullOrWhiteSpace(entry.DocumentId))
            {
                await UpsertSingleEntryAsync(connection, transaction, entry, now);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<FtsSearchResult>();
        }

        limit = Math.Max(1, limit);

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var results = new List<FtsSearchResult>();

        try
        {
            await ExecuteSearchQueryAsync(connection, query, limit, results, cancellationToken);
        }
        catch (SqliteException)
        {
            // Fallback for raw syntax or unmatched FTS characters
            var sanitized = SanitizeFtsQuery(query);
            if (!string.IsNullOrWhiteSpace(sanitized) && !string.Equals(sanitized, query, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    results.Clear();
                    await ExecuteSearchQueryAsync(connection, sanitized, limit, results, cancellationToken);
                }
                catch (SqliteException)
                {
                    return Array.Empty<FtsSearchResult>();
                }
            }
            else
            {
                return Array.Empty<FtsSearchResult>();
            }
        }

        return results;
    }

    public async Task<FtsIndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _appDb.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM search_content;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var count = Convert.ToInt32(result ?? 0);

            return new FtsIndexDiagnostics(
                TotalIndexedDocuments: count,
                IsHealthy: true,
                IndexVersion: IndexVersionValue,
                ErrorMessage: null
            );
        }
        catch (Exception ex)
        {
            return new FtsIndexDiagnostics(
                TotalIndexedDocuments: 0,
                IsHealthy: false,
                IndexVersion: IndexVersionValue,
                ErrorMessage: ex.Message
            );
        }
    }

    private static async Task UpsertSingleEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FtsIndexEntry entry,
        string now)
    {
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM search_content WHERE document_id = @documentId;";
            deleteCmd.Parameters.AddWithValue("@documentId", entry.DocumentId);
            await deleteCmd.ExecuteNonQueryAsync();
        }

        using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = @"
                INSERT INTO search_content (document_id, filename, body, headings, tables, headers, footers, frontmatter)
                VALUES (@documentId, @filename, @body, @headings, @tables, @headers, @footers, @frontmatter);
            ";
            insertCmd.Parameters.AddWithValue("@documentId", entry.DocumentId);
            insertCmd.Parameters.AddWithValue("@filename", (object?)entry.Filename ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@body", (object?)entry.Body ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@headings", (object?)entry.Headings ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@tables", (object?)entry.Tables ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@headers", (object?)entry.Headers ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@footers", (object?)entry.Footers ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@frontmatter", (object?)entry.Frontmatter ?? DBNull.Value);
            await insertCmd.ExecuteNonQueryAsync();
        }

        using (var updateDocCmd = connection.CreateCommand())
        {
            updateDocCmd.Transaction = transaction;
            updateDocCmd.CommandText = "UPDATE documents SET index_status = 'ready', updated_at = @updatedAt WHERE id = @documentId;";
            updateDocCmd.Parameters.AddWithValue("@documentId", entry.DocumentId);
            updateDocCmd.Parameters.AddWithValue("@updatedAt", now);
            await updateDocCmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteSearchQueryAsync(
        SqliteConnection connection,
        string ftsQuery,
        int limit,
        List<FtsSearchResult> results,
        CancellationToken cancellationToken)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT document_id, filename, body, headings, tables, headers, footers, frontmatter,
                   snippet(search_content, -1, '<b>', '</b>', '...', 10) AS snippet,
                   rank AS score
            FROM search_content
            WHERE search_content MATCH @query
            ORDER BY rank
            LIMIT @limit;
        ";
        cmd.Parameters.AddWithValue("@query", ftsQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var docId = reader.GetString(0);
            var filename = reader.IsDBNull(1) ? null : reader.GetString(1);
            var body = reader.IsDBNull(2) ? null : reader.GetString(2);
            var headings = reader.IsDBNull(3) ? null : reader.GetString(3);
            var tables = reader.IsDBNull(4) ? null : reader.GetString(4);
            var headers = reader.IsDBNull(5) ? null : reader.GetString(5);
            var footers = reader.IsDBNull(6) ? null : reader.GetString(6);
            var frontmatter = reader.IsDBNull(7) ? null : reader.GetString(7);
            var snippet = reader.IsDBNull(8) ? null : reader.GetString(8);
            var score = reader.IsDBNull(9) ? 0.0 : reader.GetDouble(9);

            var matchSources = DetermineMatchSources(ftsQuery, filename, body, headings, tables, headers, footers, frontmatter);

            results.Add(new FtsSearchResult(
                DocumentId: docId,
                Filename: filename,
                Score: score,
                Snippet: snippet,
                MatchSources: matchSources
            ));
        }
    }

    private static string SanitizeFtsQuery(string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery)) return string.Empty;

        var terms = rawQuery.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var cleanTerms = new List<string>();

        foreach (var term in terms)
        {
            var clean = term.Replace("\"", "").Replace("'", "").Replace(":", "").Replace("*", "").Replace("^", "").Replace("(", "").Replace(")", "");
            if (!string.IsNullOrWhiteSpace(clean))
            {
                cleanTerms.Add($"\"{clean}\"*");
            }
        }

        return string.Join(" ", cleanTerms);
    }

    private static IReadOnlyList<string> DetermineMatchSources(
        string rawQuery,
        string? filename,
        string? body,
        string? headings,
        string? tables,
        string? headers,
        string? footers,
        string? frontmatter)
    {
        var sources = new List<string>();
        var terms = rawQuery.Split(new[] { ' ', '\t', '\r', '\n', '"', '\'', ':', '*', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(t => t.Length > 1)
                            .Select(t => t.ToLowerInvariant())
                            .Distinct()
                            .ToList();

        if (terms.Count == 0)
        {
            terms.Add(rawQuery.Trim().ToLowerInvariant());
        }

        bool CheckMatch(string? content)
        {
            if (string.IsNullOrEmpty(content)) return false;
            var lowerContent = content.ToLowerInvariant();
            return terms.Any(t => lowerContent.Contains(t));
        }

        if (CheckMatch(filename)) sources.Add("filename");
        if (CheckMatch(headings)) sources.Add("heading");
        if (CheckMatch(body)) sources.Add("body");
        if (CheckMatch(tables)) sources.Add("table");
        if (CheckMatch(headers)) sources.Add("header");
        if (CheckMatch(footers)) sources.Add("footer");
        if (CheckMatch(frontmatter)) sources.Add("frontmatter");

        return sources;
    }
}
