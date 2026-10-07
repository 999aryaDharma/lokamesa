# Sprint Handoff — QA-04

**Status:** DONE
**Branch:** `fix/qa-04-batch-safety-regression-suite`
**Commits:** `1d79557` — `fix(quality): batch safety regression suite` (new `tests/DocumentIDE.SafetyTests/BatchSafetyRegressionTests.cs`, 12 tests); completion metadata (this handoff) in the following docs commit.

## What changed

- New `tests/DocumentIDE.SafetyTests/BatchSafetyRegressionTests.cs` — the Batch Safety Regression Suite. It exercises the **merged destructive-batch machinery** (BAT-04 `BatchApplyEngine`, HIST-02 conflict-aware `UndoService`, BAT-10 `BatchRenameOperation`, BAT-05 `SearchReplaceOperation`) with **fault injection** and **real byte-level assertions**. No production code changed; no mocks where real components exist — only fault-injection seams (a locking atomic-replacer wrapper, a capacity-constrained history storage mirroring `HistoryStorage` semantics on a temp root, and recording doubles for the two unimplemented app-layer recorder interfaces, same pattern as QA-07/HIST-02).
- Implementation steps mapped to tests:
  - **Step 1 — injected mutation/validation failure:** `OriginalSurvivesEveryInjectedFailure(MutationThrowsAfterTouchingTemp | ValidationThrowsAfterMutation)`.
  - **Step 2 — injected destination lock/write failure:** `OriginalSurvivesEveryInjectedFailure(DestinationOpenByExternalEditorAtCheckTime)` (real `WordEditStateProbe` exclusive-open signal → typed `OpenInWord` skip) and `(DestinationLockedAfterChecksPass)` (the destination is grabbed **after** the checks passed; `File.Replace` fails and the engine rolls the temp back → typed `MutationFailed`). Rename variant: `StrictRenameUnderExternalLockFailsClosed` (real `FileShare.Read` handle → typed `RenameFailed`).
  - **Step 3 — change file after preview then apply:** `StalePlanRejected_ExpiredPlanThrowsBeforeAnyFileIsTouched` and `StalePlanRejected_FileChangedAfterPreviewIsNeverOverwritten` (typed `Stale` skip; the mutate delegate is asserted **never invoked**).
  - **Step 4 — insufficient recovery capacity:** `NoSnapshotCapacityBlocksApply_AndExistingRecoveryIsNeverDeleted` (capacity sized so the apply fits **only if** an existing unexpired snapshot were deleted; it is not — apply is blocked typed `RecoveryBlocked`, the earlier snapshot's metadata and byte-exact copy survive) plus `OriginalSurvivesEveryInjectedFailure(RecoveryCapacityInsufficient)`.
  - **Step 5 — strict-operation non-target invariants:** `StrictSearchReplaceTouchesOnlySelectedOccurrenceAndReviewedFile` (selected occurrence replaced; deselected occurrence, unselected plan file, and out-of-plan file byte-identical) and `StrictRenameTouchesOnlyReviewedTargetAndPreservesContent` (renamed file keeps byte-exact content and `DocumentId`; bystander file name+bytes untouched; no extra artifact in the directory).
- Required tests:
  - [x] *Original survives every injected failure* — `OriginalSurvivesEveryInjectedFailure` (5-case `[Theory]`, byte-exact source assertion + no `~apply-*.tmp` leftovers + typed outcome + recovery copy holds the pre-apply original where a snapshot exists).
  - [x] *Stale plan rejected* — the two `StalePlanRejected_*` tests above.
  - [x] *No-snapshot capacity blocks apply* — `NoSnapshotCapacityBlocksApply_AndExistingRecoveryIsNeverDeleted`.
  - [x] *Undo conflict does not overwrite newer edit* — `UndoConflictDoesNotOverwriteNewerEdit` (newer edit written through the real `AtomicFileReplacer`; undo returns `Blocked`/`Conflict`/`ExternallyModified`, newer bytes byte-identical, operation not recorded as undone, snapshot copy intact, conflict stable on retry).
- CI-friendliness: zero fixed sleeps (everything awaited; no event polling needed), 12 tests in **~4 s**, isolated per-test temp roots (`qa04-<guid>`), real SQLite stack, deterministic Windows lock semantics.
- TDD note (regression suite over DONE dependencies): the mandated first behavior is already implemented by BAT-04/HIST-02, so the required RED was produced the only meaningful way for a regression suite — by *proving the test is fault-sensitive*: with a deliberately unsafe commit seam (validation gate dropped) `OriginalSurvivesEveryInjectedFailure(ValidationThrowsAfterMutation)` fails with the original overwritten; with the real merged machinery all 5 cases pass. Then the remaining steps were added and run incrementally (3 run stages: 5 → 9 → 12 tests).

## Files changed

- `tests/DocumentIDE.SafetyTests/BatchSafetyRegressionTests.cs` (new; 705 lines, 12 tests). **This is the only file changed.**
- `docs/sprints/handoffs/QA-04-HANDOFF.md` (this file).
- No `DocumentIDE.sln` edit (the `DocumentIDE.SafetyTests` project already exists from QA-07); no production files; `docs/sprints/SPRINT-STATUS.md` intentionally untouched (controller-owned per dispatch).

## Verification

- RED (mandated first behavior, fault-sensitivity with unsafe commit seam): `dotnet test tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj --nologo --filter "FullyQualifiedName~BatchSafetyRegressionTests"` → `Failed: 1, Passed: 4` — `OriginalSurvivesEveryInjectedFailure(injected: ValidationThrowsAfterMutation)` failed at `Assert.Equal(original, await File.ReadAllBytesAsync(path))`: `Expected: [116, 97, 114, 103, 101, ···] Actual: [114, 101, 112, 108, 97, ···]` (original replaced by an unvalidated commit — exactly the safety regression the test guards). Log: `.superpowers/sdd/QA-04-batch-safety-regression-suite/red-1-unsafe-commit.log`.
- GREEN (real machinery, after each TDD stage): 5/5 (`green-1-real-machinery.log`), 9/9 (`green-2-required-tests.log`), 12/12 (`green-3-strict-invariants.log`, `green-final-batch-safety.log`).
- Full SafetyTests project (QA-04 + QA-07): `dotnet test tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj --nologo` → **Passed: 24, Failed: 0, Skipped: 0** (`green-4-safetytests-full.log`).
- Affected IntegrationTests: focused batch + undo classes (`FullyQualifiedName~SearchReplaceOperationTests|FullyQualifiedName~UndoTests`) → **31 passed, 0 failed, 0 skipped**. Full-project run: 264–266 passing; failures are **pre-existing/environmental and not attributable to this change** (this change adds only one file in a different assembly): (a) `ScriptTests.Release_Build_Embeds_Production_Web_Assets` and `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` fail because `artifacts/` does not exist in the worktree — same known behavior recorded in the QA-07 handoff; the controller provides `artifacts/`; (b) intermittent `System.ObjectDisposedException: SQLitePCL.sqlite3` in the fully-parallel run, hitting a *different unrelated class each run* (`MoveCorrelationIntegrationTests` run 1, `SearchReplaceOperationTests`/`UndoTests` run 2), all of which pass in focused runs. Logs: `green-5-integration-full.log`, `green-5b-integration-rerun.log`.
- `dotnet build tests\DocumentIDE.SafetyTests\DocumentIDE.SafetyTests.csproj` → Build succeeded, 0 errors, no warnings from the new file.
- `scripts/verify.ps1` — controller runs full repository verification on this worktree.

### Step → test evidence

| Step | Tests |
|---|---|
| 1. Inject mutation validation failure | `OriginalSurvivesEveryInjectedFailure(ValidationThrowsAfterMutation)`, `(MutationThrowsAfterTouchingTemp)` |
| 2. Inject destination lock/write failure | `OriginalSurvivesEveryInjectedFailure(DestinationOpenByExternalEditorAtCheckTime)`, `(DestinationLockedAfterChecksPass)`, `StrictRenameUnderExternalLockFailsClosed` |
| 3. Change file after preview then apply | `StalePlanRejected_ExpiredPlanThrowsBeforeAnyFileIsTouched`, `StalePlanRejected_FileChangedAfterPreviewIsNeverOverwritten` |
| 4. Simulate insufficient recovery capacity | `NoSnapshotCapacityBlocksApply_AndExistingRecoveryIsNeverDeleted`, `OriginalSurvivesEveryInjectedFailure(RecoveryCapacityInsufficient)` |
| 5. Verify strict-operation non-target invariants | `StrictSearchReplaceTouchesOnlySelectedOccurrenceAndReviewedFile`, `StrictRenameTouchesOnlyReviewedTargetAndPreservesContent` |

## Acceptance criteria

- [x] **Runs in release gate** — the suite lives in `DocumentIDE.SafetyTests`, which is part of `DocumentIDE.sln` and therefore runs in `scripts/verify.ps1`'s `dotnet test`; no skipped tests; deterministic, isolated, ~4 s runtime, no fixed sleeps.

## Security & file-safety gate

- [x] No user file mutated through an unreviewed/bypass path — every test asserts the reviewed source bytes; lock/write failures fail closed (`MutationFailed`), locks are detected (`OpenInWord`), and stale/locked targets are never touched. The RED run proves the assertions catch a bypass.
- [x] No arbitrary path or secret exposed — tests use `%TEMP%\qa04-<guid>` roots and assert no `~apply-*` leftovers; no secrets or user data involved (N/A beyond the temp fixtures).
- [x] Dependency/security boundaries remain intact — test-only addition; no packages, no API surface, no privileged/ShareHost code touched.
- [x] No destructive action silently broadened its scope — Step-5 invariants prove occurrence-level and file-level scope containment for replace and rename.
- [x] Relevant higher-level safety rules remain stronger than convenience — no production change; the suite locks in spec 07 §11/§12/§21/§22/§29 behaviors (stale rejection, snapshot-before-mutation, block on insufficient recovery, block on rename write failure, conflict-aware undo).

## Deviations / spec conflicts

- The sprint's planned file was the test file only; this sprint produced exactly that file. No production changes were needed (dependencies BAT-10 and HIST-02 are DONE and passed every injected-failure case).
- Dispatch ruling followed: `docs/sprints/SPRINT-STATUS.md` was **not** edited (controller updates the board); the sprint doc's DoD line about it is overridden by the batch-B dispatch.
- TDD for a regression suite: since the behavior under test already exists, no implementation step could be driven RED→GREEN in production. The mandated first behavior was proven fault-sensitive (fails against a safety-violating commit seam, passes against the real machinery), and the remaining steps were added and verified incrementally. Recorded above with logs.
- Test-local `FileHistoryStorage` mirrors `Infrastructure.History.HistoryStorage` semantics (capacity pre-flight + metadata-save check, real files/JSON on a temp root) because the production class hardcodes `%LOCALAPPDATA%`; same known gap as QA-07/HIST-02.

## Risks discovered

- **Failed physical rename leaves a live move-intent for its TTL**: `PhysicalFileOperations.RenameItemAsync` journals the FS-07 move intent *before* `File.Move` and does not remove it when the move throws (verified while writing `StrictRenameUnderExternalLockFailsClosed`); the intent expires after 30 s, but within that window `MoveCorrelationService.CorrelateSameVolumeMoveAsync` could correlate a *different* file appearing at the reviewed destination path back to the intended document. Not asserted/fixed here (would change FS-07 behavior, outside QA-04 scope) — recommend an FS-07 follow-up to roll back the intent on rename failure.
- **Pre-existing IntegrationTests parallel-run flake**: intermittent `ObjectDisposedException: SQLitePCL.sqlite3` in full-suite parallel runs (different unrelated classes per run; all pass focused). Not caused by this change; flagged for the controller's serialized verification.
- The two release-artifact IntegrationTests need `artifacts/` to exist (controller-provided); they fail fast otherwise (known since QA-07).

## Next eligible sprint(s)

- QA-04 unblocks `REL-02` together with the remaining QA sprints (QA-01, QA-02, QA-05, QA-06) per `docs/sprints/SPRINT-STATUS.md`.
