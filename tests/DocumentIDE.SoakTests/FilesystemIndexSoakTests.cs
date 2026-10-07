using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using Xunit;
using Xunit.Abstractions;

namespace DocumentIDE.SoakTests;

/// <summary>
/// QA-02 — Filesystem &amp; Indexing Soak.
///
/// Exercises the real watcher → debounce → identity → index pipeline over long churn
/// sessions: Word-like save bursts (same-directory temp file + atomic File.Replace),
/// create/rename/move/delete cycles, and watcher overflow/reconciliation.
///
/// Everything is real (temp filesystem, SQLite AppDb, SourceWatcher,
/// FileChangeCoordinator, DocumentIdentityService, IncrementalIndexCoordinator,
/// StartupReconciler, LocalJobScheduler, FTS). The single seam is
/// <see cref="SoakWatcherSeam"/>, which can drop forwarded events and raise the
/// overflow notification Windows raises when its watcher buffer overflows — neither is
/// deterministically producible by touching files.
///
/// Runtime budget: the default profile is CI-friendly (bounded iterations, deterministic
/// seed). Set <c>DOCIDE_QA02_EXTENDED_SOAK=1</c> to run the extended Windows soak profile
/// (same tests, longer churn); set <c>DOCIDE_SOAK_SEED</c> to override the deterministic
/// seed for reproduction. Failures write a full operation log to
/// <c>%TEMP%\docide_qa02\logs\&lt;test&gt;.log</c> and print the path in the assertion message.
/// </summary>
public sealed class FilesystemIndexSoakTests
{
    /// <summary>Deterministic default seed; override with DOCIDE_SOAK_SEED for reproduction runs.</summary>
    internal const int DefaultSoakSeed = 20261007;

    private readonly ITestOutputHelper _output;

    public FilesystemIndexSoakTests(ITestOutputHelper output)
    {
        _output = output;
    }

    internal static int ResolveSeed()
    {
        string? raw = Environment.GetEnvironmentVariable("DOCIDE_SOAK_SEED");
        return int.TryParse(raw, out int seed) ? seed : DefaultSoakSeed;
    }

