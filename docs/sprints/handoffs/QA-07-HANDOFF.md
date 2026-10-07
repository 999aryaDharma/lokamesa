# Sprint Handoff — QA-07 — Crash & Interrupted-Operation Recovery

**Status:** DONE
**Branch:** `fix/qa-07-crash-interrupted-operation-recovery`
**Commits:** `4f2b3aa` (startup recovery production piece + safety test project, 10 tests), `d006140` (fault-path tests: typed repair failure, unavailable source), `d8d7b08` (review fix: ready only after FTS write), `fc414be` (review fix: recovery invoked on both host startups); completion metadata in the following docs commit.

## What changed

- New test project `tests/DocumentIDE.SafetyTests` (registered in `DocumentIDE.sln`) with `CrashRecoveryTests.cs`: 12 real-filesystem crash-recovery tests (temp dirs, real SQLite persistence stack, real `RecoverySnapshotService`, real `AtomicFileReplacer`, real parsers/FTS). No filesystem mocks; repository fault injection is used only to test the typed failure path.
- New production piece `StartupRecoveryService` (`DocumentIDE.Core/Filesystem`) — **there was no startup recovery for interrupted batch operations or orphan apply temps before this sprint**; implemented here because Step 5 and Required Test 2 had no production implementation:
  - reconciles every batch plan left in `Executing` to `Failed` (never `Completed`; success is never inferred from an interrupted operation), returning a typed `StartupRecoveryReport` (`InterruptedPlanRepair`; `RepairFailed` surfaced, never silently swallowed);
  - removes orphan transactional `~apply-<32 hex>.tmp` artifacts under configured folder sources only, recursively, never following reparse points, skipping hidden folders (mirrors `StartupReconciler`), with typed `OrphanTempFileCleanup` outcomes (`DeleteFailed` surfaced);
  - source documents are never modified; recovery data (snapshots) is never deleted.
- New `ApplyTempFiles` (`DocumentIDE.Core/Filesystem`): single source of truth for apply temp naming (`CreatePath` / `IsApplyTempFileName`). `AtomicFileReplacer` was refactored to use it (no behavior change) so the cleaner can never disagree with the writer about what is safe to delete.
- **Startup wiring (review fix `fc414be`):** `StartupRecoveryService.RecoverAsync` now runs once at product startup in both real hosts — the API (`Program.cs` startup scope, after `AppDb.InitializeAsync`, before serving requests) and the desktop product host (`WebAppHost.InitializeAndStartAsync`, same position). Both register `IWorkspaceRepository`/`IFolderSourceRepository`/`StartupRecoveryService`; the typed report is logged via the shared `DocumentIDE.Api.StartupRecoveryLogging` helper (counts + per-plan ids + non-clean details; no new HTTP endpoints). Safe no-op on an empty database, proven by host tests. `LocalJobScheduler` (FND-04) keeps jobs in memory only, so a crash discards them by design — orphaned jobs materialize exactly as the orphan temp artifacts plus stuck `Executing` plans that this service repairs.
- **Indexing lifecycle (review fix `d8d7b08`):** `SaveDocxMetadataAsync`/`SaveMarkdownMetadataAsync` no longer write `index_status='ready'`; the coordinator's post-FTS `MarkStatusAsync(..., "ready")` is the single ready transition. A hard process kill between the metadata save and the FTS write can no longer leave a document claiming `ready` with no searchable rows.

## Files changed

- `tests/DocumentIDE.SafetyTests/DocumentIDE.SafetyTests.csproj` (new; net10.0-windows, xunit, refs Core + Infrastructure)
- `tests/DocumentIDE.SafetyTests/CrashRecoveryTests.cs` (new; 12 tests)
- `src/DocumentIDE.Core/Filesystem/StartupRecoveryService.cs` (new production; typed report records)
- `src/DocumentIDE.Core/Filesystem/ApplyTempFiles.cs` (new production; temp-naming single source of truth)
- `src/DocumentIDE.Api/StartupRecoveryLogging.cs` (new; shared typed recovery log summary)
- `tests/DocumentIDE.IntegrationTests/Filesystem/StartupRecoveryHostTests.cs` (new; 4 host-level tests: seeded repair for desktop + API, empty-DB no-op for both)
- `src/DocumentIDE.Api/Program.cs` (register + invoke recovery in startup scope)
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` (register + invoke recovery after DB init)
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentMetadataRepository.cs` (removed premature `ready` transitions)
- `tests/DocumentIDE.IntegrationTests/Persistence/DocumentMetadataRepositoryTests.cs` (corrected rebuild lifecycle + hard-kill boundary test)
- `tests/DocumentIDE.Core.Tests/Indexing/IncrementalIndexCoordinatorTests.cs` (test double mirrors corrected metadata-save contract)
- **Shared-file edit:** `DocumentIDE.sln` — added `DocumentIDE.SafetyTests` project entry/configs/nesting only (controller granted this sprint exclusive sln access).
- **Shared-file edit:** `src/DocumentIDE.Infrastructure/Filesystem/AtomicFileReplacer.cs` — one line: temp path now comes from `ApplyTempFiles.CreatePath(directory)`; comment updated. No behavior change; other sprints calling `PrepareAsync` are unaffected.
- No other files touched; `docs/sprints/SPRINT-STATUS.md` intentionally untouched (controller-owned).

