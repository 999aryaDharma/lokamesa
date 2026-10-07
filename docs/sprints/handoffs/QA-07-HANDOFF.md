# Sprint Handoff — QA-07 — Crash & Interrupted-Operation Recovery

**Status:** DONE
**Branch:** `fix/qa-07-crash-interrupted-operation-recovery`
**Commits:** `4f2b3aa` (startup recovery production piece + safety test project, 10 tests), `d006140` (fault-path tests: typed repair failure, unavailable source); completion metadata in the following docs commit.

## What changed

- New test project `tests/DocumentIDE.SafetyTests` (registered in `DocumentIDE.sln`) with `CrashRecoveryTests.cs`: 12 real-filesystem crash-recovery tests (temp dirs, real SQLite persistence stack, real `RecoverySnapshotService`, real `AtomicFileReplacer`, real parsers/FTS). No filesystem mocks; repository fault injection is used only to test the typed failure path.
- New production piece `StartupRecoveryService` (`DocumentIDE.Core/Filesystem`) — **there was no startup recovery for interrupted batch operations or orphan apply temps before this sprint**; implemented here because Step 5 and Required Test 2 had no production implementation:
  - reconciles every batch plan left in `Executing` to `Failed` (never `Completed`; success is never inferred from an interrupted operation), returning a typed `StartupRecoveryReport` (`InterruptedPlanRepair`; `RepairFailed` surfaced, never silently swallowed);
  - removes orphan transactional `~apply-<32 hex>.tmp` artifacts under configured folder sources only, recursively, never following reparse points, skipping hidden folders (mirrors `StartupReconciler`), with typed `OrphanTempFileCleanup` outcomes (`DeleteFailed` surfaced);
  - source documents are never modified; recovery data (snapshots) is never deleted.
