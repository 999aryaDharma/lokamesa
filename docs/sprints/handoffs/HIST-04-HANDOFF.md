# Sprint Handoff — HIST-04 — Operation History Listing API

**Status:** DONE
**Branch:** `feat/operation-history-listing`
**Base:** `42cffb2` (SCM-17 DONE on `integration/parallel-batches`)
**Commit:** `feat(history): persist and expose the operation history listing` (branch tip; this handoff is part of that commit)

## What changed

Screen 10's **Operations** tab now reads real persisted operations. The sprint did not
change undo semantics, the recovery-snapshot rules, or the reviewed apply engine's
mutation logic.

### 1. Persisted operation record (migration `012_OperationHistory`)

- New table `operation_history` (`src/DocumentIDE.Infrastructure/Persistence/Migrations/012_OperationHistory.cs`)
  with: `id`, `workspace_id` (nullable), `plan_id`, `kind`, `outcome`, `snapshot_id`,
  `started_at`, `completed_at`, `affected_file_count`, `files_json`, `undo_json`,
  `recorded_at`, plus an index on `started_at`.
- New Core contract `src/DocumentIDE.Core/History/OperationHistoryRecord.cs`:
  `OperationHistoryRecord`, `OperationUndoRecord`, `OperationHistoryFileOutcome`,
  `OperationHistoryUndoView`, `OperationHistoryPage`, `OperationHistoryQuery`,
  `IOperationHistoryRepository`, `IOperationHistoryRecorder`, and `OperationKinds`
  (the human labels the existing apply path can persist).
- New repository/recorder `src/DocumentIDE.Infrastructure/Persistence/OperationHistoryRepository.cs`.
  All SQL is parameterized; the per-file documents are JSON serialized from the existing
  result contracts, never from client input.
- `OperationHistoryRepository` is registered in `AddPrivilegedSurfaces` as both
  `IOperationHistoryRepository` and `IOperationHistoryRecorder`.
- `IHistoryStorage`/`HistoryStorage` gained an optional `root` constructor parameter so
  tests can use an isolated storage root. Default behaviour (LocalAppData) is unchanged.

### 2. Recorded from the existing apply/undo write paths

- `BatchApplyEngine.ApplyAsync` now captures `startedAt`, and at its **existing** return
  points (Blocked / Canceled / Completed) records exactly one operation:
  - `Completed` with at least one `Success` file → `Applied`;
  - `Completed` with no successful file, or `Blocked`/`Canceled` → `Failed`;
  - `affected_file_count` = attempted files, with the engine's existing per-file outcomes.
- `UndoService.UndoAsync` records the undo review on the **same** operation row:
  - `Completed`/`Partial` undo → `Undone`;
  - `Blocked`/`Canceled` undo → the operation keeps its original `Applied`/`Failed`
    outcome (nothing was undone) and only the per-file undo review is stored.
- Neither path re-runs, re-derives or retries a mutation; the new code observes the
  existing result. A recording failure throws `InvalidOperationException`, exactly like
  the existing per-file history recorder, so it is surfaced as the surrounding typed
  apply/undo failure (never swallowed, never a fabricated row).

### 3. Bounded, privileged, read-only routes

- `GET /api/history/operations?skip=&limit=` — typed page envelope:
  `status` (`Available`/`Unavailable`), `entries`, `skip`, `limit`, `totalCount`,
  `hasMore`, `nextSkip`. Limit defaults to 50, is clamped to 1..200 (the SCM convention);
  `skip` is honoured; a negative `skip` is `400` (input rejected before any read).
  An empty history is `Available` with zero entries — never `Unavailable`.
- `GET /api/history/storage` — `usedBytes`/`capacityBytes` read from the existing
  `IHistoryStorage` (`GetTotalSizeAsync`/`GetCapacityBytesAsync`).
- `HistoryController` exposes only GETs. A read/storage failure is a typed HTTP 200
  `Unavailable` payload with `errorCode` and a generic message (no exception text,
  path or storage internals).
- The desktop host allow-list now includes `HistoryController`.

### 4. Screen 10 wiring

