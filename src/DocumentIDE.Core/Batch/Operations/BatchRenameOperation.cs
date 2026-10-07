using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Core.Batch.Operations;

/// <summary>
/// BAT-10 — Strict Batch Rename.
/// A reviewed rename target: the document identity, its reviewed on-disk path
/// and fingerprint, and the new basename to apply.
/// Rename accepts <c>NewBaseName</c> only — never an arbitrary path (spec 07
/// §29, master §101.5); physical movement is a separate operation.
/// </summary>
public sealed record BatchRenameTarget(
    string DocumentId,
    string FilePath,
    string ExpectedFingerprint,
    string NewBaseName);

/// <summary>Deterministic old/new basename preview for one reviewed file.</summary>
public sealed record BatchRenamePreviewFile(
    string DocumentId,
    string FilePath,
    string OldBaseName,
    string NewBaseName,
    string? FailureCode);

/// <summary>Typed per-file rename outcome, inspectable by callers and UI.</summary>
public sealed record BatchRenameFileResult(
    string DocumentId,
    string FilePath,
    string? TargetPath,
    BatchApplyFileOutcome Outcome,
    string? ReasonCode,
    string? Detail);

/// <summary>Typed batch rename result; failures are never silent.</summary>
public sealed record BatchRenameResult(
    BatchApplyResultStatus Status,
    IReadOnlyList<BatchRenameFileResult> Files);

/// <summary>
/// BAT-10 operation plug-in: builds old/new basename previews, validates every
/// target with the FS-07 basename rules, detects collisions across the batch
/// before changing the first file, and applies approved renames through the
/// FS-07 physical operation service (which journals move intents for watcher
/// correlation), preserving the DocumentId via the repository.
/// </summary>
public sealed class BatchRenameOperation
{
    /// <summary>
    /// Conservative target-path ceiling (classic Windows MAX_PATH). Spec 07 §21
    /// requires path length validation before a rename is applied.
    /// </summary>
    internal const int MaxTargetPathLength = 260;

    private readonly IPhysicalFileOperations _physicalOperations;
    private readonly IDocumentRepository _documents;

