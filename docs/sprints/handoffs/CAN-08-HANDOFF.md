# Sprint Handoff — CAN-08

**Status:** DONE  
**Branch:** `feat/can-08-list-view-view-switch`  
**Commits:** `32130a0` — feat(canvas): list view & view switch (handoff added in a follow-up docs commit)

## What changed

- Added `viewStore.ts` — a tiny UI-preference external store (same pattern as `canvasStore`/`graphStore`) holding only the active main view (`canvas | list | graph`), persisted to `localStorage` under `documentide_active_view` with typed validation and safe fallback to `canvas`. It is NOT a parallel document/selection store.
- Added `ViewSwitcher.tsx` — controlled segmented Canvas | List | Graph switch (`role="radiogroup"`, `aria-checked`, `data-testid="view-*-btn"`), modeled on `SearchModeSwitch`.
- Added `DocumentList.tsx` — dense list over the SAME `canvasStore.objects` (`data-document-id` = existing DocumentIds). Columns: Name, Type (derived from `docType` or extension, mirroring `DocumentNode`), Status (missing / thumbnail state / statusText). Sorting on all three columns (`aria-sort`, deterministic tie-break by id), name/type filtering, bulk selection (row checkbox, row click, select-all-visible header checkbox with indeterminate state), selection count, clear-selection, empty/no-match states.
- Selection uses the existing selection model: `canvasStore.selectedIds` / `toggleSelect` / `clearSelection` — no parallel selection store.
- `App.tsx` — minimal additive wiring: renders `ViewSwitcher` in the nav, routes `canvas → DocumentCanvas`, `list → DocumentList`, `graph → DocumentGraph` (existing placeholder routed for a later sprint), and calls `loadPersistedView()` on mount.

## Files changed

- `src/DocumentIDE.Web/src/features/list/DocumentList.tsx` (new; planned)
- `src/DocumentIDE.Web/src/features/list/DocumentList.test.tsx` (new; planned test, relocated per ruling)
- `src/DocumentIDE.Web/src/features/workspace/ViewSwitcher.tsx` (new; planned)
- `src/DocumentIDE.Web/src/features/workspace/ViewSwitcher.test.tsx` (new; optional test beside component)
- `src/DocumentIDE.Web/src/features/workspace/viewStore.ts` (new; small additive file beyond the planned pair — flagged)
- `src/DocumentIDE.Web/src/App.tsx` (modified, additive only — flagged shared-file edit; no other Batch-B sprint plans this file)

### Test-location mapping (batch ruling)

- Sprint planned `tests/frontend/list/DocumentList.test.tsx` → `src/DocumentIDE.Web/src/features/list/DocumentList.test.tsx` (vitest root is `src/DocumentIDE.Web`; active convention is tests beside components).
- Optional ViewSwitcher test → `src/DocumentIDE.Web/src/features/workspace/ViewSwitcher.test.tsx`.

## Verification

- `npx vitest run src/features/list/DocumentList.test.tsx src/features/workspace/ViewSwitcher.test.tsx src/App.test.tsx` → PASS (3 files, 25 tests)
- `npx vitest run` (full web suite) → PASS (22 files, 121 tests; baseline was 20 files / 96 tests)
- `npm run build` (tsc -b && vite build) → PASS
- `scripts/verify.ps1 — controller runs full repository verification on this worktree.`

## TDD evidence (summary)

Every behavioral step had a failing run first. Mandated first behavior RED: `TestingLibraryElementError: Unable to find an element by: [data-testid="view-list-btn"]` before `ViewSwitcher`/`DocumentList`/App routing existed. Persistence RED: `TypeError: useViewStore.getState(...).loadPersistedView is not a function`. Columns RED: no `columnheader` roles / no `DOCX`/`Missing` cells. Filter RED: `Unable to find a label with the text of: Filter documents`. Selection RED: `Unable to find a label with the text of: Select BA-Agustus.docx`. Sort RED: `expected ['doc-c','doc-a','doc-b'] to deeply equal ['doc-a','doc-b','doc-c']` plus missing `Sort by name/status` labels. Full commands/outputs are in `.superpowers/sdd/CAN-08-list-view-view-switch/task-1-report.md`.

## Acceptance criteria

- [x] **List is another view of same DocumentIds, not imported copies** — evidenced by `DocumentList.test.tsx` › `Switching view does not lose workspace` (list `data-document-id` set equals `canvasStore.objects` ids) and › `Acceptance: list is another view of the same DocumentIds` (the `objects` array reference is `toBe`-identical after filtering/sorting/selecting; the list never calls `setObjects`/`upsertObject`).
- [x] Required tests present and passing: `Switching view does not lose workspace`, `List selection can feed existing selection model`, `Returning to Canvas preserves coordinates`.
- [x] Implementation steps: persisted active-view switch (localStorage seam tested), list columns, sort/filter/bulk selection, shared selection preserved across view switches, Graph routed to the existing placeholder.

## Deviations / spec conflicts

- `viewStore.ts` added beyond the planned two-component file list — a small UI-preference store following existing store patterns; not a parallel document/selection store (flagged per common rules).
- `App.tsx` modified additively to mount the switch and route views (anticipated by the dispatch; flagged).
- `master spec §8.2` lists further List columns (folder/date/size/pages/paper/font/margins). Only metadata available on `CanvasObject` today is rendered (Name/Type/Status); unavailable columns are not faked and are left for the metadata sprints (e.g. DOCX-04 Document Lens). No higher-level requirement weakened.

## Risks discovered

- `SearchPanel.test`-style checkbox rows elsewhere double-toggle (checkbox `onChange` + parent row `onClick` bubble). `DocumentList` explicitly stops propagation on the checkbox cell and has a regression test for single-toggle. (SearchPanel itself untouched — out of scope.)

## Security & File-Safety Gate

- [x] No user file was mutated through an unreviewed/bypass path — UI-only changes; list operations never touch the filesystem.
- [x] No arbitrary path or secret is exposed — list renders stored display metadata only (no file paths).
- [x] Dependency/security boundaries remain intact — no new dependencies; reads existing stores.
- [x] No destructive action silently broadened its scope — filtering/sorting are display-only; selection feeds the existing selection model only.
- [x] Relevant higher-level safety rules remain stronger than convenience — canvas coordinates/identity untouched; no data re-import; Graph placeholder not expanded.

## Next eligible sprint(s)

- `PDF-04` — depends on `CAN-08` (and `PDF-03`, still blocked). CAN-08 itself is DONE.
- Controller should update `docs/sprints/SPRINT-STATUS.md` (not edited by this agent per batch rules).