- New `src/DocumentIDE.Web/src/features/history/historyApi.ts`: parses the real payload,
  throws on `Unavailable`, maps the API entry to `OperationHistoryEntryView`, and calls
  the existing `POST /api/batch-operations/{planId}/undo` for undo.
- `App.tsx` passes the stable `operationHistorySource` to `HistoryScreen`.
- `HistoryPanel`'s status union was **appended** with `Applied`/`Undone`/`Failed` (the
  persisted outcomes) and rendered verbatim. `Applied`/`Completed` with failed or skipped
  files still render `… with failures` / `… with skipped files`.
- `HistoryScreen`'s unavailable copy now describes a **read failure** instead of a missing
  route. `history-operations-unavailable` is rendered only when the source is absent or
  the read rejects; an empty available history renders `No operations recorded yet.`
  with the real meter.

## Files changed

**New**

- `src/DocumentIDE.Core/History/OperationHistoryRecord.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/012_OperationHistory.cs`
- `src/DocumentIDE.Infrastructure/Persistence/OperationHistoryRepository.cs`
- `src/DocumentIDE.Api/Controllers/HistoryController.cs`
- `src/DocumentIDE.Web/src/features/history/historyApi.ts`
- `src/DocumentIDE.Web/src/features/history/historyApi.test.tsx`
- `tests/DocumentIDE.Core.Tests/History/OperationHistoryQueryTests.cs`
- `tests/DocumentIDE.IntegrationTests/History/OperationHistoryListingTests.cs`
- `docs/sprints/handoffs/HIST-04-HANDOFF.md` (this file)

**Modified (append-only where noted)**

- `src/DocumentIDE.Core/Batch/BatchApplyEngine.cs` — optional recorder at the end of the
  constructor; one recording call per existing return point.
- `src/DocumentIDE.Core/History/UndoService.cs` — optional recorder at the end of the
  constructor; undo review recording.
- `src/DocumentIDE.Infrastructure/History/HistoryStorage.cs` — optional `root` at the end
  of the constructor.
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs` — registered
  migration 012 at the end of the list.
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` — repository/recorder registrations.
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` — `HistoryController` appended to the
  allow-list.
- `src/DocumentIDE.Web/src/features/history/HistoryPanel.tsx` — union/labels/chip statuses
  appended; no existing member reordered.
- `src/DocumentIDE.Web/src/features/history/HistoryScreen.tsx` — seam docs, failure copy.
- `src/DocumentIDE.Web/src/App.tsx` — injects the real source.
- `src/DocumentIDE.Web/src/App.shell.test.tsx` — history route fixtures + HIST-04 shell test.
- `src/DocumentIDE.Web/src/features/history/HistoryScreen.test.tsx` — failure-context wording
  and a rejected-load test.

## Commands and results

Environment: `C:\Users\User\.local\share\opencode\worktree\c18aac\hist-04-listing`
(`npm install` run once in `src/DocumentIDE.Web`).

| Command | Result |
| --- | --- |
| `git checkout -b feat/operation-history-listing` | branch created from detached `42cffb2` |
| `dotnet build DocumentIDE.sln -v q` | **Build succeeded**, 0 errors, 104 warnings (all pre-existing; version/analyzer/security-advisory warnings, none from new files) |
| `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~OperationHistoryListingTests"` | **11 passed / 0 failed / 0 skipped** |
| `dotnet test tests/DocumentIDE.IntegrationTests` (final run 1, `-v q`) | **1040 passed, 2 failed, 0 skipped, 1042 total** |
| `dotnet test tests/DocumentIDE.IntegrationTests` (final run 2, normal verbosity) | **1040 passed, 2 failed, 0 skipped, 1042 total** |
| `dotnet test tests/DocumentIDE.Core.Tests` | **473 passed / 0 failed / 0 skipped** |
| `dotnet test tests/DocumentIDE.SecurityTests` | **162 passed / 0 failed / 0 skipped** |
| `dotnet test tests/DocumentIDE.ArchitectureTests` | **12 passed / 0 failed / 0 skipped** |
| `npx tsc -b` (in `src/DocumentIDE.Web`) | **exit 0** |
| `npx oxlint` (in `src/DocumentIDE.Web`) | **exit 0**, 35 warnings, 0 errors; **none in `features/history`** |
| `npx vitest run` (in `src/DocumentIDE.Web`) | **86 files / 600 tests passed, 0 failed** |
| `npx vitest run src/features/history` | **4 files / 34 tests passed** |
| `git diff --check` | clean |

