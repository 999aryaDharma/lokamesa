using System;
using System.Collections.Generic;
using DocumentIDE.Core.Batch;

namespace DocumentIDE.Core.History;

/// <summary>
/// HIST-04 — the persisted outcome of one Document IDE operation.
///
/// The three values are deliberately narrow: an operation was applied, it was
/// undone, or it did not change the workspace (failed/blocked/canceled). The
/// per-file apply and undo outcomes carry the detail; this value is what the
/// History row's outcome chip renders.
/// </summary>
public enum OperationOutcome
{
    Applied,
    Undone,
    Failed,
}

/// <summary>One file outcome of the apply step (spec 02 §18 OperationHistory).</summary>
public sealed record OperationHistoryFileOutcome(
    string DocumentId,
    BatchApplyFileOutcome Outcome,
    string? ReasonCode,
    string? Detail);

/// <summary>One file outcome of an undo review (spec 02 §18 RecoverySnapshot).</summary>
public sealed record OperationHistoryUndoFileOutcome(
    string DocumentId,
    UndoFileOutcome Outcome,
    string? ReasonCode,
    string? Detail);

/// <summary>The persisted per-file undo review attached to an operation.</summary>
public sealed record OperationHistoryUndoView(
    UndoResultStatus Status,
    IReadOnlyList<OperationHistoryUndoFileOutcome> Files);

/// <summary>
/// HIST-04 — one persisted operation. <see cref="Kind"/> is the operation type
/// (e.g. <c>Search &amp; Replace</c>), <see cref="PlanId"/> is the reviewed plan
/// the operation applied, and <see cref="AffectedFileCount"/> is the number of
/// files the operation attempted, not a derived UI count.
/// </summary>
public sealed record OperationHistoryRecord(
    string OperationId,
    string Kind,
    string PlanId,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    OperationOutcome Outcome,
    int AffectedFileCount,
    IReadOnlyList<OperationHistoryFileOutcome> Files,
    OperationHistoryUndoView? Undo,
    string? SnapshotId);

/// <summary>
/// HIST-04 — the undo write path's record. It never invents apply file outcomes:
/// the repository keeps the original apply record's files when the operation is
/// already listed, and a legacy operation (applied before this sprint) is
/// inserted with the undo result's own file outcomes.
/// </summary>
public sealed record OperationUndoRecord(
    string OperationId,
    string PlanId,
    string Kind,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    OperationOutcome Outcome,
    int AffectedFileCount,
    IReadOnlyList<OperationHistoryFileOutcome> Files,
    OperationHistoryUndoView Undo,
    string? SnapshotId);

/// <summary>Typed page envelope: an empty history is an available empty page.</summary>
public sealed record OperationHistoryPage(
    IReadOnlyList<OperationHistoryRecord> Entries,
    int Skip,
    int Limit,
    int TotalCount)
{
    public bool HasMore => Skip + Entries.Count < TotalCount;

    public int? NextSkip => HasMore ? Skip + Entries.Count : null;
}

/// <summary>
/// HIST-04 — bounded pagination, matching the SCM history convention (default
/// 50, maximum 200). A negative skip is rejected before any read; a limit
/// outside the window is clamped, never used to request an unbounded page.
/// </summary>
public sealed record OperationHistoryQuery(int Skip = 0, int Limit = 50)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static OperationHistoryQuery Default { get; } = new();

    public static bool TryNormalize(
        int? skip,
        int? limit,
        out OperationHistoryQuery query,
        out string? errorCode,
        out string? error)
    {
        var normalizedSkip = skip ?? 0;
        if (normalizedSkip < 0)
        {
            query = Default;
            errorCode = "InvalidSkip";
            error = "The operation history skip must not be negative; the request was rejected before any read.";
            return false;
        }

        var normalizedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        query = new OperationHistoryQuery(normalizedSkip, normalizedLimit);
        errorCode = null;
        error = null;
        return true;
    }
}

/// <summary>Read-only owner-side listing of persisted operations, newest first.</summary>
public interface IOperationHistoryRepository
{
    Task<OperationHistoryPage> GetPageAsync(int skip, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Written by the existing apply/undo write paths. A recorder failure is
/// surfaced by those paths as a typed <see cref="InvalidOperationException"/>,
/// exactly like a failed per-file history write.
/// </summary>
public interface IOperationHistoryRecorder
{
    Task RecordAppliedAsync(OperationHistoryRecord record, CancellationToken cancellationToken = default);

    Task RecordUndoneAsync(OperationUndoRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Display labels for the operation types the reviewed apply path can persist.</summary>
public static class OperationKinds
{
    public static string Describe(BatchOperationType type) => type switch
    {
        BatchOperationType.SearchAndReplace => "Search & Replace",
        BatchOperationType.RenameFiles or BatchOperationType.Rename => "Batch Rename",
        BatchOperationType.FontChange or BatchOperationType.SetFont => "Font change",
        BatchOperationType.SetFontSize => "Font size change",
        BatchOperationType.PageSetup or BatchOperationType.SetMargins
            or BatchOperationType.SetPaperSize or BatchOperationType.SetOrientation => "Page setup",
        BatchOperationType.SetAlignment => "Alignment change",
        _ => type.ToString(),
    };
}