## Verification

- RED (mandated first behavior, before implementation): `dotnet test tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → `Failed: 1, Passed: 0` with `System.NotImplementedException : QA-07 RED: startup crash recovery is not implemented yet.` (expected reason: the crash-point test requires the missing startup repair). Log: `.superpowers/sdd/QA-07-crash-interrupted-operation-recovery/red-1-crash-temp.log`.
- GREEN: `dotnet test tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → `Passed: 12, Failed: 0, Skipped: 0`. Log: `.../safety-final-12.log`.
- Review-fix RED/GREEN: metadata-ready fix RED (`.../fix2-red-metadata-ready.log`, `Expected: "pending"/"stale"`, `Actual: "ready"` on both tests) → GREEN (`.../fix2-green-metadata-ready.log`, 6/6); host-wiring fix RED (`.../fix1-red-host-wiring.log`, `Expected: Failed`, `Actual: Executing` on both hosts) → GREEN (`.../fix1-green-host-wiring.log`, 4/4).
- `dotnet build tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → Build succeeded; `dotnet sln DocumentIDE.sln list` → lists the new project.
- Affected `dotnet test tests\DocumentIDE.Core.Tests` → 440 passed, 0 failed, 0 skipped.
- Affected `dotnet test tests\DocumentIDE.IntegrationTests` → **228 passed, 0 failed, 0 skipped** (includes the two release-artifact tests, which pass now that the release build has populated `artifacts/`; earlier they failed only because `artifacts/` was absent).
- Extra: `dotnet test tests\DocumentIDE.ArchitectureTests` → 7/7; `dotnet test tests\DocumentIDE.SecurityTests` → 52/52 (API host now runs recovery at startup).
- `scripts/verify.ps1` → PASS on the sprint tree after review fixes: build, 12 SafetyTests + 440 Core + 228 Integration + 7 Architecture tests, Web build/tests, 0 skipped; log `.superpowers/sdd/QA-07-crash-interrupted-operation-recovery/verify-qa-07.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped.

Step → test evidence:

| Step | Test(s) |
|---|---|
| 1. Crash during indexing | `CrashDuringIndexing_SourceStaysByteIdentical_AndIndexStateIsTypedNotReady`; `DocumentMetadataRepositoryTests.MetadataSaveWithoutFts_DoesNotMarkReady_HardKillBoundary`; `DocumentMetadataRepositoryTests.DerivedMetadata_CanBeDeletedAndRebuilt` (ready only after full metadata+FTS pass) |
| 2. Crash after snapshot before replace | `CrashAfterSnapshotBeforeReplace_SourceStaysOriginal_AndSnapshotRemainsUndoable`; `InjectedValidationFailureAfterSnapshot_LeavesSourceUnchanged_WithNoTempLeftover` |
| 3. Crash during temp mutation | `CrashDuringTempMutation_SourceStaysByteIdentical_AndStartupRecoveryRepairsState` |
| 4. Crash after atomic replace before history finalization | `CrashAfterAtomicReplaceBeforeHistoryFinalization_PlanReconciled_AndUndoVerifiesReplace` |
| 5. Recover/clean orphan temps on startup | Service: `OrphanTempCleanup_RemovesOnlyExactApplyTemps_AndNeverTouchesUserFiles`; `OrphanTempBehindJunction_IsNeverTraversed_OrDeleted`; `LockedOrphanTemp_ReportsTypedDeleteFailure_AndIsCleanableLater`; `OnlyExecutingPlansAreReconciled_AndRepairCoversEveryWorkspace`; `InterruptedPlanWithoutSnapshot_IsReconciled_AndUndoRefusesInsteadOfInferring`; `PlanRepairFailure_IsReportedTyped_WithoutAbortingRecovery`; `Recovery_WithUnavailableSourceDirectory_SkipsCleanupWithoutFailure`. Host wiring: `StartupRecoveryHostTests` (desktop + API seeded repair, desktop + API empty-DB no-op) |

