using System.Text.Json;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Infrastructure.Batch;

/// <summary>
/// Reviewed find/replacement parameters recovered from a persisted plan.
/// </summary>
public sealed record BatchApplyParameters(string Find, string Replacement);

/// <summary>
/// Owner-side orchestration that connects a *reviewed* batch preview plan to the
/// BAT-04 transactional apply engine. The reviewed plan is the contract: the
/// operation type must be Search &amp; Replace, the find/replacement parameters
/// come from the plan that was previewed, and each selected file is bound to the
/// on-disk document path, the preview fingerprint and the reviewed occurrence IDs
/// that the user explicitly selected.
///
/// The mutation itself is the exact (ordinal, no fuzzy/semantic/regex matching)
/// <see cref="SearchReplaceOperation"/>; staleness, external-edit state, recovery
/// snapshot and atomic replace stay owned by <see cref="BatchApplyEngine"/>.
/// </summary>
public sealed class BatchApplyCoordinator(
    IBatchPlanRepository plans,
    IDocumentRepository documents,
    BatchApplyEngine engine,
    SearchReplaceOperation operation)
{
    public async Task<BatchApplyResult> ApplyAsync(string planId, CancellationToken cancellationToken = default)
    {
        var plan = await plans.GetByIdAsync(planId) ?? throw new InvalidOperationException("UnknownPlan");
        if (plan.OperationType != BatchOperationType.SearchAndReplace)
            throw new InvalidOperationException("UnsupportedOperationType");

        var parameters = ReadParameters(plan) ?? throw new InvalidOperationException("InvalidPlanParameters");

        // Bind reviewed plan files to their current on-disk identity. The engine
        // still re-verifies existence/fingerprint/lock per file; the coordinator
        // only supplies what was reviewed, never a guess.
        var targets = new List<SearchReplaceTarget>();
        foreach (var file in plan.Files)
        {
            var document = await documents.GetByIdAsync(file.DocumentId);
            if (document == null) continue; // engine reports a typed UnknownDocument outcome for missing docs
            targets.Add(new SearchReplaceTarget(
                document.CurrentPath,
                file.ExpectedFingerprint,
                file.SelectedChangeIds?.ToArray() ?? Array.Empty<string>()));
        }

        var mutate = operation.CreateMutate(
            new SearchReplaceParameters { Find = parameters.Find, Replacement = parameters.Replacement },
            targets);

        return await engine.ApplyAsync(planId, mutate, null, cancellationToken);
    }

    private static BatchApplyParameters? ReadParameters(BatchOperationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.ParametersJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(plan.ParametersJson);
            var root = document.RootElement;
            var find = root.TryGetProperty("Find", out var findElement) ? findElement.GetString() : null;
            var replacement = root.TryGetProperty("Replacement", out var replacementElement) ? replacementElement.GetString() : null;
            if (string.IsNullOrEmpty(find) || replacement == null) return null;
            return new BatchApplyParameters(find, replacement);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
