using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;

namespace DocumentIDE.IntegrationTests.Batch;

/// <summary>
/// BAT-10 — Strict Batch Rename tests.
/// Covers the sprint's five steps and three required tests: `..\evil.docx`
/// is rejected, internal batch target collisions are detected before apply,
/// and a successful rename keeps the DocumentId.
/// </summary>
public sealed class BatchRenameTests : IDisposable
{
    private readonly string _root;
    private readonly AppDb _db;
    private readonly DocumentRepository _documents;
    private readonly MoveCorrelationService _correlation;
    private readonly PhysicalFileOperations _physical;
    private readonly BatchRenameOperation _operation;

    public BatchRenameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "batch-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _documents = new DocumentRepository(_db);

        var folderRepo = new FolderSourceRepository(_db);
        var identityProvider = new WindowsFileIdentityProvider();
        var identityService = new DocumentIdentityService(_documents, identityProvider);
        _correlation = new MoveCorrelationService(_documents, identityProvider, identityService, folderRepo);
        _physical = new PhysicalFileOperations(_correlation, recycleBin: null, documentRepository: _documents);
        _operation = new BatchRenameOperation(_physical, _documents);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    // ---------------------------------------------------------------------
    // Required test 1: `..\evil.docx` is rejected.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Apply_TraversalBasenameIsRejectedAndNothingIsTouched()
    {
        var content = Encoding.UTF8.GetBytes("source bytes");
        var (docId, path) = await SeedAsync("doc-evil", "source.md", content);

        // The escape name is unique so the assertion cannot hit another test's file.
        var escapedName = "evil-" + Guid.NewGuid().ToString("N") + ".docx";
        var newBaseName = @"..\" + escapedName;

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, newBaseName) });

        // Fail closed: the whole reviewed batch is blocked and typed.
        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(docId, file.DocumentId);
        Assert.Equal(path, file.FilePath);
        Assert.Equal(BatchApplyFileOutcome.Failure, file.Outcome);
        Assert.Equal("InvalidBaseName", file.ReasonCode);

        // The source file is untouched, no file escaped the directory, and no
        // physical operation (journal intent) was attempted.
        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        var parent = Path.GetFullPath(Path.Combine(_root, ".."));
        Assert.False(File.Exists(Path.Combine(parent, escapedName)));
        Assert.Null(_correlation.FindActiveIntentBySource(path));

        var document = await _documents.GetByIdAsync(docId);
        Assert.Equal(path, document!.CurrentPath);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("sub/evil.md")]
    [InlineData(@"sub\evil.md")]
    [InlineData(@"C:\evil.md")]
    [InlineData(@"\\server\share\evil.md")]
    [InlineData("notes.md:evil")]
    [InlineData("CON")]
    [InlineData("aux.md")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("bad<name>.md")]
    [InlineData("bad|name.md")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Apply_MalformedBaseNamesAreRejected(string newBaseName)
    {
        var content = Encoding.UTF8.GetBytes("source bytes");
        var (docId, path) = await SeedAsync("doc-bad", "valid.md", content);

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, newBaseName) });

        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, file.Outcome);
        Assert.Equal("InvalidBaseName", file.ReasonCode);
        Assert.True(File.Exists(path));
        Assert.Equal(new[] { "valid.md" }, Directory.GetFiles(_root, "*.md").Select(Path.GetFileName));
    }

    // ---------------------------------------------------------------------
    // Step 1: deterministic old/new basename preview.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task BuildPreview_ShowsOldAndNewBaseNamesDeterministicallyAndTouchesNothing()
    {
        var contentA = Encoding.UTF8.GetBytes("alpha");
        var contentB = Encoding.UTF8.GetBytes("beta");
        var (docA, pathA) = await SeedAsync("doc-a", "alpha.md", contentA);
        var (docB, pathB) = await SeedAsync("doc-b", "beta.md", contentB);

        // Input order is deliberately reversed; preview order must be
        // deterministic (DocumentId ordinal) regardless of input order.
        var targets = new[]
        {
            Target(docB, pathB, contentB, "beta-renamed.md"),
            Target(docA, pathA, contentA, @"..\escape.md")
        };

        var first = _operation.BuildPreview(targets);
        var second = _operation.BuildPreview(targets.Reverse().ToList());

        Assert.Equal(new[] { docA, docB }, first.Select(f => f.DocumentId));
        Assert.Equal(first, second);

        var invalid = first[0];
        Assert.Equal(pathA, invalid.FilePath);
        Assert.Equal("alpha.md", invalid.OldBaseName);
        Assert.Equal(@"..\escape.md", invalid.NewBaseName);
        Assert.Equal("InvalidBaseName", invalid.FailureCode);

        var valid = first[1];
        Assert.Equal("beta.md", valid.OldBaseName);
        Assert.Equal("beta-renamed.md", valid.NewBaseName);
        Assert.Null(valid.FailureCode);

        // Preview is read-only: no file was renamed or created.
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
        Assert.False(File.Exists(Path.Combine(_root, "beta-renamed.md")));
    }

    // ---------------------------------------------------------------------
    // Step 4: apply through the FS-07 physical operation journal.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Apply_RenamesThroughFs07JournalAndStaysInSameDirectory()
    {
        var content = Encoding.UTF8.GetBytes("rename me");
        var (docId, path) = await SeedAsync("doc-rename", "original.md", content);

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, "renamed.md") });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(docId, file.DocumentId);
        Assert.Equal(BatchApplyFileOutcome.Success, file.Outcome);
        Assert.Null(file.ReasonCode);

        var expectedPath = Path.Combine(_root, "renamed.md");
        Assert.Equal(expectedPath, file.TargetPath);

        // Acceptance — rename cannot become move: the target stays in the
        // reviewed source directory.
        Assert.Equal(Path.GetDirectoryName(path), Path.GetDirectoryName(file.TargetPath!));

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(expectedPath));

        // Applied through the FS-07 physical operation journal: the move
        // intent is recorded for watcher correlation under the same DocumentId.
        var intent = _correlation.FindActiveIntentByDestination(expectedPath);
        Assert.NotNull(intent);
        Assert.Equal(docId, intent!.DocumentId);
        Assert.Equal(path, intent.SourcePath);
        Assert.Equal(expectedPath, intent.DestinationPath);
    }

    // ---------------------------------------------------------------------
    // Step 5 / required test 3: successful rename keeps the DocumentId.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Apply_SuccessfulRenameKeepsDocumentIdAndPreservesMetadata()
    {
        var content = Encoding.UTF8.GetBytes("content stays byte-identical");
        var (docId, path) = await SeedAsync("doc-keep", "keep.md", content);
        var before = await _documents.GetByIdAsync(docId);

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, "keep-renamed.md") });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(BatchApplyFileOutcome.Success, Assert.Single(result.Files).Outcome);

        var expectedPath = Path.Combine(_root, "keep-renamed.md");

        // The successful rename keeps the DocumentId: canvas/graph/collection
        // references keyed by DocumentId are preserved by construction, and
        // only the path-carrying fields change.
        var after = await _documents.GetByIdAsync(docId);
        Assert.NotNull(after);
        Assert.Equal(before!.Id, after!.Id);
        Assert.Equal(expectedPath, after.CurrentPath);
        Assert.Equal(path, after.LastKnownPath);
        Assert.Equal("keep-renamed.md", after.Filename);
        Assert.Equal(".md", after.Extension);

        // Every other metadata field is preserved, and rename never rewrites
        // document content (spec 07 §21, spec 04 §22).
        Assert.Equal(before.DocumentType, after.DocumentType);
        Assert.Equal(before.SizeBytes, after.SizeBytes);
        Assert.Equal(before.ModifiedAt, after.ModifiedAt);
        Assert.Equal(before.ContentFingerprint, after.ContentFingerprint);
        Assert.Equal(before.AvailabilityStatus, after.AvailabilityStatus);
        Assert.Equal(before.IndexStatus, after.IndexStatus);
        Assert.Equal(before.CreatedAt, after.CreatedAt);
        Assert.Equal(content, await File.ReadAllBytesAsync(expectedPath));
    }

    // ---------------------------------------------------------------------
    // Step 3 / required test 2: collisions are detected before apply.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Apply_InternalBatchTargetCollisionIsDetectedBeforeFirstFileChanges()
    {
        var contentA = Encoding.UTF8.GetBytes("aaa content");
        var contentB = Encoding.UTF8.GetBytes("bbb content");
        var (docA, pathA) = await SeedAsync("doc-aaa", "aaa.md", contentA);
        var (docB, pathB) = await SeedAsync("doc-bbb", "bbb.md", contentB);
        var targets = new[]
        {
            Target(docA, pathA, contentA, "same.md"),
            Target(docB, pathB, contentB, "same.md")
        };

        // Apply blocks the whole batch before the FIRST file (doc-aaa sorts
        // first) is changed.
        var result = await _operation.ApplyAsync(targets);

        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, f => Assert.Equal(BatchApplyFileOutcome.Failure, f.Outcome));
        Assert.All(result.Files, f => Assert.Equal("DuplicateTarget", f.ReasonCode));

        // The collision is visible at preview time for both files.
        var preview = _operation.BuildPreview(targets);
        Assert.Equal(new[] { "aaa.md", "bbb.md" }, preview.Select(f => f.OldBaseName));
        Assert.All(preview, f => Assert.Equal("DuplicateTarget", f.FailureCode));

        // Nothing was renamed, nothing was created, nothing was journaled.
        Assert.True(File.Exists(pathA));
        Assert.True(File.Exists(pathB));
        Assert.Equal(contentA, await File.ReadAllBytesAsync(pathA));
        Assert.Equal(contentB, await File.ReadAllBytesAsync(pathB));
        Assert.False(File.Exists(Path.Combine(_root, "same.md")));
        Assert.Null(_correlation.FindActiveIntentBySource(pathA));
        Assert.Null(_correlation.FindActiveIntentBySource(pathB));
        Assert.Equal(pathA, (await _documents.GetByIdAsync(docA))!.CurrentPath);
        Assert.Equal(pathB, (await _documents.GetByIdAsync(docB))!.CurrentPath);
    }

    [Fact]
    public async Task Apply_ExistingDestinationIsRejectedAndLeftUntouched()
    {
        var content = Encoding.UTF8.GetBytes("x content");
        var (docId, path) = await SeedAsync("doc-x", "x.md", content);
        var occupied = Path.Combine(_root, "occupied.md");
        await File.WriteAllTextAsync(occupied, "someone else's file");
        var targets = new[] { Target(docId, path, content, "occupied.md") };

        Assert.Equal("TargetExists", Assert.Single(_operation.BuildPreview(targets)).FailureCode);

        var result = await _operation.ApplyAsync(targets);

        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, file.Outcome);
        Assert.Equal("TargetExists", file.ReasonCode);

        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(occupied));
        Assert.Null(_correlation.FindActiveIntentBySource(path));
    }

    [Fact]
    public async Task Apply_OverlongTargetPathIsRejected()
    {
        var content = Encoding.UTF8.GetBytes("long content");
        var (docId, path) = await SeedAsync("doc-long", "long.md", content);
        var overlongName = new string('x', 300) + ".md";

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, overlongName) });

        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, file.Outcome);
        Assert.Equal("PathTooLong", file.ReasonCode);
        Assert.True(File.Exists(path));
        Assert.Null(_correlation.FindActiveIntentBySource(path));
    }

    // ---------------------------------------------------------------------
    // Failure / edge cases: file changed after preview; one file fails.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Apply_ChangedFileAfterPreviewIsSkippedAsStaleAndLeftUntouched()
    {
        var content = Encoding.UTF8.GetBytes("reviewed content");
        var (docId, path) = await SeedAsync("doc-stale", "stale.md", content);
        var target = Target(docId, path, content, "stale-renamed.md");

        // The file changes after preview: the reviewed fingerprint no longer matches.
        var modified = Encoding.UTF8.GetBytes("content changed after preview");
        await File.WriteAllBytesAsync(path, modified);

        var result = await _operation.ApplyAsync(new[] { target });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, file.Outcome);
        Assert.Equal("Stale", file.ReasonCode);

        // The changed file is never renamed; its bytes stay untouched.
        Assert.True(File.Exists(path));
        Assert.Equal(modified, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Path.Combine(_root, "stale-renamed.md")));
        Assert.Null(_correlation.FindActiveIntentBySource(path));
        Assert.Equal(path, (await _documents.GetByIdAsync(docId))!.CurrentPath);
    }

    [Fact]
    public async Task Apply_DocumentPathChangedAfterPreviewIsSkippedAsStale()
    {
        var content = Encoding.UTF8.GetBytes("moved content");
        var (docId, path) = await SeedAsync("doc-moved", "moved.md", content);

        // Another operation relocated the file and updated the workspace:
        // the reviewed path no longer identifies the document's file.
        var movedPath = Path.Combine(_root, "moved-elsewhere.md");
        File.Move(path, movedPath);
        var document = (await _documents.GetByIdAsync(docId))!;
        document.CurrentPath = movedPath;
        document.LastKnownPath = path;
        await _documents.UpdateAsync(document);

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, "moved-renamed.md") });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, file.Outcome);
        Assert.Equal("Stale", file.ReasonCode);

        // Neither the reviewed path nor the relocated file was touched.
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(movedPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(movedPath));
        Assert.False(File.Exists(Path.Combine(_root, "moved-renamed.md")));
    }

    [Fact]
    public async Task Apply_OneMissingFileYieldsTypedFailureWhileOthersSucceed()
    {
        var goodContent = Encoding.UTF8.GetBytes("good content");
        var missingContent = Encoding.UTF8.GetBytes("missing content");
        var (goodId, goodPath) = await SeedAsync("doc-good", "good.md", goodContent);
        var (missingId, missingPath) = await SeedAsync("doc-missing", "missing.md", missingContent);

        var targets = new[]
        {
            Target(goodId, goodPath, goodContent, "good-renamed.md"),
            Target(missingId, missingPath, missingContent, "missing-renamed.md")
        };

        // The reviewed file vanishes after preview (deleted outside the app).
        File.Delete(missingPath);

        var result = await _operation.ApplyAsync(targets);

        // The batch completes with typed per-file outcomes: the valid file is
        // renamed, the missing file fails closed with an inspectable reason.
        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);

        var good = result.Files.Single(f => f.DocumentId == goodId);
        Assert.Equal(BatchApplyFileOutcome.Success, good.Outcome);
        Assert.True(File.Exists(Path.Combine(_root, "good-renamed.md")));
        Assert.Equal(goodContent, await File.ReadAllBytesAsync(Path.Combine(_root, "good-renamed.md")));

        var missing = result.Files.Single(f => f.DocumentId == missingId);
        Assert.Equal(BatchApplyFileOutcome.Failure, missing.Outcome);
        Assert.Equal("Unavailable", missing.ReasonCode);
        Assert.False(File.Exists(missingPath));
        Assert.False(File.Exists(Path.Combine(_root, "missing-renamed.md")));
    }

    // ---------------------------------------------------------------------
    // Cancellation, unreviewable targets, acceptance evidence.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Apply_CancelledBeforeFirstFileIsTypedAndChangesNothing()
    {
        var content = Encoding.UTF8.GetBytes("cancel content");
        var (docId, path) = await SeedAsync("doc-cancel", "cancel.md", content);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _operation.ApplyAsync(new[] { Target(docId, path, content, "cancelled.md") }, cts.Token);

        Assert.Equal(BatchApplyResultStatus.Canceled, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, file.Outcome);
        Assert.Equal("Cancelled", file.ReasonCode);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(_root, "cancelled.md")));
        Assert.Null(_correlation.FindActiveIntentBySource(path));
    }

    [Fact]
    public async Task Apply_UnreviewableTargetsAreRejectedFailClosed()
    {
        var content = Encoding.UTF8.GetBytes("valid content");
        var (docId, path) = await SeedAsync("doc-valid", "valid.md", content);
        var fingerprint = Convert.ToHexString(SHA256.HashData(content));

        var result = await _operation.ApplyAsync(new BatchRenameTarget[]
        {
            new("", path, fingerprint, "empty-id.md"),
            new(docId, "relative\\valid.md", fingerprint, "relative.md")
        });

        Assert.Equal(BatchApplyResultStatus.Blocked, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal("InvalidTarget", result.Files.Single(f => f.DocumentId == "").ReasonCode);
        Assert.Equal("InvalidPath", result.Files.Single(f => f.DocumentId == docId).ReasonCode);

        Assert.True(File.Exists(path));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(Path.Combine(_root, "empty-id.md")));
        Assert.False(File.Exists(Path.Combine(_root, "relative.md")));
    }

    [Fact]
    public async Task Apply_MissingPreviewFingerprintIsSkippedAsUnavailable()
    {
        var content = Encoding.UTF8.GetBytes("no fingerprint");
        var (docId, path) = await SeedAsync("doc-nofp", "nofp.md", content);

        var result = await _operation.ApplyAsync(new[] { new BatchRenameTarget(docId, path, "", "nofp-renamed.md") });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        var file = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, file.Outcome);
        Assert.Equal("Unavailable", file.ReasonCode);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(_root, "nofp-renamed.md")));
        Assert.Null(_correlation.FindActiveIntentBySource(path));
    }

    [Fact]
    public async Task RenameSurface_CannotExpressMoveAndOnlyRenamesPhysically()
    {
        var content = Encoding.UTF8.GetBytes("surface content");
        var (docId, path) = await SeedAsync("doc-surface", "surface.md", content);
        var fake = new RecordingPhysicalOperations();

        var result = await new BatchRenameOperation(fake, _documents)
            .ApplyAsync(new[] { Target(docId, path, content, "surface-renamed.md") });

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(BatchApplyFileOutcome.Success, Assert.Single(result.Files).Outcome);

        // Only the FS-07 rename operation is reachable; move/duplicate/delete are never called.
        Assert.Equal(new[] { "RenameItemAsync" }, fake.Calls);

        // The reviewed target contract has a new basename only — there is no
        // field in which an arbitrary destination path could be expressed.
        Assert.Equal(
            new[] { "DocumentId", "ExpectedFingerprint", "FilePath", "NewBaseName" },
            typeof(BatchRenameTarget).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    private sealed class RecordingPhysicalOperations : IPhysicalFileOperations
    {
        public List<string> Calls { get; } = new();

        public Task<PhysicalOperationResultDto> RenameItemAsync(string currentPath, string newBaseName, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(RenameItemAsync));
            var targetPath = Path.Combine(Path.GetDirectoryName(currentPath)!, newBaseName);
            return Task.FromResult(new PhysicalOperationResultDto(true, nameof(PhysicalOperationType.Rename), currentPath, targetPath));
        }

        public Task<PhysicalOperationResultDto> MoveItemAsync(string sourcePath, string destinationDirectoryPath, string? newBaseName = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(MoveItemAsync));
            throw new InvalidOperationException("Move must never be reachable from batch rename.");
        }

        public Task<PhysicalOperationResultDto> DuplicateItemAsync(string sourceFilePath, string? customNewBaseName = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(DuplicateItemAsync));
            throw new InvalidOperationException("Duplicate must never be reachable from batch rename.");
        }

        public Task<PhysicalOperationResultDto> CreateDirectoryAsync(string parentDirectoryPath, string newDirectoryName, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(CreateDirectoryAsync));
            throw new InvalidOperationException("CreateDirectory must never be reachable from batch rename.");
        }

        public Task<PhysicalOperationResultDto> CreateMarkdownFileAsync(string parentDirectoryPath, string newFileName, string? initialContent = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(CreateMarkdownFileAsync));
            throw new InvalidOperationException("CreateMarkdownFile must never be reachable from batch rename.");
        }

        public Task<PhysicalOperationResultDto> CreateDocxFileAsync(string parentDirectoryPath, string newFileName, byte[]? templateBytes = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(CreateDocxFileAsync));
            throw new InvalidOperationException("CreateDocxFile must never be reachable from batch rename.");
        }

        public Task<PhysicalOperationResultDto> DeleteItemAsync(string targetPath, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(DeleteItemAsync));
            throw new InvalidOperationException("Delete must never be reachable from batch rename.");
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static BatchRenameTarget Target(string docId, string path, byte[] content, string newBaseName) =>
        new(docId, path, Convert.ToHexString(SHA256.HashData(content)), newBaseName);

    private async Task<(string DocId, string Path)> SeedAsync(string docId, string fileName, byte[] content)
    {
        var path = Path.Combine(_root, fileName);
        await File.WriteAllBytesAsync(path, content);
        await _documents.CreateAsync(new Document
        {
            Id = docId,
            CurrentPath = path,
            Filename = fileName,
            Extension = Path.GetExtension(fileName),
            DocumentType = "markdown",
            SizeBytes = content.Length,
            ModifiedAt = "2026-01-01T00:00:00.0000000Z",
            ContentFingerprint = Convert.ToHexString(SHA256.HashData(content)),
            AvailabilityStatus = "available",
            IndexStatus = "indexed",
            CreatedAt = "2026-01-01T00:00:00.0000000Z",
            UpdatedAt = "2026-01-01T00:00:00.0000000Z"
        });
        return (docId, path);
    }
}
