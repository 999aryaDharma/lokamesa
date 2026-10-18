# Sprint Handoff — SCM-10

**Status:** DONE
**Branch:** `feat/safe-git-discard-restore`
**Commits:** `b407246` (implementation + tests) and this handoff commit

## What changed

- New `GitRestoreService` (Infrastructure) implements the reviewed destructive
  flow from spec 13 §15/§19/§20/§21/§23:
  - `ReviewAsync` → `git diff` + Git status: the affected file, the exact diff,
    the index/working-tree state, the target Git version (index or commit),
    recovery availability and a **review token** bound to the reviewed state,
  - `DiscardAsync` → `git restore --worktree -- <path>` (restores the working
    tree from the **index**, so staged content survives),
  - `RestoreFromCommitAsync` → `git restore --source=<commit> --worktree -- <path>`
    (restores the working tree from a selected commit without touching the index).
- **Discard/restore is never implicit.** An apply call without a review token is
  `ReviewRequired`; a token whose state changed is `ReviewStale`; in both cases
  Git never runs and the file is untouched. A second fingerprint check runs
  immediately before the Git invocation, so a file that changes while the
  recovery snapshot is being prepared is also refused (the newer content wins).
  The review/apply routes are POSTs and deliberately separate from the
  `GET status` refresh surface.
- **Recovery before mutation, fail closed.** A HIST-01 `RecoverySnapshotService`
  snapshot of the current content is created before Git runs; if it cannot be
  created (capacity, unreadable file, storage failure) the result is
  `RecoveryBlocked` and the file is unchanged. The snapshot records the
  post-restore fingerprint for registered documents, so the same operation is
  immediately undoable through HIST-02.
- **File-scoped only.** Exactly one normalized worktree-relative path travels
  after `--` as a discrete argument with `GIT_LITERAL_PATHSPECS=1`. There is no
  `.` pathspec, no `checkout`, no `reset`, no `--hard` and no whole-repository
  form. Paths outside the worktree, absolute/rooted paths, `..`/`.`/empty
  segments, `:` and directories are rejected typed **before** any Git mutation.
- **Conflict/untracked protection.** An unmerged path is `ConflictedPath`, an
  untracked path is `UntrackedPath`; both are refused typed before Git runs and
  the conflict state/content is left untouched (spec 13 §21).
- **Step 4.** After a successful restore the registered document is reindexed
  (`IBatchApplyReindexTrigger`), one typed Document IDE History record is written
  through the existing `JsonlHistoryStore` (`git-restore-{operationId}.jsonl`),
  and Git status is re-read and returned.
- API (append-only SCM-10 region in `SourceControlController`): `POST
  api/source-control/discard/review`, `POST discard`, `POST restore`, behind the
  existing `PrivilegedRequestGuard`. Rejected input is a typed HTTP 400 with
  nothing run; deliberate refusals and Git failures stay HTTP 200 typed so the
  pane renders them.
- Frontend: `DiscardRestoreDialog.tsx` renders the review (diff, staged state,
  target version, recovery availability), requires an explicit acknowledgement,
  and only then enables the distinct `Discard changes` / `Restore version`
  action. The review token is echoed on apply, typed refusals are surfaced in
  `role="alert"` regions, and a successful result reports the refreshed status.
  It is self-contained, props-driven and default-exported; it is **not** mounted
  in `App.tsx` (Lane C/W30 own that).

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Infrastructure/SourceControl/GitRestoreService.cs` — **new**,
  delivered as planned (review, discard, version restore, snapshot, containment,
  typed outcomes).
- `src/DocumentIDE.Web/src/features/source-control/DiscardRestoreDialog.tsx` —
  **new**, delivered as planned.
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitRestoreSafetyTests.cs` —
  **new**, delivered as planned: `GitRestoreSafetyTests` (37 tests, including
  16 theory cases) + `GitRestoreApiTests` (5 tests) = **42**.

Additional files needed to satisfy the sprint (mapping recorded):

- `src/DocumentIDE.Core/SourceControl/GitRestoreResult.cs` — **new** typed
  contract (`GitRestoreMode`, `GitRestoreOutcome`, `GitRestoreReview`,
  `GitRestoreReviewResult`, `GitRestoreResult`, `GitRestoreHistoryRecord`,
  `IGitRestoreHistoryRecorder`). Mirrors the SCM-02/06/07/08 Core placement.
