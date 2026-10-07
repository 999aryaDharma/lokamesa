using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Core.History;

public enum UndoFileOutcome
{
    Restored,
    AlreadyRestored,
    Conflict,
    Failure,
    Skipped
}

public enum UndoResultStatus
{
    Completed,
    Partial,
    Blocked,
    Canceled
}

public sealed record UndoFileResult(string DocumentId, string FilePath, UndoFileOutcome Outcome, string? ReasonCode, string? Detail);

public sealed record UndoResult(string OperationId, string? SnapshotId, UndoResultStatus Status, IReadOnlyList<UndoFileResult> Files);

/// <summary>Operation-level undone status for the history store (spec 02 §18: status + undone_at).</summary>
public sealed record UndoRecord(string OperationId, UndoResultStatus Status, DateTimeOffset RecordedUtc);

/// <summary>Persists the operation-level undo status. Called as soon as an undo changed (or confirmed) file state.</summary>
public interface IUndoHistoryRecorder
{
    Task RecordAsync(UndoRecord record, CancellationToken cancellationToken = default);
}

/// <summary>
/// HIST-02 — Conflict-Aware Undo.
/// Restores the files of a completed operation from its recovery snapshot, but only
/// when the current content still matches the recorded post-operation fingerprint.
/// Files that changed since the operation are marked as conflicts and are never
/// overwritten. Restores use the same safe-write discipline as the apply engine:
/// fingerprint comparison, per-file lock, temp copy, validation, atomic replace.
/// </summary>
public sealed class UndoService
{
    private readonly IHistoryStorage _storage;
    private readonly IDocumentRepository _documents;
    private readonly IAtomicFileReplacer _replacer;
    private readonly IExternalEditStateProbe _externalEditProbe;
    private readonly IBatchApplyReindexTrigger _reindex;
    private readonly IUndoHistoryRecorder _history;
    private readonly IDateTimeProvider _clock;

    public UndoService(
        IHistoryStorage storage,
        IDocumentRepository documents,
        IAtomicFileReplacer replacer,
        IExternalEditStateProbe externalEditProbe,
        IBatchApplyReindexTrigger reindexTrigger,
        IUndoHistoryRecorder historyRecorder,
        IDateTimeProvider? clock = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _replacer = replacer ?? throw new ArgumentNullException(nameof(replacer));
        _externalEditProbe = externalEditProbe ?? throw new ArgumentNullException(nameof(externalEditProbe));
        _reindex = reindexTrigger ?? throw new ArgumentNullException(nameof(reindexTrigger));
        _history = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
        _clock = clock ?? new SystemDateTimeProvider();
    }

    public async Task<UndoResult> UndoAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("Operation ID cannot be empty.", nameof(operationId));
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = (await _storage.GetAllMetadataAsync())
            .Where(m => string.Equals(m.OperationId, operationId, StringComparison.Ordinal))
            .OrderByDescending(m => m.CreatedUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Unknown operation '{operationId}': no recovery snapshot exists.");

        // A snapshot with no per-file records cannot be compared or restored; refusing
        // here also prevents recording a false "undone" status for such metadata.
        if (metadata.Files.Count == 0)
            throw new InvalidOperationException($"Snapshot for operation '{operationId}' has no file records; nothing can be verified or restored.");

        // Retention is 30 days: an expired snapshot is not a valid restore source.
        // Refuse before touching any file and report per-file typed outcomes.
        if (metadata.ExpiryUtc != default && metadata.ExpiryUtc < _clock.UtcNow)
        {
            var expired = metadata.Files
                .OrderBy(f => f.DocumentId, StringComparer.Ordinal)
                .ThenBy(f => f.OriginalPath, StringComparer.Ordinal)
                .Select(f => new UndoFileResult(f.DocumentId, f.OriginalPath, UndoFileOutcome.Failure, "SnapshotExpired", "Snapshot has expired; undo is only available for 30 days."))
                .ToList();
            return new UndoResult(operationId, metadata.SnapshotId, UndoResultStatus.Blocked, expired);
        }

        var ordered = metadata.Files
            .OrderBy(f => f.DocumentId, StringComparer.Ordinal)
            .ThenBy(f => f.OriginalPath, StringComparer.Ordinal)
            .ToList();

        var outcomes = new List<UndoFileResult>();
        var canceled = false;
        foreach (var record in ordered)
        {
            UndoFileResult fileResult;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileResult = await UndoFileAsync(metadata.SnapshotId, record, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Cancellation may stop before the next file, never mid-restore:
                // the replacer rolls its temp back before the exception escapes.
                canceled = true;
                break;
            }
            outcomes.Add(fileResult);
        }

        if (canceled)
        {
            var processed = outcomes.Select(o => o.DocumentId).ToHashSet(StringComparer.Ordinal);
            foreach (var record in ordered.Where(r => !processed.Contains(r.DocumentId)))
                outcomes.Add(new UndoFileResult(record.DocumentId, record.OriginalPath, UndoFileOutcome.Skipped, "Cancelled", "Undo cancelled before this file was processed."));
        }

        var restored = outcomes.Count(o => o.Outcome is UndoFileOutcome.Restored or UndoFileOutcome.AlreadyRestored);

        UndoResultStatus status;
        if (canceled)
            status = UndoResultStatus.Canceled;
        else if (outcomes.All(o => o.Outcome is UndoFileOutcome.Restored or UndoFileOutcome.AlreadyRestored))
            status = UndoResultStatus.Completed;
        else if (restored == 0)
            status = UndoResultStatus.Blocked;
        else
            status = UndoResultStatus.Partial;

        // Record undone state only when the undo actually moved files back (or confirmed
        // they already were): a fully conflicted undo changed nothing and awaits the
        // user's decision, so it must not mark the operation as undone.
        if (status is UndoResultStatus.Completed or UndoResultStatus.Partial)
            await RecordStatusAsync(operationId, status);

        return new UndoResult(operationId, metadata.SnapshotId, status, outcomes);
    }

