# Sprint Handoff — CAN-06

**Status:** DONE
**Branch:** `feat/can-06-position-persistence-pinning`
**Commits:** `feat(canvas): position persistence & pinning`

## What changed

- **Position-mode + coordinate persistence (new).** `src/DocumentIDE.Core/Workspaces/CanvasPositionService.cs` —
  pure in-memory domain service keyed by `(WorkspaceId, DocumentId)` (FS-02/FS-05 stable identity, never
  filesystem paths): `UpsertPosition` / `MoveTo` (drag → `manual`) / `Pin` / `Unpin` (unpin → `manual`,
  coordinates retained) / `Get` / `GetWorkspace` (restart/reopen restore) / `ExportSnapshot` + `LoadSnapshot`
  (durable restart roundtrip) / `ResolveRestorePosition` (occupied → nearest-safe spiral fallback, 32px steps,
  8 rings, 600px cap, typed `CanvasPositionBlockedException` with `BlockingIds` instead of silent stacking) /
  `ApplyLocalReflow` (only `auto` objects move) / `RectsOverlap` helper. No filesystem/network/DOM access.
- **Frontend durable side (new).** `src/DocumentIDE.Web/src/features/canvas/usePersistedPosition.ts` —
  localStorage persistence per workspace (`documentide_canvas_positions:<workspaceId>`), `loadPositions`
  (corrupt storage degrades to `{}`, never throws), `savePositions`, `recordDrag` (→ `manual`), `pinPosition` /
  `unpinPosition` (unpin → `manual`), `resolveRestorePosition` (exact-or-nearest-safe, typed throw when blocked).
- **Tests (new).** Backend `tests/DocumentIDE.IntegrationTests/Workspace/CanvasPositionTests.cs` (8 tests);
  frontend `src/DocumentIDE.Web/src/features/canvas/usePersistedPosition.test.ts` (7 tests).

### Position-mode mapping (CAN-05 handoff note adopted)

CAN-05's handoff asked CAN-06 to adopt its layout-local `PlaceableObject.positionMode` (`auto`/`manual`/`pinned`,
default `auto`) as the persisted mode vocabulary. Adopted exactly:

| CAN-05 layout-local | CAN-06 persisted | Notes |
|---|---|---|
| `positionMode?: PositionMode` (`auto` default) | `WorkspaceDocument.PositionMode` string + `Pinned` bool | spec 02 carries **both** fields; `CanvasPositionService` keeps them in sync: `pinned` ⟺ `Pinned=true`; `Unpin` → `manual` (still reflow-protected, spatial memory retained) |
| `isImmovable` = pinned ∨ manual | `ApplyLocalReflow` moves only `auto` | same predicate, backend-enforced |
| spiral nudge (32px × 8 rings, 4 dirs) | `ResolveRestorePosition` fallback spiral | same discipline, 600px displacement cap |

Frontend imports `PositionMode` directly from `layout/AssistedPlacement` — one vocabulary, no duplicate union.

### Test-path mapping (batch-B ruling, same as CAN-04/CAN-05)

| Sprint planned path | Actual path (ruling) |
|---|---|
| `tests/DocumentIDE.IntegrationTests/Workspace/CanvasPositionTests.cs` | same path kept; **namespace is `DocumentIDE.IntegrationTests.Canvas`** (not `.Workspace`) |

`DocumentIDE.IntegrationTests.Workspace` as a namespace breaks compilation of the whole test project:
sibling suites reference the `Workspace` *type* (`DocumentIDE.Core.Domain.Workspace`), and C# enclosing-namespace
lookup resolves `Workspace` to the new namespace first (`CS0118: 'Workspace' is a namespace but is used like
a type` in `Batch/` and `Graph/` suites). Namespace-only deviation; file path matches the plan.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~CanvasPositionTests"` → PASS (8/8, 0 skipped)
- `dotnet test tests/DocumentIDE.Core.Tests --filter "FullyQualifiedName~Workspaces"` → PASS (11/11 CAN-04 regression guard, 0 skipped)
- `npx vitest run` in `src/DocumentIDE.Web` → PASS (30 files, 183 tests, 0 skipped)
- `npx vitest run src/features/canvas/usePersistedPosition.test.ts` → PASS (7/7)
- `npm run lint` → PASS (only pre-existing `react(only-export-components)` warnings in unrelated files; none in `usePersistedPosition.*`)
- `npm run build` (`tsc -b && vite build`) → PASS
- TDD: backend Red was missing-type compile failure on `CanvasPositionService`/`CanvasRect` (plus one
  self-caught test-arithmetic bug: auto-c at (400,0)+50 → asserted 450, fixed in test); frontend Red was the
  unresolved `./usePersistedPosition` import. Namespace-collision breakage above was caught by the first run
  and fixed before Green.

