# Sprint Handoff — CAN-05

**Status:** DONE
**Branch:** `perf/can-05-spatially-indexed-assisted-placement`
**Commits:** `perf(canvas): spatially indexed assisted placement`

## What changed

- **Spatial hash/grid index (new).** `src/DocumentIDE.Web/src/features/canvas/layout/SpatialIndex.ts` —
  `SpatialHashIndex` (uniform grid, default 256px cells) with insert/update/remove/query plus
  instrumentation (`queries`, `entryVisits`, `intersectionTests`) proving queries stay local.
  Duplicate insert throws loudly so double-release bugs surface instead of corrupting the index.
- **Assisted placement engine (new).** `src/DocumentIDE.Web/src/features/canvas/layout/AssistedPlacement.ts` —
  `placeDocumentsNearOrigin(existing, specs, options)`: compact grid/fan from the origin anchor
  (source folder), collision lookup exclusively through the index, local reflow of eligible `auto`
  objects only (short spiral nudge, inside 600px `reflowRadius`, max 8 moves/run), `pinned`/`manual`
  objects never moved and never overlapped, already-placed cards of the current run never reflowed
  (keeps recorded coordinates exact), typed `blocked` outcome (`no-free-space` / `blocked-by-pinned`
  with `blockingIds`) instead of silent fallback. Pure function over an input snapshot: never mutates
  its inputs, no filesystem/network/DOM access.
- **Tests (new).** `SpatialIndex.test.ts` (6 tests) + `AssistedPlacement.test.ts` (9 tests) covering all
  four required tests plus no-reshuffle, manual-mode, local-reflow displacement, and the 100/500/1,000
  benchmark gate.

### Test-path mapping (batch-B ruling, same as CAN-04)

| Sprint planned path | Actual path (ruling) |
|---|---|
| `tests/frontend/canvas/AssistedPlacement.test.ts` | `src/DocumentIDE.Web/src/features/canvas/layout/AssistedPlacement.test.ts` |

`tests/frontend/**` is a legacy directory not wired into the vitest runner (vitest root is
`src/DocumentIDE.Web`); tests live next to the implementation, matching the CAN-04 precedent.

### Spatial index decision (benchmark-selected)

**Chosen: uniform spatial hash/grid (256px cells).** Rejected R-tree/quadtree: canvas objects are
uniformly-sized cards, so a grid gives O(1) insert/query with far less code and no rebalancing.
Justification is comparative against the brute-force full scan (the failure mode spec §35 forbids):

| Canvas size | 25 placements wall-clock | Examined / placement | Grid intersection tests | Brute-force equivalent (queries × n) |
|---|---|---|---|---|
| 100 objects | 1.7ms | 68.2 | ~1.7k | 32.5k |
| 500 objects | 1.7ms | 68.2 | ~1.7k | 162.5k |
| 1,000 objects | 2.7ms | 68.2 | ~1.7k | 325k |

Index work is **identical at 100/500/1,000 objects** (asserted in-test: 1,000-object examined total ≤
1.5× the 100-object total) — cost depends on local cell density only, never on canvas size. Recorded
usability targets (`PLACEMENT_BENCHMARK_TARGETS_MS`: 2s/5s/10s) pass with ~1000× headroom on the dev
machine (Windows, Node 24.11.0). Machine-independent gate `MAX_EXAMINED_PER_PLACEMENT = 100`.

## Verification

- `npx vitest run src/features/canvas/layout/` → PASS (2 files, 15 tests, incl. benchmark printout above)
- `npx vitest run` in `src/DocumentIDE.Web` → PASS (27 files, 168 tests, 0 skipped)
- `npm run lint` → PASS (only pre-existing warnings in unrelated files; none in `layout/`)
- `npm run build` in `src/DocumentIDE.Web` (`tsc -b && vite build`) → PASS
- No backend touched: `git status` shows only `src/DocumentIDE.Web/src/features/canvas/layout/` (new),
  plus this handoff and the SPRINT-STATUS row.

## Acceptance criteria