## Acceptance criteria

- [x] Never infer success from an interrupted operation without verification — plans stuck in `Executing` are marked `Failed` on real host startup, never `Completed` (`StartupRecoveryHostTests`); replaced content is only claimed through fingerprint verification (`…PlanReconciled_AndUndoVerifiesReplace` → undo `Restored` only because the recorded post-operation fingerprint matched); a snapshot-less interruption is refused typed by undo (`…UndoRefusesInsteadOfInferring`); indexing claims `ready` only after the FTS write succeeds (`MetadataSaveWithoutFts_DoesNotMarkReady_HardKillBoundary`).
- [x] Required test: source file remains valid at every injected crash point — byte-identical assertions in tests for indexing, snapshot-before-replace, temp mutation, validation failure, replace-before-history, and both host startups.
- [x] Required test: startup repair marks/reconciles incomplete operation — typed `StartupRecoveryReport` with `MarkedFailed` / `RepairFailed` outcomes; non-`Executing` plan states untouched; invoked by both product hosts on start.
- [x] Required test: temp artifacts are safely cleanable — exact-name cleanup matrix (decoys survive), junction escape untouched, locked temp typed `DeleteFailed` then removable on retry, orphan temps removed during real host startup.

## Security & file-safety gate

- [x] No user file mutated through an unreviewed/bypass path — recovery only deletes files matching the exact app temp pattern; tests prove user files, Word locks, decoys, and snapshots survive. No mutation path was added for documents; index `ready` now follows durable indexing instead of preceding it.
- [x] No arbitrary path or secret exposed — cleanup is confined to repository-configured folder sources; reparse points are not traversed; recovery logs contain counts, workspace/plan ids and temp paths only (no secrets); no new HTTP surface.
- [x] Dependency/security boundaries remain intact — Core + Infrastructure + the existing Api/Desktop hosts; no new packages; no privileged/ShareHost surface touched.
- [x] No destructive action silently broadened its scope — exact-length/hex temp-name predicate shared with the writer; hidden folders skipped; delete failures reported typed; snapshots preserved; recovery runs once, before requests are served.
- [x] Relevant higher-level safety rules remain stronger than convenience — interrupted operations are never completed or re-applied (`CanApply` rejects `Failed`); no force-complete/force-cleanup convenience path exists; metadata saves are no longer a "ready" side effect.

## Deviations / spec conflicts

- The sprint's planned artifact was only the test file; the minimal missing production pieces (`StartupRecoveryService`, `ApplyTempFiles`, one-line `AtomicFileReplacer` refactor) were required by Step 5 and Required Test 2 and are explicitly allowed by the sprint's "changes needed to satisfy tests/acceptance criteria are in scope". Flagged here for review.
- Review decision (controller-ruled, in scope): both product hosts now invoke recovery at startup, and the indexing ready transition moved after the FTS write. No higher-level spec was weakened.
- Test-local `FileHistoryStorage` mirrors `Infrastructure.History.HistoryStorage` on a temp root (production class hardcodes `%LOCALAPPDATA%`); snapshot copies and metadata JSON are real files on disk. Same known gap HIST-02 ledgered.

## Risks discovered

- Orphan snapshot directories: a crash during `CreateSnapshotAsync` before metadata save leaves a snapshot copy directory with no metadata JSON; this sprint does not garbage-collect it (storage leak, not source corruption). Follow-up candidate for history storage cleanup.
- `StartupRecoveryService` assumes no apply is executing; it runs once during host startup before requests are served (documented on the type and enforced by both host wirings).
- Temp files held open by another process are reported `DeleteFailed` and removed on a later startup pass (tested); no silent success.
- API host tests booted without an `AppDb` override (existing `IClassFixture<WebApplicationFactory<Program>>` classes) now execute recovery against the developer's default `%LOCALAPPDATA%\DocumentIDE\app.db` — correct product semantics, but those tests depend on local DB state (pre-existing pattern; migrations already ran there).

## Next eligible sprint(s)

- Unchanged from the current wave; QA-07 completion unblocks nothing new by itself (`REL-02` still needs QA-01/QA-02/QA-04/QA-05/QA-06).
- Current in-flight/ready set per `SPRINT-STATUS.md`: `WORD-02`, `WORD-03`, `FS-06` (in progress), `DOCX-04`, `SHR-08`, `CAN-04`, `QA-01`, `QA-02`, `CAN-08`, `BAT-08`, `BAT-09`, `BAT-10`.