## Acceptance criteria

- [x] **Manual position survives restart.** Backend `ExportSnapshot` → fresh instance → `LoadSnapshot` roundtrip
  asserts (320, 240, `manual`); frontend `recordDrag` → fresh `loadPositions` from localStorage asserts the same.
- [x] **Rename preserves position.** Positions keyed by stable `DocumentId`; `WorkspaceDocument` has no path field
  (asserted by reflection), so rename/move changes nothing in the store. Coordinates independent from filesystem paths.
- [x] **Pinned node never moves during local reflow.** `ApplyLocalReflow` moves only `auto`; pinned/manual
  coordinates byte-identical, never appear in the returned moves.
- [x] **Canvas coordinates remain independent from filesystem paths.** No path surface on records or service API
  (sprint acceptance criterion, mapped to the reflection guard + keying design above).

## Deviations / spec conflicts

- None in behavior. Two integration notes:
  1. **Namespace deviation** (see mapping table): test namespace is `.Canvas`, file path per plan.
  2. **`Pin`/`Unpin` on an unknown document fail loud** (`InvalidOperationException` naming workspace+document)
      while `MoveTo`/`UpsertPosition` create the record — pin affordances only exist for on-canvas objects, so a
      missing record indicates a caller bug; drag implies presence.

## Risks discovered

- The durable backend side is currently the `ExportSnapshot`/`LoadSnapshot` contract (in-memory service +
  serializable snapshot matching the existing `workspace_documents` table shape: `canvas_x/y`, `position_mode`,
  `pinned`); no new SQLite repository/migration was added because no `WorkspaceDocument` repository exists yet
  and inventing one is CAN-07/wiring scope. Whoever wires SQLite persistence must roundtrip through these two
  methods (or the same column set) to keep the restart guarantee.
- `CanvasPositionBlockedException` (backend) / thrown `Error` (frontend) on fully-packed restore: callers
  (CAN-07 wiring) must handle the blocked branch instead of assuming restore always succeeds — same caveat
  CAN-05 flagged for placement.

## Security & File-Safety Gate

- [x] **No user file was mutated through an unreviewed/bypass path** — pure metadata; backend imports only
  `DocumentIDE.Core.Domain`, frontend imports only a type from `./layout/AssistedPlacement`. No `fs`, `path`,
  `fetch`, or DOM writes (one `localStorage` key namespace for positions only).
- [x] **No arbitrary path or secret is exposed** — records carry ids + coordinates + mode only; no path fields exist.
- [x] **Dependency/security boundaries remain intact** — no Pdf/Preview/Share/backend-search code touched;
  `git status` shows only the 4 planned-path files plus this handoff and the SPRINT-STATUS row.
- [x] **No destructive action silently broadened its scope** — there is no destructive action; worst case is a
  typed blocked exception/error with blocking ids.
- [x] **Relevant higher-level safety rules remain stronger than convenience** — "canvas drag never physically
  moves files" and "local filesystem is source of truth" untouched; `positionMode` persistence is additive.

## Next eligible sprint(s)

- `CAN-07` — Multi-Select, Group Drag & Tidy (unblocked by this sprint; consume `CanvasPositionService.MoveTo` /
  `Pin`/`Unpin` for group-drag mode transitions, `ResolveRestorePosition` for reopen, and the frontend
  `recordDrag`/`pinPosition`/`resolveRestorePosition` helpers; handle the blocked-restore branch; wire
  `ExportSnapshot`/`LoadSnapshot` to SQLite `workspace_documents` when adding the repository)
