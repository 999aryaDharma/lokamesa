# Sprint Handoff — CAN-07

**Status:** DONE
**Branch:** `feat/can-07-multi-select-group-drag-tidy`
**Commits:** `feat(canvas): multi-select, group drag & tidy`

## What changed

- **Selection controller (new).** `src/DocumentIDE.Web/src/features/canvas/selection/SelectionController.ts` —
  pure functions over an input snapshot: `rectSelect` (ids intersecting the drag rectangle, same
  edge-touching predicate as the spatial index), `applyClick` (plain click replaces, Ctrl-click
  toggles one id), `groupDrag` (uniform translation of movable selected objects by the same delta —
  relative positions preserved exactly so stacking is structurally impossible; pinned selections stay
  put; unselected objects untouched). No filesystem/network/DOM access.
- **Tidy layout engine (new).** `src/DocumentIDE.Web/src/features/canvas/layout/TidyLayout.ts` —
  `tidySelection` (only selected objects affected, flow anchored at the selection's bounding-box origin
  so the approximate region is preserved, pinned objects never move even when selected, unselected
  objects are immovable blockers) and `tidyCanvas` (every unpinned object eligible, every pinned object
  an immovable blocker that is never overlapped). Both resolve collisions exclusively through the
  CAN-05 `SpatialHashIndex` (blockers + already-placed cards; eligible cards are invisible until placed
  so probes never collide with stale pre-tidy rects), keep reading order when practical, keep same-
  `groupId` members consecutive, and degrade to keep-in-place (never fling across the canvas) when the
  neighbourhood is fully packed. Pure functions returning new coordinates; inputs never mutated.
- **Tests (new, colocated per CAN-05 ruling).** `selection/SelectionController.test.ts` (6 tests) +
  `layout/TidyLayout.test.ts` (7 tests) covering all three required tests plus rect/empty selection,
  click semantics, pinned-in-drag, selection scope, overlap resolution, input immutability, and the
  100/500/1,000 tidy benchmark probe.

### Test-path mapping (CAN-05 ruling, same as CAN-04/CAN-05/CAN-06)

| Sprint planned path | Actual path (ruling) |
|---|---|
| `tests/frontend/canvas/TidyLayout.test.ts` | `src/DocumentIDE.Web/src/features/canvas/layout/TidyLayout.test.ts` |
| (no planned path; planned impl file `selection/SelectionController.ts` kept) | `src/DocumentIDE.Web/src/features/canvas/selection/SelectionController.test.ts` |

`tests/frontend/**` is not wired into the vitest runner (root is `src/DocumentIDE.Web`); tests live next
to the implementation, matching precedent.

### API reuse (CAN-05/CAN-06)

- `PositionMode` (`auto`/`manual`/`pinned`) imported from `layout/AssistedPlacement` — one vocabulary,
  no duplicate union (same as CAN-06 frontend).
- Collision math via `rectsOverlap` + `SpatialHashIndex` from `layout/SpatialIndex` — no new geometry.
- Pin semantics match CAN-06: `pinned` ⟺ immovable; explicit tidy is a user command (not automatic
  layout), so user-placed `manual` objects move only when the user invokes tidy on them.

### Tidy benchmark numbers (recorded for QA-06)

Overlapping jittered-card canvas, `tidyCanvas` wall-clock on the dev machine (Windows, Node 24):

| Canvas size | Elapsed | CAN-05-style target | Headroom |
|---|---|---|---|
| 100 objects | 0ms (<1ms at `Date.now` resolution) | 2s | ~2000× |
| 500 objects | 3–5ms | 5s | ~1000× |
| 1,000 objects | 7–9ms | 10s | ~1000× |

Machine-independent property: every placement probe is a local index query (asserted
`stats.indexQueries > 0`); cost scales with local cell density, never with a full canvas scan.
QA-06 should re-run these probes on its own hardware plus drag/multi-drag timing.

## Files changed

