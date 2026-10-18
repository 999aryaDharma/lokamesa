using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
using Xunit;

namespace DocumentIDE.Core.Tests.Batch;

/// <summary>
/// BAT-07 — Regex Advanced Replace tests.
/// Covers the sprint's five steps and three required tests: regex OFF by
/// default, catastrophic fixture times out safely, capture-group preview
/// matches apply. Apply always flows through the BAT-04
/// <see cref="BatchApplyEngine"/> with reviewed fingerprints and selected
/// occurrence IDs only.
/// </summary>
public sealed class RegexReplaceTests : IDisposable
{
    private readonly string _root;
    private readonly AppDb _db;
    private readonly BatchPlanRepository _plans;
    private readonly DocumentRepository _documents;
    private readonly FakeHistoryRecorder _recorder;
    private readonly FakeReindexTrigger _reindex;
    private readonly BatchApplyEngine _engine;
    private readonly RegexReplaceOperation _operation;

    public RegexReplaceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "regex-replace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _plans = new BatchPlanRepository(_db);
        _documents = new DocumentRepository(_db);
        new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "regex-replace" }).GetAwaiter().GetResult();
        _recorder = new FakeHistoryRecorder();
        _reindex = new FakeReindexTrigger();
        _engine = new BatchApplyEngine(
            _plans, _documents,
            new RecoverySnapshotService(new FakeHistoryStorage(_root)),
            new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);
        _operation = new RegexReplaceOperation(new StrictTextMutator());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    // ------------------------------------------------------------------
    // Step 1 — explicit Advanced/Regex toggle (mandated first behavior).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Regex_IsOffByDefault_AdvancedToggleRequired()
    {
        // BAT-07 mandated first behavior: regex is OFF by default (spec 07 §6,
        // master §24.3) — the toggle is an explicit opt-in on the parameters.
        Assert.False(new RegexReplaceParameters().RegexEnabled);

        var path = Path.Combine(_root, "default-off.md");
        await File.WriteAllTextAsync(path, "a.c abc a.c");

        // Default (toggle OFF): metacharacters stay literal — exact/ordinal
        // semantics identical to the BAT-05 exact operation.
        var exact = await _operation.BuildOccurrencesAsync(path, new RegexReplaceParameters
        {
            Find = "a.c",
            Replacement = "X"
        });
        Assert.Equal(new[] { "body:0", "body:8" }, exact.Select(o => o.Id).ToArray());
        Assert.All(exact, o => Assert.Equal("X", o.ReplacementOutput));

        // Explicit Advanced/Regex toggle ON: "." becomes a metacharacter.
        var regex = await _operation.BuildOccurrencesAsync(path, new RegexReplaceParameters
        {
            Find = "a.c",
            Replacement = "X",
            RegexEnabled = true
        });
        Assert.Equal(new[] { "body:0", "body:4", "body:8" }, regex.Select(o => o.Id).ToArray());

        // Preview never mutates the source file.
        Assert.Equal("a.c abc a.c", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void RegexOperation_ExposesOnlyPreviewAndReviewedApplyEntryPoints()
    {
        // "Regex never bypasses preview" (acceptance criterion): the only
        // public surface is occurrence preview building and a mutate delegate
        // that requires reviewed targets — there is no direct apply API.
        var methods = typeof(RegexReplaceOperation)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { "BuildOccurrencesAsync", "CreateMutate" }, methods);

        // No fuzzy/semantic provider is reachable from the operation or its
        // parameters (spec 07 §6: fuzzy/semantic OFF; regex is the only
        // advanced mode and only behind its own toggle).
        var forbidden = new[] { typeof(IFuzzySearchService), typeof(ISemanticSearchService) };
        var reachable = typeof(RegexReplaceOperation)
            .GetConstructors()
            .SelectMany(c => c.GetParameters().Select(p => p.ParameterType))
            .Concat(typeof(RegexReplaceOperation).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.FieldType))
            .Concat(typeof(RegexReplaceOperation).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(p => p.PropertyType))
            .Concat(typeof(RegexReplaceParameters).GetProperties().Select(p => p.PropertyType))
            .ToList();
        Assert.DoesNotContain(reachable, t => forbidden.Any(f => f.IsAssignableFrom(t)));
    }

    // ------------------------------------------------------------------
    // Step 2 — compiled regex with match timeout / non-backtracking option.
    // ------------------------------------------------------------------

    [Fact]
    public async Task MatchTimeoutParameter_IsHonored_AndFailsFast()
    {
        // Fast catastrophic fixture: 30 a's + "!" blows up ^…$ backtracking
        // (measured >120s without a timeout); with a 100ms budget it must
        // fail safely and quickly — never hang the suite.
        var path = Path.Combine(_root, "catastrophic.md");
        await File.WriteAllTextAsync(path, new string('a', 30) + "!");
        var parameters = new RegexReplaceParameters
        {
            Find = "^(a+)+$",
            Replacement = "x",
            RegexEnabled = true,
            MatchTimeout = TimeSpan.FromMilliseconds(100)
        };

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<RegexMatchTimeoutException>(() =>
            _operation.BuildOccurrencesAsync(path, parameters));
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"expected a fast typed timeout, took {stopwatch.ElapsedMilliseconds}ms");

        // A non-positive timeout is a typed parameter error, applied before file access.
        var invalid = await Assert.ThrowsAsync<ArgumentException>(() =>
            _operation.BuildOccurrencesAsync(path, parameters with { MatchTimeout = TimeSpan.Zero }));
        Assert.Contains("InvalidParameters", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonBacktrackingOption_CompletesCatastrophicFixtureSafely()
    {
        // The optional non-backtracking engine (supported by this runtime)
        // evaluates the same pathological pattern in linear time — no timeout,
        // no hang, deterministic result.
        var path = Path.Combine(_root, "catastrophic-nonbacktracking.md");
        await File.WriteAllTextAsync(path, new string('a', 30) + "!");
        var parameters = new RegexReplaceParameters
        {
            Find = "^(a+)+$",
            Replacement = "x",
            RegexEnabled = true,
            NonBacktracking = true,
            MatchTimeout = TimeSpan.FromMilliseconds(100)
        };

        var stopwatch = Stopwatch.StartNew();
        var occurrences = await _operation.BuildOccurrencesAsync(path, parameters);
        stopwatch.Stop();

        Assert.Empty(occurrences); // "aaa…a!" cannot satisfy ^…$
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"expected linear-time evaluation, took {stopwatch.ElapsedMilliseconds}ms");
    }

    // ------------------------------------------------------------------
    // Step 3 — preview captures / replacement output.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Preview_ShowsExpandedCaptureOutput()
    {
        var path = Path.Combine(_root, "captures.md");
        await File.WriteAllTextAsync(path, "John Smith");
        var parameters = new RegexReplaceParameters
        {
            Find = @"(\w+) (Smith)",
            Replacement = "$1-S",
            RegexEnabled = true
        };

        var preview = Assert.Single(await _operation.BuildOccurrencesAsync(path, parameters));

        Assert.Equal("body:0", preview.Id);
        Assert.Equal("Body", preview.Origin);
        Assert.Equal("John Smith", preview.Context);
        // Regex preview must show the exact captured result (spec 07 §16):
        // the expanded output, not the raw substitution template.
        Assert.Equal("John-S", preview.ReplacementOutput);

        // Preview builds occurrences without mutating the source.
        Assert.Equal("John Smith", await File.ReadAllTextAsync(path));
    }

    // ------------------------------------------------------------------
    // Step 4 — reject invalid/pathological expressions cleanly.
    // ------------------------------------------------------------------

    [Fact]
    public async Task InvalidPattern_IsRejectedWithTypedError_BeforeAnyFileAccess()
    {
        // The path never exists: a typed rejection here proves the pattern was
        // compiled and rejected before any file access or matching.
        var missingPath = Path.Combine(_root, "never-created.docx");

        var invalid = new RegexReplaceParameters { Find = "[unclosed", Replacement = "x", RegexEnabled = true };
        var preview = await Assert.ThrowsAsync<ArgumentException>(() =>
            _operation.BuildOccurrencesAsync(missingPath, invalid));
        Assert.Contains("InvalidRegexPattern", preview.Message, StringComparison.Ordinal);

        // CreateMutate compiles eagerly: no mutate delegate ever exists for a bad pattern.
        var mutate = Assert.Throws<ArgumentException>(() =>
            _operation.CreateMutate(invalid, Array.Empty<SearchReplaceTarget>()));
        Assert.Contains("InvalidRegexPattern", mutate.Message, StringComparison.Ordinal);

        // A pattern incompatible with the opt-in non-backtracking engine is
        // rejected cleanly too ("where the runtime supports it").
        var incompatible = invalid with { Find = "(?<=foo)bar", NonBacktracking = true };
        var rejected = Assert.Throws<ArgumentException>(() =>
            _operation.CreateMutate(incompatible, Array.Empty<SearchReplaceTarget>()));
        Assert.Contains("InvalidRegexPattern", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathologicalPattern_TimesOutSafely_OnDocxCrossRunMatching()
    {
        // The timeout must bound DOCX logical (cross-run) matching, not just
        // Markdown: 30 a's + "!" split across two runs, pattern ^…$ would
        // backtrack exponentially forever without the per-match budget.
        var documentXml =
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p>" +
            "<w:r><w:t>" + new string('a', 15) + "</w:t></w:r>" +
            "<w:r><w:t>" + new string('a', 15) + "!</w:t></w:r>" +
            "</w:p></w:body></w:document>";
        var path = CreateTestDocx(documentXml);
        var parameters = new RegexReplaceParameters
        {
            Find = "^(a+)+$",
            Replacement = "x",
            RegexEnabled = true,
            MatchTimeout = TimeSpan.FromMilliseconds(100)
        };

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<RegexMatchTimeoutException>(() =>
            _operation.BuildOccurrencesAsync(path, parameters));
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 3000,
            $"expected a fast typed timeout on DOCX matching, took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ZeroLengthMatches_AreRejectedTyped_NotSilentlySkipped()
    {
        // A zero-length match has no reviewable span; silently skipping it at
        // apply time would break preview==apply, so it is rejected typed.
        var path = Path.Combine(_root, "zero.md");
        await File.WriteAllTextAsync(path, "abc");
        var parameters = new RegexReplaceParameters { Find = "x*", Replacement = "!", RegexEnabled = true };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _operation.BuildOccurrencesAsync(path, parameters));
        Assert.Equal("ZeroLengthMatch", exception.Message);
    }

    // ------------------------------------------------------------------
    // Step 5 — apply only reviewed match IDs (through the BAT-04 engine).
    // ------------------------------------------------------------------

    [Fact]
    public async Task Apply_ReviewedMatchIdsOnly_PreviewMatchesApply()
    {
        const string content = "John Smith and Jane Smith";
        // Same text in DOCX, split across runs — regex matching and capture
        // expansion must cross run boundaries.
        var package = CreateTestDocxBytes(
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p>" +
            "<w:r><w:t>John Sm</w:t></w:r><w:r><w:t>ith and Jane Sm</w:t></w:r><w:r><w:t>ith</w:t></w:r>" +
            "</w:p></w:body></w:document>");
        var (planId, docxPath) = await SeedAsync("doc1", "reviewed.docx", package);
        var (_, mdPath) = await SeedAsync("doc2", "reviewed.md", Encoding.UTF8.GetBytes(content), planId);

        var parameters = new RegexReplaceParameters
        {
            Find = @"(\w+) (Smith)",
            Replacement = "$1-S",
            RegexEnabled = true
        };

        var docxOccurrences = await _operation.BuildOccurrencesAsync(docxPath, parameters);
        var mdOccurrences = await _operation.BuildOccurrencesAsync(mdPath, parameters);
        Assert.Equal(2, docxOccurrences.Count);
        Assert.Equal(2, mdOccurrences.Count);

        // The reviewed promise: capture-group preview output per occurrence.
        Assert.Equal(new[] { "John-S", "Jane-S" }, docxOccurrences.Select(o => o.ReplacementOutput));
        Assert.Equal(new[] { "John-S", "Jane-S" }, mdOccurrences.Select(o => o.ReplacementOutput));
        Assert.All(docxOccurrences, o => Assert.Equal("Body", o.Origin));

        // Review: only the second occurrence of each file is selected.
        await SelectAsync(planId, "doc1", new[] { docxOccurrences[1].Id });
        await SelectAsync(planId, "doc2", new[] { mdOccurrences[1].Id });

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        // Recovery-snapshot gate ran; both files went through the engine boundary.
        Assert.NotNull(result.SnapshotId);
        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, f => Assert.Equal(BatchApplyFileOutcome.Success, f.Outcome));

        // Preview == apply for the selected occurrence (identical expansion).
        var matcher = new LogicalTextMatcher();
        Assert.Equal("John Smith and Jane-S",
            Assert.Single(matcher.Search(docxPath, "Jane-S")).FullParagraphText);
        Assert.Equal("John Smith and Jane-S", await File.ReadAllTextAsync(mdPath));

        // The deselected first occurrence is untouched in both representations.
        Assert.Single(matcher.Search(docxPath, "John Smith"));
        Assert.Empty(matcher.Search(docxPath, "John-S"));
    }

    [Fact]
    public async Task Apply_ChangedFileAfterPreview_IsSkippedAsStale()
    {
        var (planId, path) = await SeedAsync("doc", "stale.md", Encoding.UTF8.GetBytes("John Smith"));
        var parameters = new RegexReplaceParameters
        {
            Find = @"(\w+) (Smith)",
            Replacement = "$1-S",
            RegexEnabled = true
        };
        var occurrences = await _operation.BuildOccurrencesAsync(path, parameters);
        await SelectAsync(planId, "doc", occurrences.Select(o => o.Id).ToList());

        // The file changes after preview: the reviewed fingerprint no longer matches.
        var changed = Encoding.UTF8.GetBytes("John Smith changed after preview");
        await File.WriteAllBytesAsync(path, changed);

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Stale", skipped.ReasonCode);
        Assert.Equal(changed, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_OneFileFailure_YieldsTypedPerFileOutcomes()
    {
        var goodBytes = Encoding.UTF8.GetBytes("John Smith");
        // Not a DOCX package: its reviewed mutation fails at apply time while
        // the other file is valid (sprint edge case "one file fails").
        var badBytes = Encoding.UTF8.GetBytes("not a docx package");
        var (planId, goodPath) = await SeedAsync("aaa", "good.md", goodBytes);
        var (_, badPath) = await SeedAsync("bbb", "bad.docx", badBytes, planId);

        var parameters = new RegexReplaceParameters
        {
            Find = @"(\w+) (Smith)",
            Replacement = "$1-S",
            RegexEnabled = true
        };
        var goodOccurrences = await _operation.BuildOccurrencesAsync(goodPath, parameters);
        await SelectAsync(planId, "aaa", goodOccurrences.Select(o => o.Id).ToList());
        await SelectAsync(planId, "bbb", new[] { "occ_word/document.xml_Body_p0_idx0_len10" });

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);

        var good = result.Files.Single(f => f.DocumentId == "aaa");
        Assert.Equal(BatchApplyFileOutcome.Success, good.Outcome);
        Assert.Equal("John-S", await File.ReadAllTextAsync(goodPath));

        var bad = result.Files.Single(f => f.DocumentId == "bbb");
        Assert.Equal(BatchApplyFileOutcome.Failure, bad.Outcome);
        Assert.Equal("MutationFailed", bad.ReasonCode);
        Assert.Equal(badBytes, await File.ReadAllBytesAsync(badPath));

        // Typed per-file outcomes recorded for both the success and the failure.
        Assert.Equal(2, _recorder.Records.Count);
        Assert.Contains(_recorder.Records, r => r.DocumentId == "aaa" && r.Outcome == BatchApplyFileOutcome.Success);
        Assert.Contains(_recorder.Records, r => r.DocumentId == "bbb" && r.Outcome == BatchApplyFileOutcome.Failure && r.ReasonCode == "MutationFailed");
    }

    [Fact]
    public async Task Apply_UnknownReviewedOccurrenceId_FailsClosed()
    {
        var content = Encoding.UTF8.GetBytes("John Smith");
        var (planId, path) = await SeedAsync("doc", "fail-closed.md", content);
        var parameters = new RegexReplaceParameters
        {
            Find = @"(\w+) (Smith)",
            Replacement = "$1-S",
            RegexEnabled = true
        };
        var occurrences = await _operation.BuildOccurrencesAsync(path, parameters);
        Assert.Single(occurrences);

        // A reviewed selection the file does not contain must fail closed —
        // the mutation never guesses which occurrence was meant.
        await SelectAsync(planId, "doc", new[] { "body:9999" });

        var mutate = _operation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var failure = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Failure, failure.Outcome);
        Assert.Equal("MutationFailed", failure.ReasonCode);
        Assert.Contains("SelectedOccurrenceNotFound", failure.Detail, StringComparison.Ordinal);
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
    }

    // ------------------------------------------------------------------
    // Test harness: reviewed-plan seeding + BAT-04 engine wiring (mirrors
    // the BAT-05 integration harness so regex apply uses the exact same
    // fingerprint/snapshot/atomic-replace boundary).
    // ------------------------------------------------------------------

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

    /// <summary>Minimal valid DOCX package bytes with the given document XML.</summary>
    private byte[] CreateTestDocxBytes(string documentXml)
    {
        using var memory = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var ct = archive.CreateEntry("[Content_Types].xml");
            using (var w = new StreamWriter(ct.Open()))
            {
                w.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                        <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                        <Default Extension="xml" ContentType="application/xml"/>
                        <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                    </Types>
                    """);
            }

            var rels = archive.CreateEntry("_rels/.rels");
            using (var w = new StreamWriter(rels.Open()))
            {
                w.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                    </Relationships>
                    """);
            }

            var doc = archive.CreateEntry("word/document.xml");
            using (var w = new StreamWriter(doc.Open()))
            {
                w.Write(documentXml);
            }
        }
        return memory.ToArray();
    }

    /// <summary>Writes the minimal DOCX package to disk and returns its path.</summary>
    private string CreateTestDocx(string documentXml)
    {
        string filePath = Path.Combine(_root, $"test_{Guid.NewGuid():N}.docx");
        File.WriteAllBytes(filePath, CreateTestDocxBytes(documentXml));
        return filePath;
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
        public Task SaveMetadataAsync(SnapshotMetadata metadata) { _metadata.RemoveAll(m => m.SnapshotId == metadata.SnapshotId); _metadata.Add(metadata); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() => Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(_metadata);
        public Task DeleteSnapshotAsync(string snapshotId) { _metadata.RemoveAll(m => m.SnapshotId == snapshotId); return Task.CompletedTask; }
        public Task<long> GetTotalSizeAsync() => Task.FromResult(0L);
        public Task<long> GetCapacityBytesAsync() => Task.FromResult(CapacityBytes);
    }
}
