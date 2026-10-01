using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.History;

namespace DocumentIDE.Core.Batch;

public enum BatchApplyFileOutcome
{
    Success,
    Skipped,
    Failure
}

public enum BatchApplyResultStatus
{
    Completed,
    Blocked,
    Canceled
}

/// <summary>External editing state of a document. Unknown means certainty is unavailable; callers must fail safe.</summary>
public enum ExternalEditState
{
    NotLocked,
    Locked,
    Unknown
}

public sealed record BatchApplyFileResult(
    string DocumentId,
    string FilePath,
    BatchApplyFileOutcome Outcome,
    string? ReasonCode,
    string? Detail);

public sealed record BatchApplyResult(
    string PlanId,
    string? SnapshotId,
    BatchApplyResultStatus Status,
    IReadOnlyList<BatchApplyFileResult> Files);

public sealed record BatchApplyHistoryRecord(
    string PlanId,
    string DocumentId,
    string FilePath,
    BatchApplyFileOutcome Outcome,
    string? ReasonCode,
    string? Detail,
    string? SnapshotId,
    DateTimeOffset RecordedUtc);

/// <summary>Probes whether a document is likely open in an external editor (e.g. Microsoft Word).</summary>
public interface IExternalEditStateProbe
{
    Task<ExternalEditState> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>Transactional single-file replace: same-volume temp copy, mutate, validate, atomic replace.</summary>
public interface IAtomicFileReplacer
{
    Task<PreparedFileReplacement> PrepareAsync(
        string sourcePath,
        Func<string, CancellationToken, Task> mutate,
        Func<string, CancellationToken, Task>? validate,
        CancellationToken cancellationToken = default);

    Task CommitAsync(PreparedFileReplacement replacement, CancellationToken cancellationToken = default);

    Task RollbackAsync(PreparedFileReplacement replacement, CancellationToken cancellationToken = default);
}

public sealed record PreparedFileReplacement(string SourcePath, string TempPath);

/// <summary>Persists per-file apply outcomes for history/undo.</summary>
public interface IBatchApplyHistoryRecorder
{
    Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Triggers re-indexing of a successfully changed document.</summary>
public interface IBatchApplyReindexTrigger
{
    Task TriggerAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// BAT-04 — Transactional Safe Apply Engine.
/// Applies a reviewed operation plan: per-file lock, fingerprint equality check,
/// external-edit state check, recovery snapshot, temp mutation, validation,
/// atomic replace, and per-file success/skipped/failure records.
/// Operation-specific mutation logic is injected; this type owns only the transactional boundary.
/// </summary>
public sealed class BatchApplyEngine
{
    private readonly IBatchPlanRepository _plans;
    private readonly IDocumentRepository _documents;
    private readonly RecoverySnapshotService _snapshots;
    private readonly IExternalEditStateProbe _externalEditProbe;
    private readonly IAtomicFileReplacer _replacer;
    private readonly IBatchApplyHistoryRecorder _history;
    private readonly IBatchApplyReindexTrigger _reindex;

    // Step 1: process-wide document-level application lock, serializing applies per file.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

    public BatchApplyEngine(
        IBatchPlanRepository plans,
        IDocumentRepository documents,
        RecoverySnapshotService snapshots,
        IExternalEditStateProbe externalEditProbe,
        IAtomicFileReplacer replacer,
        IBatchApplyHistoryRecorder historyRecorder,
        IBatchApplyReindexTrigger reindexTrigger)
    {
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
        _externalEditProbe = externalEditProbe ?? throw new ArgumentNullException(nameof(externalEditProbe));
        _replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
        _history = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
        _reindex = reindexTrigger ?? throw new ArgumentNullException(nameof(reindexTrigger));
    }