- `src/DocumentIDE.Web/src/features/canvas/selection/SelectionController.ts` (new)
- `src/DocumentIDE.Web/src/features/canvas/selection/SelectionController.test.ts` (new)
- `src/DocumentIDE.Web/src/features/canvas/layout/TidyLayout.ts` (new)
- `src/DocumentIDE.Web/src/features/canvas/layout/TidyLayout.test.ts` (new)
- `docs/sprints/SPRINT-STATUS.md` (CAN-07 row → DONE)
- `docs/sprints/handoffs/CAN-07-HANDOFF.md` (this file)

## Verification

- `npx vitest run src/features/canvas/layout/TidyLayout.test.ts src/features/canvas/selection/SelectionController.test.ts` → PASS (2 files, 12 tests: 6 + 6)
- `npx vitest run` in `src/DocumentIDE.Web` → PASS (33 files, 202 tests, 0 skipped)
- `npm run lint` → PASS (only pre-existing warnings in unrelated files; none in `selection/` or `layout/TidyLayout.*`)
- `npm run build` (`tsc -b && vite build`) → PASS
- TDD: Red was the unresolved `./TidyLayout` import (pinned-exclusion test), then the unresolved
  `./SelectionController` import; one self-caught Green bug (stale `void placed;` after refactor) fixed
  before proceeding. The region test carries a `moves.length > 0` guard so it cannot pass vacuously.
  No backend touched: `git status` shows only the files above.

## Acceptance criteria

- [x] **Tidy is organizational only; no filesystem mutation.** Both modules are pure geometry: inputs
  snapshot-compared byte-identical after `tidyCanvas` + `tidySelection` (immutability test); imports are
  types + `SpatialIndex`/`AssistedPlacement` only — no `fs`, `path`, `fetch`, or DOM writes. Canvas drag
  still only changes workspace metadata, never physical files.

## Deviations / spec conflicts

- None in behavior. Two integration notes:
  1. **Tidy canvas moves `manual` objects (explicit command only).** Spec 03 §13 says "operates on
     unpinned objects" (manual is unpinned); §9's "strongly avoid moving manual" governs *automatic*
     layout. Tidy is always user-invoked, so manual objects move only on explicit user request; pinned
     objects never move under any tidy path (asserted, including pinned-in-selection).
  2. **Fully-packed neighbourhood keeps overlap in place.** When no free cell exists within the probe
     window, the card keeps its original coordinates instead of being flung across the canvas (region
     preservation wins over overlap resolution; callers see an empty/absent move for that id).

## Risks discovered

- Duplicate object ids throw loudly from `SpatialHashIndex.insert` (CAN-05 discipline: caller bug
  surfaces instead of corrupting the run) — wiring code must pass a de-duplicated snapshot.
- `orderForTidy` groups by the optional `groupId` field; no producer sets it yet, so group preservation
  is currently ordering-stability only until a grouping sprint wires membership through.

## Security & File-Safety Gate

- [x] **No user file was mutated through an unreviewed/bypass path** — pure in-memory geometry; no
  `fs`/`path`/`fetch`/DOM/store writes; immutability test proves inputs untouched.
- [x] **No arbitrary path or secret is exposed** — inputs carry ids/titles/bounds/mode only; no path fields.
- [x] **Dependency/security boundaries remain intact** — frontend-only; zero backend (C#) files touched;
  no API surface added; results are computed coordinates, not applied to any store.
- [x] **No destructive action silently broadened its scope** — there is no destructive action; worst case
  is keep-in-place with no move recorded for that id.
- [x] **Relevant higher-level safety rules remain stronger than convenience** — "canvas drag never
  physically moves files" and "local filesystem is source of truth" untouched; tidy returns coordinates
  without applying them anywhere.

## Next eligible sprint(s)

- `QA-06` — Performance Benchmark Gate (unblocked on the CAN-07 axis; reuse the recorded tidy numbers
  above plus drag/multi-drag timing at 100/500/1,000 objects)
