# Sprint Handoff — BAT-11 — Batch Results & History UI

**Status:** DONE  
**Branch:** `feat/bat-11-batch-results-history-ui`  
**Commits:** `bd8e9b0` — `feat(batch): batch results & history ui` (component + tests); this handoff follows in a docs commit.  
**Base:** `e34fb32` (includes BAT-10 + HIST-02, both DONE on `SPRINT-STATUS.md`)

## What changed

- New `HistoryPanel` (Web, `src/DocumentIDE.Web/src/features/history/HistoryPanel.tsx`) — presentational operation-history surface, exactly the sprint's five steps:
  - **Step 1 — timeline:** ordered `<ol aria-label="Operation history">`; each entry renders `<time dateTime={recordedUtc}>`, operation type, file count, and an empty state when nothing was recorded. Entry order is the caller's (the panel does not reorder or invent entries).
  - **Step 2 — per-outcome counts:** succeeded/skipped/failed counts derived from the per-file `Success`/`Skipped`/`Failure` outcomes; status wording is truthful: `Partial`, `Blocked`, `Cancelled` are rendered explicitly, and a `Completed` operation that still has failed/skipped files renders `Completed with failures` / `Completed with skipped files` — never a complete-success claim (acceptance criterion).
  - **Step 3 — inspect/undo actions:** typed callbacks `onInspect(operationId)` and `onUndo(operationId)`. Undo is only offered when a recovery snapshot exists **and** at least one file actually succeeded; otherwise the entry states why (`No recovery snapshot…` / `No files were changed; nothing to undo.`) so a blocked/no-op operation is never presented as silently undoable.
  - **Step 4 — storage usage:** `usedBytes`/`capacityBytes` (mirrors `IHistoryStorage.GetTotalSizeAsync`/`GetCapacityBytesAsync`) shown as GiB + percent with a progress element; warning (`role="alert"`, testid `history-storage-warning`) at `>= 80%` of the budget per spec 00 §101.9 / spec 02 §history budget (`history budget = 5 GiB`, `warning = 80%`). Zero capacity is guarded (no NaN).
  - **Step 5 — failure navigation:** failed apply files and undo `Conflict`/`Failure` files render `Open <documentName>` buttons calling `onOpenDocument(documentId)`; reason codes and details are shown per file so a changed-after-preview (`Stale`) file stays inspectable.
  - **HIST-02 conflict policy:** undo results are reviewed inline — per-file undo outcomes (`Restored`/`AlreadyRestored`/`Conflict`/`Failure`/`Skipped`) with reason codes/details, and the explicit wording “N file(s) changed after the operation and … not overwritten.” No force/overwrite affordance exists; re-undo passes only the operation id (asserted in tests).
- View-model layer mirrors the Core contracts (string unions like `UndoConflictDialog` does): `BatchApplyFileOutcome` (`Success`/`Skipped`/`Failure`), `UndoFileOutcome` (`Restored`/`AlreadyRestored`/`Conflict`/`Failure`/`Skipped`), HIST-02 statuses (`Completed`/`Partial`/`Blocked`/`Canceled`), and the `BatchApplyHistoryRecord`/`UndoFileResult` field set (`documentId`, `outcome`, `reasonCode`, `detail`, `snapshotId`, `recordedUtc`). The only extra fields (`operationType`, `recordedUtc`) come from spec 02 §18 `OperationHistory` (`operation_type`, `created_at`), because the timeline needs them; no other data is invented.
- No API/DI wiring (downstream owns it), no call `fetch`, no new dependencies, no App shell change (avoiding fake wiring). No `.cs` file touched.
- Shared-file rule respected: exactly the two planned Web files were created; `docs/sprints/SPRINT-STATUS.md` deliberately **not** edited (controller-owned per the Batch B dispatch ruling).

## Files changed

- `src/DocumentIDE.Web/src/features/history/HistoryPanel.tsx` — new (180 lines).
- `src/DocumentIDE.Web/src/features/history/HistoryPanel.test.tsx` — new (220 lines, 14 tests).
- `docs/sprints/handoffs/BAT-11-HANDOFF.md` — this file.
- **Planned-file mapping:** the sprint planned `tests/frontend/history/HistoryPanel.test.tsx`; per the Batch B test-location ruling the test was created at `src/DocumentIDE.Web/src/features/history/HistoryPanel.test.tsx` (legacy `tests/frontend/**` is not wired into the vitest runner; root is `src/DocumentIDE.Web`). The component was created at its planned path unchanged.

## Verification

Environment: `D:\document-ide\.worktrees\bat-11\src\DocumentIDE.Web` (dependencies via `npm ci`).