### Expected environmental failures (fresh worktree, no `artifacts/`)

Both final integration runs fail exactly these two, and only these two:

- `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  — `artifacts/desktop/DocumentIDE.Desktop.exe` absent.
- `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
  — `artifacts/installer/DocumentIDE-Setup.msi` absent.

They require a full release build (`scripts/verify-release.ps1`) and are not caused by this
change.

### Flake disclosure

The **first exploratory** full integration run reported 3 failures (1038 passed). The
third failure was not captured before the next run and **did not recur** in the two final
full runs (1040 passed, 2 environmental failures each). It is reported plainly as an
unidentified one-off; no required test is skipped.

## TDD evidence (Red → Green)

1. **Core RED:** `OperationHistoryQueryTests` failed to compile with
   `CS0103: The name 'OperationHistoryQuery' does not exist in the current context`.
   After the Core contract landed: **14/14** green.
2. **API RED:** `OperationHistoryListingTests` could not compile before the record,
   migration, repository and controller existed (types absent). After implementation:
   **11/11** green, including applied/undone/failed outcomes, pagination, empty vs
   unavailable, storage usage, `401`/`403`, exactly-one-record, and a surfaced recording
   failure.
3. **Frontend wiring RED (captured):** temporarily removing `source={operationHistorySource}`
   from `App.tsx` made the new shell test fail with
   `Unable to find role="list" and name "Operation history"` (1 failed, 11 skipped).
   Restoring the prop made it pass. Focused history suite: **34/34**; full Web suite
   **600/600**.
4. **Refactor:** no behavioural drift; all gates re-run after the final repository change
   (undo no longer rewrites the apply completion timestamp).

## Acceptance criteria — evidence

- [x] **Screen 10's Operations tab renders real operations with real outcome chips and the
      real storage meter.** `historyApi.test.tsx` renders the shipped screen with
      `createOperationHistorySource()` against the real payload shape (`Applied with
      failures`, `Undone`, 2-file count, `2.0 GiB of 5.0 GiB (40%)`); `App.shell.test.tsx`
      proves the shell wiring; `OperationHistoryListingTests` proves the payload comes from
      the persisted row and the real `IHistoryStorage`.
- [x] **`history-operations-unavailable` is only shown for a genuine failure, never for
      "no data".** `historyApi.test.tsx` has separate tests for an `Unavailable` page / an
      unavailable storage read (unavailable, no list, no empty claim) and for an
      `Available` empty page (`No operations recorded yet.`, no unavailable element).
      The API test proves an empty history is `Available`, `totalCount=0`.
- [x] **No fabricated rows, counts or meter values.** Rows come only from
      `operation_history`; `affectedFileCount` and per-file outcomes are the engine's
      existing result; `usedBytes`/`capacityBytes` are compared directly to
      `IHistoryStorage` in `StorageUsage_ReflectsTheRecordedSnapshotBytesAndRealCapacity`.
- [x] **Required tests:** applied/undone/failed (3), pagination (1), empty page (1),
      storage (1), typed route failure (2), token/origin/host guard (1), exactly one record
      (1), recording failure surfaced (1); frontend real-payload, conflict undo, completed
      undo, unavailable, empty, storage-unavailable (6).
- [x] No required test skipped.

## Security & file-safety gate

- [x] **No user file mutated through an unreviewed path** — the new controller is
      read-only (two GETs); every test mutation goes through the existing reviewed
      `POST /api/batch-operations/{planId}/apply|undo` path.
- [x] **No arbitrary shell command / path injection** — no shell is invoked; every SQL
      write is parameterized; the routes take only numeric `skip`/`limit`.
