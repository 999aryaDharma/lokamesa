using System;
using System.Collections.Generic;
using DocumentIDE.Core.Batch;
using Xunit;

namespace DocumentIDE.Core.Tests.Batch;

public class OperationPlanTests
{
    [Fact]
    public void Plan_CanBeCreatedAndRead_WithExpectedPropertiesAndFingerprints()
    {
        // Arrange
        var planId = Guid.NewGuid().ToString();
        var workspaceId = Guid.NewGuid().ToString();
        var createdAt = DateTimeOffset.UtcNow;
        var expiresAt = createdAt.AddHours(1);
        var docId1 = Guid.NewGuid().ToString();
        var docId2 = Guid.NewGuid().ToString();

        var files = new List<BatchOperationPlanFile>
        {
            new BatchOperationPlanFile
            {
                PlanId = planId,
                DocumentId = docId1,
                ExpectedFingerprint = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // SHA256
                MatchCount = 3,
                Selected = true,
                SelectedChangeIds = new List<string> { "occ-1", "occ-2", "occ-3" },
                PlanPayloadJson = "{\"location\":\"body\"}"
            },
            new BatchOperationPlanFile
            {
                PlanId = planId,
                DocumentId = docId2,
                ExpectedFingerprint = "f2ca1bb6c7e907d06dafe4687e579fce76b37e4e93b7605022da52e6ccc26fd2",
                MatchCount = 1,
                Selected = false,
                SelectedChangeIds = new List<string> { "occ-4" },
                PlanPayloadJson = null
            }
        };

        // Act
        var plan = new BatchOperationPlan
        {
            Id = planId,
            WorkspaceId = workspaceId,
            OperationType = BatchOperationType.SearchAndReplace,
            Status = BatchPlanStatus.Previewed,
            ScopeJson = "{\"scope\":\"Folder\",\"folderId\":\"f-123\"}",
            ParametersJson = "{\"find\":\"oldText\",\"replace\":\"newText\"}",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            IdempotencyKey = "idempotency-key-123",
            Files = files
        };

        // Assert
        Assert.Equal(planId, plan.Id);
        Assert.Equal(workspaceId, plan.WorkspaceId);
        Assert.Equal(BatchOperationType.SearchAndReplace, plan.OperationType);
        Assert.Equal(BatchPlanStatus.Previewed, plan.Status);
        Assert.Equal("{\"scope\":\"Folder\",\"folderId\":\"f-123\"}", plan.ScopeJson);
        Assert.Equal("{\"find\":\"oldText\",\"replace\":\"newText\"}", plan.ParametersJson);
        Assert.Equal(createdAt, plan.CreatedAt);
        Assert.Equal(expiresAt, plan.ExpiresAt);
        Assert.Equal("idempotency-key-123", plan.IdempotencyKey);
        Assert.Equal(2, plan.Files.Count);

        var file1 = plan.Files[0];
        Assert.Equal(docId1, file1.DocumentId);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", file1.ExpectedFingerprint);
        Assert.Equal(3, file1.MatchCount);
        Assert.True(file1.Selected);
        Assert.Equal(new[] { "occ-1", "occ-2", "occ-3" }, file1.SelectedChangeIds);
    }

    [Fact]
    public void Plan_IsExpired_ReturnsTrue_WhenNowPastExpiresAtOrStatusIsExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredPlan = new BatchOperationPlan
        {
            Status = BatchPlanStatus.Previewed,
            ExpiresAt = now.AddMinutes(-5)
        };

        Assert.True(expiredPlan.IsExpired(now));

        var explicitExpiredPlan = new BatchOperationPlan
        {
            Status = BatchPlanStatus.Expired,
            ExpiresAt = now.AddMinutes(30)
        };

        Assert.True(explicitExpiredPlan.IsExpired(now));

        var validPlan = new BatchOperationPlan
        {
            Status = BatchPlanStatus.Previewed,
            ExpiresAt = now.AddMinutes(30)
        };

