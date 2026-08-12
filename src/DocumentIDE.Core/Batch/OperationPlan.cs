using System;
using System.Collections.Generic;
using System.Linq;

namespace DocumentIDE.Core.Batch;

public enum BatchOperationType
{
    SearchAndReplace,
    FontChange,
    PageSetup,
    Rename,
    SetFont,
    SetFontSize,
    SetMargins,
    SetPaperSize,
    SetOrientation,
    SetAlignment,
    RenameFiles
}

public enum BatchPlanStatus
{
    Draft,
    Previewed,
    Executing,
    Completed,
    Expired,
    Failed
}

public class BatchOperationPlanFile
{
    public string PlanId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string ExpectedFingerprint { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public bool Selected { get; set; } = true;
    public List<string> SelectedChangeIds { get; set; } = new();
    public string? PlanPayloadJson { get; set; }
}

public class BatchOperationPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WorkspaceId { get; set; } = string.Empty;
    public BatchOperationType OperationType { get; set; }
    public BatchPlanStatus Status { get; set; } = BatchPlanStatus.Draft;
    public string ScopeJson { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public string? IdempotencyKey { get; set; }
    public List<BatchOperationPlanFile> Files { get; set; } = new();

    public bool IsExpired(DateTimeOffset now)
    {
        return Status == BatchPlanStatus.Expired || now >= ExpiresAt;
    }

    public bool CanApply(DateTimeOffset now, out string? errorReason)
    {
        if (Status == BatchPlanStatus.Completed)
        {
            errorReason = "Plan has already been completed and cannot be applied again (idempotency violation).";
            return false;
        }

        if (Status == BatchPlanStatus.Executing)
        {
            errorReason = "Plan is currently executing.";
            return false;
        }

        if (Status == BatchPlanStatus.Failed)
        {
            errorReason = "Plan previously failed and cannot be applied.";
            return false;
        }

        if (IsExpired(now))
        {
            errorReason = "Plan has expired.";
            return false;
        }

        if (Status != BatchPlanStatus.Previewed)
        {
            errorReason = $"Plan must be in Previewed state to apply (current status: {Status}).";
            return false;
        }

        errorReason = null;
        return true;
    }

    public void MarkExecuting(DateTimeOffset now, string? idempotencyKey = null)
    {
        if (!CanApply(now, out var errorReason))
        {
            throw new InvalidOperationException(errorReason);
        }

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            IdempotencyKey = idempotencyKey;
        }

        Status = BatchPlanStatus.Executing;
    }

    public void MarkCompleted()
    {
        if (Status != BatchPlanStatus.Executing && Status != BatchPlanStatus.Previewed)
        {
            throw new InvalidOperationException($"Cannot complete plan in status '{Status}'.");
        }

        Status = BatchPlanStatus.Completed;
    }

    public void MarkFailed(string? reason = null)
    {
        Status = BatchPlanStatus.Failed;
    }

    public void MarkExpired()
    {
        Status = BatchPlanStatus.Expired;
    }

    public void ValidateApplyParameters(BatchOperationType requestedType, string requestedParametersJson, IEnumerable<string>? requestedFileIds = null)
    {
        if (requestedType != OperationType)
        {
            throw new InvalidOperationException($"Cannot apply plan with operation type '{requestedType}' because reviewed plan operation type is '{OperationType}'.");
        }

        var normalizedRequested = (requestedParametersJson ?? string.Empty).Trim();
        var normalizedReviewed = (ParametersJson ?? string.Empty).Trim();

        if (!string.Equals(normalizedRequested, normalizedReviewed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Apply parameters differ from reviewed plan parameters.");
        }

        if (requestedFileIds != null)
        {
            var selectedDocIds = Files.Where(f => f.Selected).Select(f => f.DocumentId).ToHashSet();
            foreach (var docId in requestedFileIds)
            {
                if (!selectedDocIds.Contains(docId))
                {
                    throw new InvalidOperationException($"File '{docId}' was not part of the reviewed plan or was unselected.");
                }
            }
        }
    }
}

public class OperationPlan : BatchOperationPlan
{
}
