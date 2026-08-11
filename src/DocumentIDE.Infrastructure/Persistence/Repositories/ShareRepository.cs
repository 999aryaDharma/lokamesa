using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Domain.Sharing;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

public class ShareRepository : IShareRepository
{
    private readonly AppDb _appDb;

    public ShareRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task<Share?> GetByIdAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, mode, access_mode, token_hash, allow_original_download, allow_pdf_download, created_at, updated_at, expires_at FROM shares WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var share = MapShare(reader);
            await PopulateMembersAndArtifactsAsync(conn, share);
            return share;
        }

        return null;
    }

    public async Task<Share?> GetByTokenHashAsync(string tokenHash)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, mode, access_mode, token_hash, allow_original_download, allow_pdf_download, created_at, updated_at, expires_at FROM shares WHERE token_hash = @tokenHash;";
        cmd.Parameters.AddWithValue("@tokenHash", tokenHash);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var share = MapShare(reader);
            await PopulateMembersAndArtifactsAsync(conn, share);
            return share;
        }

        return null;
    }

    public async Task<IReadOnlyList<Share>> GetAllAsync()
    {
        var list = new List<Share>();
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, mode, access_mode, token_hash, allow_original_download, allow_pdf_download, created_at, updated_at, expires_at FROM shares;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapShare(reader));
        }

        foreach (var share in list)
        {
            await PopulateMembersAndArtifactsAsync(conn, share);
        }

        return list;
    }

    public async Task CreateAsync(Share share)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO shares (id, mode, access_mode, token_hash, allow_original_download, allow_pdf_download, created_at, updated_at, expires_at)
                VALUES (@id, @mode, @accessMode, @tokenHash, @allowOriginalDownload, @allowPdfDownload, @createdAt, @updatedAt, @expiresAt);
            ";
            cmd.Parameters.AddWithValue("@id", share.Id);
            cmd.Parameters.AddWithValue("@mode", share.Mode.ToString());
            cmd.Parameters.AddWithValue("@accessMode", share.AccessMode.ToString());
            cmd.Parameters.AddWithValue("@tokenHash", share.TokenHash);
            cmd.Parameters.AddWithValue("@allowOriginalDownload", share.AllowOriginalDownload ? 1 : 0);
            cmd.Parameters.AddWithValue("@allowPdfDownload", share.AllowPdfDownload ? 1 : 0);
            cmd.Parameters.AddWithValue("@createdAt", share.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", share.UpdatedAt);
            cmd.Parameters.AddWithValue("@expiresAt", (object?)share.ExpiresAt ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync();

            foreach (var member in share.Members)
            {
                using var memberCmd = conn.CreateCommand();
                memberCmd.Transaction = tx;
                memberCmd.CommandText = @"
                    INSERT INTO share_members (id, share_id, document_id, display_path, added_at)
                    VALUES (@id, @shareId, @documentId, @displayPath, @addedAt);
                ";
                memberCmd.Parameters.AddWithValue("@id", member.Id);
                memberCmd.Parameters.AddWithValue("@shareId", share.Id);
                memberCmd.Parameters.AddWithValue("@documentId", member.DocumentId);
                memberCmd.Parameters.AddWithValue("@displayPath", member.DisplayPath);
                memberCmd.Parameters.AddWithValue("@addedAt", member.AddedAt);
                await memberCmd.ExecuteNonQueryAsync();
            }

            foreach (var artifact in share.SnapshotArtifacts)
            {
                using var artifactCmd = conn.CreateCommand();
                artifactCmd.Transaction = tx;
                artifactCmd.CommandText = @"
                    INSERT INTO share_snapshot_artifacts (id, share_id, document_id, artifact_type, relative_path, content_hash, size_bytes, created_at)
                    VALUES (@id, @shareId, @documentId, @artifactType, @relativePath, @contentHash, @sizeBytes, @createdAt);
                ";
                artifactCmd.Parameters.AddWithValue("@id", artifact.Id);
                artifactCmd.Parameters.AddWithValue("@shareId", share.Id);
                artifactCmd.Parameters.AddWithValue("@documentId", artifact.DocumentId);
                artifactCmd.Parameters.AddWithValue("@artifactType", artifact.ArtifactType);
                artifactCmd.Parameters.AddWithValue("@relativePath", artifact.RelativePath);
                artifactCmd.Parameters.AddWithValue("@contentHash", artifact.ContentHash);
                artifactCmd.Parameters.AddWithValue("@sizeBytes", artifact.SizeBytes);
                artifactCmd.Parameters.AddWithValue("@createdAt", artifact.CreatedAt);
                await artifactCmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(Share share)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE shares
            SET mode = @mode,
                access_mode = @accessMode,
                token_hash = @tokenHash,
                allow_original_download = @allowOriginalDownload,
                allow_pdf_download = @allowPdfDownload,
                updated_at = @updatedAt,
                expires_at = @expiresAt
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", share.Id);
        cmd.Parameters.AddWithValue("@mode", share.Mode.ToString());
        cmd.Parameters.AddWithValue("@accessMode", share.AccessMode.ToString());
        cmd.Parameters.AddWithValue("@tokenHash", share.TokenHash);
        cmd.Parameters.AddWithValue("@allowOriginalDownload", share.AllowOriginalDownload ? 1 : 0);
        cmd.Parameters.AddWithValue("@allowPdfDownload", share.AllowPdfDownload ? 1 : 0);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@expiresAt", (object?)share.ExpiresAt ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM shares WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddMemberAsync(ShareMember member)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO share_members (id, share_id, document_id, display_path, added_at)
            VALUES (@id, @shareId, @documentId, @displayPath, @addedAt);
        ";
        cmd.Parameters.AddWithValue("@id", member.Id);
        cmd.Parameters.AddWithValue("@shareId", member.ShareId);
        cmd.Parameters.AddWithValue("@documentId", member.DocumentId);
        cmd.Parameters.AddWithValue("@displayPath", member.DisplayPath);
        cmd.Parameters.AddWithValue("@addedAt", member.AddedAt);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveMemberAsync(string shareId, string documentId)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM share_members WHERE share_id = @shareId AND document_id = @documentId;";
        cmd.Parameters.AddWithValue("@shareId", shareId);
        cmd.Parameters.AddWithValue("@documentId", documentId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ShareMember>> GetMembersAsync(string shareId)
    {
        var list = new List<ShareMember>();
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, share_id, document_id, display_path, added_at FROM share_members WHERE share_id = @shareId;";
        cmd.Parameters.AddWithValue("@shareId", shareId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapMember(reader));
        }

        return list;
    }

    public async Task AddSnapshotArtifactAsync(ShareSnapshotArtifact artifact)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO share_snapshot_artifacts (id, share_id, document_id, artifact_type, relative_path, content_hash, size_bytes, created_at)
            VALUES (@id, @shareId, @documentId, @artifactType, @relativePath, @contentHash, @sizeBytes, @createdAt);
        ";
        cmd.Parameters.AddWithValue("@id", artifact.Id);
        cmd.Parameters.AddWithValue("@shareId", artifact.ShareId);
        cmd.Parameters.AddWithValue("@documentId", artifact.DocumentId);
        cmd.Parameters.AddWithValue("@artifactType", artifact.ArtifactType);
        cmd.Parameters.AddWithValue("@relativePath", artifact.RelativePath);
        cmd.Parameters.AddWithValue("@contentHash", artifact.ContentHash);
        cmd.Parameters.AddWithValue("@sizeBytes", artifact.SizeBytes);
        cmd.Parameters.AddWithValue("@createdAt", artifact.CreatedAt);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ShareSnapshotArtifact>> GetSnapshotArtifactsAsync(string shareId)
    {
        var list = new List<ShareSnapshotArtifact>();
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, share_id, document_id, artifact_type, relative_path, content_hash, size_bytes, created_at FROM share_snapshot_artifacts WHERE share_id = @shareId;";
        cmd.Parameters.AddWithValue("@shareId", shareId);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapArtifact(reader));
        }

        return list;
    }

    private static Share MapShare(SqliteDataReader reader)
    {
        var modeStr = reader.GetString(1);
        Enum.TryParse<ShareMode>(modeStr, true, out var mode);

        var accessStr = reader.GetString(2);
        Enum.TryParse<AccessMode>(accessStr, true, out var accessMode);

        return new Share
        {
            Id = reader.GetString(0),
            Mode = mode,
            AccessMode = accessMode,
            TokenHash = reader.GetString(3),
            AllowOriginalDownload = reader.GetInt32(4) != 0,
            AllowPdfDownload = reader.GetInt32(5) != 0,
            CreatedAt = reader.GetString(6),
            UpdatedAt = reader.GetString(7),
            ExpiresAt = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }

    private static ShareMember MapMember(SqliteDataReader reader)
    {
        return new ShareMember
        {
            Id = reader.GetString(0),
            ShareId = reader.GetString(1),
            DocumentId = reader.GetString(2),
            DisplayPath = reader.GetString(3),
            AddedAt = reader.GetString(4)
        };
    }

    private static ShareSnapshotArtifact MapArtifact(SqliteDataReader reader)
    {
        return new ShareSnapshotArtifact
        {
            Id = reader.GetString(0),
            ShareId = reader.GetString(1),
            DocumentId = reader.GetString(2),
            ArtifactType = reader.GetString(3),
            RelativePath = reader.GetString(4),
            ContentHash = reader.GetString(5),
            SizeBytes = reader.GetInt64(6),
            CreatedAt = reader.GetString(7)
        };
    }

    private static async Task PopulateMembersAndArtifactsAsync(SqliteConnection conn, Share share)
    {
        using (var cmdMember = conn.CreateCommand())
        {
            cmdMember.CommandText = "SELECT id, share_id, document_id, display_path, added_at FROM share_members WHERE share_id = @shareId;";
            cmdMember.Parameters.AddWithValue("@shareId", share.Id);
            using var readerMember = await cmdMember.ExecuteReaderAsync();
            while (await readerMember.ReadAsync())
            {
                share.Members.Add(MapMember(readerMember));
            }
        }

        using (var cmdArtifact = conn.CreateCommand())
        {
            cmdArtifact.CommandText = "SELECT id, share_id, document_id, artifact_type, relative_path, content_hash, size_bytes, created_at FROM share_snapshot_artifacts WHERE share_id = @shareId;";
            cmdArtifact.Parameters.AddWithValue("@shareId", share.Id);
            using var readerArtifact = await cmdArtifact.ExecuteReaderAsync();
            while (await readerArtifact.ReadAsync())
            {
                share.SnapshotArtifacts.Add(MapArtifact(readerArtifact));
            }
        }
    }
}
