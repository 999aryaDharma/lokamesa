# WB-03 — Wildboard Canvas Surface

**Label:** `future-improvement`
**Roadmap version:** `v1.7.0`
**Status:** SPECIFIED
**Priority:** `P0`
**Kind:** feature
**Recommended branch:** `feat/wb-03-wildboard-canvas-surface`

## Goal

Render a wildboard workspace as an interactive canvas of `text`, `sheet`, and `stack` objects — reusing the existing selection/drag/zoom engine — with inline text editing, a Markdown editor for sheets, stack collapse/expand, and a workspace picker that can create/open wildboards.

## Depends On

- `WB-02` — Space-Object Domain, Vault & Atomic Writes
- `CAN-01` — Canvas Foundation — **DONE**
- `CAN-06` — Position Persistence & Pinning — **DONE**
- `CAN-07` — Multi-Select, Group Drag & Tidy — **DONE**
- `MD-01` — Source-First Markdown Editor — **DONE**

Hard rule: do not start unless `WB-02` is `DONE` and the V1 dependencies are `DONE`.

## Required Reading

- `AGENTS.md`
- `docs/specs/03-workspace-canvas-spec.md`
- `docs/specs/08-markdown-graph-spec.md`
- `docs/future-improvement/specs/1.7.0-wildboard-core.md` §7–§8
- `docs/future-improvement/specs/00-shared-future-contracts.md` §5–§7
- `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.tsx`
- `src/DocumentIDE.Web/src/features/canvas/selection/SelectionController.ts`
- `src/DocumentIDE.Web/src/features/canvas/canvasStore.ts`
- `src/DocumentIDE.Web/src/features/canvas/layout/AssistedPlacement.ts`

## In Scope

- Pure viewport math extracted to a shared module and adopted by `DocumentCanvas`.
- `wildboardStore` with the same cached-snapshot contract.
- `wildboardApi` client.
- `WildboardCanvas` with pan/zoom/select/group-drag/persist.
- Node components: text (inline edit), sheet (Markdown editor), stack (collapse/expand).
- Stack creation from a multi-selection.
- Delete with confirmation.
- `MarkdownEditor` content-injection props.
- `WorkspacePicker` (list, create wildboard, persist selection) and `App` integration.

## Out of Scope

- Quick Capture panel and global hotkey (`WB-04`).
- Recovery, security, perf hardening (`WB-05`).
- Rich block editor (`1.8.0`).
- Media, links, labels (`1.8.0`/`1.9.0`).
- Cross-workspace drag-and-drop.

## Planned Files / Artifacts

**Create**

- `src/DocumentIDE.Web/src/features/canvas/viewport.ts`
- `src/DocumentIDE.Web/src/features/canvas/viewport.test.ts`
- `src/DocumentIDE.Web/src/features/wildboard/wildboardStore.ts`
- `src/DocumentIDE.Web/src/features/wildboard/wildboardStore.test.ts`
- `src/DocumentIDE.Web/src/features/wildboard/wildboardApi.ts`
- `src/DocumentIDE.Web/src/features/wildboard/wildboardApi.test.ts`
- `src/DocumentIDE.Web/src/features/wildboard/WildboardCanvas.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/WildboardCanvas.test.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/WildboardCanvas.perf.test.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/nodes/WildboardTextNode.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/nodes/WildboardSheetNode.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/nodes/StackNode.tsx`
- `src/DocumentIDE.Web/src/features/workspace/WorkspacePicker.tsx`
- `src/DocumentIDE.Web/src/features/workspace/WorkspacePicker.test.tsx`
- `src/DocumentIDE.Web/src/App.wildboard.test.tsx`

**Modify**

- `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.tsx` — use `viewport.ts` (no behavior change).
- `src/DocumentIDE.Web/src/features/markdown/MarkdownEditor.tsx` — add `contentLoader` / `contentSaver`.
- `src/DocumentIDE.Web/src/features/workspace/workspaceApi.ts` — add `fetchWorkspaces`.
- `src/DocumentIDE.Web/src/App.tsx` — kind-aware rendering and workspace picker mount.

## Interfaces & Contracts

**Consumes**

