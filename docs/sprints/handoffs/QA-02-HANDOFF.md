# Sprint Handoff — QA-02 — Filesystem & Indexing Soak

**Status:** DONE
**Branch:** `test/qa-02-filesystem-indexing-soak`
**Commits:** `fecd2fb` (production fixes found by the soak), `6191a13` (soak test project + solution entry); completion metadata in the following docs commit.

## What changed

- New test project `tests/DocumentIDE.SoakTests` (registered in `DocumentIDE.sln`) with `FilesystemIndexSoakTests.cs` + `SoakHarness.cs`: a real filesystem + real component soak — temp source tree, real SQLite `AppDb`, real `SourceWatcher`, `FileChangeCoordinator`, `DocumentIdentityService`, `MoveCorrelationService`, `IncrementalIndexCoordinator`, `FtsIndex`, `DocumentMetadataRepository`, `StartupReconciler` and `LocalJobScheduler`. The only fake is `SoakWatcherSeam` (narrow fault-injection seam): it forwards every real watcher event and can drop forwarded events (simulating lost events during a Windows watcher-buffer overflow) and raise the overflow notification Windows raises in that situation — neither is deterministically producible by touching files.
- Automated churn harness (`ChurnHarness`): deterministic-seed Word-like saves (same-directory `.tmp` + atomic `File.Replace`, i.e. exactly what Word Ctrl+S and the app's `AtomicFileReplacer` do), creates, renames, moves into a subfolder and deletes, all with poll-with-timeout convergence waits and a tracked path→DocumentId/content-token model.
- Required tests:
  - `WordLikeSaveBursts_DoNotLeakDocumentIdentities` — the mandated first behavior (No duplicate identity leak): 5 bursts default / 250 extended; asserts the original `DocumentId` survives every save, exactly one record represents the path, no two records share an NTFS identity.
  - `ChurnWithLostEventsAndOverflow_IndexConvergesToFilesystemState` — churn; a lost-event window whose operations are *proven unseen* by the pipeline (create/save/rename/move/delete); overflow notification (duplicated near-simultaneously to exercise job dedupe); real `StartupReconciler` + `ReindexWorkspaceAsync`; asserts records and FTS match the filesystem exactly (one available record per live file with preserved identity, missing records for deleted files, no stale search hits, no duplicate paths/identities, `records == live + deleted`).
  - `ChurnSessions_JobQueueAndMemoryRemainBounded` — two identical bounded sessions (15 cycles default / 120 extended): `records == files ever created` in both, index-state map ≤ record count, queue drains to zero, exactly 1 distinct reconciliation job per overflow (2 total — dedupe absorbs duplicates), managed-memory growth bound (48 MiB; measured ~60 KiB default / ~0.7 MiB extended).
- **Production fixes (leaks/races discovered — 12-implementation-plan Task 14.2 “Fix leaks/races discovered”; sprint allows changes needed to satisfy tests):**
  - `DocumentIdentityService` — an in-place atomic replace (Word save) rotates the NTFS file index; the old code treated that as “two distinct duplicate files”, marked the previous record missing and minted a new `DocumentId` on **every save**. A document found by path now adopts the probed identity in place and keeps its `DocumentId` (master §14.1 “update identity/path”, 01-architecture §10.1 “Word save → mark document Modified”). Distinct copies at other paths still receive distinct DocumentIds (FS-02 required test still passes).
  - `FileChangeCoordinator` — debounce timers were processed concurrently, so a move’s `Deleted` (source) could overtake its `Created` (destination) and clobber the moved document’s path/status back to `missing`. The debounced verification stage is now serialized on a semaphore; raw callbacks stay non-blocking.
  - `IncrementalIndexCoordinator.ReindexWorkspaceAsync` — previously attempted a read for missing documents (publishing `error`) while leaving stale FTS rows searchable. Missing documents now leave the active index through the existing `Deleted` pipeline path, so index state converges to the filesystem after reconciliation with lost events.
- The extended soak profile is gated behind `DOCIDE_QA02_EXTENDED_SOAK=1` (same tests, longer churn); `DOCIDE_SOAK_SEED` overrides the deterministic seed (default `20261007`). Failures write a full operation log to `%TEMP%\docide_qa02\logs\<test>.log` and print the path in the assertion message.

## Files changed

- `tests/DocumentIDE.SoakTests/DocumentIDE.SoakTests.csproj` (new; net10.0-windows, xunit, refs Core + Infrastructure)
- `tests/DocumentIDE.SoakTests/FilesystemIndexSoakTests.cs` (new; the 3 required tests + rig wiring)
- `tests/DocumentIDE.SoakTests/SoakHarness.cs` (new; real-component rig, watcher fault seam, churn harness)
- `src/DocumentIDE.Core/Filesystem/DocumentIdentityService.cs` (same-path identity adoption)
- `src/DocumentIDE.Core/Filesystem/FileChangeCoordinator.cs` (serialized debounced verification stage)
- `src/DocumentIDE.Core/Indexing/IncrementalIndexCoordinator.cs` (missing docs exit the active index on workspace reindex)
- **Shared-file edit:** `DocumentIDE.sln` — added `DocumentIDE.SoakTests` project entry/configs/nesting only (controller ruled QA-01 + QA-02 are the only sprints touching `.sln`; merge order QA-01 then QA-02).
- No other files touched; `docs/sprints/SPRINT-STATUS.md` intentionally untouched (controller-owned).

## Verification

- RED (mandated first behavior, test written before the fix): `dotnet test tests\DocumentIDE.SoakTests\DocumentIDE.SoakTests.csproj` → `Failed: 1, Passed: 0`; 5 save bursts produced 5 extra `missing` records and the `DocumentId` rotated on every save (`original=0503b1cb…`, `final=7fc79b50…`). Log: `.superpowers/sdd/QA-02-filesystem-indexing-soak/red-1-word-save-bursts.log`.
- GREEN: same command → `Passed: 3, Failed: 0` (after the three fixes; RED/GREEN per behavior: identity `red-1`→`green-1`; move race `red-2-move-delete-create-race.log` + `red-2-soak-operation-log.txt` → `green-2-convergence.log`; stale FTS after reconcile `red-3-stale-fts-after-reconcile.log` → `green-3-convergence.log`).
- Default soak suite stability: 3 consecutive full runs → 3/3 passed each (8–15 s).
- Extended Windows soak (Step 5): `$env:DOCIDE_QA02_EXTENDED_SOAK=1; dotnet test tests\DocumentIDE.SoakTests\...` → **3/3 passed, 1 m 16 s** (250 save bursts; 40 churn cycles; 17 dropped watcher events; reconcile new=1 modified=1 renamed=2 missing=1; 2×120 bounded cycles with `27/27` then `51/51` records, jobs 1→2, ~0.7 MiB memory growth). Log: `.../extended-soak.log`.
- Affected `dotnet test tests\DocumentIDE.Core.Tests` → **440 passed, 0 failed, 0 skipped**.
- Affected `dotnet test tests\DocumentIDE.IntegrationTests` → **266 passed, 0 failed, 0 skipped** (the two release-artifact tests passed once the controller-provided `artifacts/` landed; before that they failed only on missing `artifacts/` files, unrelated to this change).
- Flake audit: 10 full IntegrationTests runs on this tree vs 10 on the base commit (changes stashed) — both trees show the same class of ~1/10 transient failures in *unrelated* parallel-load-sensitive tests (`SourceWatcherTests.Physical_SourceWatcher_Detects_Real_File_Creation`, `SafeApplyTests.Apply_CancellationLeavesNoHalfWrittenOriginal`, `FtsIndexTests.ConcurrentSearchWhileUpdate…`, `BatchPreviewHostTests…`); each failed at most once in 10 runs and passed standalone, on both trees.
- `dotnet sln DocumentIDE.sln list` → lists `tests\DocumentIDE.SoakTests\DocumentIDE.SoakTests.csproj`.
- `scripts/verify.ps1` — controller runs full repository verification on this worktree.

Step → test evidence:

| Step | Evidence |
|---|---|
| 1. Automated churn harness | `SoakHarness.cs` (`ChurnHarness`, deterministic seed, poll-with-timeout waits, operation log) used by all three tests |
| 2. Create/save/rename/move/delete cycles | `ChurnWithLostEventsAndOverflow_IndexConvergesToFilesystemState` (Phase A + ghost window), `ChurnSessions_JobQueueAndMemoryRemainBounded` |
| 3. Watcher overflow / reconciliation | `SoakWatcherSeam` lost-event window + `InjectOverflowAsync` + real `StartupReconciler` + `ReindexWorkspaceAsync` in test 2; per-phase overflow in test 3 |
| 4. Duplicate jobs/DocumentIds + memory growth | Test 1 (one identity/record), test 3 (`records == files`, distinct jobs 1→2, queue drained, memory bound of 48 MiB vs measured ≤1 MiB) |
| 5. Extended Windows soak | `DOCIDE_QA02_EXTENDED_SOAK=1` run → 3/3 passed in 1 m 16 s |

## Acceptance criteria

- [x] Failures produce reproducible logs — deterministic seed (`20261007`, override `DOCIDE_SOAK_SEED`), every churn operation and every raw/processed watcher event is logged with the post-event DB state to `%TEMP%\docide_qa02\logs\<test>.log`; assertion messages include the seed, the log path and a document/tracked-file diagnostics dump. All RED runs in this sprint were reproduced and archived under `.superpowers/sdd/QA-02-filesystem-indexing-soak/`.

## Security & file-safety gate

- [x] No user file mutated through an unreviewed/bypass path — the soak only reads/writes its own throwaway `%TEMP%\docide_qa02\...` tree; the production changes are read/index/DB-state paths (identity adoption, event serialization, reindex state), never source-content mutation. “Word-like saves” are performed by the test itself on temp files.
- [x] No arbitrary path or secret is exposed — no new endpoints, no path/secret surfaced anywhere; soak logs contain temp paths only.
- [x] Dependency/security boundaries remain intact — Core + Infrastructure + test project; no new packages (same xunit/test SDK versions as existing test projects); no privileged API or ShareHost surface touched.
- [x] No destructive action silently broadened its scope — no deletes outside the soak’s own temp tree; the coordinator fix adds serialization only; missing-document reindex emits the existing typed `Deleted` path (FTS removal + missing metadata) instead of an error state.
- [x] Relevant higher-level safety rules remain stronger than convenience — the identity fix aligns with 00-master §14.1/§48 and 01-architecture §10.1; ambiguous cross-path identities are still never auto-merged and distinct copies keep distinct DocumentIds (FS-02 suite green).

## Deviations / spec conflicts

- The sprint’s planned artifact was the test file; the three production fixes above were required by the mandated tests (“changes needed to satisfy tests/acceptance criteria are in scope”; 12-implementation-plan Task 14.2 explicitly says “Fix leaks/races discovered”). Flagged prominently for review.
- `SoakHarness.cs` was added inside the new test project (harness vs. tests split). The planned-path mapping is unchanged: the required tests live in `tests/DocumentIDE.SoakTests/FilesystemIndexSoakTests.cs`.
- The extended soak is the same three tests under `DOCIDE_QA02_EXTENDED_SOAK=1` rather than a permanently-skipped test, so the default suite stays CI-friendly with 0 skipped tests.

## Risks discovered

- `LocalJobScheduler._jobsById` retains completed job records for the process lifetime (bounded per operation, but unbounded across arbitrarily long sessions). The default and extended profiles show negligible growth; a future sprint should consider pruning completed jobs. Not a leak of the pipeline itself.
- Serializing the debounced verification stage is a correctness-first tradeoff: a slow resolution (e.g. blocked file) delays other debounced events. Raw callbacks remain non-blocking and events are debounced first.
- A dropped *rename* followed by an atomic save of the same file before reconciliation is inherently uncorrelatable (both the rename event and the pre-save identity are gone); the pipeline safely creates a new DocumentId instead of silently merging identities. Documented in the test comment; not a defect.
- A near-simultaneous duplicate overflow enqueues through `LocalJobScheduler` dedupe (same `(type, folderSource, fingerprint)` key) — verified in test 2; the stub reconciliation `DelegateJob` still only reports progress, so real overflow-reconciliation wiring remains a product-integration follow-up (pre-existing, out of QA-02 scope).

## Next eligible sprint(s)

- Unchanged by this sprint by itself: `REL-02` still needs `QA-01`, `QA-04`, `QA-05`, `QA-06` (and `QA-02`, now done). Controller updates `SPRINT-STATUS.md`.
