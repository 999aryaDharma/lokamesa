using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;

namespace DocumentIDE.Infrastructure.History;

/// <summary>
/// BAT-11 / HIST-02 owner-side history persistence.
///
/// Append-only JSONL records under <c>%LOCALAPPDATA%\DocumentIDE\history</c>:
/// <list type="bullet">
/// <item><c>apply-{planId}.jsonl</c> — one line per applied file outcome (BAT-04 step 6).</item>
/// <item><c>undo-{planId}.jsonl</c> — one line per operation-level undo status (spec 02 §18).</item>
/// <item><c>git-restore-{operationId}.jsonl</c> — one line per SCM-10
/// discard/restore operation (spec 13 §15.2 step 8). This is the same
/// Document IDE History store, not a parallel one.</item>
/// </list>
/// The store is intentionally transport-agnostic: the privileged API reads these
/// records back for the owner history view, and a failed record write surfaces as
/// a typed failure instead of a silently dropped audit entry.
/// </summary>
public sealed class JsonlHistoryStore : IBatchApplyHistoryRecorder, IUndoHistoryRecorder, IGitRestoreHistoryRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonlHistoryStore(string? root = null)
    {
        _root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocumentIDE",
            "history");
        Directory.CreateDirectory(_root);
    }

    public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default) =>
        AppendAsync($"apply-{Sanitize(record.PlanId)}.jsonl", record, cancellationToken);

    public Task RecordAsync(UndoRecord record, CancellationToken cancellationToken = default) =>
        AppendAsync($"undo-{Sanitize(record.OperationId)}.jsonl", record, cancellationToken);

    /// <summary>
    /// SCM-10 — records one discard/restore operation in Document IDE History
    /// (spec 13 §15.2 step 8). The Git mutation has already completed when this
    /// is called, so a failed write is surfaced to the caller as a typed
    /// warning instead of silently dropping the audit entry.
    /// </summary>
    public Task RecordAsync(GitRestoreHistoryRecord record, CancellationToken cancellationToken = default) =>
        AppendAsync($"git-restore-{Sanitize(record.OperationId)}.jsonl", record, cancellationToken);

    /// <summary>Reads back the Document IDE History records of one SCM-10 operation.</summary>
    public async Task<IReadOnlyList<GitRestoreHistoryRecord>> GetGitRestoreRecordsAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, $"git-restore-{Sanitize(operationId)}.jsonl");
        if (!File.Exists(path)) return Array.Empty<GitRestoreHistoryRecord>();
        var records = new List<GitRestoreHistoryRecord>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<GitRestoreHistoryRecord>(line, JsonOptions);
                if (record != null) records.Add(record);
            }
            catch (JsonException)
            {
                // A torn final line (crash mid-append) is skipped rather than
                // failing the whole history read; earlier records stay readable.
            }
        }

        return records;
    }

    public async Task<IReadOnlyList<BatchApplyHistoryRecord>> GetApplyRecordsAsync(string planId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, $"apply-{Sanitize(planId)}.jsonl");
        if (!File.Exists(path)) return Array.Empty<BatchApplyHistoryRecord>();
        var records = new List<BatchApplyHistoryRecord>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<BatchApplyHistoryRecord>(line, JsonOptions);
                if (record != null) records.Add(record);
            }
            catch (JsonException)
            {
                // A torn final line (crash mid-append) is skipped rather than
                // failing the whole history read; earlier records stay readable.
            }
        }
        return records;
    }

    public async Task<IReadOnlyList<UndoRecord>> GetUndoRecordsAsync(string planId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_root, $"undo-{Sanitize(planId)}.jsonl");
        if (!File.Exists(path)) return Array.Empty<UndoRecord>();
        var records = new List<UndoRecord>();
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<UndoRecord>(line, JsonOptions);
                if (record != null) records.Add(record);
            }
            catch (JsonException)
            {
            }
        }
        return records;
    }

    private async Task AppendAsync<T>(string fileName, T record, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_root, fileName);
        var payload = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, payload, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Sanitize(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("History key cannot be empty.", nameof(id));
        return id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal)
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id)))
            : id;
    }
}
