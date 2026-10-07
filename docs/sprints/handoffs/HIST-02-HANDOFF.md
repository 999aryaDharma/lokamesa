# Sprint Handoff — HIST-02 — Conflict-Aware Undo

**Status:** DONE
**Branch:** `fix/hist-02-conflict-aware-undo`
**Commits:** `3a3e8da` (per-file snapshots), `9b993ad` (post-op fingerprints), `6fbf684` (UndoService), `91bafb1` (edge cases/race/cancel), `4e93027` (dialog), `c9e8ca4` (review fix pass), `a356db5` (nullable cleanup); completion metadata in the following docs commit.

## What changed
- `RecoverySnapshotService` upgraded from a flat placeholder fingerprint list to per-file records (`SnapshotFileRecord`: document id, original path, snapshot copy name, original fingerprint, post-operation fingerprint), real streaming SHA-256 fingerprints, and unique snapshot copy names for duplicate file names (case-insensitive). `CreateSnapshotAsync` now takes `SnapshotSourceFile(DocumentId, Path)`; the HIST-01 path-only overload is retained and fails closed at undo (`UnknownDocument`). New `RecordPostOperationFingerprintAsync`.
- `BatchApplyEngine` passes document ids into the snapshot and records the post-operation fingerprint from the fully prepared temp file **before** the atomic commit — either a mutation commits with verifiable undo data, or it fails closed with the original untouched (`MutationFailed`, no commit).
- New `UndoService` (Core/History): identity-based target resolution via `IDocumentRepository` (moves are followed; unknown documents fail closed), shared process-wide per-file lock (`Core.Filesystem.FileOperationLocks`, also used by the apply engine), bounded fingerprint read with reparse guard, external-edit probe (skip `Locked` → `OpenInWord`, `Unknown` → `LockUnknown`), typed outcomes (`Restored` / `AlreadyRestored` / `Conflict` / `Failure` / `Skipped`) and statuses (`Completed` / `Partial` / `Blocked` / `Canceled`), temp-copy → snapshot-copy fingerprint validation → re-check immediately before commit → atomic `File.Replace`, expired-snapshot refusal, typed reindex warning (`ReindexFailed`) on a `Restored` outcome, and `IUndoHistoryRecorder` recording the operation-level undone status only when files moved (Completed/Partial).
- New `UndoConflictDialog` (Web): read-only per-file review — outcome labels, reason codes/details, restored/conflict/failed/skipped summary, and an explicit "changed after the operation … not overwritten" warning. No force-restore/bypass path exists.
- No API/DI wiring (downstream: BAT-11), no new dependencies.

## Files changed
- `src/DocumentIDE.Core/History/RecoverySnapshotService.cs` (per-file metadata + fingerprints)
- `src/DocumentIDE.Core/History/UndoService.cs` (new)
- `src/DocumentIDE.Core/Batch/BatchApplyEngine.cs` (snapshot identity + pre-commit fingerprint recording)
- `src/DocumentIDE.Core/Filesystem/FileOperationLocks.cs` (new shared per-file lock registry)
- `src/DocumentIDE.Web/src/features/history/UndoConflictDialog.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/History/UndoTests.cs` (new, 23 tests)
- `tests/DocumentIDE.Web`-side: `src/DocumentIDE.Web/src/features/history/UndoConflictDialog.test.tsx` (new, 7 tests)
- Test-double/maintenance edits: `SafeApplyTests.cs` (metadata upsert + shared-registry reflection), `SearchReplaceOperationTests.cs`, `RegexReplaceTests.cs` (metadata upsert), `SourceWatcherTests.cs` (flake stabilization)
- Planned-file mapping: all three planned files created under planned names (`UndoService.cs`, `UndoConflictDialog.tsx`, `UndoTests.cs`). Snapshot/engine extensions were required to satisfy the sprint: spec 02 §19 places `document_id`/`original_fingerprint`/`post_operation_fingerprint` on the recovery snapshot, and BAT-04 previously could not record them.