    private async Task<UndoFileResult> UndoFileAsync(string snapshotId, SnapshotFileRecord record, CancellationToken cancellationToken)
    {
        // Identity-based resolution: the document may have been renamed/moved since the
        // operation, so the stored original path is never written to directly. An
        // unknown document fails closed instead of guessing a target path.
        if (string.IsNullOrWhiteSpace(record.DocumentId))
            return Failure(record, string.Empty, "UnknownDocument", "Snapshot file has no document identity; undo fails closed.");

        var document = await _documents.GetByIdAsync(record.DocumentId);
        if (document == null)
            return Failure(record, string.Empty, "UnknownDocument", "Document is no longer known to the workspace.");

        string path;
        try
        {
            if (!Path.IsPathFullyQualified(document.CurrentPath))
                throw new ArgumentException("Document path must be absolute.");
            path = Path.GetFullPath(document.CurrentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return Failure(record, document.CurrentPath, "InvalidPath", ex.Message);
        }

        // The process-wide per-file lock is shared with the batch apply engine, so an
        // apply and an undo targeting the same file can never interleave their
        // check→commit sequences.
        var fileLock = FileOperationLocks.GetOrAdd(path);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            return await UndoLockedAsync(snapshotId, record, path, cancellationToken);
        }
        finally
        {
            fileLock.Release();
            FileOperationLocks.EvictIfIdle(path, fileLock);
        }
    }