- `npx vitest run src/features/history/HistoryPanel.test.tsx` → **14 passed / 0 failed / 0 skipped** (log `focused-final.log` in the SDD dir).
- `npx vitest run` (full Web suite, directly affected project) → **21 files / 110 tests passed, 0 skipped** (`web-full-suite-final.log`).
- `npm run build` (`tsc -b && vite build`) → **PASS** (`web-build-final.log`).
- `npm run lint` (oxlint) → no errors; only pre-existing warnings in files outside this change.
- TDD evidence: RED → GREEN per behavior, one cycle per implementation step; logs `.superpowers/sdd/BAT-11-batch-results-history-ui/cycle{1..7}-{red,green}.log`. The mandated first behavior (“Partial result is visible”) failed first as an unresolved `./HistoryPanel` module import (compile RED, expected: behavior absent).
- `scripts/verify.ps1` - PASS on the sprint tree: build + 12 Safety + 7 Architecture + 440 Core + 266 Integration + Web build/tests, 0 skipped (`.superpowers/sdd/BAT-11-batch-results-history-ui/verify-bat-11.log`).

## Acceptance criteria

- [x] **UI never claims complete success for a partial operation** — `Partial`/`Blocked`/`Canceled` render as such and `Completed` with failed/skipped files renders `Completed with failures`/`Completed with skipped files`; never “done”/“complete success”. Evidence: `Partial result is visible…`, `never claims complete success when a Completed operation still has failures`, `renders a Completed operation with only skipped files as completed with skips`, `renders a Blocked operation as blocked, not done`.
- [x] Required test — **Partial result is visible**: explicit partial wording + succeeded/skipped/failed counts (mandated first RED; compile RED shown before the component existed).
- [x] Required test — **History usage warning appears at threshold**: warning at exactly 80% (4.0/5.0 GiB), absent at 78%, zero-capacity guard.
- [x] Required test — **Undo action follows conflict policy**: conflicts surfaced with `ExternallyModified` + reason/detail and the “not overwritten” warning; `Restored` shown only where restored; no force/overwrite path; `onUndo` receives only the operation id.
- [x] All five Implementation Steps completed (timeline, counts, actions, storage usage/warning, failure links).
- [x] No required test skipped; no unresolved placeholders.

## Security & file-safety gate

- [x] **No user file was mutated through an unreviewed/bypass path** — the panel is strictly read-only: it only invokes caller-provided callbacks and never touches the filesystem or API.
- [x] **No arbitrary path or secret is exposed** — only document ids, caller-resolved display names, reason codes and detail strings already carried by the contracts are rendered; no raw paths, no storage internals beyond byte totals.
- [x] **Dependency/security boundaries remain intact** — no new packages, no API/DI/network surface, Web feature stays presentational.
- [x] **No destructive action silently broadened its scope** — undo is a single typed operation-id callback; conflicts are surfaced and there is no force/overwrite control.
- [x] **Higher-level safety rules remain stronger than convenience** — partial/blocked/canceled states are worded truthfully, undo is withheld when there is nothing restorable, and HIST-02's “never overwrite newer work” policy is visible in the UI copy.

## Deviations / spec conflicts

- None; no spec weakened. Two presentation decisions worth review:
  1. Core `BatchApplyResultStatus` currently has no `Partial` member, while spec 02 §18 `OperationHistory` defines `partial` and HIST-02 `UndoResultStatus` has `Partial`. The view-model status union mirrors the HIST-02/spec-02 vocabulary (`Completed`/`Partial`/`Blocked`/`Canceled`) so a partial operation can be rendered once the `OperationHistory` store supplies it; the panel does not compute a `Partial` status by itself.
  2. `Completed` with any failed/skipped files renders a qualified label rather than a bare `Completed`, to satisfy the acceptance criterion for partial results.
- `docs/sprints/SPRINT-STATUS.md` not updated (controller-owned per the Batch B dispatch ruling).

## Risks discovered

- Entry ordering is the caller's responsibility (the panel renders the given order); the downstream history loader must sort by `recordedUtc` if the spec-mandated “Today 16:31” ordering is desired.
- Timestamps render with `toLocaleString()` (locale-dependent display); tests assert the `dateTime` attribute, not the formatted label.
- Storage sizes display in GiB with one decimal only; sub-GiB libraries read “0.0 GiB”. Acceptable for the 5 GiB budget, worth revisiting if the budget becomes configurable.
- The undo summary line is derived from the last visible `UndoResult`; if the downstream API only exposes the operation-level `UndoRecord` status (Completed/Partial/Blocked/Canceled) without per-file outcomes, the conflict rows cannot be populated — downstream should pass the per-file `UndoResult` when available.

## Next eligible sprint(s)

- None implied by this sprint; the controller updates the board and dispatches follow-ups after integration (QA-04 “Batch Safety Regression Suite” was READY alongside BAT-11 in the Batch B plan).
