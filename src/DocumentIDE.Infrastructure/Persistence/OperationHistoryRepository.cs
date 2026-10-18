using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.History;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence;

/// <summary>
/// HIST-04 — SQLite persistence for Document IDE operation history
/// (spec 02 §18 OperationHistory).
///
/// One row per applied operation, keyed by the reviewed plan/operation id. The
/// apply path inserts it; the undo path updates the same row with the per-file
/// undo review instead of creating a second row. All writes are parameterized;
/// the per-file documents are bounded JSON produced from the existing result
/// contracts, never from client input.
/// </summary>
public sealed class OperationHistoryRepository : IOperationHistoryRepository, IOperationHistoryRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppDb _appDb;

    public OperationHistoryRepository(AppDb appDb)
    {
        _appDb = appDb ?? throw new ArgumentNullException(nameof(appDb));
    }

    public async Task RecordAppliedAsync(OperationHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.OperationId))
            throw new ArgumentException("Operation id cannot be empty.", nameof(record));

        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        // One operation id => one row. A repeated apply is already rejected by the
        // reviewed plan's status before this point (spec 07 §25); the upsert keeps
        // that guarantee at the persistence boundary too.
        cmd.CommandText = """
            INSERT INTO operation_history
                (id, workspace_id, plan_id, kind, outcome, snapshot_id, started_at,
                 completed_at, affected_file_count, files_json, undo_json, recorded_at)
            VALUES
                (@id, NULL, @planId, @kind, @outcome, @snapshotId, @startedAt,
                 @completedAt, @affectedFileCount, @filesJson, @undoJson, @recordedAt)
            ON CONFLICT(id) DO UPDATE SET
                plan_id = excluded.plan_id,
                kind = excluded.kind,
                outcome = excluded.outcome,
                snapshot_id = excluded.snapshot_id,
                completed_at = excluded.completed_at,
                affected_file_count = excluded.affected_file_count,
                files_json = excluded.files_json,
                undo_json = excluded.undo_json,
                recorded_at = excluded.recorded_at;
            """;
        AddParameters(cmd, record);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordUndoneAsync(OperationUndoRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.OperationId))
            throw new ArgumentException("Operation id cannot be empty.", nameof(record));

        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        using var cmd = conn.CreateCommand();
        // A blocked/cancelled undo did not revert the operation: it keeps the
        // original Applied/Failed outcome and only records the review. A successful
        // undo moves exactly the same row to Undone; it never creates a duplicate.
        cmd.CommandText = """
            INSERT INTO operation_history
                (id, workspace_id, plan_id, kind, outcome, snapshot_id, started_at,
                 completed_at, affected_file_count, files_json, undo_json, recorded_at)
            VALUES
                (@id, NULL, @planId, @kind, @outcome, @snapshotId, @startedAt,
                 @completedAt, @affectedFileCount, @filesJson, @undoJson, @recordedAt)
            ON CONFLICT(id) DO UPDATE SET
                outcome = CASE
                    WHEN excluded.outcome = 'Undone' THEN 'Undone'
                    ELSE operation_history.outcome
                END,
                undo_json = excluded.undo_json,
                snapshot_id = COALESCE(operation_history.snapshot_id, excluded.snapshot_id);
            """;
        AddParameters(cmd, new OperationHistoryRecord(
            record.OperationId,
            record.Kind,
            record.PlanId,
            record.StartedUtc,
            record.CompletedUtc,
            record.Outcome,
            record.AffectedFileCount,
            record.Files,
            record.Undo,
            record.SnapshotId));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<OperationHistoryPage> GetPageAsync(int skip, int limit, CancellationToken cancellationToken = default)
    {
        // The controller rejects a negative skip before any read; clamping here is a
        // second boundary so no caller can ask for an unbounded page.
        var boundedSkip = Math.Max(0, skip);
        var boundedLimit = Math.Clamp(limit, 1, OperationHistoryQuery.MaxLimit);

        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync(cancellationToken);

        int totalCount;
        using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM operation_history;";
            totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }

        var entries = new List<OperationHistoryRecord>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, plan_id, kind, outcome, snapshot_id, started_at, completed_at,
                       affected_file_count, files_json, undo_json
                FROM operation_history
                ORDER BY started_at DESC, rowid DESC
                LIMIT @limit OFFSET @skip;
                """;
            cmd.Parameters.AddWithValue("@limit", boundedLimit);
            cmd.Parameters.AddWithValue("@skip", boundedSkip);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add(Map(reader));
            }
        }

        return new OperationHistoryPage(entries, boundedSkip, boundedLimit, totalCount);
    }

    private static void AddParameters(SqliteCommand cmd, OperationHistoryRecord record)
    {
        cmd.Parameters.AddWithValue("@id", record.OperationId);
        cmd.Parameters.AddWithValue("@planId", record.PlanId);
        cmd.Parameters.AddWithValue("@kind", record.Kind);
        cmd.Parameters.AddWithValue("@outcome", record.Outcome.ToString());
        cmd.Parameters.AddWithValue("@snapshotId", (object?)record.SnapshotId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", record.StartedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@affectedFileCount", record.AffectedFileCount);
        cmd.Parameters.AddWithValue("@filesJson", JsonSerializer.Serialize(record.Files, JsonOptions));
        cmd.Parameters.AddWithValue(
            "@undoJson",
            record.Undo is null ? DBNull.Value : JsonSerializer.Serialize(record.Undo, JsonOptions));
        cmd.Parameters.AddWithValue("@recordedAt", record.CompletedUtc.ToString("O"));
    }

    private static OperationHistoryRecord Map(SqliteDataReader reader)
    {
        var filesJson = reader.GetString(8);
        var undoJson = reader.IsDBNull(9) ? null : reader.GetString(9);

        return new OperationHistoryRecord(
            OperationId: reader.GetString(0),
            Kind: reader.GetString(2),
            PlanId: reader.GetString(1),
            StartedUtc: DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
            CompletedUtc: DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
            Outcome: Enum.Parse<OperationOutcome>(reader.GetString(3), ignoreCase: false),
            AffectedFileCount: reader.GetInt32(7),
            Files: JsonSerializer.Deserialize<OperationHistoryFileOutcome[]>(filesJson, JsonOptions)
                ?? Array.Empty<OperationHistoryFileOutcome>(),
            Undo: undoJson is null
                ? null
                : JsonSerializer.Deserialize<OperationHistoryUndoView>(undoJson, JsonOptions),
            SnapshotId: reader.IsDBNull(4) ? null : reader.GetString(4));
    }
}