    private async Task<UndoFileResult> UndoLockedAsync(string snapshotId, SnapshotFileRecord record, string path, CancellationToken cancellationToken)
    {
        string current;
        try
        {
            current = await FingerprintAsync(path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Failure(record, path, "Missing", "File does not exist at the document's current path.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Failure(record, path, "Unavailable", ex.Message);
        }

        var postOperation = record.PostOperationFingerprint;
        if (string.IsNullOrEmpty(postOperation))
        {
            if (FingerprintEquals(current, record.OriginalFingerprint))
                return AlreadyRestored(record, path);
            return Conflict(record, path, "Unverifiable", "No post-operation fingerprint is recorded and the file differs from the snapshot; not overwritten.");
        }

        if (FingerprintEquals(current, postOperation))
        {
            // Mirror the apply engine's external-editor check: writing to a file that
            // may be open in Word (or whose lock state is unknowable) fails safe.
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
                return Skipped(record, path, "OpenInWord", "Document appears to be open in an external editor.");
            if (state == ExternalEditState.Unknown)
                return Skipped(record, path, "LockUnknown", "External editing state could not be determined; file skipped safely.");

            return await RestoreAsync(snapshotId, record, path, cancellationToken);
        }

        if (FingerprintEquals(current, record.OriginalFingerprint))
            return AlreadyRestored(record, path);

        return Conflict(record, path, "ExternallyModified", "File changed since the operation; newer content was not overwritten.");
    }

    private async Task<UndoFileResult> RestoreAsync(string snapshotId, SnapshotFileRecord record, string path, CancellationToken cancellationToken)
    {
        var snapshotFileName = record.SnapshotFileName;
        if (string.IsNullOrWhiteSpace(snapshotFileName) ||
            !string.Equals(Path.GetFileName(snapshotFileName), snapshotFileName, StringComparison.Ordinal))
            return Failure(record, path, "SnapshotMissing", "Snapshot copy name is missing or invalid.");

        var snapshotCopyPath = Path.Combine(_storage.GetSnapshotRootPath(snapshotId), snapshotFileName);
        if (!File.Exists(snapshotCopyPath))
            return Failure(record, path, "SnapshotMissing", "Snapshot copy no longer exists; cannot restore.");

        if (string.IsNullOrWhiteSpace(record.OriginalFingerprint))
            return Failure(record, path, "Unverifiable", "Snapshot has no original fingerprint; restore fails closed.");

        PreparedFileReplacement? prepared = null;
        try
        {
            prepared = await _replacer.PrepareAsync(
                path,
                (tempPath, ct) => CopySnapshotAsync(snapshotCopyPath, tempPath, ct),
                (tempPath, ct) => ValidateSnapshotCopyAsync(record.OriginalFingerprint, tempPath, ct),
                cancellationToken);

            // Narrow the check→replace window: the target must still hold exactly the
            // post-operation content at commit time, otherwise newer work wins.
            var beforeCommit = await FingerprintAsync(path, CancellationToken.None);
            if (!FingerprintEquals(beforeCommit, record.PostOperationFingerprint))
            {
                await _replacer.RollbackAsync(prepared, CancellationToken.None);
                return Conflict(record, path, "ExternallyModified", "File changed while the restore was prepared; newer content was not overwritten.");
            }

            await _replacer.CommitAsync(prepared, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            if (prepared != null)
                await _replacer.RollbackAsync(prepared, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            if (prepared != null)
                await _replacer.RollbackAsync(prepared, CancellationToken.None);
            return Failure(record, path, "RestoreFailed", ex.Message);
        }

        // The file is restored at this point. A reindex-trigger failure must not be
        // reported as a restore failure and must not stop the undone-status record:
        // it is surfaced as a typed warning on the Restored outcome.
        try
        {
            await _reindex.TriggerAsync(record.DocumentId, CancellationToken.None);
            return new UndoFileResult(record.DocumentId, path, UndoFileOutcome.Restored, null, null);
        }
        catch (Exception ex)
        {
            return new UndoFileResult(record.DocumentId, path, UndoFileOutcome.Restored, "ReindexFailed", ex.Message);
        }
    }

    private async Task RecordStatusAsync(string operationId, UndoResultStatus status)
    {
        try
        {
            await _history.RecordAsync(new UndoRecord(operationId, status, _clock.UtcNow), CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Undo status recording failed for operation '{operationId}'.", ex);
        }
    }

    private static async Task CopySnapshotAsync(string snapshotCopyPath, string tempPath, CancellationToken cancellationToken)
    {
        using var source = new FileStream(snapshotCopyPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var destination = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task ValidateSnapshotCopyAsync(string originalFingerprint, string tempPath, CancellationToken cancellationToken)
    {
        var fingerprint = await FingerprintAsync(tempPath, cancellationToken);
        if (!FingerprintEquals(fingerprint, originalFingerprint))
            throw new InvalidDataException("SnapshotCopyMismatch");
    }

    private static bool FingerprintEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static async Task<string> FingerprintAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedAsync(path, cancellationToken);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static UndoFileResult Failure(SnapshotFileRecord record, string path, string reasonCode, string? detail) =>
        new(record.DocumentId, path, UndoFileOutcome.Failure, reasonCode, detail);

    private static UndoFileResult Conflict(SnapshotFileRecord record, string path, string reasonCode, string? detail) =>
        new(record.DocumentId, path, UndoFileOutcome.Conflict, reasonCode, detail);

    private static UndoFileResult Skipped(SnapshotFileRecord record, string path, string reasonCode, string? detail) =>
        new(record.DocumentId, path, UndoFileOutcome.Skipped, reasonCode, detail);

    private static UndoFileResult AlreadyRestored(SnapshotFileRecord record, string path) =>
        new(record.DocumentId, path, UndoFileOutcome.AlreadyRestored, null, null);

    // Mirrors BatchPreviewService / BatchApplyEngine read semantics so undo-time
    // fingerprints stay byte-identical with preview/apply-time fingerprints.
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
}