    public BatchRenameOperation(IPhysicalFileOperations physicalOperations, IDocumentRepository documents)
    {
        _physicalOperations = physicalOperations ?? throw new ArgumentNullException(nameof(physicalOperations));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    /// <summary>
    /// Step 1: deterministic old/new basename preview for every reviewed file
    /// (DocumentId ordinal order), with FS-07 basename-rule failures and batch
    /// collisions surfaced per file instead of thrown. Never mutates the
    /// filesystem.
    /// </summary>
    public IReadOnlyList<BatchRenamePreviewFile> BuildPreview(IReadOnlyList<BatchRenameTarget> reviewedTargets)
    {
        ArgumentNullException.ThrowIfNull(reviewedTargets);

        return ValidateTargets(reviewedTargets)
            .Select(v => new BatchRenamePreviewFile(
                v.Target.DocumentId,
                v.Target.FilePath,
                Path.GetFileName(v.Target.FilePath ?? string.Empty),
                v.Target.NewBaseName,
                v.FailureCode))
            .ToList();
    }

    public async Task<BatchRenameResult> ApplyAsync(
        IReadOnlyList<BatchRenameTarget> reviewedTargets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewedTargets);

        // Steps 2-3: validate every target (FS-07 basename rules) and detect
        // collisions across the batch before changing the first file.
        var validations = ValidateTargets(reviewedTargets);

        if (validations.Any(v => v.FailureCode != null))
        {
            var blocked = validations
                .Select(v => v.FailureCode != null
                    ? new BatchRenameFileResult(v.Target.DocumentId, v.Target.FilePath, null, BatchApplyFileOutcome.Failure, v.FailureCode, v.Detail)
                    : new BatchRenameFileResult(v.Target.DocumentId, v.Target.FilePath, null, BatchApplyFileOutcome.Skipped, "BatchBlocked", "Not applied because the reviewed batch failed validation."))
                .ToList();
            return new BatchRenameResult(BatchApplyResultStatus.Blocked, blocked);
        }

        var results = new List<BatchRenameFileResult>(validations.Count);
        var canceled = false;
        try
        {
            foreach (var validation in validations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await RenameAsync(validation, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is typed, never silent: files already renamed keep
            // their recorded outcomes; everything not attempted is skipped
            // explicitly (spec 07 §26).
            canceled = true;
            var processed = results.Select(r => r.DocumentId).ToHashSet(StringComparer.Ordinal);
            foreach (var validation in validations.Where(v => !processed.Contains(v.Target.DocumentId)))
            {
                results.Add(new BatchRenameFileResult(validation.Target.DocumentId, validation.Target.FilePath, null, BatchApplyFileOutcome.Skipped, "Cancelled", "Apply cancelled before this file was attempted."));
            }
        }

        return new BatchRenameResult(canceled ? BatchApplyResultStatus.Canceled : BatchApplyResultStatus.Completed, results);
    }

    /// <summary>
    /// Step 4: one file is renamed only through the FS-07 physical operation
    /// service (basename-validated, collision-checked, move-intent journaled),
    /// after verifying the document path and content fingerprint still match
    /// the reviewed plan. Failures are converted into typed per-file outcomes.
    /// </summary>
    private async Task<BatchRenameFileResult> RenameAsync(TargetValidation validation, CancellationToken cancellationToken)
    {
        var target = validation.Target;
        var sourcePath = validation.FullSourcePath!;

        var document = await _documents.GetByIdAsync(target.DocumentId);
        if (document == null)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "UnknownDocument", "Document is no longer known to the workspace.");
        }

        // Stale path: the reviewed path must still identify the document's file.
        string documentPath;
        try
        {
            if (!Path.IsPathFullyQualified(document.CurrentPath))
                throw new ArgumentException("Document path must be absolute.");
            documentPath = Path.GetFullPath(document.CurrentPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "InvalidPath", ex.Message);
        }

        if (!string.Equals(documentPath, sourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Skipped, "Stale", "Document path changed after preview; re-preview required.");
        }

        // Participate in the process-wide per-file operation lock shared with
        // batch apply and conflict-aware undo (the BatchApplyEngine discipline),
        // so check → rename → repository update cannot interleave with another
        // component mutating the same document.
        var fileLock = FileOperationLocks.GetOrAdd(sourcePath);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            return await RenameLockedAsync(validation, document, documentPath, cancellationToken);
        }
        finally
        {
            fileLock.Release();
            FileOperationLocks.EvictIfIdle(sourcePath, fileLock);
        }
    }

    private async Task<BatchRenameFileResult> RenameLockedAsync(
        TargetValidation validation,
        Document document,
        string documentPath,
        CancellationToken cancellationToken)
    {
        var target = validation.Target;
        var sourcePath = validation.FullSourcePath!;

        // A reviewed file without a preview fingerprint cannot be verified → typed skip.
        if (string.IsNullOrEmpty(target.ExpectedFingerprint))
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Skipped, "Unavailable", "Plan file has no preview fingerprint; skipped.");
        }

        // Stale content: the current bytes must equal the reviewed fingerprint.
        string fingerprint;
        try
        {
            var bytes = await SearchReplaceOperation.ReadBoundedAsync(documentPath, cancellationToken);
            fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "Unavailable", ex.Message);
        }

        if (!string.Equals(fingerprint, target.ExpectedFingerprint, StringComparison.Ordinal))
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Skipped, "Stale", "File changed after preview; re-preview required.");
        }

        try
        {
            var operation = await _physicalOperations.RenameItemAsync(sourcePath, target.NewBaseName, cancellationToken);
            if (!operation.Success)
            {
                return new BatchRenameFileResult(target.DocumentId, sourcePath, operation.TargetPath, BatchApplyFileOutcome.Failure, "RenameFailed", $"{operation.ErrorCode}: {operation.ErrorMessage}");
            }

            // Step 5: keep the DocumentId and update only the path-carrying
            // fields; all other metadata (content fingerprint, size, canvas /
            // collection references keyed by DocumentId, ...) is preserved.
            document.LastKnownPath = document.CurrentPath;
            document.CurrentPath = operation.TargetPath;
            document.Filename = Path.GetFileName(operation.TargetPath);
            document.Extension = Path.GetExtension(operation.TargetPath);
            document.UpdatedAt = DateTime.UtcNow.ToString("O");

            try
            {
                await _documents.UpdateAsync(document);
            }
            catch (Exception ex)
            {
                // The file was renamed but the workspace index could not be
                // updated; report it honestly instead of claiming success.
                return new BatchRenameFileResult(target.DocumentId, sourcePath, operation.TargetPath, BatchApplyFileOutcome.Failure, "MetadataUpdateFailed", $"File was renamed to '{operation.TargetPath}' but the document record could not be updated: {ex.Message}");
            }

            return new BatchRenameFileResult(target.DocumentId, sourcePath, operation.TargetPath, BatchApplyFileOutcome.Success, null, null);
        }
        catch (InvalidPathException ex)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "InvalidBaseName", ex.Message);
        }
        catch (PathCollisionException ex)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, ex.TargetPath, BatchApplyFileOutcome.Failure, "TargetExists", ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "SourceMissing", ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "SourceMissing", ex.Message);
        }
        catch (IOException ex)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "RenameFailed", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new BatchRenameFileResult(target.DocumentId, sourcePath, null, BatchApplyFileOutcome.Failure, "RenameFailed", ex.Message);
        }
    }

    private static List<TargetValidation> ValidateTargets(IReadOnlyList<BatchRenameTarget> targets)
    {
        var ordered = targets.OrderBy(t => t.DocumentId, StringComparer.Ordinal).ToList();
        var validations = new List<TargetValidation>(ordered.Count);

        foreach (var target in ordered)
        {
            string? failureCode = null;
            string? detail = null;
            string? fullSourcePath = null;
            string? fullTargetPath = null;

            if (string.IsNullOrWhiteSpace(target.DocumentId))
            {
                failureCode = "InvalidTarget";
                detail = "DocumentId is required.";
            }
            else if (string.IsNullOrWhiteSpace(target.FilePath) || !Path.IsPathFullyQualified(target.FilePath))
            {
                failureCode = "InvalidPath";
                detail = "Reviewed file path must be absolute.";
            }
            else
            {
                try
                {
                    fullSourcePath = Path.GetFullPath(target.FilePath);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    failureCode = "InvalidPath";
                    detail = ex.Message;
                }
            }

            if (failureCode == null && !PathSafetyValidator.IsValidBasename(target.NewBaseName, out var nameError))
            {
                failureCode = "InvalidBaseName";
                detail = nameError;
            }

            if (failureCode == null)
            {
                var parentDirectory = Path.GetDirectoryName(fullSourcePath);
                if (string.IsNullOrEmpty(parentDirectory))
                {
                    failureCode = "InvalidPath";
                    detail = "Cannot determine the parent directory of the reviewed file.";
                }
                else
                {
                    fullTargetPath = Path.GetFullPath(Path.Combine(parentDirectory, target.NewBaseName));
                    if (fullTargetPath.Length > MaxTargetPathLength)
                    {
                        failureCode = "PathTooLong";
                        detail = $"Target path exceeds the {MaxTargetPathLength} character limit.";
                    }
                }
            }

            validations.Add(new TargetValidation(target, fullSourcePath, fullTargetPath, failureCode, detail));
        }

        // Internal batch target collisions: two reviewed files mapping to the
        // same target path (Windows paths compare case-insensitively).
        foreach (var group in validations
            .Select((validation, index) => (Validation: validation, Index: index))
            .Where(x => x.Validation.FailureCode == null)
            .GroupBy(x => x.Validation.FullTargetPath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            foreach (var (validation, index) in group)
            {
                validations[index] = validation with
                {
                    FailureCode = "DuplicateTarget",
                    Detail = $"Another reviewed file targets the same path '{validation.FullTargetPath}'."
                };
            }
        }

        // Existing destinations fail closed before the first file changes;
        // a case-only rename of the same file is not a collision.
        for (var i = 0; i < validations.Count; i++)
        {
            var validation = validations[i];
            if (validation.FailureCode != null) continue;
            if (string.Equals(validation.FullTargetPath, validation.FullSourcePath, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(validation.FullTargetPath) || Directory.Exists(validation.FullTargetPath))
            {
                validations[i] = validation with
                {
                    FailureCode = "TargetExists",
                    Detail = $"Target path already exists: '{validation.FullTargetPath}'."
                };
            }
        }

        return validations;
    }

    private sealed record TargetValidation(
        BatchRenameTarget Target,
        string? FullSourcePath,
        string? FullTargetPath,
        string? FailureCode,
        string? Detail);
}
