using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Tests.Batch;

public sealed class BatchScopeResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "batch-scope-" + Guid.NewGuid().ToString("N"));
    private readonly List<Document> _documents = new();
    private readonly List<FolderSource> _sources;

    public BatchScopeResolverTests()
    {
        Directory.CreateDirectory(_root);
        _sources = new() { new FolderSource { WorkspaceId = "ws", CanonicalPath = _root } };
    }

    private Document Add(string id, string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "unchanged source");
        var document = new Document { Id = id, CurrentPath = path, Filename = Path.GetFileName(path), Extension = Path.GetExtension(path) };
        _documents.Add(document);
        return document;
    }

    private BatchScopeResult Resolve(BatchScopeRequest request) =>
        new BatchScopeResolver().Resolve(request, _documents, _sources);

    [Fact]
    public void FolderScope_RecursivelyIncludesNestedFiles_WithoutMatchingOrMutation()
    {
        var root = Add("b", "root.md");
        var nested = Add("a", Path.Combine("Month", "Draft", "nested.docx"));
        Add("ignored", "image.png");

        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root });

        Assert.Equal(new[] { "a", "b" }, result.Documents.Select(d => d.Id));
        Assert.Equal(2, result.FilesScannedCount);
        Assert.Equal(2, result.SubfolderCount);
        Assert.Equal("unchanged source", File.ReadAllText(root.CurrentPath));
        Assert.Equal("unchanged source", File.ReadAllText(nested.CurrentPath));
    }

    [Fact]
    public void FolderScope_NonRecursiveExcludesNestedFiles()
    {
        Add("root", "root.md");
        Add("nested", Path.Combine("Month", "nested.md"));

        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root, IncludeSubfolders = false });

        Assert.Equal("root", Assert.Single(result.Documents).Id);
        Assert.Equal(0, result.SubfolderCount);
    }

    [Fact]
    public void ExcludedArchiveBranch_IsAbsent_WithoutExcludingArchiveSibling()
    {
        Add("root", "root.md");
        Add("archive", Path.Combine("Archive", "Old", "a.md"));
        Add("sibling", Path.Combine("Archive2", "b.md"));
        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root, ExcludedBranches = new[] { "Archive" } });
        Assert.Equal(new[] { "root", "sibling" }, result.Documents.Select(d => d.Id));
        Assert.Equal(2, result.FilesScannedCount);
        Assert.Equal(1, result.SubfolderCount);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData(".")]
    public void ExclusionOutsideFolderOrWholeRoot_IsRejected(string branch)
    {
        Add("root", "root.md");
        Assert.Throws<ArgumentException>(() => Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root, ExcludedBranches = new[] { branch } }));
    }

    [Fact]
    public void SelectionScope_NeverSilentlyExpands_AndDeduplicatesIds()
    {
        Add("picked", "picked.md");
        Add("sibling", "sibling.md");
        Add("nested", Path.Combine("Month", "nested.md"));
        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = new[] { "picked", "picked" } });
        Assert.Equal("picked", Assert.Single(result.Documents).Id);
        Assert.Equal(1, result.FilesScannedCount);
    }

    [Fact]
    public void EmptySelection_ResolvesNothing()
    {
        Add("sibling", "sibling.md");
        Assert.Empty(Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection }).Documents);
    }

    [Fact]
    public void UnknownSelectedDocument_ProducesInspectableFailure()
    {
        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = new[] { "unknown" } });
        Assert.Empty(result.Documents);
        Assert.Equal(BatchScopeFailureReason.UnknownDocument, Assert.Single(result.Failures).Reason);
        Assert.Equal("unknown", result.Failures[0].DocumentId);
    }

    [Fact]
    public void WorkspaceScope_UsesOnlySourcesInRequestedWorkspace()
    {
        Add("ours", "ours.md");
        var other = Add("theirs", Path.Combine("Other", "theirs.md"));
        _sources[0].CanonicalPath = Path.Combine(_root, "Own");
        Directory.CreateDirectory(_sources[0].CanonicalPath);
        _documents[0].CurrentPath = Path.Combine(_sources[0].CanonicalPath, "ours.md");
        File.WriteAllText(_documents[0].CurrentPath, "ours");
        _sources.Add(new() { WorkspaceId = "other", CanonicalPath = Path.GetDirectoryName(other.CurrentPath)! });
        Assert.Equal("ours", Assert.Single(Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Workspace }).Documents).Id);
    }

    [Fact]
    public void CollectionScope_ResolvesExplicitWorkspaceMembershipOnly()
    {
        Add("member", "member.md");
        Add("sibling", "sibling.md");
        var collections = new[] { new BatchCollectionSnapshot("collection", "ws", new[] { "member" }) };
        var result = new BatchScopeResolver().Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Collection, CollectionId = "collection" }, _documents, _sources, collections);
        Assert.Equal("member", Assert.Single(result.Documents).Id);
        Assert.Throws<ArgumentException>(() => new BatchScopeResolver().Resolve(new() { WorkspaceId = "other", Type = BatchScopeType.Collection, CollectionId = "collection" }, _documents, _sources, collections));
    }

    [Theory]
    [InlineData(BatchScopeType.Folder)]
    [InlineData(BatchScopeType.Collection)]
    [InlineData((BatchScopeType)99)]
    public void MissingOrInvalidScope_IsRejected(BatchScopeType type)
    {
        Assert.Throws<ArgumentException>(() => Resolve(new() { WorkspaceId = "ws", Type = type }));
    }

    [Fact]
    public void FolderOutsideConfiguredSource_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root + "-sibling" }));
    }

    [Fact]
    public void ContainmentPolicy_ExcludesUncontainedFiles_AndReportsFailure()
    {
        Add("inside", "inside.md");
        _documents.Add(new() { Id = "outside", CurrentPath = Path.Combine(_root + "-sibling", "outside.md"), Extension = ".md" });
        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = new[] { "outside", "inside" } });
        Assert.Equal("inside", Assert.Single(result.Documents).Id);
        Assert.Equal(BatchScopeFailureReason.OutsideSource, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void MissingFile_DoesNotRemoveOtherValidTargets()
    {
        Add("valid", "valid.md");
        var missing = Add("missing", "missing.md");
        File.Delete(missing.CurrentPath);
        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = _root });
        Assert.Equal("valid", Assert.Single(result.Documents).Id);
        Assert.Equal(BatchScopeFailureReason.Unavailable, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void JunctionEscape_IsRejectedInSelectionAndFolderScopes()
    {
        var source = Path.Combine(_root, "Source");
        var target = Path.Combine(_root, "Outside");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "secret.md"), "must not enter batch");
        var junction = Path.Combine(source, "Escape");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", junction, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try
        {
            _sources[0].CanonicalPath = source;
            _documents.Add(new() { Id = "escape", CurrentPath = Path.Combine(junction, "secret.md"), Extension = ".md" });
            var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = new[] { "escape" } });
            Assert.Empty(result.Documents);
            Assert.Equal(BatchScopeFailureReason.ReparsePoint, Assert.Single(result.Failures).Reason);
            Assert.Throws<ArgumentException>(() => Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = junction }));
            _sources[0].CanonicalPath = junction;
            Assert.Empty(Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Workspace }).Documents);
            Assert.Equal("must not enter batch", File.ReadAllText(Path.Combine(target, "secret.md")));
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public void MalformedAndUnsupportedSelectedPaths_AreReportedWithoutDroppingValidFile()
    {
        Add("valid", "valid.md");
        Add("unsupported", "image.png");
        _documents.Add(new() { Id = "invalid", CurrentPath = "not-an-absolute-path", Extension = ".md" });
        var result = Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Selection, DocumentIds = new[] { "valid", "unsupported", "invalid" } });
        Assert.Equal("valid", Assert.Single(result.Documents).Id);
        Assert.Equal(new[] { BatchScopeFailureReason.InvalidPath, BatchScopeFailureReason.UnsupportedType }, result.Failures.Select(f => f.Reason));
    }

    [Fact]
    public void MissingFolder_IsRejectedBeforePreview()
    {
        Assert.Throws<ArgumentException>(() => Resolve(new() { WorkspaceId = "ws", Type = BatchScopeType.Folder, FolderPath = Path.Combine(_root, "Missing") }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