## Verification
- `scripts/verify.ps1` → PASS on final tree: build, 440 Core + 223 Integration + 7 Architecture tests, Web build, 20 files / 96 Web tests, 0 skipped; log `.superpowers/sdd/HIST-02-conflict-aware-undo/verify-HIST-02-final.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped (solution config omits it).
- Flake note: the first full verify run failed only on pre-existing `SourceWatcherTests.One_Word_Like_Burst_Becomes_One_Logical_Modification` (fixed 250 ms sleep racing a 50 ms debounce under parallel load; reproduced 3/5 runs by the reviewer). Fixed by polling with a 5 s timeout plus a settle window; 3 consecutive green Integration runs (223/223) after the fix.
- TDD evidence: compile-RED first for the mandated "unchanged file restores" behavior; behavioral RED for post-op recording, expiry, empty-snapshot guard, reindex failure, probe skips and shared-lock facts; per-cycle logs in `.superpowers/sdd/HIST-02-conflict-aware-undo/`.
- Fresh-context review of `5ef5d9f..4e93027`: 0 Critical, 6 Important, 7 Minor; all six Important fixed in one RED→GREEN fix pass (`c9e8ca4`), suite green after the pass.

## Acceptance criteria
- [x] Undo uses the same safe-write discipline — per-file shared lock, fingerprint compare, temp-copy + snapshot-copy fingerprint validation, re-check before commit, strict atomic `File.Replace`, typed per-file outcomes. Evidence: `Undo_UnchangedFileRestoresOriginalBytes`, `Undo_ExternalEditDuringRestorePreparationIsNotOverwritten`, `Undo_CancellationDuringBatchIsTypedAndLeavesNoPartialRestore`.
- [x] Unchanged file restores — `Undo_UnchangedFileRestoresOriginalBytes` (+ idempotence `Undo_SecondUndoIsIdempotent`, moved-file `Undo_MovedFileRestoresAtCurrentPath`).
- [x] Externally edited file is not blindly overwritten — `Undo_ExternallyEditedFileIsNotOverwritten`, `Undo_ExternalEditDuringRestorePreparationIsNotOverwritten` (byte-level asserts).
- [x] Undo batch reports partial/conflict outcomes — `Undo_BatchReportsPartialAndConflictOutcomes` (Restored + Conflict, `Partial`).
- [x] Record undone status and trigger reindex — `Undo_UnchangedFileRestoresOriginalBytes` (recorder `Completed`, reindex once); `Undo_ReindexFailureAfterRestoreIsReportedTyped` (typed warning, record still accurate).
- [x] Per-file conflict review — `UndoConflictDialog` tests (7/7).

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — every write goes through fingerprint checks, snapshot-copy validation and atomic replace; no force-restore path exists; the dialog is review-only.
- [x] No arbitrary path or secret is exposed — targets resolve by document identity; `SnapshotFileName` is validated as a bare name; no new API surface.
- [x] Dependency/security boundaries remain intact — Core contracts + Infrastructure implementations reused; no new packages.
- [x] No destructive action silently broadened its scope — conflicts/locked/unknown/missing snapshots/expired snapshots all refuse; over-inclusive un-done states impossible (empty snapshot guard).
- [x] Relevant higher-level safety rules remain stronger than convenience — unchanged fingerprints are required before any restore; newer work survives.

## Deviations / spec conflicts
- No spec weakened. Deviations accepted by design (ledgered): conflicts are never force-restorable in this sprint (review-only dialog); post-op fingerprints moved into `RecoverySnapshot` per spec 02 §19, which required extending BAT-04's engine (record-before-commit); `IUndoHistoryRecorder` is a placeholder contract until the `OperationHistory` store lands (BAT-11).
- Legacy HIST-01 snapshot payloads (no per-file records) are not restorable: undo refuses with `InvalidOperationException` instead of recording a false "undone" status. No production data exists (feature unwired).

## Risks discovered
- Cross-process writers inside undo's re-check→replace window cannot be excluded by locks; mitigated by fingerprint checks + atomic replace (same accepted class as BAT-04).
- Canceled undo that already restored some files is not recorded (status `Canceled`, no history entry); a retry self-heals. Flagged for BAT-11 if partial-undone history becomes user-visible.
- `HistoryStorage` capacity check double-counts copies + `SizeBytes` on re-save (pre-existing HIST-01 debt; re-save passes whenever creation passed).
- No test instantiates the real `Infrastructure.History.HistoryStorage` JSON round-trip; fakes share process semantics. Cheap follow-up noted.
- Pre-existing NuGet advisories (AngleSharp/HtmlSanitizer/SQLitePCLRaw) surfaced during runs — unrelated, no dependency changes here.

## Next eligible sprint(s)
- `QA-07` — Crash & Interrupted-Operation Recovery (deps FND-04 + HIST-02 both DONE) → newly READY.
- Still blocked: `BAT-11` and `QA-04` (need BAT-10 as well).
- Other READY unchanged: `DOCX-04`, `SHR-08`, `CAN-04`, `QA-01`, `QA-02`, `CAN-08`, `BAT-08`, `BAT-09`, `BAT-10`.
