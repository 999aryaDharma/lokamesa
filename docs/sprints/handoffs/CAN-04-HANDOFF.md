# Sprint Handoff — CAN-04

**Status:** DONE  
**Branch:** `feat/can-04-folder-picker-file-release`  
**Commits:** `feat(core): canvas document release service`; `feat(canvas): folder picker & file release`

## What changed

- **Folder picker UI (new).** `FolderPicker.tsx` — lightweight popover opened by a folder single click:
  lists supported folder contents (`.docx`, `.md`; unsupported files filtered out), case-insensitive
  search within the picker, checkbox multi-select, `Open selected`, `Open all`, close button + Escape.
  Purely presentational: all releases flow through `onOpenSelected` / `onOpenAll` callbacks.
- **Canvas wiring (minimal, additive).** `DocumentCanvas.tsx` — optional `folderFiles` /
  `onReleaseDocuments` props; folder single click is deferred by a 250 ms click/double-click arbitration
  window, then opens the picker (selects the folder, releases nothing). A second click or double click
  cancels the pending open so the popover can never swallow a real double-click's second click; the
  folder double click runs `Open all` and leaves selection unchanged (no double toggle). Picker closes
  after release, on Escape, on close, or when the folder object disappears. Documents keep the existing
  double-click Word behavior.
- **Core release service (new).** `CanvasDocumentService.cs` — pure service that creates/activates
  `WorkspaceDocument` objects from a folder listing; `ReleaseAll` (all supported files) and
  `ReleaseSelected` (only the selected DocumentIds, listing order). Deterministic typed rejections
  (`unsupported-file-type`, `missing-document-id`, `duplicate-document-id`, `not-in-folder-listing`).
  No filesystem surface at all: no move/rename/delete/write/copy, no injected collaborators,
  candidates carry identity + file name only (no physical paths).
- **Tests.** 12 frontend tests (`FolderPicker.test.tsx`, including all three required behaviors and the
  realistic click/click/dblclick arbitration sequence) and 10 Core tests
  (`CanvasDocumentServiceTests.cs`, including explicit no-filesystem / no-move guards).

## Files changed

- `src/DocumentIDE.Web/src/features/canvas/FolderPicker.tsx` (new)
- `src/DocumentIDE.Web/src/features/canvas/FolderPicker.test.tsx` (new)
- `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.tsx` (shared file — additive edit, flagged below)
- `src/DocumentIDE.Core/Workspaces/CanvasDocumentService.cs` (new)
- `tests/DocumentIDE.Core.Tests/Workspaces/CanvasDocumentServiceTests.cs` (new)
- `docs/sprints/handoffs/CAN-04-HANDOFF.md` (new)

### Test-path mapping (batch-B ruling)

| Sprint planned path | Actual path (ruling) |
|---|---|
| `tests/frontend/canvas/FolderPicker.test.tsx` | `src/DocumentIDE.Web/src/features/canvas/FolderPicker.test.tsx` |

`tests/frontend/**` is a legacy directory not wired into the vitest runner (vitest root is
`src/DocumentIDE.Web`); the active convention places the test next to the component.

## Verification

- `npx vitest run src/features/canvas/FolderPicker.test.tsx` → PASS (12/12)
- `npx vitest run` in `src/DocumentIDE.Web` → PASS (21 files, 108 tests)
- `npm run build` in `src/DocumentIDE.Web` (`tsc -b && vite build`) → PASS
- `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj --filter FullyQualifiedName~CanvasDocumentService` → PASS (10/10)
- `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj` → PASS (450/450, includes the 10 new)
- `scripts/verify.ps1` - PASS on the sprint tree: build + 7 Architecture + 12 Safety + 450 Core + 266 Integration + Web build/tests, 0 skipped (`.superpowers/sdd/CAN-04-folder-picker-file-release/verify-can-04.log`).

## Acceptance criteria

- [x] **No physical move occurs.**
  - Core: `CanvasDocumentService` only constructs `WorkspaceDocument` metadata objects.
    Explicit guard tests: IL scan of every type in `DocumentIDE.Core.Workspaces` proves zero
    `System.IO.*` member calls (positive-control test proves the scanner detects `System.IO.Path.GetExtension`);
    reflection guard proves the public surface has no move/rename/delete/copy/write verb, the
    constructor takes no dependencies, the class has no instance state, and `WorkspaceDocument`
    has no path property. Behavioral test releases files that do not exist on disk (zero I/O).
  - Web: release flows through presentational callbacks (`onReleaseDocuments`); the picker/canvas
    never writes files or store-level file state. Single click provably releases nothing
    (required test "Single click does not dump all files").

## Deviations / spec conflicts

- None in behavior. Two integration notes:
  1. **CAN-02 integration:** the worktree base (`e34fb32`) does not contain CAN-02's `FolderNode.tsx`
     (branch `feat/can-02-folder-canvas-object`, `eb7b952`, is not an ancestor of this worktree base).
     Folder click wiring therefore targets the base inline folder card in `DocumentCanvas.tsx`.
     CAN-02's id-based `onSingleClick(id)` maps 1:1 to our `handleFolderSingleClick(id)` — but its
     `onDoubleClick(id)` is id-based while our card double-click path consumes a `CanvasObject`
     (`onDoubleClick?: (object: CanvasObject) => void`; unchanged by the arbitration fix). CAN-02
     reconciliation therefore needs an id→object adapter (look up the folder in `objects`) or an agreed
     signature change.
  2. **Supported types:** `.docx` and `.md`, mirroring FS-01 `FolderSourceService` and the master spec
     ("supported `.docx` and `.md` content"). `DocumentNode`'s looser `.doc` / `.markdown` inference was
     intentionally not adopted for folder listing.
- `SPRINT-STATUS.md` deliberately not edited (batch-B controller ruling; controller updates the board).

## Risks discovered

- Duplicate `DocumentId` candidates are rejected (first occurrence wins) rather than double-released;
  downstream placement (CAN-05) should consume only `Released`.
- The picker popover is anchored at a fixed canvas position (`top-16 left-6`); anchored-to-folder
  positioning is not required by this sprint and can be refined later.

## Security & File-Safety Gate

- [x] **No user file was mutated through an unreviewed/bypass path** — no filesystem API is reachable:
  IL-scan guard + no `System.IO` calls at all in `DocumentIDE.Core.Workspaces`; the web component is
  callback-only. Canvas release ≠ physical operation.
- [x] **No arbitrary path or secret is exposed** — candidates carry `DocumentId` + `Filename` only;
  `WorkspaceDocument` has no path field; the picker renders file names, never paths.
- [x] **Dependency/security boundaries remain intact** — Core service has no dependencies
  (parameterless constructor, zero instance state); no API surface/endpoint added.
- [x] **No destructive action silently broadened its scope** — unsupported, missing-id, duplicate, and
  unknown-selected ids all fail closed with typed rejections; single click releases nothing;
  `Open selected` releases exactly the checked DocumentIds.
- [x] **Relevant higher-level safety rules remain stronger than convenience** — "Canvas drag/release
  never physically moves files" enforced by contract + guards; local filesystem remains source of truth.

## Next eligible sprint(s)

- `CAN-05` — Spatially Indexed Assisted Placement (dependency CAN-04; awaits controller board update)