- New `ApplyTempFiles` (`DocumentIDE.Core/Filesystem`): single source of truth for apply temp naming (`CreatePath` / `IsApplyTempFileName`). `AtomicFileReplacer` was refactored to use it (no behavior change) so the cleaner can never disagree with the writer about what is safe to delete.
- No API/DI wiring: `StartupRecoveryService` is ready for the app startup orchestration but is not invoked from `Program.cs` (same precedent as HIST-02's unwired services; app startup call-site is an integration follow-up). `LocalJobScheduler` (FND-04) keeps jobs in memory only, so a crash discards them by design — orphaned jobs materialize exactly as the orphan temp artifacts plus stuck `Executing` plans that this service repairs.

## Files changed

- `tests/DocumentIDE.SafetyTests/DocumentIDE.SafetyTests.csproj` (new; net10.0-windows, xunit, refs Core + Infrastructure)
- `tests/DocumentIDE.SafetyTests/CrashRecoveryTests.cs` (new; 12 tests)
- `src/DocumentIDE.Core/Filesystem/StartupRecoveryService.cs` (new production; typed report records)
- `src/DocumentIDE.Core/Filesystem/ApplyTempFiles.cs` (new production; temp-naming single source of truth)
- **Shared-file edit:** `DocumentIDE.sln` — added `DocumentIDE.SafetyTests` project entry/configs/nesting only (controller granted this sprint exclusive sln access).
- **Shared-file edit:** `src/DocumentIDE.Infrastructure/Filesystem/AtomicFileReplacer.cs` — one line: temp path now comes from `ApplyTempFiles.CreatePath(directory)`; comment updated. No behavior change; other sprints calling `PrepareAsync` are unaffected.
- No other files touched; `docs/sprints/SPRINT-STATUS.md` intentionally untouched (controller-owned).

## Verification

- RED (mandated first behavior, before implementation): `dotnet test tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → `Failed: 1, Passed: 0` with `System.NotImplementedException : QA-07 RED: startup crash recovery is not implemented yet.` (expected reason: the crash-point test requires the missing startup repair). Log: `.superpowers/sdd/QA-07-crash-interrupted-operation-recovery/red-1-crash-temp.log`.
- GREEN: `dotnet test tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → `Passed: 12, Failed: 0, Skipped: 0`. Log: `.../safety-final-12.log`.
- `dotnet build tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → Build succeeded; `dotnet sln DocumentIDE.sln list` → lists the new project.
- Affected `dotnet test tests\DocumentIDE.Core.Tests` → 440 passed, 0 failed, 0 skipped.
- Affected `dotnet test tests\DocumentIDE.IntegrationTests` → 221 passed / 2 failed; both failures are pre-existing release-artifact existence checks (`artifacts/desktop/DocumentIDE.Desktop.exe`, `artifacts/installer/DocumentIDE-Setup.msi`) that require `scripts/build-release.ps1` (npm + WiX). Filtered run excluding those two → 221/221 passed, 0 skipped.
- `scripts/verify.ps1` — controller runs full repository verification on this worktree.

Step → test evidence:

| Step | Test(s) |
|---|---|
| 1. Crash during indexing | `CrashDuringIndexing_SourceStaysByteIdentical_AndIndexStateIsTypedNotReady` |
| 2. Crash after snapshot before replace | `CrashAfterSnapshotBeforeReplace_SourceStaysOriginal_AndSnapshotRemainsUndoable`; `InjectedValidationFailureAfterSnapshot_LeavesSourceUnchanged_WithNoTempLeftover` |
| 3. Crash during temp mutation | `CrashDuringTempMutation_SourceStaysByteIdentical_AndStartupRecoveryRepairsState` |
| 4. Crash after atomic replace before history finalization | `CrashAfterAtomicReplaceBeforeHistoryFinalization_PlanReconciled_AndUndoVerifiesReplace` |
| 5. Recover/clean orphan temps on startup | `OrphanTempCleanup_RemovesOnlyExactApplyTemps_AndNeverTouchesUserFiles`; `OrphanTempBehindJunction_IsNeverTraversed_OrDeleted`; `LockedOrphanTemp_ReportsTypedDeleteFailure_AndIsCleanableLater`; `OnlyExecutingPlansAreReconciled_AndRepairCoversEveryWorkspace`; `InterruptedPlanWithoutSnapshot_IsReconciled_AndUndoRefusesInsteadOfInferring`; `PlanRepairFailure_IsReportedTyped_WithoutAbortingRecovery`; `Recovery_WithUnavailableSourceDirectory_SkipsCleanupWithoutFailure` |

## Acceptance criteria

- [x] Never infer success from an interrupted operation without verification — plans stuck in `Executing` are marked `Failed`, never `Completed` (`OnlyExecutingPlansAreReconciled_…`); replaced content is only claimed through fingerprint verification (`…PlanReconciled_AndUndoVerifiesReplace` → undo `Restored` only because the recorded post-operation fingerprint matched); a snapshot-less interruption is refused typed by undo (`…UndoRefusesInsteadOfInferring`).
- [x] Required test: source file remains valid at every injected crash point — byte-identical assertions in tests for indexing, snapshot-before-replace, temp mutation, validation failure, and replace-before-history.
- [x] Required test: startup repair marks/reconciles incomplete operation — typed `StartupRecoveryReport` with `MarkedFailed` / `RepairFailed` outcomes; non-`Executing` plan states untouched.
- [x] Required test: temp artifacts are safely cleanable — exact-name cleanup matrix (decoys survive), junction escape untouched, locked temp typed `DeleteFailed` then removable on retry.

## Security & file-safety gate

- [x] No user file mutated through an unreviewed/bypass path — recovery only deletes files matching the exact app temp pattern; tests prove user files, Word locks, decoys, and snapshots survive. No mutation path was added for documents.
- [x] No arbitrary path or secret exposed — cleanup is confined to repository-configured folder sources; reparse points are not traversed; no new API surface or secrets.
- [x] Dependency/security boundaries remain intact — Core + Infrastructure only; no new packages; no privileged/ShareHost surface touched.
- [x] No destructive action silently broadened its scope — exact-length/hex temp-name predicate shared with the writer; hidden folders skipped; delete failures reported typed; snapshots preserved.
- [x] Relevant higher-level safety rules remain stronger than convenience — interrupted operations are never completed or re-applied (`CanApply` rejects `Failed`); no force-complete/force-cleanup convenience path exists.

## Deviations / spec conflicts

- The sprint's planned artifact was only the test file; the minimal missing production pieces (`StartupRecoveryService`, `ApplyTempFiles`, one-line `AtomicFileReplacer` refactor) were required by Step 5 and Required Test 2 and are explicitly allowed by the sprint's "changes needed to satisfy tests/acceptance criteria are in scope". Flagged here for review.
- Startup recovery is not wired into the app startup call-site (no automatic invocation yet). The app currently triggers reconciliation via controllers; wiring `StartupRecoveryService.RecoverAsync` before background execution begins is a follow-up integration task. Behavior, safety, and typing are fully tested at the service boundary.
- Test-local `FileHistoryStorage` mirrors `Infrastructure.History.HistoryStorage` on a temp root (production class hardcodes `%LOCALAPPDATA%`); snapshot copies and metadata JSON are real files on disk. Same known gap HIST-02 ledgered.

## Risks discovered

- Orphan snapshot directories: a crash during `CreateSnapshotAsync` before metadata save leaves a snapshot copy directory with no metadata JSON; this sprint does not garbage-collect it (storage leak, not source corruption). Follow-up candidate for history storage cleanup.
- Indexing ready-before-FTS window: `SaveMarkdownMetadataAsync`/`SaveDocxMetadataAsync` mark `index_status='ready'` before the FTS write. The injected-interruption test proves typed `error` handling, but a hard process kill exactly between the metadata save and the FTS write can leave `ready` without search rows; no startup reconciler repairs that window today. Flagged for QA-02/indexing follow-up — does not affect source-file safety.
- `StartupRecoveryService` assumes no apply is executing; it must be invoked once at startup before background execution begins (documented on the type).
- Temp files held open by another process are reported `DeleteFailed` and removed on a later pass (tested); no silent success.

## Next eligible sprint(s)

- Unchanged from the current wave; QA-07 completion unblocks nothing new by itself (`REL-02` still needs QA-01/QA-02/QA-04/QA-05/QA-06).
- Current in-flight/ready set per `SPRINT-STATUS.md`: `WORD-02`, `WORD-03`, `FS-06` (in progress), `DOCX-04`, `SHR-08`, `CAN-04`, `QA-01`, `QA-02`, `CAN-08`, `BAT-08`, `BAT-09`, `BAT-10`.