    internal static bool ExtendedSoakEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("DOCIDE_QA02_EXTENDED_SOAK"), "1", StringComparison.Ordinal);

    // -------------------------------------------------------------------------
    // Required Test 1: No duplicate identity leak.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A Word-like Ctrl+S is an atomic replacement in the source folder: Word writes a
    /// temporary file and renames it over the target, which rotates the NTFS file index
    /// at the same path. Per 01-system-architecture §10.1 and 00-master §14.1 that is a
    /// *modification* of the existing document: DocumentId, canvas position, collections
    /// and history references must survive. A burst chain must never leave behind a trail
    /// of "missing" duplicate records and must never mint a new DocumentId per save.
    /// </summary>
    [Fact]
    public async Task WordLikeSaveBursts_DoNotLeakDocumentIdentities()
    {
        int seed = ResolveSeed();
        int bursts = ExtendedSoakEnabled ? 250 : 5;

        using var rig = new SoakRig(nameof(WordLikeSaveBursts_DoNotLeakDocumentIdentities), seed);
        rig.Log($"seed={seed} extended={ExtendedSoakEnabled} bursts={bursts}");

        string path = Path.Combine(rig.RootPath, "laporan-keuangan.md");
        byte[] initialBytes = Encoding.UTF8.GetBytes("# Versi 0\nKonten awal NUSA_DUA_0");
        await File.WriteAllBytesAsync(path, initialBytes);

        var original = await rig.Identity.ResolveDocumentAsync(path);
        Assert.Equal("available", original.AvailabilityStatus);
        rig.Log($"seeded DocumentId={original.Id}");

        rig.StartWatching();

        byte[] finalBytes = initialBytes;
        for (int cycle = 1; cycle <= bursts; cycle++)
        {
            byte[] bytes = Encoding.UTF8.GetBytes($"# Versi {cycle}\nSave burst {cycle} NUSA_DUA_{cycle}");
            SoakSupport.AtomicReplaceFromBytes(path, bytes);
            finalBytes = bytes;

            string expectedFingerprint = SoakSupport.Sha256Hex(bytes);
            bool persisted = await SoakSupport.WaitUntilAsync(async () =>
            {
                var doc = await rig.Documents.GetByPathAsync(path);
                return doc != null
                       && string.Equals(doc.ContentFingerprint, expectedFingerprint, StringComparison.Ordinal)
                       && doc.IndexStatus == "ready";
            }, SoakTimeouts.Persist);

            Assert.True(persisted,
                $"Save burst {cycle}/{bursts} never converged (expected fingerprint {expectedFingerprint}). " +
                $"Deterministic seed={seed}; log: {rig.LogPath}");
        }

        await rig.WaitForQuietAsync();
        var all = await rig.Documents.GetByFolderSourcePathAsync(rig.RootPath);
        string diagnostics = DescribeDocuments(all, seed, rig.LogPath);

        // 1. The live record at the path is the SAME document that existed before the bursts.
        var finalDoc = await rig.Documents.GetByPathAsync(path);
        Assert.True(finalDoc != null,
            $"No document record found for '{path}' after {bursts} save bursts. {diagnostics}");
        Assert.True(string.Equals(finalDoc!.Id, original.Id, StringComparison.Ordinal),
            $"DocumentId leaked across Word-like save bursts: original={original.Id}, final={finalDoc.Id}. {diagnostics}");

        // 2. Exactly one record (in any state) represents this physical file path.
        var related = all.Where(d =>
                string.Equals(d.CurrentPath, path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(d.LastKnownPath, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(related.Count == 1,
            $"Duplicate identity leak: expected 1 record for '{path}', found {related.Count}. {diagnostics}");

        // 3. No two records share the same NTFS identity.
        var duplicateIdentities = all
            .Where(d => !string.IsNullOrEmpty(d.FilesystemVolumeId) && !string.IsNullOrEmpty(d.FilesystemObjectId))
            .GroupBy(d => d.FilesystemVolumeId + ":" + d.FilesystemObjectId)
            .Where(g => g.Count() > 1)
            .ToList();
        Assert.True(duplicateIdentities.Count == 0,
            $"Duplicate filesystem identity shared by multiple records: {string.Join(", ", duplicateIdentities.Select(g => g.Key))}. {diagnostics}");

        Assert.True(string.Equals(finalDoc.ContentFingerprint, SoakSupport.Sha256Hex(finalBytes), StringComparison.Ordinal),
            $"Recorded fingerprint does not match final on-disk content. {diagnostics}");

        _output.WriteLine($"OK: {bursts} bursts, single DocumentId {finalDoc.Id}, no duplicate records.");
    }

    // -------------------------------------------------------------------------
    // Required Test 2: Index converges to filesystem state.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Create/save/rename/move/delete churn, then a window in which watcher events are
    /// lost (as when the Windows watcher buffer overflows), followed by the overflow
    /// notification and the real reconciliation + reindex pipeline. Afterwards the
    /// document records and FTS index must match the filesystem exactly: one available
    /// record per live file (identity preserved), missing records for deleted files, and
    /// no stale search hits for deleted content.
    /// </summary>
    [Fact]
    public async Task ChurnWithLostEventsAndOverflow_IndexConvergesToFilesystemState()
    {
        int seed = ResolveSeed();
        int normalCycles = ExtendedSoakEnabled ? 40 : 8;

        using var rig = new SoakRig(nameof(ChurnWithLostEventsAndOverflow_IndexConvergesToFilesystemState), seed);
        var churn = new ChurnHarness(rig, seed);
        rig.Log($"seed={seed} extended={ExtendedSoakEnabled} normalCycles={normalCycles}");

        // Known starting state (as if index already ran). The watcher is live, so the
        // real create → identity → index pipeline establishes each record.
        rig.StartWatching();
        await Track(churn.CreateAsync("alpha.md", Token(seed, "alpha", 0)), churn);
        await Track(churn.CreateAsync("beta.md", Token(seed, "beta", 0)), churn);
        await Track(churn.CreateAsync("gamma.md", Token(seed, "gamma", 0)), churn);

        // Phase A: normal churn through the live pipeline.
        for (int c = 1; c <= normalCycles; c++)
        {
            if (c % 3 == 0 && churn.LiveFiles.Count < 16)
                await Track(churn.CreateAsync($"generated-{c}.md", Token(seed, "new", c)), churn);
            if (c % 4 == 0 && churn.LiveFiles.Count > 1)
                Assert.True(await churn.RenameAsync(churn.PickLive(), $"r{c}"), RigMessage(rig, seed, "rename"));
            if (c % 6 == 0 && churn.LiveFiles.Count > 1)
                Assert.True(await churn.MoveToSubfolderAsync(churn.PickLive(), $"m{c}"), RigMessage(rig, seed, "move"));
            Assert.True(await churn.SaveAsync(churn.PickLive(), Token(seed, "save", c)), RigMessage(rig, seed, "save"));
        }

        // Phase B: lost-event window — the watcher drops every event (buffer overflow).
        rig.Watcher.DropRawEvents = true;
        rig.Log("drop window ON");

        var ghostCreate = await churn.CreateAsync("ghost-created.md", Token(seed, "ghost-new", 0), waitForConvergence: false);
        Assert.NotNull(ghostCreate);

        var dropped = churn.LiveFiles.Where(f => f != ghostCreate).Take(4).ToList();
        Assert.True(dropped.Count >= 4, "Phase B needs at least four pre-existing files to churn.");

        var ghostSaved = dropped[0];
        var ghostRenamed = dropped[1];
        var ghostMoved = dropped[2];
        var ghostDeleted = dropped[3];
        string ghostRenamedOldPath = ghostRenamed.Path;
        string ghostMovedOldPath = ghostMoved.Path;
        string ghostDeletedPath = ghostDeleted.Path;
        string ghostSavedFingerprintBefore = (await rig.GetByPathAsync(ghostSaved.Path))!.ContentFingerprint!;

        await churn.SaveAsync(ghostSaved, Token(seed, "ghost-save", 0), waitForConvergence: false);
        await churn.RenameAsync(ghostRenamed, "ghost-renamed", waitForConvergence: false);
        await churn.MoveToSubfolderAsync(ghostMoved, "ghost-moved", waitForConvergence: false);
        await churn.DeleteAsync(ghostDeleted, waitForConvergence: false);

        // Give the real watcher time to flush everything it observed while events are
        // being dropped, then prove none of the phase-B operations reached the pipeline.
        await Task.Delay(350);
        int droppedEvents = rig.Watcher.DroppedEventCount;
        Assert.True(droppedEvents >= 4,
            $"Fault injection did not actually drop watcher events (dropped={droppedEvents}). {RigMessage(rig, seed, "fault-injection")}");

        Assert.True(await rig.GetByPathAsync(ghostCreate.Path) == null,
            $"{RigMessage(rig, seed, "fault-injection")} The dropped create was seen by the pipeline.");
        var savedDoc = await rig.GetByPathAsync(ghostSaved.Path);
        Assert.True(savedDoc != null && string.Equals(savedDoc.ContentFingerprint, ghostSavedFingerprintBefore, StringComparison.Ordinal),
            $"{RigMessage(rig, seed, "fault-injection")} The dropped save was seen by the pipeline.");
        var renamedDoc = await rig.GetByPathAsync(ghostRenamedOldPath);
        Assert.True(renamedDoc != null && renamedDoc.AvailabilityStatus == "available",
            $"{RigMessage(rig, seed, "fault-injection")} The dropped rename was seen by the pipeline.");
        var movedDoc = await rig.GetByPathAsync(ghostMovedOldPath);
        Assert.True(movedDoc != null && movedDoc.AvailabilityStatus == "available",
            $"{RigMessage(rig, seed, "fault-injection")} The dropped move was seen by the pipeline.");
        var deletedDoc = await rig.GetByPathAsync(ghostDeletedPath);
        Assert.True(deletedDoc != null && deletedDoc.AvailabilityStatus == "available",
            $"{RigMessage(rig, seed, "fault-injection")} The dropped delete was seen by the pipeline.");

        rig.Watcher.DropRawEvents = false;
        rig.Log($"drop window OFF (dropped {droppedEvents} raw events, verified unseen by pipeline)");

        // Phase C: a visible save after the gap (on a file NOT churned during the lost-event
        // window — saving a ghost-churned file would rotate its identity before
        // reconciliation can correlate it, which is information the watcher never had),
        // then the overflow notification.
        var untouched = churn.LiveFiles.FirstOrDefault(f => f != ghostCreate && !dropped.Contains(f));
        Assert.True(untouched != null, "Phase C needs a file outside the lost-event window.");
        Assert.True(await churn.SaveAsync(untouched!, Token(seed, "post-gap", 0)), RigMessage(rig, seed, "post-gap save"));
        Assert.True(await churn.InjectOverflowAsync(), RigMessage(rig, seed, "overflow"));
        rig.Watcher.InjectOverflow(rig.Source.Id); // duplicated near-simultaneously; dedupe must absorb it
        await rig.WaitForQuietAsync();

        // Reconciliation (as the app runs on startup / after overflow) + reindex.
        var reconciliation = await rig.Reconciler.ReconcileWorkspaceAsync(rig.WorkspaceId);
        rig.Log($"reconciled: new={reconciliation.NewCount} modified={reconciliation.ModifiedCount} renamed={reconciliation.RenamedCount} missing={reconciliation.MissingCount}");
        await rig.WaitForQuietAsync();
        await rig.Indexer.ReindexWorkspaceAsync(rig.WorkspaceId);
        await rig.WaitForQuietAsync();

        var docs = await rig.Documents.GetByFolderSourcePathAsync(rig.RootPath);
        string diagnostics = DescribeDocuments(docs, seed, rig.LogPath, churn);

        // A. Every live file: exactly one available record at its path, index ready,
        //    identity preserved when it was known before the lost-event window.
        foreach (var file in churn.LiveFiles)
        {
            var doc = await rig.GetByPathAsync(file.Path);
            Assert.True(doc != null && doc.AvailabilityStatus == "available",
                $"Live file '{file.Path}' has no available record after reconciliation. {diagnostics}");
            if (file.DocumentId.Length > 0)
            {
                Assert.True(string.Equals(doc!.Id, file.DocumentId, StringComparison.Ordinal),
                    $"DocumentId not preserved for '{file.Path}': expected {file.DocumentId}, got {doc!.Id}. {diagnostics}");
            }
            Assert.True(doc!.IndexStatus == "ready",
                $"Live file '{file.Path}' is not indexed after reindex (status={doc.IndexStatus}). {diagnostics}");

            var hits = await rig.Fts.SearchAsync(file.LastToken);
            Assert.True(hits.Count == 1 && string.Equals(hits[0].DocumentId, doc.Id, StringComparison.Ordinal),
                $"Index did not converge for '{file.Path}' token='{file.LastToken}': hits=[{string.Join(", ", hits.Select(h => h.DocumentId))}], expected={doc.Id}. {diagnostics}");
        }

        // B. Every deleted file: record marked missing and no stale search hits.
        foreach (var file in churn.DeletedFiles)
        {
            var doc = await rig.GetByPathAsync(file.Path);
            Assert.True(doc != null && doc.AvailabilityStatus == "missing",
                $"Deleted file '{file.Path}' is not marked missing (doc={(doc == null ? "<null>" : doc.AvailabilityStatus)}). {diagnostics}");

            var stale = await rig.Fts.SearchAsync(file.LastToken);
            Assert.True(stale.Count == 0,
                $"Deleted file '{file.Path}' is still searchable by token '{file.LastToken}': hits=[{string.Join(", ", stale.Select(h => h.DocumentId))}]. {diagnostics}");
        }

        // C. No duplicate live records: one available record per canonical path, no
        //    shared NTFS identity, and no orphan records beyond live + deleted files.
        var available = docs.Where(d => d.AvailabilityStatus == "available").ToList();
        var duplicatePaths = available
            .GroupBy(d => d.CurrentPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicatePaths.Count == 0,
            $"Multiple available records for the same path: {string.Join(", ", duplicatePaths)}. {diagnostics}");

        var duplicateIdentities = available
            .Where(d => !string.IsNullOrEmpty(d.FilesystemVolumeId) && !string.IsNullOrEmpty(d.FilesystemObjectId))
            .GroupBy(d => d.FilesystemVolumeId + ":" + d.FilesystemObjectId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(duplicateIdentities.Count == 0,
            $"Duplicate filesystem identity shared by available records: {string.Join(", ", duplicateIdentities)}. {diagnostics}");

        int expectedRecords = churn.LiveFiles.Count + churn.DeletedFiles.Count;
        Assert.True(docs.Count == expectedRecords,
            $"Record leak: expected {expectedRecords} records (live+deleted), found {docs.Count}. {diagnostics}");

        _output.WriteLine(
            $"OK: reconciled (new={reconciliation.NewCount}, modified={reconciliation.ModifiedCount}, " +
            $"renamed={reconciliation.RenamedCount}, missing={reconciliation.MissingCount}); " +
            $"live={churn.LiveFiles.Count} deleted={churn.DeletedFiles.Count} records={docs.Count}; droppedEvents={droppedEvents}.");
    }

    // -------------------------------------------------------------------------
    // Required Test 3: Memory / job queue remains bounded.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs two identical bounded churn sessions. Between the sessions the number of
    /// document records, index states, scheduled jobs and managed memory must not grow
    /// beyond what the second session's own operations can account for. The job queue
    /// must drain to zero, and each overflow must produce exactly one reconciliation
    /// job (duplicate scheduling is absorbed by the deduplication map).
    /// </summary>
    [Fact]
    public async Task ChurnSessions_JobQueueAndMemoryRemainBounded()
    {
        int seed = ResolveSeed();
        int cyclesPerPhase = ExtendedSoakEnabled ? 120 : 15;

        using var rig = new SoakRig(nameof(ChurnSessions_JobQueueAndMemoryRemainBounded), seed);
        var churn = new ChurnHarness(rig, seed);
        rig.Log($"seed={seed} extended={ExtendedSoakEnabled} cyclesPerPhase={cyclesPerPhase}");

        rig.StartWatching();
        await Track(churn.CreateAsync("bounded-a.md", Token(seed, "bounded-a", 0)), churn);
        await Track(churn.CreateAsync("bounded-b.md", Token(seed, "bounded-b", 0)), churn);
        await Track(churn.CreateAsync("bounded-c.md", Token(seed, "bounded-c", 0)), churn);

        var phase1 = await RunBoundedPhaseAsync(rig, churn, seed, phase: 1, cycles: cyclesPerPhase);
        var phase2 = await RunBoundedPhaseAsync(rig, churn, seed, phase: 2, cycles: cyclesPerPhase);

        string diagnostics = DescribeDocuments(
            await rig.Documents.GetByFolderSourcePathAsync(rig.RootPath), seed, rig.LogPath, churn);

        Assert.True(phase1.ActiveJobsAtEnd == 0 && phase2.ActiveJobsAtEnd == 0,
            $"Job queue did not drain: active after phase1={phase1.ActiveJobsAtEnd}, phase2={phase2.ActiveJobsAtEnd}. {diagnostics}");
        Assert.True(phase1.DistinctJobs == 1,
            $"Phase 1 scheduled {phase1.DistinctJobs} distinct jobs for one overflow (expected exactly 1). {diagnostics}");
        Assert.True(phase2.DistinctJobs == 2,
            $"Phase 2 scheduled {phase2.DistinctJobs} distinct jobs in total for two overflows (expected exactly 2 — duplicates must be deduplicated). {diagnostics}");

        Assert.True(phase1.DocumentRecords == phase1.ExpectedRecords,
            $"Record leak in phase 1: {phase1.DocumentRecords} records for {phase1.ExpectedRecords} files ever created. {diagnostics}");
        Assert.True(phase2.DocumentRecords == phase2.ExpectedRecords,
            $"Record leak in phase 2: {phase2.DocumentRecords} records for {phase2.ExpectedRecords} files ever created. {diagnostics}");

        Assert.True(phase1.IndexStates <= phase1.DocumentRecords,
            $"Index state map grew beyond the document count in phase 1: states={phase1.IndexStates}, records={phase1.DocumentRecords}. {diagnostics}");
        Assert.True(phase2.IndexStates <= phase2.DocumentRecords,
            $"Index state map grew beyond the document count in phase 2: states={phase2.IndexStates}, records={phase2.DocumentRecords}. {diagnostics}");

        long growth = phase2.ManagedBytes - phase1.ManagedBytes;
        Assert.True(growth < 48 * 1024 * 1024,
            $"Managed memory grew by {growth / (1024 * 1024)} MiB across two identical churn sessions (bound: 48 MiB). {diagnostics}");

        _output.WriteLine(
            $"OK: phase1 jobs={phase1.DistinctJobs} records={phase1.DocumentRecords} states={phase1.IndexStates} mem={phase1.ManagedBytes / 1024}KiB; " +
            $"phase2 records={phase2.DocumentRecords} mem-growth={growth / 1024}KiB.");
    }

    private static async Task<PhaseSnapshot> RunBoundedPhaseAsync(SoakRig rig, ChurnHarness churn, int seed, int phase, int cycles)
    {
        int createsBefore = churn.FilesEverCreated;
        for (int c = 1; c <= cycles; c++)
        {
            if (c % 5 == 0)
            {
                var created = await churn.CreateAsync($"bounded-p{phase}-{c}.md", Token(seed, $"bounded-p{phase}", c));
                Assert.True(created != null, RigMessage(rig, seed, $"phase{phase} create c{c}"));
            }
            if (c == cycles && churn.LiveFiles.Count > 3)
            {
                Assert.True(await churn.DeleteAsync(churn.PickLive()), RigMessage(rig, seed, $"phase{phase} delete"));
            }
            Assert.True(await churn.SaveAsync(churn.PickLive(), Token(seed, $"save-p{phase}", c)),
                RigMessage(rig, seed, $"phase{phase} save"));
        }

        Assert.True(await churn.InjectOverflowAsync(), RigMessage(rig, seed, $"phase{phase} overflow"));
        await rig.WaitForQuietAsync();

        bool drained = await SoakSupport.WaitUntilAsync(
            () => Task.FromResult(rig.Jobs.GetActiveJobs().Count == 0),
            SoakTimeouts.DefaultWait);

        long managedBytes = GC.GetTotalMemory(forceFullCollection: true);

        var docs = await rig.Documents.GetByFolderSourcePathAsync(rig.RootPath);

        var snapshot = new PhaseSnapshot(
            DocumentRecords: docs.Count,
            ExpectedRecords: churn.FilesEverCreated,
            IndexStates: rig.GetIndexStates().Count,
            DistinctJobs: rig.DistinctJobCount,
            ActiveJobsAtEnd: drained ? 0 : rig.Jobs.GetActiveJobs().Count,
            ManagedBytes: managedBytes);

        rig.Log($"phase {phase} snapshot: records={snapshot.DocumentRecords}/{snapshot.ExpectedRecords} " +
                $"states={snapshot.IndexStates} jobs={snapshot.DistinctJobs} managed={snapshot.ManagedBytes}");

        Assert.True(createsBefore + 1 <= churn.FilesEverCreated, "Phase did not create any files; churn model is wrong.");
        return snapshot;
    }

    private readonly record struct PhaseSnapshot(
        int DocumentRecords,
        int ExpectedRecords,
        int IndexStates,
        int DistinctJobs,
        int ActiveJobsAtEnd,
        long ManagedBytes);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string Token(int seed, string tag, int cycle) => $"NUSA_DUA_{seed}_{tag}_{cycle}";

    private static async Task Track(Task<TrackedFile?> operation, ChurnHarness churn)
    {
        var tracked = await operation;
        Assert.True(tracked != null, "Churn harness could not establish a document record for a newly created file.");
    }

    private static string RigMessage(SoakRig rig, int seed, string operation) =>
        $"Churn operation '{operation}' failed to converge. seed={seed}; log: {rig.LogPath}";

    private static string DescribeDocuments(
        IReadOnlyList<Document> docs,
        int seed,
        string logPath,
        ChurnHarness? churn = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"--- QA-02 diagnostics (seed={seed}) ---");
        sb.AppendLine($"log: {logPath}");
        foreach (var d in docs.OrderBy(d => d.CreatedAt, StringComparer.Ordinal))
        {
            sb.AppendLine(
                $"doc id={d.Id} status={d.AvailabilityStatus} index={d.IndexStatus} " +
                $"current='{d.CurrentPath}' last='{d.LastKnownPath}' " +
                $"identity={d.FilesystemVolumeId}:{d.FilesystemObjectId} fp={d.ContentFingerprint}");
        }
        if (churn != null)
        {
            sb.AppendLine("tracked live files:");
            foreach (var f in churn.LiveFiles)
                sb.AppendLine($"  path='{f.Path}' id={f.DocumentId} token={f.LastToken}");
            sb.AppendLine("tracked deleted files:");
            foreach (var f in churn.DeletedFiles)
                sb.AppendLine($"  path='{f.Path}' id={f.DocumentId} token={f.LastToken}");
        }
        return sb.ToString();
    }
}
