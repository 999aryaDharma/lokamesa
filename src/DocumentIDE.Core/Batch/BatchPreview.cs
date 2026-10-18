namespace DocumentIDE.Core.Batch;

public sealed record BatchPreviewRequest(BatchScopeRequest Scope, string Find, string Replacement);
public sealed record BatchPreviewOccurrence(string Id, string Origin, string Context, bool Selected = true);
public sealed record BatchPreviewFile(string DocumentId, string FileName, bool Selected, string LockState,
    string? FailureCode, IReadOnlyList<BatchPreviewOccurrence> Occurrences);
public sealed record BatchPreviewScope
{
    public required string WorkspaceId { get; init; }
    public BatchScopeType Type { get; init; }
    public IReadOnlyList<string> DocumentIds { get; init; } = Array.Empty<string>();
    public string? FolderPath { get; init; }
    public bool IncludeSubfolders { get; init; } = true;
    public IReadOnlyList<string> ExcludedBranches { get; init; } = Array.Empty<string>();
    public string? CollectionId { get; init; }
    public IReadOnlyList<string> IncludedSubfolders { get; init; } = Array.Empty<string>();
    public int SubfolderCount => IncludedSubfolders.Count;
    public IReadOnlyList<BatchPreviewScopeFailure> Failures { get; init; } = Array.Empty<BatchPreviewScopeFailure>();
}
public sealed record BatchPreviewScopeFailure(string DocumentId, string FailureCode);
public sealed record BatchPreviewBreakdown(int Body, int Table, int Header, int Footer);
public sealed record BatchPreviewResponse(string PlanId, string? JobId, string Status, double Progress,
    string? ErrorCode, BatchPreviewScope Scope, int FilesScanned, int FilesAffected, int OccurrenceCount,
    BatchPreviewBreakdown Breakdown, IReadOnlyList<BatchPreviewFile> Files);
public sealed record BatchPreviewSelection(IReadOnlyList<BatchPreviewFileSelection> Files);
public sealed record BatchPreviewFileSelection(string DocumentId, bool Selected, IReadOnlyList<string> SelectedOccurrenceIds);
public sealed record BatchDocumentOption(string Id, string FileName);
public sealed record BatchCollectionOption(string Id, string Name);
public sealed record BatchScopeOptions(IReadOnlyList<BatchDocumentOption> Documents, IReadOnlyList<BatchCollectionOption> Collections);