- `src/DocumentIDE.Infrastructure/History/JsonlHistoryStore.cs` — **extended**
  (not a parallel store): implements `IGitRestoreHistoryRecorder`, appends
  `git-restore-{operationId}.jsonl` under the same Document IDE History root, and
  adds a read-back for evidence/inspection. Existing apply/undo records are
  untouched.
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — append-only
  SCM-10 region: 5 DTOs, 3 POST routes and mapping helpers. The only edits to
  existing members are the required `GitRestoreService` ctor parameter/field;
  no member was reordered, reformatted or deleted.
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` — one `TryAddSingleton`
  registration for `IGitRestoreHistoryRecorder` (same `JsonlHistoryStore`).
- `src/DocumentIDE.Api/Program.cs` — append-only scoped DI registration of
  `GitRestoreService` after the SCM-08 registration (scoped because the recovery
  snapshot service, document repository and reindex seam are scoped).
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  appended SCM-10 model types.
- `src/DocumentIDE.Web/src/features/source-control/DiscardRestoreDialog.test.tsx`
  — 7 colocated component tests (same CAN-05 convention as `GitHistoryPanel`).

Lane safety (verified with `git status --short` / `git diff --name-only`):

- `WebView/WebAppHost.cs` (`AllowedControllers`) — **not touched**.
- `App.tsx` — **not touched** (the dialog is not mounted).
- `docs/sprints/SPRINT-STATUS.md`, `scripts/verify-release.ps1`, `docs/quality/**`,
  manifests/roadmaps — **not touched**. The board row SCM-10 is reported for the
  controller to flip (the sprint prompt explicitly reserves the board).
- `TestGitRepository.cs` was **not** modified (the existing fixture was
  sufficient).

## TDD evidence

Backend cycles were RED-first; the mandated first behavior was

> Dirty file discard requires explicit review.

- **Cycle 1 (first failing test):** `dotnet test ... --filter
  FullyQualifiedName~GitRestoreSafetyTests` → build failed `CS0246` for
  `GitRestoreService`, `IGitRestoreHistoryRecorder` and
  `GitRestoreHistoryRecord`. GREEN after the Core contract + minimal service
  (`DirtyFileDiscardRequiresExplicitReview`) → **1 passed**.
- **Cycle 2 (review + file-scoped discard):** RED — 2 failures,
  `NotImplementedException` for the review path and the token-bearing apply path.
  GREEN → **3 passed**.
- **Cycle 3 (recovery ordering, fail-closed):** one failure was a test-authoring
  bug (mixed `/` vs `\` in an expected snapshot path); the ordering behavior
  itself held. Fixed the assertion → **5 passed**. Honest note: the snapshot
  happened before Git from cycle 2's pipeline, so the blocked-snapshot case was
  coverage-pinning.
- **Cycle 4 (stale/conflicted/untracked):** one genuine failure — the clean-file
  `BlockedReason` did not read as "there is nothing to restore". Improved the
  user-facing message (`ReviewApplicability`) → **9 passed**.
- **Cycle 5 (reindex/history/version restore):** one genuine failure — the test
  asserted the wrong file remained dirty after restoring from its own commit
  (the restored file matches HEAD, so only the unrelated edit is dirty). Fixed
  the test → **17 passed**.
- **Cycle 6 (containment, literal pathspec, failure taxonomy):** the first run
  passed **34** — coverage-pinning for behavior introduced by the cycle-2
  pipeline (README-style honesty, same as SCM-08's "Honest notes").
- **Cycle 7 (API):** build RED `CS0246` for the SCM-10 DTOs; after implementing
  them, three failures were a shared-fixture test bug (history records
  accumulated across tests). Switched to a per-test host → **39 passed**.
- **Cycle 8 (frontend):** RED `Failed to resolve import
  "./DiscardRestoreDialog"`; GREEN `DiscardRestoreDialog.test.tsx` → **7 passed**.
- **Post-review hardening (genuine REDs):**
  - `FileChangingDuringPreparationIsDetectedBeforeGitRuns` failed with
    `Expected: ReviewStale / Actual: Succeeded` (the newer content was
    overwritten) → added the pre-mutation fingerprint re-check.
  - `UnreadableFileIsTypedFailureAndNothingIsMutated` failed with an escaping
    `System.IO.IOException` → added `ReadCurrentStateAsync` returning a typed
    error.
  - Both GREEN → **42 passed**.

## Verification

- `dotnet build DocumentIDE.sln` → **Build succeeded, 0 errors** (163 warnings;
  the pre-existing analyzer/NU1603/NU1902/NU1903 set — no new warning from the
  SCM-10 files).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitRestore"`
  → **42 passed / 0 failed / 0 skipped** (37 service + 5 API).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **961 passed / 2 failed /
  0 skipped / 963 total**.
  - The two failures are the documented release-artifact checks that cannot pass
    in a fresh worktree because `artifacts/` is gitignored/absent:
    `ScriptTests.Release_Build_Embeds_Production_Web_Assets` and
    `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`.
    This diff touches neither `scripts/` nor packaging. The same two failures
    are recorded in the SCM-01…SCM-08 handoffs.
  - The known load-sensitive flakes (`PdfMergeMemoryTests`, host tests) did
    **not** reproduce in either full run. No test was skipped.
- `dotnet test tests/DocumentIDE.Core.Tests` → **459 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.SecurityTests` → **91 passed / 0 failed**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12 passed / 0 failed**.
- `npx tsc -b` (`src/DocumentIDE.Web`) → exit 0.
- `npx oxlint` (`src/DocumentIDE.Web`) → exit 0, **34 warnings / 0 errors**; none
  from `DiscardRestoreDialog.tsx` (all are the pre-existing
  `react(only-export-components)` notes on other files).
- `npx vitest run` (`src/DocumentIDE.Web`) → **75 files / 520 tests passed,
  0 failed** (SCM-10 dialog: 7).

## Acceptance criteria

- [x] **Destructive restore/discard is never hidden behind a generic refresh
      action.** The only discard/restore surface is `POST discard`/`restore`,
      which requires the token produced by `POST discard/review`; a missing token
      is a typed `ReviewRequired` 400 with zero Git invocations
      (`DirtyFileDiscardRequiresExplicitReview`,
      `DiscardEndpointWithoutReviewTokenIsTypedBadRequestAndChangesNothing`).
      The frontend has no refresh control and its destructive button stays
      disabled until the review is acknowledged (`shows what will be lost and
      keeps the destructive action disabled until acknowledged`).
- [x] **Conflicted files are protected from unsafe restore.**
      `ConflictedFileIsRefusedTypedAndItsStateIsUntouched` creates a real merge
      conflict, asserts the typed `ConflictedPath` refusal, the unchanged conflict
      content and that `git status` still reports `UU`.
      `UntrackedFileIsRefusedTypedAndNeverDeleted` covers the untracked case.
- [x] **Document IDE History records the operation.**
      `RestoreReindexesRefreshesStatusAndRecordsDocumentIdeHistory` asserts the
      typed record (operation id, mode, outcome, path, snapshot id) and the
      post-restore fingerprint in the HIST-01 snapshot;
      `DocumentIdeHistoryStorePersistsTheRestoreOperation` proves the real
      `JsonlHistoryStore` persists and reads the record back; the API test asserts
      the host's history store received it (`historyRecorded: true`).

Required tests:

- [x] Dirty file discard requires explicit review — `DirtyFileDiscardRequiresExplicitReview`
      (the mandatory RED-first test), plus `ReviewTokenIsInvalidatedWhenTheFileChangesAfterReview`
      and `FileChangingDuringPreparationIsDetectedBeforeGitRuns`.
- [x] Restore creates recovery coverage when available —
      `RecoverySnapshotExistsBeforeTheMutation` (asserts the snapshot and its copy
      exist **at the instant Git is invoked**) and
      `FailedRecoverySnapshotBlocksTheMutation` (fail closed).
- [x] Restore reindexes and refreshes Git status —
      `RestoreReindexesRefreshesStatusAndRecordsDocumentIdeHistory`.
- [x] A single-file restore cannot replace unrelated files —
      `ReviewThenDiscardRestoresOnlyTheReviewedFile` (two dirty files, the
      unrelated one byte-identical, index and HEAD unchanged) and
      `RestoreFromCommitWritesOnlyTheSelectedWorktreeFile`.

Implementation steps:

- [x] Step 1 diff/review confirmation — `ReviewAsync` + `discard/review` +
      `DiscardRestoreDialog`.
- [x] Step 2 recovery snapshot where practical — HIST-01 snapshot before the
      mutation; blocked when it cannot be created (deleted-file exception below).
- [x] Step 3 file-scoped restore — `git restore [--source=<commit>] --worktree -- <path>`
      with a literal pathspec and structured arguments.
- [x] Step 4 reindex and refresh status — reindex trigger, Document IDE History
      record and status re-read in the same result.

Failure / edge cases:

- [x] Git is not installed / not discoverable — `MissingGitIsTypedUnavailableForReviewAndApply`.
- [x] Repository/worktree disappears — `WorktreeDisappearingIsTypedFailureNotAnException`.
- [x] Path contains spaces, Unicode or special characters —
      `PathsWithSpacesUnicodeAndBracketsStayExactAndNeverWiden` (and the hostile
      path theory).
- [x] Index lock unavailable — `IndexLockContentionIsTypedAndLeavesTheFileUnchanged`.
- [x] Word changed a DOCX between status/diff/restore —
      `ReviewTokenIsInvalidatedWhenTheFileChangesAfterReview`,
      `FileChangingDuringPreparationIsDetectedBeforeGitRuns`;
      an externally locked file is
      `UnreadableFileIsTypedFailureAndNothingIsMutated`.
- [x] Hook / permission / authentication / conflict / remote rejection — conflict
      is the typed `ConflictedPath` refusal above; permission/lock are typed Git
      failures; hook, authentication and remote-rejection outcomes are **N/A** for
      this surface in one sentence: `git restore` runs no commit/push/pull hook and
      never contacts a remote, so those classifications cannot occur here.

## Deviations / spec conflicts

- **`docs/sprints/SPRINT-STATUS.md` was not updated.** The sprint prompt for this
  task explicitly reserves the board for the controller. The SCM-10 row is
  reported as DONE with commit `b407246` and this handoff.
- **Deleted tracked files bypass the recovery snapshot deliberately.** A tracked
  file deleted in the working tree has no current content to lose; a discard
  *recreates* it from Git, so blocking it for lack of a snapshot would prevent a
  non-destructive recovery. The review states `recoveryAvailable: false` with the
  reason, and the operation proceeds without a snapshot
  (`DeletedTrackedFileIsRecreatedAndReportsNoContentToPreserve`). This is the
  sprint's "where practical" judgment, recorded loudly here. Every case with
  content to lose is snapshot-or-blocked.
- **Discard restores from the index, not HEAD** (`git restore --worktree`).
  This is Git's working-tree-only semantics: staged content survives a discard
  and only the unstaged edit is lost
  (`DiscardPreservesStagedContentAndNeverRewritesTheIndex`). Discarding a staged
  change itself is the separate, explicit `unstage` action.
- **Version restore is a separate `POST restore` route** with a hex-validated
  commit argument (`--source=<commit>`), because the sprint goal explicitly
  includes restoring a selected document from a Git version and spec §15.2
  requires showing the target version. It still writes only the working tree.
- **A new record kind in the existing `JsonlHistoryStore`.** The sprint requires
  Document IDE History to record the operation; extending the existing store
  (same root, same class) was chosen over either abusing
  `BatchApplyHistoryRecord` (an apply/undo-specific shape) or inventing a second
  history store.
- **One extra Core file** (`GitRestoreResult.cs`) beyond the planned list; it is
  the typed contract consumed by Infrastructure/API and follows the
  SCM-02/06/07/08 Core placement.
- **API tests replace `IHistoryStorage` and `IGitRestoreHistoryRecorder`** with
  temp instances so a test run never writes to the real
  `%LOCALAPPDATA%\DocumentIDE` history/snapshot store.
- **`npm ci` was run** in the fresh worktree to restore the existing
  `node_modules` from the committed lockfile; no dependency or lockfile change.

## Risks discovered

- **The review token is a staleness/consent binding, not an authorization
  capability.** It is a deterministic hash of the reviewed state, so any caller
  that already holds the privileged session token could compute it. The actual
  authorization boundary remains `PrivilegedRequestGuard` (Origin/Host/session
  token); the token's job is to force a review of *this* exact state and to
  invalidate stale confirmations.
- **The check→restore window is narrowed, not eliminated.** A second fingerprint
  check runs immediately before Git, but Git performs the write afterwards; an
  external save in that last window can still be overwritten. This matches the
  BAT-04/HIST-02 discipline and its documented residual window.
- **Diff capture is bounded after capture, not during it.** `git diff` output is
  read in full by the SCM-01 runner and then clipped to `MaxReviewDiffCharacters`
  (64 KiB) with `IsDiffTruncated`. A pathological multi-hundred-megabyte textual
  diff would still be materialized once. The same applies to SCM-08's `log`
  reads; a streaming read is a cross-sprint runner improvement.
- **No reparse-point/junction check in the Git write path.** Containment is
  enforced on canonical paths (SCM-02/03/08 pattern), but a junction *inside* the
  worktree could redirect a write outside it. FS-08 owns reparse containment; a
  follow-up should reuse it here. This does not widen the pathspec: the argument
  is still exactly one literal worktree-relative path.
- **Repository-configured smudge/clean filters still run** during `git restore`
  (normal Git semantics); hooks are not invoked by restore, and the service never
  commits, pushes or pulls.
- **The surface is not reachable from the shipped desktop host yet.** The
  controller is deliberately absent from `WebAppHost.AllowedControllers` (W30)
  and the dialog is not mounted in `App.tsx` (Lane C), exactly as SCM-08 left its
  panel.
- **`GitDocumentPath.TryNormalizeRelativePath` trims leading/trailing
  whitespace** (shared with SCM-06/08). Windows filenames cannot end in a space,
  so this is a consistency choice rather than a practical loss.

## Next eligible sprint(s)

- SCM-12 (Safe Fast-Forward Pull) becomes eligible once SCM-11 is DONE; this
  sprint deliberately delivered no pull/merge/remote surface.
- The SCM-10 API surface still needs the separately tracked W30 reachability work
  (controller allow-list + `App.tsx` mounting), which this sprint was told not to
  touch.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — every
      mutation requires the review token; missing/stale tokens, conflicts,
      untracked paths, containment failures, recovery failures and unreadable
      files all terminate before Git runs
      (`DirtyFileDiscardRequiresExplicitReview`,
      `ReviewTokenIsInvalidatedWhenTheFileChangesAfterReview`,
      `ConflictedFileIsRefusedTypedAndItsStateIsUntouched`,
      `FailedRecoverySnapshotBlocksTheMutation`,
      `HostilePathsAreRejectedBeforeAnyDestructiveGitRuns`).
- [x] **no arbitrary shell command can be injected through a filename/path** — no
      shell exists (structured `ArgumentList` only); the one path travels after
      `--` as a discrete argument with `GIT_LITERAL_PATHSPECS=1`
      (`PathsWithSpacesUnicodeAndBracketsStayExactAndNeverWiden` pins the exact
      argument list); commit ids are hex-only; no repository text becomes an
      option.
- [x] **no repository escape is possible through path handling** — canonical
      worktree containment is checked before any Git/filesystem mutation; the
      hostile-path theory covers `..`, `.`, `//`, absolute, drive-qualified,
      colon and directory inputs, and asserts an outside file is untouched.
- [x] **no secret/credential output is exposed in UI logs** — the review/apply
      payloads carry only the selected file's diff, typed statuses and safe
      messages; there are no environment dumps, no remote commands and no
      credential-helper interaction. `PrivilegedRequestGuard` remains the
      boundary.
- [x] **no destructive action silently broadened its scope** — the restore
      invocation is exactly `restore [--source=<hex>] --worktree -- <path>`; the
      two-file and staged-content tests prove the other file and the index are
      untouched; HEAD/commit history are unchanged; the frontend has no "select
      all"/repository-wide control.
- [x] **relevant higher-level safety rules remain stronger than convenience** —
      recovery exists before mutation (asserted at the Git invocation instant) and
      blocks when unavailable; conflicts and untracked files are never
      overwritten; stale reviews are rejected; staged content survives a discard;
      no auto-commit/stage/push was added.
