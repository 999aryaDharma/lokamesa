using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Graph;
using DocumentIDE.Core.Pdf;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace DocumentIDE.PerformanceTests;

// QA-06 — Performance Benchmark Gate (backend legs).
//
// Black-box gates over real code paths:
//   - SRCH-02/05: warm FTS5 search (FtsIndex + DocumentSearchService) during index writes.
//   - GR-05 axis: bounded neighborhood relation lookup (BacklinkService) vs full-graph scan.
//   - PDF-03: streaming ordered merge peak memory (PdfMergeService).
// Canvas placement (CAN-05/CAN-07 TypeScript paths) is gated by the colocated
// vitest gate `Qa06CanvasPerformanceGate.test.ts`; graph render bounding by
// `Qa06GraphPerformanceGate.test.ts`. Numbers from all legs are recorded in
// docs/quality/performance-baseline.md; thresholds below derive from the
// measured baselines (acceptance criterion), not invented claims.
//
// TDD history: the first full run failed (graph lookup-counter accounting:
// warmup call counted against the post-warmup assertion) and the provisional
// tight bounds produced the measurements from which these thresholds were
// set. See the QA-06 handoff for the Red evidence.
public sealed class PerformanceBenchmarks : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _workDir;
    private readonly List<string> _dbPaths = new();

    public PerformanceBenchmarks(ITestOutputHelper output)
    {
        _output = output;
        _workDir = Path.Combine(Path.GetTempPath(), "qa06-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_workDir))
            {
                Directory.Delete(_workDir, recursive: true);
            }
            foreach (var dbPath in _dbPaths)
            {
                foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
                {
                    try
                    {
                        var p = dbPath + suffix;
                        if (File.Exists(p))
                        {
                            File.Delete(p);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------
    // Leg 1 — SRCH: warm FTS search stays responsive during index writes.
    // ------------------------------------------------------------------
    //
    // Measured baseline (500 seeded docs, 40 warm probes, 100 concurrent
    // index writes, dev machine Windows / .NET 10):
    //   p50 = 0.57ms, p95 = 1.74ms, max = 1.77ms.
    // Thresholds carry ~140x headroom: they trip only on structural
    // regressions (lost WAL concurrency, FTS fallback to full scan), never
    // on ordinary machine variance.
    private const double SearchP95ThresholdMs = 250.0;
    private const double SearchMaxThresholdMs = 1000.0;

    [Fact]
    public async Task Fts_WarmSearch_ResponsiveDuringIndexWrites()
    {
        const int seedDocs = 500;
        const string marker = "nusadua001benchmark";
        var (db, fts) = CreateFtsIndex();

        var entries = Enumerable.Range(0, seedDocs).Select(i =>
            new FtsIndexEntry(
                DocumentId: $"qa06-doc-{i:0000}",
                Filename: $"report-{i:0000}.md",
                Body: i % 100 == 0
                    ? $"Site inspection {marker} for sector {i}. Pump operating nominally."
                    : $"Routine administrasi paragraph {i} with filler tokens lorem ipsum dolor sit amet {i} {i * 7}.",
                Headings: $"Heading sector {i}")).ToList();
        foreach (var chunk in entries.Chunk(100))
        {
            await fts.IndexBatchAsync(chunk);
        }

        var docRepo = new FakeDocumentRepository();
        foreach (var e in entries)
        {
            docRepo.Documents[e.DocumentId] = new Document
            {
                Id = e.DocumentId,
                Filename = e.Filename ?? (e.DocumentId + ".md"),
                Extension = "md",
                DocumentType = "markdown",
                CurrentPath = $"/workspace/{e.Filename}",
            };
        }
        var search = new DocumentSearchService(fts, docRepo);

        // Warm the read path (page cache, query plan) before measuring.
        for (int i = 0; i < 10; i++)
        {
            var warm = await search.SearchAsync(marker, new DocumentSearchOptions(Limit: 50));
            Assert.Contains(warm.Results, r => r.Snippets.Count > 0 || r.MatchSources.Count > 0);
        }

        // Background index writes while warm searches run: the SRCH-05
        // realtime-UI scenario (user typing while the indexer commits).
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var writer = Task.Run(async () =>
        {
            for (int batch = 0; batch < 4; batch++)
            {
                var updates = Enumerable.Range(0, 25).Select(i =>
                    new FtsIndexEntry(
                        DocumentId: $"qa06-doc-{(batch * 25 + i):0000}",
                        Filename: $"report-{(batch * 25 + i):0000}.md",
                        Body: $"Revised administrasi paragraph batch {batch} revision {i} {marker}-adjacent.",
                        Headings: $"Heading sector revised {batch}-{i}"));
                await fts.IndexBatchAsync(updates, cts.Token);
            }
        }, cts.Token);

        const int probeCount = 40;
        var latencies = new List<double>(probeCount);
        for (int i = 0; i < probeCount; i++)
        {
            var sw = Stopwatch.StartNew();
            var result = await search.SearchAsync(marker, new DocumentSearchOptions(Limit: 50), cts.Token);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);
            Assert.Contains(result.Results, r => r.DocumentId.StartsWith("qa06-doc-", StringComparison.Ordinal));
        }

        await writer; // Must complete: no SQLITE_BUSY deadlock under WAL.

        latencies.Sort();
        double p50 = latencies[probeCount / 2];
        double p95 = latencies[(int)(probeCount * 0.95)];
        double max = latencies[^1];
        _output.WriteLine($"QA06 search-under-write: n={seedDocs} docs, probes={probeCount}, " +
            $"p50={p50:F2}ms p95={p95:F2}ms max={max:F2}ms " +
            $"(thresholds p95<{SearchP95ThresholdMs}ms max<{SearchMaxThresholdMs}ms).");

        // Spec 11 §28 gate: interactive reads run while the writer commits,
        // which requires WAL concurrency on the real FTS database.
        Assert.Equal("wal", await GetJournalModeAsync(db));

        Assert.True(p95 < SearchP95ThresholdMs,
            $"Warm search p95 {p95:F2}ms exceeds encoded threshold {SearchP95ThresholdMs}ms.");
        Assert.True(max < SearchMaxThresholdMs,
            $"Warm search max {max:F2}ms exceeds encoded threshold {SearchMaxThresholdMs}ms.");
    }

    // ------------------------------------------------------------------
    // Leg 2 — Graph: bounded neighborhood lookup vs full-graph scan.
    // ------------------------------------------------------------------
    //
    // Measured baseline (chain graphs, 500 docs/~1000 rels vs 2000
    // docs/~4000 rels, real BacklinkService, null document repo):
    //   neighborhood = 0.01ms, neighborhood/full = 0.014, 4x-growth cost = 0.89x.
    // The service issues exactly two scoped lookups per inspector call and
    // never a full GetRelationsAsync scan — the backend analog of the GR-05
    // bounded-neighborhood rule.
    private const double GraphNeighborhoodVsFullMaxRatio = 0.5;
    private const double GraphSizeIndependenceMaxRatio = 2.0;
    private const double GraphNeighborhoodMaxMs = 500.0;

    [Fact]
    public async Task Graph_NeighborhoodLookup_BoundedNotFullScan()
    {
        var small = BuildChainGraph(docCount: 500, workspaceId: "qa06-small");
        var large = BuildChainGraph(docCount: 2000, workspaceId: "qa06-large");

        var smallSvc = new BacklinkService(small, documentRepository: null);
        var largeSvc = new BacklinkService(large, documentRepository: null);

        const string smallMid = "qa06-gdoc-0250";
        const string largeMid = "qa06-gdoc-1000";

        // Warmup (JIT) outside measurement; counters baselined after it.
        await smallSvc.GetInspectorRelationsAsync("qa06-small", smallMid);
        int smallSourceBase = small.GetBySourceDocumentCalls;
        int smallTargetBase = small.GetByTargetDocumentCalls;
        int smallFullBase = small.GetRelationsCalls;
        int largeSourceBase = large.GetBySourceDocumentCalls;
        int largeTargetBase = large.GetByTargetDocumentCalls;
        int largeFullBase = large.GetRelationsCalls;

        var smallSw = Stopwatch.StartNew();
        var smallRel = await smallSvc.GetInspectorRelationsAsync("qa06-small", smallMid);
        smallSw.Stop();

        var largeSw = Stopwatch.StartNew();
        var largeRel = await largeSvc.GetInspectorRelationsAsync("qa06-large", largeMid);
        largeSw.Stop();

        // Correctness: chain links i->i+1, i->i+2 are found on both sides.
        Assert.Equal(2, smallRel.References.Count);
        Assert.Equal(2, smallRel.Backlinks.Count);
        Assert.Equal(2, largeRel.References.Count);
        Assert.Equal(2, largeRel.Backlinks.Count);

        // No full scan: exactly one scoped lookup per direction per call.
        Assert.Equal(1, small.GetBySourceDocumentCalls - smallSourceBase);
        Assert.Equal(1, small.GetByTargetDocumentCalls - smallTargetBase);
        Assert.Equal(0, small.GetRelationsCalls - smallFullBase);
        Assert.Equal(1, large.GetBySourceDocumentCalls - largeSourceBase);
        Assert.Equal(1, large.GetByTargetDocumentCalls - largeTargetBase);
        Assert.Equal(0, large.GetRelationsCalls - largeFullBase);

        // Contrast with the forbidden path: full-graph scan cost.
        var fullSw = Stopwatch.StartNew();
        var all = await large.GetRelationsAsync("qa06-large");
        fullSw.Stop();

        double neighborhoodMs = largeSw.Elapsed.TotalMilliseconds;
        double fullMs = Math.Max(fullSw.Elapsed.TotalMilliseconds, 0.01);
        double ratio = neighborhoodMs / fullMs;
        double sizeRatio = neighborhoodMs / Math.Max(smallSw.Elapsed.TotalMilliseconds, 0.01);
        _output.WriteLine($"QA06 graph: small={smallSw.Elapsed.TotalMilliseconds:F2}ms " +
            $"large-neighborhood={neighborhoodMs:F2}ms full-scan({all.Count} rels)={fullMs:F2}ms " +
            $"neighborhood/full={ratio:F3} (threshold {GraphNeighborhoodVsFullMaxRatio}), " +
            $"4x-growth cost={sizeRatio:F2}x (threshold {GraphSizeIndependenceMaxRatio}).");

        Assert.True(neighborhoodMs < GraphNeighborhoodMaxMs,
            $"Neighborhood lookup {neighborhoodMs:F2}ms exceeds {GraphNeighborhoodMaxMs}ms.");
        Assert.True(ratio < GraphNeighborhoodVsFullMaxRatio,
            $"Neighborhood/full ratio {ratio:F3} exceeds {GraphNeighborhoodVsFullMaxRatio}.");
        Assert.True(sizeRatio < GraphSizeIndependenceMaxRatio,
            $"4x graph growth cost {sizeRatio:F2}x exceeds {GraphSizeIndependenceMaxRatio}x.");
    }

    // ------------------------------------------------------------------
    // Leg 3 — PDF: streaming merge peak memory stays bounded.
    // ------------------------------------------------------------------
    //
    // Measured baselines: PDF-03 recorded 18.0% (6 x ~4.2MB inputs) on the
    // same streaming path; QA-06 re-measures at 6 x ~512KB and observes
    // 2.6-6.8% (GC-measurement noise band). Encoded threshold is the spec
    // bound: peak additional < 50% of total source bytes.
    [Fact]
    public async Task PdfMerge_PeakMemory_Bounded()
    {
        const int fileCount = 6;
        const int payloadBytes = 512 * 1024;
        var inputs = new List<string>(fileCount);
        for (int i = 0; i < fileCount; i++)
        {
            string path = Path.Combine(_workDir, $"qa06-{i:00}.pdf");
            File.WriteAllBytes(path, MinimalPdfBuilder.CreateSinglePage($"QA06-MARKER-{i:00}", 595, 842, payloadBytes));
            inputs.Add(path);
        }
        long totalSourceBytes = inputs.Sum(p => new FileInfo(p).Length);
        var service = new PdfMergeService();
        string output = Path.Combine(_workDir, "qa06-merged.pdf");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var sw = Stopwatch.StartNew();
        OrderedMergeResult? mergeResult = null;
        long peakAdditional = await service.MeasurePeakAdditionalBytesAsync(
            async () =>
            {
                mergeResult = await service.MergeOrderedAsync(
                    new OrderedMergeRequest(inputs, output, PdfCollisionBehavior.Fail));
            });
        sw.Stop();

        Assert.NotNull(mergeResult);
        Assert.Equal(PdfExportStatus.Succeeded, mergeResult.Status);
        Assert.Equal(fileCount, mergeResult.PageCount);

        double ratio = (double)peakAdditional / totalSourceBytes;
        _output.WriteLine($"QA06 pdf-merge: additional={peakAdditional:N0} bytes, " +
            $"totalSources={totalSourceBytes:N0} bytes, ratio={ratio:P1} (threshold <50%), " +
            $"wall-clock={sw.Elapsed.TotalMilliseconds:F0}ms.");

        Assert.True(peakAdditional < totalSourceBytes / 2,
            $"Peak additional bytes {peakAdditional:N0} must be < half of total source bytes {totalSourceBytes:N0}.");
    }

    // ------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------

    private (AppDb Db, FtsIndex Fts) CreateFtsIndex()
    {
        string dbPath = Path.Combine(_workDir, $"qa06-{Guid.NewGuid():N}.db");
        _dbPaths.Add(dbPath);
        var db = new AppDb(dbPath);
        db.InitializeAsync().GetAwaiter().GetResult();
        return (db, new FtsIndex(db));
    }

    private static async Task<string> GetJournalModeAsync(AppDb db)
    {
        using var connection = db.CreateConnection();
        await connection.OpenAsync();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var value = await cmd.ExecuteScalarAsync();
        return (value?.ToString() ?? string.Empty).ToLowerInvariant();
    }

    private static FakeRelationRepository BuildChainGraph(int docCount, string workspaceId)
    {
        var repo = new FakeRelationRepository();
        for (int i = 0; i < docCount; i++)
        {
            string source = $"qa06-gdoc-{i:0000}";
            foreach (int target in new[] { i + 1, i + 2 })
            {
                if (target >= docCount)
                {
                    continue;
                }
                repo.Add(new Relation
                {
                    Id = Guid.NewGuid().ToString(),
                    WorkspaceId = workspaceId,
                    SourceDocumentId = source,
                    TargetDocumentId = $"qa06-gdoc-{target:0000}",
                    RelationType = RelationType.Wikilink,
                    RelationOrigin = RelationOrigin.Explicit,
                });
            }
        }
        return repo;
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public Dictionary<string, Document> Documents { get; } = new();

        public Task<Document?> GetByIdAsync(string id) =>
            Task.FromResult(Documents.TryGetValue(id, out var doc) ? doc : null);

        public Task<Document?> GetByPathAsync(string currentPath) =>
            Task.FromResult(Documents.Values.FirstOrDefault(d => d.CurrentPath == currentPath));

        public Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId) =>
            Task.FromResult(Documents.Values.FirstOrDefault(d =>
                d.FilesystemVolumeId == volumeId && d.FilesystemObjectId == objectId));

        public Task<List<Document>> GetDocumentsByFingerprintAsync(string fingerprint) =>
            Task.FromResult(Documents.Values.Where(d => d.ContentFingerprint == fingerprint).ToList());

        public Task<IReadOnlyList<Document>> GetByFolderSourcePathAsync(string folderPath) =>
            Task.FromResult<IReadOnlyList<Document>>(Documents.Values.Where(d => d.CurrentPath.StartsWith(folderPath)).ToList());

        public Task<IReadOnlyList<Document>> GetByWorkspaceAsync(string workspaceId) =>
            Task.FromResult<IReadOnlyList<Document>>(Documents.Values.ToList());

        public Task CreateAsync(Document document)
        {
            Documents[document.Id] = document;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Document document)
        {
            Documents[document.Id] = document;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id)
        {
            Documents.Remove(id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRelationRepository : IRelationRepository
    {
        private readonly List<Relation> _all = new();
        private readonly Dictionary<string, List<Relation>> _bySource = new();
        private readonly Dictionary<string, List<Relation>> _byTarget = new();

        public int GetBySourceDocumentCalls { get; private set; }
        public int GetByTargetDocumentCalls { get; private set; }
        public int GetRelationsCalls { get; private set; }

        public void Add(Relation relation)
        {
            _all.Add(relation);
            if (!_bySource.TryGetValue(relation.SourceDocumentId, out var s))
            {
                s = new List<Relation>();
                _bySource[relation.SourceDocumentId] = s;
            }
            s.Add(relation);
            if (!_byTarget.TryGetValue(relation.TargetDocumentId, out var t))
            {
                t = new List<Relation>();
                _byTarget[relation.TargetDocumentId] = t;
            }
            t.Add(relation);
        }

        public Task AddAsync(Relation relation)
        {
            Add(relation);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Relation> relations)
        {
            foreach (var r in relations)
            {
                Add(r);
            }
            return Task.CompletedTask;
        }

        public Task<Relation?> GetByIdAsync(string id) =>
            Task.FromResult(_all.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<Relation>> GetRelationsAsync(string workspaceId, string? origin = null, string? type = null)
        {
            GetRelationsCalls++;
            return Task.FromResult<IReadOnlyList<Relation>>(_all.Where(r =>
                r.WorkspaceId == workspaceId
                && (origin == null || r.RelationOrigin == origin)
                && (type == null || r.RelationType == type)).ToList());
        }

        public Task<IReadOnlyList<Relation>> GetBySourceDocumentAsync(string workspaceId, string sourceDocumentId)
        {
            GetBySourceDocumentCalls++;
            if (_bySource.TryGetValue(sourceDocumentId, out var list))
            {
                return Task.FromResult<IReadOnlyList<Relation>>(list.Where(r => r.WorkspaceId == workspaceId).ToList());
            }
            return Task.FromResult<IReadOnlyList<Relation>>(Array.Empty<Relation>());
        }

        public Task<IReadOnlyList<Relation>> GetByTargetDocumentAsync(string workspaceId, string targetDocumentId)
        {
            GetByTargetDocumentCalls++;
            if (_byTarget.TryGetValue(targetDocumentId, out var list))
            {
                return Task.FromResult<IReadOnlyList<Relation>>(list.Where(r => r.WorkspaceId == workspaceId).ToList());
            }
            return Task.FromResult<IReadOnlyList<Relation>>(Array.Empty<Relation>());
        }

        public Task<IReadOnlyList<Relation>> GetBacklinksAsync(string workspaceId, string targetDocumentId) =>
            GetByTargetDocumentAsync(workspaceId, targetDocumentId);

        public Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations) =>
            Task.CompletedTask;

        public Task DeleteAsync(string id) => Task.CompletedTask;

        public Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType) =>
            Task.CompletedTask;
    }

    private static class MinimalPdfBuilder
    {
        public static byte[] CreateSinglePage(string marker, int pageWidth, int pageHeight, int paddingBytes)
        {
            string content = $"BT /F1 12 Tf 72 720 Td ({marker}) Tj ET\n";
            byte[] contentBytes = Encoding.ASCII.GetBytes(content);
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth} {pageHeight}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
                $"<< /Length {contentBytes.Length} >>\nstream\n{content}endstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            };

            var sb = new StringBuilder();
            sb.Append("%PDF-1.7\n");
            var offsets = new List<long>();
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(sb.Length);
                sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }
            if (paddingBytes > 0)
            {
                int emitted = 0;
                while (emitted < paddingBytes)
                {
                    int chunk = Math.Min(1024, paddingBytes - emitted);
                    sb.Append('%');
                    sb.Append('0', chunk - 1);
                    sb.Append('\n');
                    emitted += chunk;
                }
            }
            long xrefAt = sb.Length;
            sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (long offset in offsets)
            {
                sb.Append($"{offset:0000000000} 00000 n \n");
            }
            sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefAt}\n%%EOF");
            return Encoding.ASCII.GetBytes(sb.ToString());
        }
    }
}