        Assert.False(validPlan.IsExpired(now));
    }

    [Fact]
    public void CompletedPlan_CannotApplyTwice_EnforcesIdempotency()
    {
        var now = DateTimeOffset.UtcNow;
        var plan = new BatchOperationPlan
        {
            Status = BatchPlanStatus.Previewed,
            ExpiresAt = now.AddMinutes(30)
        };

        // First apply check passes
        Assert.True(plan.CanApply(now, out var reason1));
        Assert.Null(reason1);

        // Transition to Executing and Completed
        plan.MarkExecuting(now);
        Assert.Equal(BatchPlanStatus.Executing, plan.Status);

        plan.MarkCompleted();
        Assert.Equal(BatchPlanStatus.Completed, plan.Status);

        // Second apply check fails (Completed plan cannot apply twice)
        Assert.False(plan.CanApply(now, out var reason2));
        Assert.NotNull(reason2);
        Assert.Contains("completed", reason2, StringComparison.OrdinalIgnoreCase);

        // Calling MarkExecuting again throws InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() => plan.MarkExecuting(now));
        Assert.Contains("completed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_CanApply_Fails_WhenExpiredOrNotInPreviewedState()
    {
        var now = DateTimeOffset.UtcNow;

        var draftPlan = new BatchOperationPlan
        {
            Status = BatchPlanStatus.Draft,
            ExpiresAt = now.AddMinutes(30)
        };
        Assert.False(draftPlan.CanApply(now, out var draftReason));
        Assert.Contains("Previewed", draftReason);

        var expiredPlan = new BatchOperationPlan
        {
            Status = BatchPlanStatus.Previewed,
            ExpiresAt = now.AddMinutes(-1)
        };
        Assert.False(expiredPlan.CanApply(now, out var expiredReason));
        Assert.Contains("expired", expiredReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_DisallowsApplyParameters_NotExplicitlyReviewedInPreview()
    {
        var now = DateTimeOffset.UtcNow;
        var docId1 = Guid.NewGuid().ToString();
        var docId2 = Guid.NewGuid().ToString();

        var plan = new BatchOperationPlan
        {
            OperationType = BatchOperationType.SearchAndReplace,
            Status = BatchPlanStatus.Previewed,
            ParametersJson = "{\"find\":\"hello\",\"replace\":\"world\"}",
            ExpiresAt = now.AddMinutes(30),
            Files = new List<BatchOperationPlanFile>
            {
                new BatchOperationPlanFile { DocumentId = docId1, Selected = true },
                new BatchOperationPlanFile { DocumentId = docId2, Selected = false }
            }
        };

        // Matching parameters pass validation
        plan.ValidateApplyParameters(BatchOperationType.SearchAndReplace, "{\"find\":\"hello\",\"replace\":\"world\"}", new[] { docId1 });

        // Arbitrary/mismatched operation type throws
        var exType = Assert.Throws<InvalidOperationException>(() =>
            plan.ValidateApplyParameters(BatchOperationType.FontChange, "{\"find\":\"hello\",\"replace\":\"world\"}"));
        Assert.Contains("operation type", exType.Message, StringComparison.OrdinalIgnoreCase);

        // Modified payload parameters throw
        var exParams = Assert.Throws<InvalidOperationException>(() =>
            plan.ValidateApplyParameters(BatchOperationType.SearchAndReplace, "{\"find\":\"hello\",\"replace\":\"HACKED\"}"));
        Assert.Contains("parameters differ", exParams.Message, StringComparison.OrdinalIgnoreCase);

        // Unreviewed or unselected document IDs throw
        var unselectedDocId = docId2;
        var exDoc = Assert.Throws<InvalidOperationException>(() =>
            plan.ValidateApplyParameters(BatchOperationType.SearchAndReplace, "{\"find\":\"hello\",\"replace\":\"world\"}", new[] { unselectedDocId }));
        Assert.Contains("unselected", exDoc.Message, StringComparison.OrdinalIgnoreCase);
    }
}