- [x] **No global reshuffle on local insert.** Test "no global reshuffle on local insert" places 2 docs
  onto a 200-object canvas: `reflowed` is empty and every distant object is byte-identical before/after.
  Reflow is structurally confined: only colliding `auto` objects within `reflowRadius` of the origin,
  capped at `maxReflowMoves`, each nudge ≤ ~362px (test asserts ≤ 400px).
- [x] **New files appear near origin.** First candidate is the origin itself; compact fan keeps all
  placements within one screen (< 800px, asserted) and mutually non-overlapping.
- [x] **Pinned objects never move.** Pinned (and manual) blockers force candidate-skip; `tryLocalReflow`
  refuses immovable blockers; tests assert pinned/manual coordinates unchanged, zero overlap with
  placements, and no pinned/manual id ever appears in `reflowed`.
- [x] **Placement uses nearby candidates instead of full scan.** `SpatialHashIndex.query` visits only
  entries in cells touched by the query rect; instrumented stats asserted bounded and size-independent
  (table above).
- [x] **1,000-object benchmark meets recorded usability target.** 2.7ms vs 10s target; strict
  machine-independent gates also pass.

## Deviations / spec conflicts

- None in behavior. Three integration notes:
  1. **Position-mode metadata is layout-local and additive.** `PlaceableObject.positionMode`
     (`auto`/`manual`/`pinned`, default `auto`) lives only in the layout module; durable pin/position
     persistence belongs to CAN-06, which should adopt this exact vocabulary when wiring the store.
  2. **Duplicate ids fail loud, not typed.** A duplicate spec/existing id throws from `insert()` rather
     than returning a `blocked` result — deliberate: it indicates a caller double-release bug, and the
     throw names the id. Documented here instead of adding a third blocked code for programmer error.
  3. **Candidate probing is triangular in *batch* size, constant in *canvas* size.** Placing k new docs
     costs ~k²/2 index queries (each new card skips cells taken by earlier cards of the same run: 325
     queries for 25 docs at every canvas size). Fine for picker-scale releases (tens of files); a future
     "next-free-cell cursor" can linearize this if open-all of hundreds becomes hot. Flagged for QA-06.
- TDD process note: Red was established at module level (test failed on missing `./AssistedPlacement`
  import); a mid-cycle failure (just-placed cards reflowed → stale coordinates) drove the `placedIds`
  fix; the fraction-of-n benchmark gate failed honestly (68.2 > 25 at n=100) and was replaced with the
  stronger size-independence gate.

## Risks discovered

- Reflow nudges are small spiral steps (32px × up to 8 rings, 4 directions); a densely packed origin
  surrounded by `auto` objects still resolves via candidate-skip to the next free fan cell, but pathological
  full-canvas packing returns `blocked` — callers (CAN-06/07 wiring) must handle the `blocked` branch
  instead of assuming placement always succeeds.
- `positionMode` defaults to `auto` when absent, so pre-CAN-06 canvas objects without mode metadata are
  treated as reflow-eligible — correct for now, but CAN-06 must set `manual` on user-dragged objects or
  assisted placement could nudge them.

## Security & File-Safety Gate

- [x] **No user file was mutated through an unreviewed/bypass path** — pure in-memory geometry; module
  imports only `../types` (type-only) and `./SpatialIndex`. No `fs`, `path`, `fetch`, or DOM access.
- [x] **No arbitrary path or secret is exposed** — inputs carry ids/titles/bounds only; no path fields exist.
- [x] **Dependency/security boundaries remain intact** — frontend-only; zero backend (C#) files touched;
  no API surface added; positions are computed, not applied to any store (no canvas-store coupling yet —
  CAN-06 wiring point).
- [x] **No destructive action silently broadened its scope** — there is no destructive action; worst case
  is a typed `blocked` result with `blockingIds`.
- [x] **Relevant higher-level safety rules remain stronger than convenience** — "canvas drag never
  physically moves files" and "local filesystem is source of truth" untouched; `positionMode` is
  additive metadata, no existing type was weakened.

## Next eligible sprint(s)

- `CAN-06` — Position Persistence & Pinning (blocked on CAN-05; should adopt `PositionMode` vocabulary
  and handle the `blocked` result branch)
