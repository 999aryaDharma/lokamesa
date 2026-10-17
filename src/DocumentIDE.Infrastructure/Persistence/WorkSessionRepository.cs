using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.History;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence;

/// <summary>
/// SCM-09 — SQLite persistence for work sessions and their Git commit
/// correlations (spec 13 §13/§24).
///
/// Derived metadata is stored in the application database under the local
/// application data folder; it is never written into the user's Git worktree
/// (spec 13 §23) and never duplicates Git state. Every write is parameterized,
/// so a commit id or session label cannot inject SQL.
/// </summary>
public sealed class WorkSessionRepository : IWorkSessionRepository
{
    private readonly AppDb _appDb;

    public WorkSessionRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task CreateAsync(WorkSession session, CancellationToken cancellationToken = default)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO work_sessions (id, workspace_id, repository_root, worktree_root, started_at, ended_at, label)
            VALUES (@id, @workspaceId, @repositoryRoot, @worktreeRoot, @startedAt, @endedAt, @label);
            """;
        cmd.Parameters.AddWithValue("@id", session.Id);
        cmd.Parameters.AddWithValue("@workspaceId", (object?)session.WorkspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@repositoryRoot", (object?)session.RepositoryRoot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@worktreeRoot", (object?)session.WorktreeRoot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", session.StartedAt);
        cmd.Parameters.AddWithValue("@endedAt", (object?)session.EndedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@label", (object?)session.Label ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WorkSession?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        WorkSession? session;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, workspace_id, repository_root, worktree_root, started_at, ended_at, label
                FROM work_sessions WHERE id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", sessionId);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            session = MapSession(reader);
        }

        session.ChangedDocuments.AddRange(await ReadDocumentsAsync(conn, sessionId, cancellationToken));
        session.CommitCorrelations.AddRange(await ReadCommitsAsync(conn, sessionId, cancellationToken));
        return session;
    }

    public async Task<bool> RecordChangedDocumentAsync(
        WorkSessionDocument document,
        CancellationToken cancellationToken = default)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await SessionExistsAsync(conn, document.SessionId, cancellationToken))
        {
            return false;
        }

        using var cmd = conn.CreateCommand();
        // One association per document per session: recording the same document
        // again refreshes when/where it was last seen instead of duplicating it.
        cmd.CommandText = """
            INSERT INTO work_session_documents (session_id, document_id, path, recorded_at)
            VALUES (@sessionId, @documentId, @path, @recordedAt)
            ON CONFLICT(session_id, document_id)
            DO UPDATE SET path = excluded.path, recorded_at = excluded.recorded_at;
            """;
        cmd.Parameters.AddWithValue("@sessionId", document.SessionId);
        cmd.Parameters.AddWithValue("@documentId", document.DocumentId);
        cmd.Parameters.AddWithValue("@path", (object?)document.Path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recordedAt", document.RecordedAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AttachCommitAsync(WorkSessionCommit commit, CancellationToken cancellationToken = default)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        if (!await SessionExistsAsync(conn, commit.SessionId, cancellationToken))
        {
            return false;
        }

        using var cmd = conn.CreateCommand();
        // Correlating the same commit twice keeps exactly one correlation.
        cmd.CommandText = """
            INSERT INTO work_session_commits (session_id, commit_id, short_id, summary, recorded_at)
            VALUES (@sessionId, @commitId, @shortId, @summary, @recordedAt)
            ON CONFLICT(session_id, commit_id)
            DO UPDATE SET short_id = excluded.short_id, summary = excluded.summary, recorded_at = excluded.recorded_at;
            """;
        cmd.Parameters.AddWithValue("@sessionId", commit.SessionId);
        cmd.Parameters.AddWithValue("@commitId", commit.CommitId);
        cmd.Parameters.AddWithValue("@shortId", (object?)commit.ShortId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@summary", (object?)commit.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@recordedAt", commit.RecordedAt);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        // Child rows cascade (PRAGMA foreign_keys=ON); only derived Document IDE
        // metadata is dropped, never anything inside the Git repository.
        cmd.CommandText = "DELETE FROM work_sessions WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", sessionId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task<bool> SessionExistsAsync(
        SqliteConnection conn,
        string sessionId,
        CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM work_sessions WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", sessionId);
        return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<List<WorkSessionDocument>> ReadDocumentsAsync(
        SqliteConnection conn,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var documents = new List<WorkSessionDocument>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, document_id, path, recorded_at
            FROM work_session_documents WHERE session_id = @sessionId
            ORDER BY recorded_at, rowid;
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new WorkSessionDocument
            {
                SessionId = reader.GetString(0),
                DocumentId = reader.GetString(1),
                Path = reader.IsDBNull(2) ? null : reader.GetString(2),
                RecordedAt = reader.GetString(3),
            });
        }

        return documents;
    }

    private static async Task<List<WorkSessionCommit>> ReadCommitsAsync(
        SqliteConnection conn,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var commits = new List<WorkSessionCommit>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, commit_id, short_id, summary, recorded_at
            FROM work_session_commits WHERE session_id = @sessionId
            ORDER BY recorded_at, rowid;
            """;
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            commits.Add(new WorkSessionCommit
            {
                SessionId = reader.GetString(0),
                CommitId = reader.GetString(1),
                ShortId = reader.IsDBNull(2) ? null : reader.GetString(2),
                Summary = reader.IsDBNull(3) ? null : reader.GetString(3),
                RecordedAt = reader.GetString(4),
            });
        }

        return commits;
    }

    private static WorkSession MapSession(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        WorkspaceId = reader.IsDBNull(1) ? null : reader.GetString(1),
        RepositoryRoot = reader.IsDBNull(2) ? null : reader.GetString(2),
        WorktreeRoot = reader.IsDBNull(3) ? null : reader.GetString(3),
        StartedAt = reader.GetString(4),
        EndedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
        Label = reader.IsDBNull(6) ? null : reader.GetString(6),
    };
}