- `GET/POST api/workspaces`, `GET api/workspaces/{id}` (`WB-01`).
- `api/workspaces/{workspaceId}/wildboard/objects…` (`WB-02`).
- `applyClick`, `groupDrag` from `SelectionController`.
- `ZOOM_MIN`, `ZOOM_MAX` from `features/canvas/types`.

**Produces**

```ts
// features/canvas/viewport.ts
export interface ViewportState { x: number; y: number; zoom: number }
export function clampZoom(zoom: number): number;
export function zoomAtPoint(viewport: ViewportState, point: { x: number; y: number }, nextZoom: number): ViewportState;
```

```ts
// features/wildboard/wildboardStore.ts
export interface WildboardObjectView {
  id: string; kind: 'text' | 'sheet' | 'stack';
  x: number; y: number; width: number; height: number;
  title?: string; availabilityStatus?: 'available' | 'missing';
}
export interface WildboardState {
  viewport: ViewportState; objects: WildboardObjectView[]; selectedIds: Set<string>;
}
export const wildboardStore: {
  getState(): WildboardState & Actions;
  subscribe(listener: () => void): () => void;
};
// Actions: setViewport, setObjects, upsertObject, removeObject, toggleSelect, setSelection, reset
```

```ts
// features/wildboard/wildboardApi.ts
export interface WildboardObjectDto {
  id: string; kind: string; canvasX: number; canvasY: number;
  width: number; height: number; title?: string | null;
  availabilityStatus: 'available' | 'missing'; vaultRelativePath?: string | null;
}
export function fetchWildboardObjects(workspaceId: string, fetchImpl?: typeof fetch): Promise<WildboardObjectDto[]>;
export function createWildboardObject(workspaceId: string, body: {
  kind: 'text' | 'sheet' | 'stack'; body?: string; title?: string;
  x: number; y: number; width?: number; height?: number;
}, fetchImpl?: typeof fetch): Promise<WildboardObjectDto>;
export function fetchWildboardContent(workspaceId: string, objectId: string, fetchImpl?: typeof fetch): Promise<string>;
export function saveWildboardContent(workspaceId: string, objectId: string, body: string, fetchImpl?: typeof fetch): Promise<void>;
export function patchWildboardPlacement(workspaceId: string, objectId: string, p: {
  x: number; y: number; width: number; height: number;
}, fetchImpl?: typeof fetch): Promise<void>;
export function deleteWildboardObject(workspaceId: string, objectId: string, confirm: boolean, fetchImpl?: typeof fetch): Promise<void>;
export function addToStack(workspaceId: string, stackId: string, objectId: string, fetchImpl?: typeof fetch): Promise<void>;
```

```ts
// features/markdown/MarkdownEditor.tsx (additive props)
contentLoader?: () => Promise<string>;
contentSaver?: (source: string) => Promise<void>;
```

`contentLoader`/`contentSaver` take precedence over `documentId` loading when supplied; existing behaviour is unchanged when they are absent.

## Implementation Steps

### Step group A — shared viewport math (refactor, no behavior change)

- [ ] **A1.** Write `viewport.test.ts`: `ClampZoom_Bounds_To_0_1_And_3`, `ZoomAtPoint_Keeps_Anchor_Fixed`, `ZoomAtPoint_Clamps_Zoom_And_Keeps_Anchor`.
- [ ] **A2.** Run `npx vitest run src/features/canvas/viewport.test.ts`. Expected: FAIL (module missing).
- [ ] **A3.** Implement `viewport.ts`; move the arithmetic currently in `DocumentCanvas.onWheel` (`DocumentCanvas.tsx:355-374`) into `zoomAtPoint`.
- [ ] **A4.** Refactor `DocumentCanvas.onWheel` to call `zoomAtPoint`. Run the full frontend suite. Expected: PASS with no change to existing canvas tests.
- [ ] **A5.** Commit: `refactor(canvas): extract viewport zoom math`.

### Step group B — store and API client

