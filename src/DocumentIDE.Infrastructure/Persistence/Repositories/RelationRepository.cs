using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

public class RelationRepository : IRelationRepository
{
    private readonly AppDb _appDb;

    public RelationRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task AddAsync(Relation relation)
    {
        if (relation == null) throw new ArgumentNullException(nameof(relation));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO relations (id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at)
            VALUES (@id, @workspaceId, @sourceDocId, @targetDocId, @relType, @relOrigin, @confidence, @metadataJson, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                workspace_id = excluded.workspace_id,
                source_document_id = excluded.source_document_id,
                target_document_id = excluded.target_document_id,
                relation_type = excluded.relation_type,
                relation_origin = excluded.relation_origin,
                confidence = excluded.confidence,
                metadata_json = excluded.metadata_json,
                updated_at = excluded.updated_at;
        ";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        BindParameters(cmd, relation);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddRangeAsync(IEnumerable<Relation> relations)
    {
        if (relations == null) throw new ArgumentNullException(nameof(relations));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();

        var sql = @"
            INSERT INTO relations (id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at)
            VALUES (@id, @workspaceId, @sourceDocId, @targetDocId, @relType, @relOrigin, @confidence, @metadataJson, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                workspace_id = excluded.workspace_id,
                source_document_id = excluded.source_document_id,
                target_document_id = excluded.target_document_id,
                relation_type = excluded.relation_type,
                relation_origin = excluded.relation_origin,
                confidence = excluded.confidence,
                metadata_json = excluded.metadata_json,
                updated_at = excluded.updated_at;
        ";

        foreach (var rel in relations)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            BindParameters(cmd, rel);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<Relation?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at FROM relations WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapReaderToRelation(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<Relation>> GetRelationsAsync(string workspaceId, string? origin = null, string? type = null)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        var query = "SELECT id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at FROM relations WHERE workspace_id = @workspaceId";
        
        if (!string.IsNullOrWhiteSpace(origin))
        {
            query += " AND relation_origin = @origin";
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            query += " AND relation_type = @type";
        }
        query += " ORDER BY created_at ASC;";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = query;
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        if (!string.IsNullOrWhiteSpace(origin))
        {
            cmd.Parameters.AddWithValue("@origin", origin);
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            cmd.Parameters.AddWithValue("@type", type);
        }

        var results = new List<Relation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapReaderToRelation(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<Relation>> GetBySourceDocumentAsync(string workspaceId, string sourceDocumentId)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at
            FROM relations
            WHERE workspace_id = @workspaceId AND source_document_id = @sourceDocId
            ORDER BY created_at ASC;
        ";
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@sourceDocId", sourceDocumentId);

        var results = new List<Relation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapReaderToRelation(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<Relation>> GetByTargetDocumentAsync(string workspaceId, string targetDocumentId)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at
            FROM relations
            WHERE workspace_id = @workspaceId AND target_document_id = @targetDocId
            ORDER BY created_at ASC;
        ";
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@targetDocId", targetDocumentId);

        var results = new List<Relation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapReaderToRelation(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<Relation>> GetBacklinksAsync(string workspaceId, string targetDocumentId)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        // Backlinks list explicit and manual inbound relations (excluding inferred/semantic relations)
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at
            FROM relations
            WHERE workspace_id = @workspaceId AND target_document_id = @targetDocId AND relation_origin IN (@explicitOrigin, @manualOrigin)
            ORDER BY created_at ASC;
        ";
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@targetDocId", targetDocumentId);
        cmd.Parameters.AddWithValue("@explicitOrigin", RelationOrigin.Explicit);
        cmd.Parameters.AddWithValue("@manualOrigin", RelationOrigin.Manual);

        var results = new List<Relation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapReaderToRelation(reader));
        }

        return results;
    }

    public async Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations)
    {
        if (newDerivedRelations == null) throw new ArgumentNullException(nameof(newDerivedRelations));

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();

        // 1. Delete all existing explicit derived relations for (workspaceId, sourceDocumentId)
        using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = @"
                DELETE FROM relations
                WHERE workspace_id = @workspaceId AND source_document_id = @sourceDocId AND relation_origin = @explicitOrigin;
            ";
            deleteCmd.Parameters.AddWithValue("@workspaceId", workspaceId);
            deleteCmd.Parameters.AddWithValue("@sourceDocId", sourceDocumentId);
            deleteCmd.Parameters.AddWithValue("@explicitOrigin", RelationOrigin.Explicit);
            await deleteCmd.ExecuteNonQueryAsync();
        }

        // 2. Insert new derived relations
        var insertSql = @"
            INSERT INTO relations (id, workspace_id, source_document_id, target_document_id, relation_type, relation_origin, confidence, metadata_json, created_at, updated_at)
            VALUES (@id, @workspaceId, @sourceDocId, @targetDocId, @relType, @relOrigin, @confidence, @metadataJson, @createdAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                workspace_id = excluded.workspace_id,
                source_document_id = excluded.source_document_id,
                target_document_id = excluded.target_document_id,
                relation_type = excluded.relation_type,
                relation_origin = excluded.relation_origin,
                confidence = excluded.confidence,
                metadata_json = excluded.metadata_json,
                updated_at = excluded.updated_at;
        ";

        foreach (var rel in newDerivedRelations)
        {
            rel.WorkspaceId = workspaceId;
            rel.SourceDocumentId = sourceDocumentId;
            rel.RelationOrigin = RelationOrigin.Explicit;

            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = insertSql;
            BindParameters(cmd, rel);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM relations WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType)
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM relations
            WHERE workspace_id = @workspaceId
              AND source_document_id = @sourceDocId
              AND target_document_id = @targetDocId
              AND relation_origin = @manualOrigin
              AND relation_type = @relType;
        ";
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@sourceDocId", sourceDocumentId);
        cmd.Parameters.AddWithValue("@targetDocId", targetDocumentId);
        cmd.Parameters.AddWithValue("@manualOrigin", RelationOrigin.Manual);
        cmd.Parameters.AddWithValue("@relType", relationType);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void BindParameters(SqliteCommand cmd, Relation relation)
    {
        cmd.Parameters.AddWithValue("@id", relation.Id ?? Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@workspaceId", relation.WorkspaceId);
        cmd.Parameters.AddWithValue("@sourceDocId", relation.SourceDocumentId);
        cmd.Parameters.AddWithValue("@targetDocId", relation.TargetDocumentId);
        cmd.Parameters.AddWithValue("@relType", relation.RelationType);
        cmd.Parameters.AddWithValue("@relOrigin", relation.RelationOrigin);
        cmd.Parameters.AddWithValue("@confidence", relation.Confidence);
        cmd.Parameters.AddWithValue("@metadataJson", (object?)relation.MetadataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", relation.CreatedAt ?? DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt", relation.UpdatedAt ?? DateTime.UtcNow.ToString("O"));
    }

    private static Relation MapReaderToRelation(SqliteDataReader reader)
    {
        return new Relation
        {
            Id = reader.GetString(0),
            WorkspaceId = reader.GetString(1),
            SourceDocumentId = reader.GetString(2),
            TargetDocumentId = reader.GetString(3),
            RelationType = reader.GetString(4),
            RelationOrigin = reader.GetString(5),
            Confidence = reader.GetDouble(6),
            MetadataJson = reader.IsDBNull(7) ? null : reader.GetString(7),
            CreatedAt = reader.GetString(8),
            UpdatedAt = reader.GetString(9)
        };
    }
}
