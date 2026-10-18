using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;

namespace DocumentIDE.Core.Batch;

public enum BatchScopeType { Selection, Folder, Workspace, Collection }

public sealed record BatchScopeRequest
{
    public required string WorkspaceId { get; init; }
    public required BatchScopeType Type { get; init; }
    public IReadOnlyList<string> DocumentIds { get; init; } = Array.Empty<string>();
    public string? FolderPath { get; init; }
    public bool IncludeSubfolders { get; init; } = true;
    public IReadOnlyList<string> ExcludedBranches { get; init; } = Array.Empty<string>();
    public string? CollectionId { get; init; }
}

public sealed record BatchCollectionSnapshot(string Id, string WorkspaceId, IReadOnlyList<string> DocumentIds);
public enum BatchScopeFailureReason { UnknownDocument, OutsideSource, Unavailable, InvalidPath, ReparsePoint, UnsupportedType }
public sealed record BatchScopeFailure(string DocumentId, BatchScopeFailureReason Reason);

public sealed record BatchScopeResult(IReadOnlyList<Document> Documents, IReadOnlyList<string> IncludedSubfolders)
{
    public IReadOnlyList<BatchScopeFailure> Failures { get; init; } = Array.Empty<BatchScopeFailure>();
    public int FilesScannedCount => Documents.Count;
    public int SubfolderCount => IncludedSubfolders.Count;
}

/// <summary>Resolves a workspace document snapshot without matching or modifying source content.</summary>
public sealed class BatchScopeResolver
{
    public BatchScopeResult Resolve(BatchScopeRequest request, IReadOnlyList<Document> documents,
        IReadOnlyList<FolderSource> folderSources, IReadOnlyList<BatchCollectionSnapshot>? collections = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        var sources = folderSources.Where(s => s.WorkspaceId == request.WorkspaceId).ToArray();
        string? root = null;
        HashSet<string>? ids = null;
        var exclusions = new List<string>();
        switch (request.Type)
        {
            case BatchScopeType.Selection:
                ids = request.DocumentIds.ToHashSet(StringComparer.Ordinal);
                break;
            case BatchScopeType.Folder:
                if (string.IsNullOrWhiteSpace(request.FolderPath) || !Path.IsPathFullyQualified(request.FolderPath))
                    throw new ArgumentException("Folder scope requires an absolute folder path.");
                root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.FolderPath));
                if (!SourceContainmentPolicy.IsContained(root, sources))
                    throw new ArgumentException("Folder is outside configured workspace sources.");
                try
                {
                    if (!Directory.Exists(root) || HasReparsePoint(root))
                        throw new ArgumentException("Folder is unavailable or traverses a reparse point.");
                }
                catch (IOException ex) { throw new ArgumentException("Folder cannot be inspected safely.", ex); }
                catch (UnauthorizedAccessException ex) { throw new ArgumentException("Folder cannot be inspected safely.", ex); }
                foreach (var branch in request.ExcludedBranches)
                {
                    if (string.IsNullOrWhiteSpace(branch)) throw new ArgumentException("Excluded branch must name a folder.");
                    var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(branch, root));
                    if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || !Within(path, root))
                        throw new ArgumentException("Excluded branch must be below the scope folder.");
                    exclusions.Add(path);
                }
                break;
            case BatchScopeType.Workspace:
                break;
            case BatchScopeType.Collection:
                var collection = collections?.SingleOrDefault(c => c.Id == request.CollectionId && c.WorkspaceId == request.WorkspaceId);
                if (collection == null) throw new ArgumentException("Collection must belong to the requested workspace.");
                ids = collection.DocumentIds.ToHashSet(StringComparer.Ordinal);
                break;
            default:
                throw new ArgumentException("Unknown batch scope.");
        }

        var files = new List<Document>();
        var failures = new List<BatchScopeFailure>();
        var knownIds = documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        if (ids != null)
            failures.AddRange(ids.Where(id => !knownIds.Contains(id)).Order(StringComparer.Ordinal)
                .Select(id => new BatchScopeFailure(id, BatchScopeFailureReason.UnknownDocument)));
        foreach (var document in documents.DistinctBy(d => d.Id).OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            if (ids != null && !ids.Contains(document.Id)) continue;
            try
            {
                if (!Path.IsPathFullyQualified(document.CurrentPath)) throw new ArgumentException("Document path must be absolute.");
                var path = Path.GetFullPath(document.CurrentPath);
                if (root != null && (!Within(path, root) || exclusions.Any(exclusion => Within(path, exclusion)) ||
                    (!request.IncludeSubfolders && !string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)))) continue;
                if (!SourceContainmentPolicy.IsContained(path, sources))
                {
                    if (ids != null) failures.Add(new(document.Id, BatchScopeFailureReason.OutsideSource));
                    continue;
                }
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".md", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                {
                    if (ids != null) failures.Add(new(document.Id, BatchScopeFailureReason.UnsupportedType));
                    continue;
                }
                if (!File.Exists(path))
                {
                    failures.Add(new(document.Id, BatchScopeFailureReason.Unavailable));
                    continue;
                }
                if (HasReparsePoint(path))
                {
                    failures.Add(new(document.Id, BatchScopeFailureReason.ReparsePoint));
                    continue;
                }
                files.Add(document);
            }
            catch (ArgumentException) { failures.Add(new(document.Id, BatchScopeFailureReason.InvalidPath)); }
            catch (IOException) { failures.Add(new(document.Id, BatchScopeFailureReason.Unavailable)); }
            catch (UnauthorizedAccessException) { failures.Add(new(document.Id, BatchScopeFailureReason.Unavailable)); }
        }
        var subfolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root != null)
            foreach (var file in files)
                for (var directory = Path.GetDirectoryName(Path.GetFullPath(file.CurrentPath)); directory != null && !directory.Equals(root, StringComparison.OrdinalIgnoreCase);
                     directory = Path.GetDirectoryName(directory))
                    subfolders.Add(directory);
        return new(files, subfolders.Order(StringComparer.OrdinalIgnoreCase).ToArray()) { Failures = failures };
    }

    private static bool Within(string path, string root) =>
        SourceContainmentPolicy.IsContained(path, new[] { new FolderSource { CanonicalPath = root } });

    // ponytail: reject all reparse traversal; allow contained links when FS-08 resolves targets reliably.
    private static bool HasReparsePoint(string path)
    {
        for (string? component = path; component != null; component = Path.GetDirectoryName(component))
            if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
