using System.Text.Json;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Jobs;
using DocumentIDE.Infrastructure.Jobs;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;

namespace DocumentIDE.Infrastructure.Batch;

public sealed record BatchPreviewState(BatchPreviewScope Scope, string? JobId);

public sealed class BatchPreviewCoordinator(AppDb db, LocalJobScheduler scheduler, BatchPreviewService scanner)
{
    private readonly BatchPlanRepository _plans = new(db);

    public async Task<BatchScopeOptions?> GetScopeOptionsAsync(string workspaceId)
    {
        if (await new WorkspaceRepository(db).GetByIdAsync(workspaceId) == null) return null;
        var scope = await ResolveAsync(new() { WorkspaceId = workspaceId, Type = BatchScopeType.Workspace });
        return new(scope.Documents.Select(d => new BatchDocumentOption(d.Id, Path.GetFileName(d.CurrentPath))).ToArray(),
            await new BatchCollectionReader(db).GetOptionsAsync(workspaceId));
    }

    public async Task<(string PlanId, string JobId)> StartAsync(BatchPreviewRequest request)
    {
        Validate(request);
        if (await new WorkspaceRepository(db).GetByIdAsync(request.Scope.WorkspaceId) == null) throw new ArgumentException("UnknownWorkspace");
        var scope = await ResolveAsync(request.Scope);
        if (scope.Documents.Count > 1000) throw new ArgumentException("ScopeLimit");
        var tree = EnumerateSubfolders(request.Scope);
        var previewScope = new BatchPreviewScope {
            WorkspaceId = request.Scope.WorkspaceId, Type = request.Scope.Type, DocumentIds = request.Scope.DocumentIds.ToArray(),
            FolderPath = request.Scope.FolderPath, IncludeSubfolders = request.Scope.IncludeSubfolders,
            ExcludedBranches = request.Scope.ExcludedBranches.ToArray(), CollectionId = request.Scope.CollectionId,
            IncludedSubfolders = tree, Failures = scope.Failures.Select(f => new BatchPreviewScopeFailure(f.DocumentId, f.Reason.ToString())).ToArray() };
        var plan = new BatchOperationPlan { WorkspaceId = request.Scope.WorkspaceId,
            ScopeJson = JsonSerializer.Serialize(new BatchPreviewState(previewScope, null)),
            ParametersJson = JsonSerializer.Serialize(new { request.Find, request.Replacement }) };
        await _plans.SavePlanAsync(plan);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var job = await scheduler.EnqueueJobAsync("BatchPreview", plan.Id, null, new DelegateJob(async context =>
            {
                await ready.Task;
                await scanner.BuildAsync(plan.Id, request, scope, context.CancellationToken, progress => context.ReportProgress(progress));
            }));
            plan.ScopeJson = JsonSerializer.Serialize(new BatchPreviewState(previewScope, job.JobId));
            await _plans.SavePlanAsync(plan);
            ready.SetResult();
            return (plan.Id, job.JobId);
        }
        catch { ready.TrySetCanceled(); await _plans.UpdateStatusAsync(plan.Id, BatchPlanStatus.Failed); throw; }
    }

    public async Task<BatchPreviewResponse?> GetAsync(string id)
    {
        var plan = await _plans.GetByIdAsync(id);
        if (plan == null) return null;
        var state = ReadState(plan);
        if (state == null) return null;
        var job = state.JobId == null ? null : scheduler.GetJob(state.JobId);
        var status = plan.IsExpired(DateTimeOffset.UtcNow) ? "Expired" : plan.Status == BatchPlanStatus.Previewed ? "Completed" : job?.State.ToString() ?? "Failed";
        var files = plan.Files.Select(file => {
            var payload = JsonSerializer.Deserialize<BatchPreviewFile>(file.PlanPayloadJson!)!;
            var selectedIds = file.SelectedChangeIds.ToHashSet(StringComparer.Ordinal);
            return payload with { Selected = file.Selected, Occurrences = payload.Occurrences.Select(o => o with { Selected = selectedIds.Contains(o.Id) }).ToArray() };
        }).ToArray();
        var occurrences = files.SelectMany(f => f.Occurrences).ToArray();
        return new(plan.Id, state.JobId, status, status == "Completed" ? 100 : Math.Clamp(job?.ProgressPercentage ?? 0, 0, 100),
            status == "Failed" ? "PreviewFailed" : status == "Expired" ? "ExpiredPlan" : null, state.Scope,
            files.Length, files.Count(f => f.Occurrences.Count > 0), occurrences.Length,
            new(occurrences.Count(o => o.Origin == "Body"), occurrences.Count(o => o.Origin == "Table"), occurrences.Count(o => o.Origin == "Header"), occurrences.Count(o => o.Origin == "Footer")), files);
    }

    public async Task<BatchPreviewResponse?> SaveSelectionAsync(string id, BatchPreviewSelection selection)
    {
        var plan = await _plans.GetByIdAsync(id);
        if (plan == null || ReadState(plan) == null) return null;
        var scope = new BatchScopeRequest { WorkspaceId = plan.WorkspaceId, Type = BatchScopeType.Selection, DocumentIds = plan.Files.Select(f => f.DocumentId).ToArray() };
        await scanner.SaveSelectionAsync(id, selection, await ResolveAsync(scope));
        return await GetAsync(id);
    }

    public async Task<bool> CancelAsync(string id)
    {
        var plan = await _plans.GetByIdAsync(id); if (plan == null) return false;
        var state = ReadState(plan); if (state == null) return false;
        if (state.JobId != null) scheduler.CancelJob(state.JobId);
        return true;
    }

    private async Task<BatchScopeResult> ResolveAsync(BatchScopeRequest scope) => new BatchScopeResolver().Resolve(scope,
        await new DocumentRepository(db).GetByWorkspaceAsync(scope.WorkspaceId), await new FolderSourceRepository(db).GetByWorkspaceAsync(scope.WorkspaceId),
        await new BatchCollectionReader(db).GetByWorkspaceAsync(scope.WorkspaceId));

    private static BatchPreviewState? ReadState(BatchOperationPlan plan)
    {
        if (plan.OperationType != BatchOperationType.SearchAndReplace) return null;
        try { var state = JsonSerializer.Deserialize<BatchPreviewState>(plan.ScopeJson); return state?.Scope == null ? null : state; }
        catch (JsonException) { return null; }
    }

    private static void Validate(BatchPreviewRequest request)
    {
        if (request?.Scope == null || string.IsNullOrWhiteSpace(request.Scope.WorkspaceId) || request.Scope.WorkspaceId.Length > 128 ||
            request.Scope.DocumentIds == null || request.Scope.ExcludedBranches == null || request.Scope.DocumentIds.Count > 1000 || request.Scope.ExcludedBranches.Count > 1000 ||
            request.Scope.DocumentIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128) ||
            string.IsNullOrEmpty(request.Find) || request.Find.Length > 4096 || request.Replacement == null || request.Replacement.Length > 4096)
            throw new ArgumentException("InvalidRequest");
    }

    private static IReadOnlyList<string> EnumerateSubfolders(BatchScopeRequest scope)
    {
        if (scope.Type != BatchScopeType.Folder || !scope.IncludeSubfolders) return Array.Empty<string>();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scope.FolderPath!));
        var exclusions = scope.ExcludedBranches.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p, root))).ToArray();
        var folders = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        try
        {
            while (pending.TryPop(out var current))
                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    var path = Path.GetFullPath(directory);
                    if (exclusions.Any(ex => path.Equals(ex, StringComparison.OrdinalIgnoreCase) || path.StartsWith(ex + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("ScopeReparsePoint");
                    if (folders.Count == 10000) throw new ArgumentException("ScopeLimit");
                    folders.Add(path); pending.Push(path);
                }
        }
        catch (IOException) { throw new ArgumentException("ScopeUnavailable"); }
        catch (UnauthorizedAccessException) { throw new ArgumentException("ScopeUnavailable"); }
        return folders.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
