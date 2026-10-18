using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Semantic;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Semantic;

public class SqliteEmbeddingStore : IEmbeddingStore
{
    private readonly AppDb _appDb;

    public SqliteEmbeddingStore(AppDb appDb)
    {
        _appDb = appDb ?? throw new ArgumentNullException(nameof(appDb));
    }

    public async Task SaveEmbeddingAsync(DocumentEmbedding embedding, CancellationToken cancellationToken = default)
    {
        if (embedding == null) throw new ArgumentNullException(nameof(embedding));
        if (string.IsNullOrEmpty(embedding.DocumentId)) throw new ArgumentException("DocumentId cannot be empty.", nameof(embedding));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO document_embeddings (document_id, provider_id, model_id, content_fingerprint, vector_json, created_at)
            VALUES (@document_id, @provider_id, @model_id, @content_fingerprint, @vector_json, @created_at)
            ON CONFLICT(document_id) DO UPDATE SET
                provider_id = excluded.provider_id,
                model_id = excluded.model_id,
                content_fingerprint = excluded.content_fingerprint,
                vector_json = excluded.vector_json,
                created_at = excluded.created_at;
        ";

        cmd.Parameters.AddWithValue("@document_id", embedding.DocumentId);
        cmd.Parameters.AddWithValue("@provider_id", embedding.ProviderId ?? string.Empty);
        cmd.Parameters.AddWithValue("@model_id", embedding.ModelId ?? string.Empty);
        cmd.Parameters.AddWithValue("@content_fingerprint", embedding.ContentFingerprint ?? string.Empty);
        cmd.Parameters.AddWithValue("@vector_json", JsonSerializer.Serialize(embedding.Vector ?? Array.Empty<float>()));
        cmd.Parameters.AddWithValue("@created_at", embedding.CreatedAt ?? DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DocumentEmbedding?> GetEmbeddingAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(documentId)) return null;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT document_id, provider_id, model_id, content_fingerprint, vector_json, created_at
            FROM document_embeddings
            WHERE document_id = @document_id;
        ";
        cmd.Parameters.AddWithValue("@document_id", documentId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var vectorJson = reader.GetString(4);
            var vector = JsonSerializer.Deserialize<float[]>(vectorJson) ?? Array.Empty<float>();

            return new DocumentEmbedding
            {
                DocumentId = reader.GetString(0),
                ProviderId = reader.GetString(1),
                ModelId = reader.GetString(2),
                ContentFingerprint = reader.GetString(3),
                Vector = vector,
                CreatedAt = reader.GetString(5)
            };
        }

        return null;
    }

    public async Task DeleteEmbeddingAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(documentId)) return;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM document_embeddings WHERE document_id = @document_id;";
        cmd.Parameters.AddWithValue("@document_id", documentId);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DocumentEmbedding>> GetAllEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT document_id, provider_id, model_id, content_fingerprint, vector_json, created_at
            FROM document_embeddings;
        ";

        var list = new List<DocumentEmbedding>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var vectorJson = reader.GetString(4);
            var vector = JsonSerializer.Deserialize<float[]>(vectorJson) ?? Array.Empty<float>();

            list.Add(new DocumentEmbedding
            {
                DocumentId = reader.GetString(0),
                ProviderId = reader.GetString(1),
                ModelId = reader.GetString(2),
                ContentFingerprint = reader.GetString(3),
                Vector = vector,
                CreatedAt = reader.GetString(5)
            });
        }

        return list;
    }
}