- [ ] **B1.** Write `wildboardStore.test.ts`: single-object update replaces only that object, `getState()` returns the same reference between updates, `setSelection` creates a new `Set`.
- [ ] **B2.** Run → FAIL → implement `wildboardStore.ts` using the `canvasStore.ts` cached-snapshot pattern (`cachedSnapshot`, `buildSnapshot`, `notify`) → PASS.
- [ ] **B3.** Write `wildboardApi.test.ts`: each function hits the documented route, sends `X-DocumentIDE-Session-Token`, throws on non-2xx, and `deleteWildboardObject` appends `?confirm=true`.
- [ ] **B4.** Run → FAIL → implement `wildboardApi.ts` following `workspaceApi.ts`'s `apiBase()`/`getJson` pattern → PASS.
- [ ] **B5.** Commit: `feat(wildboard): add wildboard store and API client`.

### Step group C — Markdown editor injection

- [ ] **C1.** Extend the existing Markdown editor tests (locate with `Get-ChildItem src/DocumentIDE.Web/src/features/markdown`) with `Loads_From_ContentLoader_When_Provided` and `Saves_Through_ContentSaver_When_Provided`.
- [ ] **C2.** Run → FAIL → add the two props with precedence rules → PASS. Confirm existing editor tests are unchanged.
- [ ] **C3.** Commit: `feat(markdown): allow injected content loader and saver`.

### Step group D — canvas nodes

- [ ] **D1.** Implement `WildboardTextNode` (memoized): renders title or a muted placeholder, `role="button"`, `tabIndex={0}`, `aria-label="Sticky note: <title>"`, `aria-selected`. Double-click or `Enter` enters inline editing in a `<textarea>`; `Escape` cancels; `Ctrl+Enter` or blur saves.
- [ ] **D2.** Implement `WildboardSheetNode`: card with title and excerpt; double-click opens `SheetShell` containing `MarkdownEditor` wired to `contentLoader`/`contentSaver`.
- [ ] **D3.** Implement `StackNode`: collapsed shows a pile with a count badge; `Enter`/click toggles expanded; expanded renders member cards at their stored coordinates translated by the stack offset; dragging a member out removes it from the stack via `DELETE …/stack`.
- [ ] **D4.** Commit: `feat(wildboard): add text, sheet and stack nodes`.

### Step group E — canvas surface

- [ ] **E1.** Write `WildboardCanvas.test.tsx`:
  - renders one node per object from the store;
  - `Empty_Canvas_Shows_Capture_And_Create_Sheet_Actions`;
  - `DoubleClick_Text_Saves_Content` (asserts `saveWildboardContent`);
  - `Drag_Persists_Placement_On_MouseUp` (asserts `patchWildboardPlacement`, one call per moved object);
  - `Group_Drag_Preserves_Relative_Positions`;
  - `CtrlG_Creates_Stack_From_Selection` (asserts `createWildboardObject({kind:'stack'})` then `addToStack` per member);
  - `Delete_Asks_For_Confirmation_And_Deletes`;
  - `Delete_Cancel_Does_Nothing`;
  - `DoubleClick_Never_Calls_OpenInWord`.
- [ ] **E2.** Run → FAIL → implement `WildboardCanvas` (pan on middle/space/background, wheel zoom via `zoomAtPoint`, selection via `applyClick`, drag via `groupDrag`, window-level `mousemove`/`mouseup`) → PASS.
- [ ] **E3.** Write `WildboardCanvas.perf.test.tsx`: render 1,000 objects without throwing; a single `upsertObject` re-renders exactly one node (mock the node module and count calls). Run → PASS.
- [ ] **E4.** Commit: `feat(wildboard): add wildboard canvas surface`.

### Step group F — workspace picker and shell integration

- [ ] **F1.** Write `WorkspacePicker.test.tsx`: lists workspaces, "New wildboard" calls `createWorkspace(name,'wildboard')` and selects the result, selection persists to `localStorage` key `documentide_active_workspace`.
- [ ] **F2.** Run → FAIL → implement `WorkspacePicker` and `fetchWorkspaces`.
- [ ] **F3.** Write `App.wildboard.test.tsx`: with a `wildboard` capability payload the app mounts `WildboardCanvas`, does not mount `DocumentCanvas` or `ViewSwitcher`, and still hides Add-folder/Batch/PDF/Share.
- [ ] **F4.** Wire `App.tsx`: resolve workspace → `useWorkspaceCapabilities` → kind-aware render. Extend `resolveWorkspaceId` to prefer the persisted selection.
- [ ] **F5.** Run `npx vitest run src/App.test.tsx src/App.wildboard.test.tsx`. Expected: PASS, existing `App.test.tsx` unchanged.
- [ ] **F6.** Commit: `feat(web): add workspace picker and wildboard shell`.

