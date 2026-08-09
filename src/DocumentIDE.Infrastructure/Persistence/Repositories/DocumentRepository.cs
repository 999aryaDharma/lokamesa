using System;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

public class DocumentRepository : IDocumentRepository
{
    private readonly AppDb _appDb;

    public DocumentRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task<Document?> GetByIdAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, filesystem_volume_id, filesystem_object_id, current_path, last_known_path, filename, extension, document_type, content_fingerprint, availability_status, index_status, created_at, updated_at FROM documents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapDocument(reader);
        }

        return null;
    }

    public async Task<Document?> GetByPathAsync(string currentPath)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, filesystem_volume_id, filesystem_object_id, current_path, last_known_path, filename, extension, document_type, content_fingerprint, availability_status, index_status, created_at, updated_at FROM documents WHERE current_path = @currentPath;";
        cmd.Parameters.AddWithValue("@currentPath", currentPath);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapDocument(reader);
        }

        return null;
    }

    public async Task CreateAsync(Document document)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO documents (id, filesystem_volume_id, filesystem_object_id, current_path, last_known_path, filename, extension, document_type, content_fingerprint, availability_status, index_status, created_at, updated_at)
            VALUES (@id, @volumeId, @objectId, @currentPath, @lastKnownPath, @filename, @extension, @documentType, @fingerprint, @availabilityStatus, @indexStatus, @createdAt, @updatedAt);
        ";
        cmd.Parameters.AddWithValue("@id", document.Id);
        cmd.Parameters.AddWithValue("@volumeId", (object?)document.FilesystemVolumeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@objectId", (object?)document.FilesystemObjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@currentPath", document.CurrentPath);
        cmd.Parameters.AddWithValue("@lastKnownPath", (object?)document.LastKnownPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@filename", document.Filename);
        cmd.Parameters.AddWithValue("@extension", document.Extension);
        cmd.Parameters.AddWithValue("@documentType", document.DocumentType);
        cmd.Parameters.AddWithValue("@fingerprint", (object?)document.ContentFingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@availabilityStatus", document.AvailabilityStatus);
        cmd.Parameters.AddWithValue("@indexStatus", document.IndexStatus);
        cmd.Parameters.AddWithValue("@createdAt", document.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", document.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(Document document)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE documents
            SET filesystem_volume_id = @volumeId,
                filesystem_object_id = @objectId,
                current_path = @currentPath,
                last_known_path = @lastKnownPath,
                filename = @filename,
                extension = @extension,
                document_type = @documentType,
                content_fingerprint = @fingerprint,
                availability_status = @availabilityStatus,
                index_status = @indexStatus,
                updated_at = @updatedAt
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", document.Id);
        cmd.Parameters.AddWithValue("@volumeId", (object?)document.FilesystemVolumeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@objectId", (object?)document.FilesystemObjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@currentPath", document.CurrentPath);
        cmd.Parameters.AddWithValue("@lastKnownPath", (object?)document.LastKnownPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@filename", document.Filename);
        cmd.Parameters.AddWithValue("@extension", document.Extension);
        cmd.Parameters.AddWithValue("@documentType", document.DocumentType);
        cmd.Parameters.AddWithValue("@fingerprint", (object?)document.ContentFingerprint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@availabilityStatus", document.AvailabilityStatus);
        cmd.Parameters.AddWithValue("@indexStatus", document.IndexStatus);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    private static Document MapDocument(SqliteDataReader reader)
    {
        return new Document
        {
            Id = reader.GetString(0),
            FilesystemVolumeId = reader.IsDBNull(1) ? null : reader.GetString(1),
            FilesystemObjectId = reader.IsDBNull(2) ? null : reader.GetString(2),
            CurrentPath = reader.GetString(3),
            LastKnownPath = reader.IsDBNull(4) ? null : reader.GetString(4),
            Filename = reader.GetString(5),
            Extension = reader.GetString(6),
            DocumentType = reader.GetString(7),
            ContentFingerprint = reader.IsDBNull(8) ? null : reader.GetString(8),
            AvailabilityStatus = reader.GetString(9),
            IndexStatus = reader.GetString(10),
            CreatedAt = reader.GetString(11),
            UpdatedAt = reader.GetString(12)
        };
    }
}
