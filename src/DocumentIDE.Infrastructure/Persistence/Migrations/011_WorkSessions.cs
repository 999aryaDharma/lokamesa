using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

/// <summary>
/// SCM-09 — work sessions and their derived Git commit correlations
/// (spec 13 §13/§24).
///
/// These tables hold disposable Document IDE metadata only. They are
/// intentionally not duplicated Git state (no index/HEAD/commit tables) and
/// they carry no foreign key into <c>workspaces</c>/<c>documents</c>: a session
/// may start before a workspace record exists and a changed document may not be
/// registered yet. Dropping these rows never makes the Git repository unusable.
/// </summary>
public sealed class Migration_011_WorkSessions : IMigration
{
    public int Version => 11;
    public string Name => "011_WorkSessions";
    public async Task UpAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var cmd = connection.CreateCommand(); cmd.Transaction = transaction;
        cmd.CommandText = """
            CREATE TABLE work_sessions (
                id TEXT PRIMARY KEY,
                workspace_id TEXT,
                repository_root TEXT,
                worktree_root TEXT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                label TEXT);
            CREATE INDEX idx_work_sessions_started ON work_sessions(started_at);
            CREATE TABLE work_session_documents (
                session_id TEXT NOT NULL,
                document_id TEXT NOT NULL,
                path TEXT,
                recorded_at TEXT NOT NULL,
                PRIMARY KEY(session_id, document_id),
                FOREIGN KEY(session_id) REFERENCES work_sessions(id) ON DELETE CASCADE);
            CREATE INDEX idx_work_session_documents_session ON work_session_documents(session_id);
            CREATE TABLE work_session_commits (
                session_id TEXT NOT NULL,
                commit_id TEXT NOT NULL,
                short_id TEXT,
                summary TEXT,
                recorded_at TEXT NOT NULL,
                PRIMARY KEY(session_id, commit_id),
                FOREIGN KEY(session_id) REFERENCES work_sessions(id) ON DELETE CASCADE);
            CREATE INDEX idx_work_session_commits_session ON work_session_commits(session_id);
            """;
        await cmd.ExecuteNonQueryAsync();
    }
}
