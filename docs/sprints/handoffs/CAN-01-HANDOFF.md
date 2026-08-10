# Sprint Handoff — CAN-01

**Status:** REVIEW  
**Branch:** `feat/can-01-canvas-foundation`  
**Commit:** `feat(canvas): canvas foundation` (pending below)  
**Implementation Agent:** Codex  
**Reviewer Agent:** Antigravity

---

## What Changed

### New Files

| File | Purpose |
|---|---|
| `src/DocumentIDE.Web/src/features/canvas/types.ts` | Type definitions: `Viewport`, `CanvasObject`, `CanvasState`, storage key constant `documentide_canvas_viewport`, zoom bounds `ZOOM_MIN`/`ZOOM_MAX` |
| `src/DocumentIDE.Web/src/features/canvas/canvasStore.ts` | Lightweight external store (no third-party deps) compatible with `useSyncExternalStore`. Caches snapshot for referential stability. Persists viewport to `localStorage`. |
| `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.tsx` | Main canvas component: white/off-white surface + dot-grid background, CSS transform pan/zoom, memoized object cards, floating zoom toolbar |
| `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.test.tsx` | Vitest TDD test suite (7 tests) |

### Modified Files

| File | Change |
|---|---|
| `src/DocumentIDE.Web/src/App.tsx` | Replaced placeholder heading with `DocumentCanvas` as full-screen app surface |
| `src/DocumentIDE.Web/src/App.test.tsx` | Updated to assert canvas surface renders instead of old heading |
| `docs/sprints/SPRINT-STATUS.md` | CAN-01 → REVIEW |
| `docs/sprints/handoffs/CAN-01-HANDOFF.md` | This file |

---

## TDD Evidence

Tests were written **before** implementation (Red phase), then implementation was added to make them pass (Green phase). No tests were weakened or skipped.

**First failing test (Red):** `Viewport_Persists_In_Store_Or_LocalStorage` — failed because `canvasStore.ts` did not exist yet.

**Refactor note:** After initial Green run, `useSyncExternalStore` caused an infinite loop because `getState()` returned a new object reference on every call. Fixed by caching the snapshot in the store (`cachedSnapshot`) and only rebuilding it inside `notify()`. This is a well-known React 18 requirement.

---

## Tests Run & Results

### Frontend (`npm test` in `src/DocumentIDE.Web`)

```
✓ src/App.test.tsx (1 test)
✓ src/features/canvas/DocumentCanvas.test.tsx (7 tests)
  ✓ Viewport_Persists_In_Store_Or_LocalStorage > saves viewport x, y, zoom to localStorage when viewport changes
  ✓ Viewport_Persists_In_Store_Or_LocalStorage > loads persisted viewport from localStorage on store initialization
  ✓ Viewport_Persists_In_Store_Or_LocalStorage > clamps restored zoom to [0.1, 3.0] bounds
  ✓ Renders_100_Objects_And_Pans_Smoothly > renders 100 canvas objects without throwing or producing error elements
  ✓ Renders_100_Objects_And_Pans_Smoothly > pan changes viewport state without errors
  ✓ No_Dashboard_Cards_In_Default_Canvas > renders no statistic/metric cards in default empty canvas state
  ✓ No_Dashboard_Cards_In_Default_Canvas > default canvas has clean white/off-white background, not a colored dashboard

Test Files: 2 passed (2) | Tests: 8 passed (8)
```

### Backend (`dotnet test`)

```
Passed! - Failed: 0, Passed: 5 - DocumentIDE.Core.Tests.dll (net10.0)
Passed! - Failed: 0, Passed: 17 - DocumentIDE.IntegrationTests.dll (net10.0)
Total: 22 passed, 0 failed
```

### Full Repository Verification (`scripts/verify.ps1`)

```
[PASS] dotnet build DocumentIDE.sln
[PASS] dotnet test
[PASS] npm ci & npm run build
[PASS] npm test
========================================
  ALL VERIFICATION STEPS PASSED SUCCESSFULLY!
========================================
```

---

## Acceptance Criteria Mapping

| Criterion | Evidence |
|---|---|
| Canvas state is workspace metadata only | `CanvasObject` carries only id, position, size, title, type. No file paths. No physical move on drag. Non-negotiable rule verified in types. |
| Clean white spatial visual, no dashboard | `DocumentCanvas.tsx` uses `#F8FAFC` background + dot-grid SVG CSS pattern. No stats cards, no metric banners. Test `No_Dashboard_Cards_In_Default_Canvas` asserts absence of dashboard keywords. |
| 100 objects rendering benchmark | Test `Renders_100_Objects_And_Pans_Smoothly` creates and renders 100 `CanvasObject` entries and asserts spot-checked titles. Objects use `memo()` + `will-change: transform` for GPU compositing. |
| Viewport persists across sessions | Viewport auto-saved to `localStorage` key `documentide_canvas_viewport` on every `setViewport()` call. Loaded on mount via `loadPersistedViewport()`. Test verifies both save and load paths. |

---

## Security & File-Safety Gate

- [x] **No user file mutated through unreviewed/bypass path** — this sprint is pure UI/state. No file I/O, no backend calls.
- [x] **No arbitrary path or secret exposed** — canvas objects store only display metadata (id, position, title, type). No local filesystem paths in any canvas type.
- [x] **Dependency/security boundaries remain intact** — no changes to API, auth, or desktop security layer.
- [x] **No destructive action silently broadened its scope** — N/A, no destructive operations in scope.
- [x] **Higher-level safety rules remain stronger than convenience** — `AGENTS.md` non-negotiable "Canvas drag never physically moves files" is architecturally enforced: `CanvasObject` has no file path field and no mutation API.

---

## Deviations from Sprint Spec

| Item | Detail |
|---|---|
| Store library | Sprint said "Zustand or React Context". Used a custom lightweight pub/sub store (no third-party dependency). Functionally equivalent, zero added dependencies. |
| Test file location | Sprint spec listed `tests/frontend/canvas/DocumentCanvas.test.tsx`. Test placed co-located at `src/features/canvas/DocumentCanvas.test.tsx` (Vitest convention, consistent with existing `App.test.tsx`). |
| `useSyncExternalStore` snapshot caching | Required an extra refactor step after initial Green to prevent React 18 infinite loop. Documented in TDD Evidence. |

---

## Risks Discovered

- **`SQLitePCLRaw.lib.e_sqlite3` vulnerability** (NU1903, high severity): Pre-existing since FND-03. Not introduced by this sprint. Tracking: [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). Should be addressed in a dedicated dependency-update sprint before REL-01.
- **100-object benchmark is a render test, not a paint-frame benchmark**: The `Renders_100_Objects_And_Pans_Smoothly` test verifies React renders 100 objects without error. It does not measure actual frame time. A formal 60 FPS paint benchmark requires browser tooling (CAN-05/QA-06 scope).

---

## Next Dependency-Ready Sprints

After CAN-01 is approved and marked DONE:

| Sprint | Now Unblocked |
|---|---|
| CAN-02 | Folder Canvas Object (requires FS-01, CAN-01) — still needs FS-01 |
| CAN-03 | Document Paper Object & Thumbnail Shell (requires IDX-04, CAN-01) — still needs IDX-04 |
| GR-03 | Graph View (requires GR-02, CAN-01) — still needs GR-02 |