    public async Task<BatchApplyResult> ApplyAsync(
        string planId,
        Func<string, CancellationToken, Task> mutate,
        Func<string, CancellationToken, Task>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = await _plans.GetByIdAsync(planId) ?? throw new InvalidOperationException("UnknownPlan");
        var now = DateTimeOffset.UtcNow;
        if (!plan.CanApply(now, out var reason))
            throw new InvalidOperationException(reason);

        plan.MarkExecuting(now);
        await _plans.UpdateStatusAsync(plan.Id, BatchPlanStatus.Executing);

        var outcomes = new List<BatchApplyFileResult>();
        var heldLocks = new List<SemaphoreSlim>();
        string? snapshotId = null;
        var canceled = false;
        var selected = SelectedFiles(plan).ToArray();
        var ready = new List<ReadyFile>();

        try
        {
            // Steps 1-3: per-file lock, fingerprint equality check, external-edit state check.
            foreach (var file in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var check = await CheckFileAsync(file, heldLocks, cancellationToken);
                if (check.Outcome != null)
                {
                    // Step 6: record per-file success/skipped/failure.
                    await RecordOutcomeAsync(plan, check.Outcome, null, cancellationToken);
                    outcomes.Add(check.Outcome);
                }
                else
                {
                    ready.Add(check.Ready!);
                }
            }

            // Step 4: recovery snapshot before any destructive change. If recovery
            // cannot be created, mutation is blocked (fail closed).
            if (ready.Count > 0)
            {
                try
                {
                    snapshotId = (await _snapshots.CreateSnapshotAsync(plan.Id, ready.Select(r => r.Path))).SnapshotId;
                }
                catch (Exception ex)
                {
                    foreach (var entry in ready)
                    {
                        var blocked = new BatchApplyFileResult(entry.File.DocumentId, entry.Path, BatchApplyFileOutcome.Failure, "RecoveryBlocked", ex.Message);
                        await RecordOutcomeAsync(plan, blocked, null, cancellationToken);
                        outcomes.Add(blocked);
                    }

                    plan.MarkFailed();
                    await _plans.UpdateStatusAsync(plan.Id, BatchPlanStatus.Failed);
                    return new(plan.Id, null, BatchApplyResultStatus.Blocked, outcomes);
                }
            }

            // Step 5: per-file temp mutation, validation, atomic replace.
            // Cancellation may stop before the next file, but never aborts halfway
            // through writing a single file: mutation happens on a temp copy and the
            // commit is an atomic replace.
            var pending = new List<ReadyFile>(ready);
            foreach (var entry in ready)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pending.Remove(entry);
                PreparedFileReplacement? prepared = null;
                try
                {
                    prepared = await _replacer.PrepareAsync(entry.Path, mutate, validate, cancellationToken);
                    await _replacer.CommitAsync(prepared, cancellationToken);
                    var success = new BatchApplyFileResult(entry.File.DocumentId, entry.Path, BatchApplyFileOutcome.Success, null, null);
                    await RecordOutcomeAsync(plan, success, snapshotId, cancellationToken);
                    await TriggerReindexAsync(entry.File.DocumentId, cancellationToken);
                    outcomes.Add(success);
                }
                catch (OperationCanceledException)
                {
                    if (prepared != null)
                        await _replacer.RollbackAsync(prepared, CancellationToken.None);
                    var cancelled = new BatchApplyFileResult(entry.File.DocumentId, entry.Path, BatchApplyFileOutcome.Failure, "Cancelled", "Apply cancelled before commit; original unchanged.");
                    await RecordOutcomeAsync(plan, cancelled, snapshotId, cancellationToken);
                    outcomes.Add(cancelled);
                    canceled = true;
                    break;
                }
                catch (Exception ex)
                {
                    if (prepared != null)
                        await _replacer.RollbackAsync(prepared, CancellationToken.None);
                    outcomes.Add(new(entry.File.DocumentId, entry.Path, BatchApplyFileOutcome.Failure, "MutationFailed", ex.Message));
                }
                finally
                {
                    entry.Lock.Release();
                    heldLocks.Remove(entry.Lock);
                }
            }

            if (canceled)
            {
                foreach (var entry in pending)
                {
                    var skipped = new BatchApplyFileResult(entry.File.DocumentId, entry.Path, BatchApplyFileOutcome.Skipped, "Cancelled", "Apply cancelled before this file was attempted.");
                    await RecordOutcomeAsync(plan, skipped, snapshotId, cancellationToken);
                    outcomes.Add(skipped);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation during the check phase: record not-yet-processed files.
            canceled = true;
            var processedIds = outcomes.Select(o => o.DocumentId).ToHashSet(StringComparer.Ordinal);
            foreach (var file in selected.Where(f => !processedIds.Contains(f.DocumentId)))
            {
                var path = ready.FirstOrDefault(r => r.File.DocumentId == file.DocumentId)?.Path ?? string.Empty;
                var skipped = new BatchApplyFileResult(file.DocumentId, path, BatchApplyFileOutcome.Skipped, "Cancelled", "Apply cancelled before this file was processed.");
                await RecordOutcomeAsync(plan, skipped, snapshotId, cancellationToken);
                outcomes.Add(skipped);
            }
        }
        finally
        {
            foreach (var held in heldLocks)
                held.Release();
        }

        if (canceled)
        {
            plan.MarkFailed();
            await _plans.UpdateStatusAsync(plan.Id, BatchPlanStatus.Failed);
            return new(plan.Id, snapshotId, BatchApplyResultStatus.Canceled, outcomes);
        }

        plan.MarkCompleted();
        await _plans.UpdateStatusAsync(plan.Id, BatchPlanStatus.Completed);
        return new(plan.Id, snapshotId, BatchApplyResultStatus.Completed, outcomes);
    }

    private async Task<FileCheckResult> CheckFileAsync(BatchOperationPlanFile file, List<SemaphoreSlim> heldLocks, CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdAsync(file.DocumentId);
        if (document == null)
            return Failure(file.DocumentId, string.Empty, "UnknownDocument", "Document is no longer known to the workspace.");

        string path;
        try
        {
            if (!Path.IsPathFullyQualified(document.CurrentPath))
                throw new ArgumentException("Document path must be absolute.");
            path = Path.GetFullPath(document.CurrentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Failure(file.DocumentId, document.CurrentPath, "InvalidPath", ex.Message);
        }

        // Step 1: document-level application lock for this file.
        var fileLock = _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken);
        heldLocks.Add(fileLock);

        // Step 2: current fingerprint must equal the preview fingerprint.
        string fingerprint;
        try
        {
            var bytes = await ReadBoundedAsync(path, cancellationToken);
            fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ReleaseLock(fileLock, heldLocks);
            return Failure(file.DocumentId, path, "Unavailable", ex.Message);
        }

        if (!string.Equals(fingerprint, file.ExpectedFingerprint, StringComparison.Ordinal))
        {
            ReleaseLock(fileLock, heldLocks);
            return Skipped(file.DocumentId, path, "Stale", "File changed after preview; re-preview required.");
        }

        // Step 3: external Word/editing state check. Unknown certainty fails safe (skip).
        ExternalEditState state;
        try
        {
            state = await _externalEditProbe.ProbeAsync(path, cancellationToken);
        }
        catch
        {
            state = ExternalEditState.Unknown;
        }

        if (state == ExternalEditState.Locked)
        {
            ReleaseLock(fileLock, heldLocks);
            return Skipped(file.DocumentId, path, "OpenInWord", "Document appears to be open in an external editor.");
        }

        if (state == ExternalEditState.Unknown)
        {
            ReleaseLock(fileLock, heldLocks);
            return Skipped(file.DocumentId, path, "LockUnknown", "External editing state could not be determined; file skipped safely.");
        }

        return new FileCheckResult(null, new ReadyFile(file, path, fileLock));
    }

    private static void ReleaseLock(SemaphoreSlim fileLock, List<SemaphoreSlim> heldLocks)
    {
        fileLock.Release();
        heldLocks.Remove(fileLock);
    }

    private async Task RecordOutcomeAsync(BatchOperationPlan plan, BatchApplyFileResult fileResult, string? snapshotId, CancellationToken cancellationToken)
    {
        try
        {
            await _history.RecordAsync(new BatchApplyHistoryRecord(
                plan.Id,
                fileResult.DocumentId,
                fileResult.FilePath,
                fileResult.Outcome,
                fileResult.ReasonCode,
                fileResult.Detail,
                snapshotId,
                DateTimeOffset.UtcNow), cancellationToken);
        }
        catch (Exception ex)
        {
            // History recording must never fail silently; surface as a typed failure.
            throw new InvalidOperationException($"History record failed for document '{fileResult.DocumentId}'.", ex);
        }
    }

    private async Task TriggerReindexAsync(string documentId, CancellationToken cancellationToken)
    {
        try
        {
            await _reindex.TriggerAsync(documentId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Reindex trigger failed for document '{documentId}'.", ex);
        }
    }

    private static FileCheckResult Failure(string documentId, string path, string reasonCode, string? detail) =>
        new(new BatchApplyFileResult(documentId, path, BatchApplyFileOutcome.Failure, reasonCode, detail), null);

    private static FileCheckResult Skipped(string documentId, string path, string reasonCode, string? detail) =>
        new(new BatchApplyFileResult(documentId, path, BatchApplyFileOutcome.Skipped, reasonCode, detail), null);

    private readonly record struct FileCheckResult(BatchApplyFileResult? Outcome, ReadyFile? Ready);

    private sealed record ReadyFile(BatchOperationPlanFile File, string Path, SemaphoreSlim Lock);

    // Mirrors BatchPreviewService.ReadBoundedAsync so apply-time fingerprints match preview-time fingerprints.
    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        for (string? component = path; component != null; component = Path.GetDirectoryName(component))
            if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) throw new IOException("ReparsePoint");
        var limit = Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) ? 100 * 1024 * 1024 : 10 * 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (stream.Length > limit) throw new InvalidDataException("FileTooLarge");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        if (stream.ReadByte() != -1) throw new IOException("SourceChanged");
        return bytes;
    }

    private static IEnumerable<BatchOperationPlanFile> SelectedFiles(BatchOperationPlan plan) =>
        plan.Files.Where(f => f.Selected && !string.IsNullOrEmpty(f.ExpectedFingerprint))
            .OrderBy(f => f.DocumentId, StringComparer.Ordinal);
}