- [x] **No repository escape** — no Git or filesystem path input on this surface.
- [x] **No secret/credential output** — `Unavailable` payloads omit exception text and
      storage internals; the session token stays in the header.
- [x] **No destructive action broadened** — no new mutation route; undo is unchanged and
      still requires a recovery snapshot; `UndoConflictDialog` cannot express force/overwrite.
- [x] **Listing is read-only; undo keeps its existing reviewed/guarded semantics** —
      the only change on the undo path is an observation write after the existing result.

## Deviations / spec conflicts

1. **Branch name:** the sprint recommends `feat/operation-history-listing-api`; the
   dispatch instructed `feat/operation-history-listing`, which is the branch used.
2. **Outcome derivation:** `Completed` with no successful file is recorded `Failed`
   (nothing was applied); `Completed` with at least one success is `Applied`, even when
   some files failed/skipped, because the per-file outcomes carry that detail. A
   `Blocked`/`Canceled` apply is `Failed`.
3. **Blocked/cancelled undo keeps the operation outcome.** A conflicted or cancelled undo
   did not revert the operation, so it stays `Applied`/`Failed`; only the per-file undo
   review is stored. A `Completed`/`Partial` undo flips the same row to `Undone`.
4. **Default tab remains Git.** The sprint scopes the seam wiring, not the default; both
   tabs are now real. UI-03's handoff had suggested restoring the Operations default once a
   listing exists — that remains an explicit follow-up/decision, not silently changed here.
5. **`workspace_id` is stored as NULL.** The reviewed apply/undo paths do not carry a
   workspace id, and inventing one would be fabricating data. The column exists for spec 02
   §18 alignment.
6. **No backfill.** Operations applied before this sprint were never persisted as operation
   rows and are not backfilled; new applies/undos are listed. Undo of a pre-HIST-04
   operation still inserts a row from the real undo result, so the review becomes visible.

## Residual risks

1. **The operation-row write is at-most-once, not transactional with the mutation.**
   `operation_history.id` is the plan id and uses an upsert, so a duplicate row is
   impossible and a retried apply is rejected by the completed plan before mutation
   (spec 07 §25). But if the DB write fails *after* a successful apply, the file is changed,
   the plan is `Completed`, the API surfaces the recording failure as `409`, and a retry
   cannot re-apply. The operation is then missing from the listing. The per-file JSONL
   audit still records the file outcomes, so the data is not wholly lost; closing this gap
   would need a durable outbox/retry or a pre-mutation provisional row (a schema/state-machine
   change outside this sprint). The failure is surfaced, never swallowed.
2. **The UI renders the newest 50 operations and has no "load more" control.** The route
   supports `skip`; the shipped panel does not page yet. This is a bounded first page, not
   an invented count.
3. **Undo review time is not a separate persisted timestamp.** The row keeps the apply
   `started_at`/`completed_at`; an undo does not rewrite them (verified by test). A future
   `undone_at`/review timestamp would need an additional column.
4. **`HistoryStorage.GetTotalSizeAsync` includes the `metadata` subdirectory** under the
   snapshot root (pre-existing implementation; its comment says "excluding metadata").
   The meter reports the storage implementation's real number, consistent with the rest of
   History; not changed here.
5. **`Failed` for an all-skipped apply** shows `Failed` with `0 succeeded / N skipped`
   rather than a `Failure` per-file outcome. That is truthful (nothing changed) but a
   reviewer may expect the per-file reason (`Stale`) to dominate. It is shown per file.
6. **Environment:** the two release-artifact tests fail in a fresh worktree; they require
   `artifacts/`.
7. **One unidentified integration flake** in the first exploratory run, not reproduced.
8. **Outcome vocabulary is now wider in `HistoryPanel`.** The union was appended, so any
   caller still passing the older four statuses compiles and renders as before.

## Next eligible work

- Decide whether to restore the Operations tab as screen 10's default (UI-03 residual).
- If product needs more than the first page, add a bounded "load more" using the existing
  `skip`/`nextSkip` envelope.
- Consider a durable outbox or pre-mutation provisional row to make the operation record
  retry-safe when its write fails after a successful mutation.
