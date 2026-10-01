using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Docx;
using DocumentIDE.Core.History;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.IntegrationTests.Docx;

namespace DocumentIDE.IntegrationTests.Batch;

/// <summary>
/// BAT-05 — Exact Search &amp; Replace Operation tests.
/// Covers the sprint's five steps and three required tests:
/// split-run DOCX replacement, fuzzy/semantic providers never called,
/// deselected occurrence remains unchanged.
/// </summary>
public sealed class SearchReplaceOperationTests : IDisposable
{
    private readonly string _root;
    private readonly AppDb _db;
    private readonly BatchPlanRepository _plans;
    private readonly DocumentRepository _documents;
    private readonly FakeHistoryRecorder _recorder;
    private readonly FakeReindexTrigger _reindex;
    private readonly BatchApplyEngine _engine;
    private readonly SearchReplaceOperation _operation;

    public SearchReplaceOperationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "search-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _plans = new BatchPlanRepository(_db);
        _documents = new DocumentRepository(_db);
        new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "search-replace" }).GetAwaiter().GetResult();
        _recorder = new FakeHistoryRecorder();
        _reindex = new FakeReindexTrigger();
        _engine = new BatchApplyEngine(
            _plans, _documents,
            new RecoverySnapshotService(new FakeHistoryStorage(_root)),
            new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);
        _operation = new SearchReplaceOperation(new StrictTextMutator());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Apply_SplitRunDocxReplacementWorks()
    {
        // The find text "target" is split across two OOXML runs ("Hello tar" | "get world");
        // the user visually sees one word, so logical matching and replacement must cross the run boundary.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Hello tar</w:t></w:r><w:r><w:t>get world</w:t></w:r></w:p></w:body></w:document>");
        var (planId, path) = await SeedAsync("doc", "split-run.docx", package);

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "Replaced" };
        var occurrences = await _operation.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        var matcher = new LogicalTextMatcher();
        Assert.Empty(matcher.Search(path, "target"));
        var replaced = Assert.Single(matcher.Search(path, "Replaced"));
        Assert.Equal("Hello Replaced world", replaced.FullParagraphText);
        Assert.Equal(DocxStructureOrigin.Body, replaced.Origin);
    }

    [Fact]
    public async Task Apply_ChangedFileAfterPreviewIsSkippedAsStale()
    {
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>target content</w:t></w:r></w:p></w:body></w:document>");
        var (planId, path) = await SeedAsync("doc", "stale.docx", package);

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "Replaced" };
        var occurrences = await _operation.BuildOccurrencesAsync(path, parameters);
        await SelectAsync(planId, "doc", occurrences.Select(o => o.Id).ToList());

        // The file changes after preview: the reviewed fingerprint no longer matches.
        var modified = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>target content changed after preview</w:t></w:r></w:p></w:body></w:document>");
        await File.WriteAllBytesAsync(path, modified);

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Stale", skipped.ReasonCode);
        Assert.Equal(modified, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Parameters_RejectInvalidQueryOrReplacement()
    {
        // Validation must fire before any file access; "missing.docx" never exists.
        var missingPath = Path.Combine(_root, "missing.docx");

        var emptyFind = await Assert.ThrowsAsync<ArgumentException>(() =>
            _operation.BuildOccurrencesAsync(missingPath, new SearchReplaceParameters { Find = "", Replacement = "x" }));
        Assert.Contains("InvalidParameters", emptyFind.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _operation.BuildOccurrencesAsync(missingPath, new SearchReplaceParameters { Find = "target", Replacement = null! }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _operation.BuildOccurrencesAsync(missingPath, new SearchReplaceParameters { Find = new string('a', 4097), Replacement = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _operation.BuildOccurrencesAsync(missingPath, new SearchReplaceParameters { Find = "target", Replacement = new string('b', 4097) }));

        Assert.Throws<ArgumentException>(() =>
            _operation.CreateMutate(new SearchReplaceParameters { Find = "", Replacement = "x" }, Array.Empty<SearchReplaceTarget>()));
        Assert.Throws<ArgumentException>(() =>
            _operation.CreateMutate(new SearchReplaceParameters { Find = "target", Replacement = null! }, Array.Empty<SearchReplaceTarget>()));
    }

    [Fact]
    public async Task Build_MarkdownExactMatchesAreOrdinal()
    {
        var content = Encoding.UTF8.GetBytes("Alpha target beta Target targert target-target");
        var path = Path.Combine(_root, "notes.md");
        await File.WriteAllBytesAsync(path, content);

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "x" };
        var occurrences = await _operation.BuildOccurrencesAsync(path, parameters);

        // Ordinal exact matches only: "Target" (case) and "targert" (edit distance)
        // never match; the two occurrences inside "target-target" both do.
        Assert.Equal(new[] { "body:6", "body:33", "body:40" }, occurrences.Select(o => o.Id));
        Assert.All(occurrences, o => Assert.Equal("Body", o.Origin));

        // Preview builds occurrences without mutating the source.
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Build_FuzzyAndSemanticProvidersAreNeverCalled()
    {
        // Near misses: case variants and edit-distance variants of "target".
        const string nearMisses = "Target targert tgarget TARGET target";
        var docx = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>{nearMisses}</w:t></w:r></w:p></w:body></w:document>");
        var docxPath = Path.Combine(_root, "near-miss.docx");
        await File.WriteAllBytesAsync(docxPath, docx);
        var mdPath = Path.Combine(_root, "near-miss.md");
        await File.WriteAllBytesAsync(mdPath, Encoding.UTF8.GetBytes(nearMisses));

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "x" };
        var docxOccurrences = await _operation.BuildOccurrencesAsync(docxPath, parameters);
        var mdOccurrences = await _operation.BuildOccurrencesAsync(mdPath, parameters);

        // Behavioral proof: matching is exact/ordinal — only the one literal
        // "target" is an occurrence in either representation.
        Assert.Single(docxOccurrences);
        var mdOccurrence = Assert.Single(mdOccurrences);
        Assert.Equal("Body", mdOccurrence.Origin);

        // Structural proof: no fuzzy or semantic provider type is reachable from
        // the operation (constructor, fields, properties) or its parameters, so
        // destructive replace can never call one (spec 07 §6/§28, master §24.3).
        var forbidden = new[] { typeof(IFuzzySearchService), typeof(ISemanticSearchService) };
        var reachable = typeof(SearchReplaceOperation)
            .GetConstructors()
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .Concat(typeof(SearchReplaceOperation).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.FieldType))
            .Concat(typeof(SearchReplaceOperation).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(p => p.PropertyType))
            .Concat(typeof(SearchReplaceParameters).GetProperties().Select(p => p.PropertyType))
            .ToList();
        Assert.DoesNotContain(reachable, t => forbidden.Any(f => f.IsAssignableFrom(t)));

        // The parameter type is query + replacement only — no matching-mode knob.
        Assert.Equal(
            new[] { "Find", "Replacement" },
            typeof(SearchReplaceParameters).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Build_OccurrenceIdsContextAndOriginsAreDeterministic()
    {
        const string longParagraphPrefix = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"; // 40
        const string longParagraphSuffix = "yyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyyy"; // 40
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            $"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            "<w:p><w:r><w:t>tar</w:t></w:r><w:r><w:t>get</w:t></w:r></w:p>" +
            $"<w:p><w:r><w:t>{longParagraphPrefix}target{longParagraphSuffix}</w:t></w:r></w:p>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>target</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            "</w:body></w:document>", extraEntries: new[]
            {
                ("word/header1.xml", Encoding.UTF8.GetBytes("<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>header target</w:t></w:r></w:p></w:hdr>")),
                ("word/footer1.xml", Encoding.UTF8.GetBytes("<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>footer target</w:t></w:r></w:p></w:ftr>"))
            });
        var docxPath = Path.Combine(_root, "origins.docx");
        await File.WriteAllBytesAsync(docxPath, package);
        var mdPath = Path.Combine(_root, "long.md");
        await File.WriteAllBytesAsync(mdPath, Encoding.UTF8.GetBytes(longParagraphPrefix + "target" + longParagraphSuffix));

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "x" };
        var docxOccurrences = await _operation.BuildOccurrencesAsync(docxPath, parameters);
        var mdOccurrences = await _operation.BuildOccurrencesAsync(mdPath, parameters);

        // DOCX: stable occurrence IDs (part + origin + paragraph + logical offset)
        // covering body across runs, table, header, and footer (spec 07 §7).
        Assert.Equal(new[] { "Body", "Body", "Table", "Header", "Footer" }, docxOccurrences.Select(o => o.Origin));
        Assert.Equal(new[]
        {
            "occ_word/document.xml_Body_p0_idx0_len6",
            "occ_word/document.xml_Body_p1_idx40_len6",
            "occ_word/document.xml_Table_p2_idx0_len6",
            "occ_word/header1.xml_Header_p0_idx7_len6",
            "occ_word/footer1.xml_Footer_p0_idx7_len6"
        }, docxOccurrences.Select(o => o.Id));

        // DOCX contexts: no ellipsis inside a short paragraph; 30-char radius with
        // ellipses when the match sits in a long paragraph.
        Assert.Equal("target", docxOccurrences[0].Context);
        Assert.Equal(
            "..." + new string('x', 30) + "target" + new string('y', 30) + "...",
            docxOccurrences[1].Context);
        Assert.Equal("header target", docxOccurrences[3].Context);

        // Markdown: body:{index} id and the BAT-03 30-char context window (no ellipsis).
        var mdOccurrence = Assert.Single(mdOccurrences);
        Assert.Equal("body:40", mdOccurrence.Id);
        Assert.Equal("Body", mdOccurrence.Origin);
        Assert.Equal(
            new string('x', 30) + "target" + new string('y', 30),
            mdOccurrence.Context);
    }

    [Fact]
    public async Task Apply_DeselectedOccurrencesRemainUnchanged()
    {
        // DOCX: two occurrences inside one paragraph — only the reviewed,
        // selected occurrence may be replaced.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>target alpha target</w:t></w:r></w:p></w:body></w:document>");
        var mdBytes = Encoding.UTF8.GetBytes("target here and target there");
        var (planId, docxPath) = await SeedAsync("doc1", "reviewed.docx", package);
        var (_, mdPath) = await SeedAsync("doc2", "reviewed.md", mdBytes, planId);

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "Replaced" };
        var docxOccurrences = await _operation.BuildOccurrencesAsync(docxPath, parameters);
        var mdOccurrences = await _operation.BuildOccurrencesAsync(mdPath, parameters);
        Assert.Equal(2, docxOccurrences.Count);
        Assert.Equal(2, mdOccurrences.Count);

        // Review: the first occurrence stays, only the second is selected.
        await SelectAsync(planId, "doc1", new[] { docxOccurrences[1].Id });
        await SelectAsync(planId, "doc2", new[] { mdOccurrences[1].Id });

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, f => Assert.Equal(BatchApplyFileOutcome.Success, f.Outcome));

        // DOCX: the deselected first occurrence is untouched; only the selected one changed.
        var matcher = new LogicalTextMatcher();
        var remaining = Assert.Single(matcher.Search(docxPath, "target"));
        Assert.Equal(0, remaining.LogicalStartIndex);
        var replaced = Assert.Single(matcher.Search(docxPath, "Replaced"));
        Assert.Equal("target alpha Replaced", replaced.FullParagraphText);

        // Markdown: exact output proves the deselected occurrence's bytes are identical.
        Assert.Equal("target here and Replaced there", await File.ReadAllTextAsync(mdPath));
    }

    [Fact]
    public async Task Apply_OneFileFailureYieldsTypedPerFileOutcomes()
    {
        var goodBytes = Encoding.UTF8.GetBytes("target good");
        // Not a DOCX package: its reviewed mutation fails at apply time while
        // the other file is valid (sprint edge case "one file fails").
        var badBytes = Encoding.UTF8.GetBytes("not a docx package");
        var (planId, goodPath) = await SeedAsync("aaa", "good.md", goodBytes);
        var (_, badPath) = await SeedAsync("bbb", "bad.docx", badBytes, planId);

        var parameters = new SearchReplaceParameters { Find = "target", Replacement = "Replaced" };
        var goodOccurrences = await _operation.BuildOccurrencesAsync(goodPath, parameters);
        await SelectAsync(planId, "aaa", goodOccurrences.Select(o => o.Id).ToList());
        // The reviewed plan believed this package contained the occurrence; the
        // mutation failure must surface as a typed per-file outcome, not a silent skip.
        await SelectAsync(planId, "bbb", new[] { "occ_word/document.xml_Body_p0_idx0_len6" });

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);

        var good = result.Files.Single(f => f.DocumentId == "aaa");
        Assert.Equal(BatchApplyFileOutcome.Success, good.Outcome);
        Assert.Equal("Replaced good", await File.ReadAllTextAsync(goodPath));

        var bad = result.Files.Single(f => f.DocumentId == "bbb");
        Assert.Equal(BatchApplyFileOutcome.Failure, bad.Outcome);
        Assert.Equal("MutationFailed", bad.ReasonCode);
        Assert.Equal(badBytes, await File.ReadAllBytesAsync(badPath));

        // Typed per-file outcomes are recorded for both the success and the failure.
        Assert.Equal(2, _recorder.Records.Count);
        Assert.Contains(_recorder.Records, r => r.DocumentId == "aaa" && r.Outcome == BatchApplyFileOutcome.Success);
        Assert.Contains(_recorder.Records, r => r.DocumentId == "bbb" && r.Outcome == BatchApplyFileOutcome.Failure && r.ReasonCode == "MutationFailed");
        Assert.Equal(BatchPlanStatus.Completed, (await _plans.GetByIdAsync(planId))!.Status);
    }

    private async Task<(string PlanId, string Path)> SeedAsync(string docId, string fileName, byte[] content, string? planId = null)
    {
        var path = Path.Combine(_root, fileName);
        await File.WriteAllBytesAsync(path, content);
        await _documents.CreateAsync(new Document { Id = docId, CurrentPath = path, Filename = fileName, Extension = Path.GetExtension(fileName) });
        BatchOperationPlan plan;
        if (planId == null)
        {
            plan = new BatchOperationPlan
            {
                WorkspaceId = "ws",
                Status = BatchPlanStatus.Previewed,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            };
            await _plans.SavePlanAsync(plan);
        }
        else
        {
            plan = (await _plans.GetByIdAsync(planId))!;
        }
        plan.Files.Add(new BatchOperationPlanFile
        {
            PlanId = plan.Id,
            DocumentId = docId,
            ExpectedFingerprint = Convert.ToHexString(SHA256.HashData(content)),
            Selected = true
        });
        await _plans.SavePlanAsync(plan);
        return (plan.Id, path);
    }

    private async Task SelectAsync(string planId, string docId, IReadOnlyList<string> occurrenceIds)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var file = plan.Files.Single(f => f.DocumentId == docId);
        file.Selected = true;
        file.SelectedChangeIds = occurrenceIds.Distinct(StringComparer.Ordinal).ToList();
        await _plans.SavePlanAsync(plan);
    }

    private async Task<IReadOnlyList<SearchReplaceTarget>> CreateTargetsAsync(string planId)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var targets = new List<SearchReplaceTarget>();
        foreach (var file in plan.Files)
        {
            var document = await _documents.GetByIdAsync(file.DocumentId);
            targets.Add(new SearchReplaceTarget(document!.CurrentPath, file.ExpectedFingerprint, file.SelectedChangeIds));
        }
        return targets;
    }

    private sealed class FakeHistoryRecorder : IBatchApplyHistoryRecorder
    {
        public List<BatchApplyHistoryRecord> Records { get; } = new();
        public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReindexTrigger : IBatchApplyReindexTrigger
    {
        public List<string> DocumentIds { get; } = new();
        public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHistoryStorage : IHistoryStorage
    {
        private readonly string _snapshotRoot;
        private readonly List<SnapshotMetadata> _metadata = new();

        public FakeHistoryStorage(string root, long capacityBytes = 5L * 1024 * 1024 * 1024)
        {
            _snapshotRoot = Path.Combine(root, "snapshots");
            CapacityBytes = capacityBytes;
        }

        public long CapacityBytes { get; }

        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_snapshotRoot, snapshotId);
        public Task SaveMetadataAsync(SnapshotMetadata metadata) { _metadata.Add(metadata); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() => Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(_metadata);
        public Task DeleteSnapshotAsync(string snapshotId) { _metadata.RemoveAll(m => m.SnapshotId == snapshotId); return Task.CompletedTask; }
        public Task<long> GetTotalSizeAsync() => Task.FromResult(0L);
        public Task<long> GetCapacityBytesAsync() => Task.FromResult(CapacityBytes);
    }
}
