using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for FolderSource records.
/// FS-01: Folder Source Attachment.
/// </summary>
public class FolderSourceRepository : IFolderSourceRepository
{
    private readonly AppDb _appDb;

    public FolderSourceRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task CreateAsync(FolderSource folderSource)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO folder_sources
                (id, workspace_id, canonical_path, filesystem_volume_id, filesystem_object_id,
                 display_name, availability_status, last_reconciled_at, created_at, updated_at)
            VALUES
                (@id, @workspaceId, @canonicalPath, @fsVolumeId, @fsObjectId,
                 @displayName, @availabilityStatus, @lastReconciledAt, @createdAt, @updatedAt);
        ";
        cmd.Parameters.AddWithValue("@id", folderSource.Id);
        cmd.Parameters.AddWithValue("@workspaceId", folderSource.WorkspaceId);
        cmd.Parameters.AddWithValue("@canonicalPath", folderSource.CanonicalPath);
        cmd.Parameters.AddWithValue("@fsVolumeId", (object?)folderSource.FilesystemVolumeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fsObjectId", (object?)folderSource.FilesystemObjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@displayName", folderSource.DisplayName);
        cmd.Parameters.AddWithValue("@availabilityStatus", folderSource.AvailabilityStatus);
        cmd.Parameters.AddWithValue("@lastReconciledAt", (object?)folderSource.LastReconciledAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", folderSource.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", folderSource.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<FolderSource>> GetByWorkspaceAsync(string workspaceId)
    {
        var results = new List<FolderSource>();

        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, workspace_id, canonical_path, filesystem_volume_id, filesystem_object_id,
                   display_name, availability_status, last_reconciled_at, created_at, updated_at
            FROM folder_sources
            WHERE workspace_id = @workspaceId
            ORDER BY created_at ASC;
        ";
        cmd.Parameters.AddWithValue("@workspaceId", workspaceId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new FolderSource
            {
                Id = reader.GetString(0),
                WorkspaceId = reader.GetString(1),
                CanonicalPath = reader.GetString(2),
                FilesystemVolumeId = reader.IsDBNull(3) ? null : reader.GetString(3),
                FilesystemObjectId = reader.IsDBNull(4) ? null : reader.GetString(4),
                DisplayName = reader.GetString(5),
                AvailabilityStatus = reader.GetString(6),
                LastReconciledAt = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = reader.GetString(8),
                UpdatedAt = reader.GetString(9),
            });
        }

        return results;
    }

    public async Task UpdateAvailabilityAsync(string id, string availabilityStatus)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE folder_sources
            SET availability_status = @status, updated_at = @updatedAt
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", availabilityStatus);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastReconciledAtAsync(string id, string lastReconciledAt)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE folder_sources
            SET last_reconciled_at = @lastReconciledAt, updated_at = @updatedAt
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@lastReconciledAt", lastReconciledAt);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM folder_sources WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }
}