## Required Tests

- [ ] `viewport.test.ts` — clamp and anchor.
- [ ] `wildboardStore.test.ts` — reference stability and single-object update.
- [ ] `wildboardApi.test.ts` — routes, token, error handling.
- [ ] Markdown editor content-injection tests.
- [ ] `WildboardCanvas.test.tsx` — nine cases in E1.
- [ ] `WildboardCanvas.perf.test.tsx` — 1,000 objects, single-node re-render.
- [ ] `WorkspacePicker.test.tsx` and `App.wildboard.test.tsx`.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `WildboardCanvas.test.tsx > Drag_Persists_Placement_On_MouseUp`

## Failure / Edge Cases

- API failure while saving content → node keeps the user's text and shows an inline error; the store is not updated to a false "saved" state.
- Object marked `missing` → renders a muted "missing" badge; opening the editor offers recreate/remove (full recovery UI is `WB-05`).
- Deleting an object that is a stack member → stack count updates; no orphan membership (FK cascade).
- Dropping a member outside the stack bounds → membership removed and placement persisted.
- Zoom at min/max → pan unchanged, zoom clamped.
- Empty title → node shows the placeholder, not an empty button with no accessible name.
- Keyboard-only user → every node reachable by `Tab`, every action has an `Enter`/`Space` path.
- Window blur during a drag → drag state clears on `mouseup`/`blur`; no dangling listener.

## Security & File-Safety Gate

- [ ] No document mutation path is imported by wildboard modules.
- [ ] No `openInWord`, batch, or share action is reachable from a wildboard surface.
- [ ] No filesystem path from the vault is rendered or exposed in the DOM beyond an opaque object id.
- [ ] Delete always requires explicit confirmation.
- [ ] Canvas interactions only write workspace metadata (placement), never files.

## Acceptance Criteria

- [ ] A wildboard workspace renders its objects on an interactive canvas.
- [ ] Pan, wheel zoom (anchor-preserving), click/additive/rect selection, and group drag behave like the document canvas.
- [ ] Dragging persists placement per object.
- [ ] Text edits persist to the vault file through the API.
- [ ] Sheet opens the Markdown editor and saves through `contentSaver`.
- [ ] A stack collapses/expands and never moves or deletes member objects.
- [ ] Delete requires confirmation and supports cancel.
- [ ] A user can create and open a wildboard from the workspace picker.
- [ ] Document canvas behaviour is unchanged.

## Definition of Done

- [ ] implementation steps complete
- [ ] required tests pass
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` passes
- [ ] no required test skipped
- [ ] no unresolved placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] handoff written to `docs/future-improvement/handoffs/WB-03-HANDOFF.md`
- [ ] coherent implementation commit

## Commit Guidance

```text
feat(wildboard): wildboard canvas surface
```

## Ready-to-Run Agent Prompt

```text
Implement WB-03 — Wildboard Canvas Surface only.

Read AGENTS.md, docs/specs/03 and 08, docs/future-improvement/specs/1.7.0-wildboard-core.md,
docs/future-improvement/specs/00-shared-future-contracts.md, and ADR-003 first.
This sprint is part of roadmap v1.7.0; do not implement WB-04 or later.

Use strict TDD. Reuse the existing canvas interaction engine; do not fork it.
The Step A refactor must leave existing DocumentCanvas tests green.
Canvas drag persists placement metadata only — it never moves files.
Do not mount Word, batch, PDF, share, or source-control surfaces for a wildboard.

At completion:
1. run the required tests and scripts/verify.ps1,
2. self-review git diff,
3. map acceptance criteria to evidence,
4. write docs/future-improvement/handoffs/WB-03-HANDOFF.md,
5. report changed files, exact commands/results, deviations/risks,
6. commit using the commit guidance.
```
