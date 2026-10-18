# UI-01 — macOS Three-Pane Shell Redesign

**Status:** READY
**Priority:** P1
**Kind:** feature (UI redesign)
**Recommended branch:** `feat/ui-01-macos-shell-redesign`

## Goal

Replace the current flat top-nav app shell with the macOS three-pane shell
(unified toolbar + source-list sidebar + content + right inspector) defined by
the design of record, and restyle every mounted panel to match it, **without
changing any already-shipped product behavior** (API contracts, gesture
semantics, or the G1–G8 reachability guarantees already proven by
`App.shell.test.tsx`).

## Design of record

- `D:\document-ide\ui.pen` (git-ignored; open in the Pen app for any pencil
  MCP work) — the macOS screen series, frames `App — <Screen> (macOS)` /
  `Dialogs & overlays (macOS)`, row at `y = -160`, `x = 4670…21510`.
- `docs/design/UI-DESIGN-BRIEF-MACOS.md` — layout metrics, `mac-*` tokens,
  typography, macOS patterns (unified toolbar, source-list sidebar, right
  inspector, sheets, segmented control, search field, depth stepper, push
  buttons), the 12-screen inventory, and known gaps/caveats. Read this file in
  full before touching any component.
- `docs/design/screens-mac/*.png` — full-resolution exports (1440×900) per
  screen, named `01-canvas.png` … `12-dialogs.png`. Use these as the pixel
  reference; re-derive exact values from `.pen` only when a PNG is ambiguous
  (use the pencil MCP tools — `.pen` files are reachable only through
  `tools.pencil.*`, never `Read`/`Grep`).

This design is now the reference for how the shipped app should look.
Reproduce its layout metrics, tokens, and macOS patterns in code. Where the
design is silent on a behavior this sprint must not change (see "Non-goals"),
keep the current behavior and apply only the visual treatment.

## Depends On

— (none; this is a parallel UI track against the current `integration/parallel-batches` tip. Does not depend on or block any SCM-\* sprint.)

**Soft dependency:** the `.docx` faint page-1 preview consumes the read-only route delivered by
`docs/sprints/ui/UI-02-document-preview-content-api.md`. Until UI-02 lands, the DOCX sheet must
use the existing `LowDetailPaperLayout` fallback — **this sprint must not add a backend route**.
The `.md` preview needs no new route (`GET /api/documents/{documentId}/markdown` already exists).

**Hard rule:** Do not start downstream work on top of this branch until it is reviewed and merged.

## Required Reading

- `docs/design/UI-DESIGN-BRIEF-MACOS.md` (full)
- `docs/specs/00-master-product-technical-spec.md` (product behavior invariants)
- `docs/specs/03-workspace-canvas-spec.md`
- `AGENTS.md` and `docs/agent/` policies

Also inspect, before writing any code:

- `src/DocumentIDE.Web/src/App.tsx` — current shell composition.
- `src/DocumentIDE.Web/src/App.shell.test.tsx` and `src/DocumentIDE.Web/src/App.test.tsx` — the existing reachability/behavior gate tests this sprint must keep green.
- every component under `src/DocumentIDE.Web/src/features/**` that the shell currently mounts (toolbar buttons, `ViewSwitcher`, `SearchPanel`, `SharePanel`, `AddFolderSource`, `BatchPreviewPanel`, `PdfExportPanel`, `MarkdownEditor`, `DocumentCanvas`, `DocumentList`, `DocumentGraph`, `SourceControlPanel` and its sub-panels, `HistoryPanel`, `QuickLook`, dialogs under `features/pdf`, `features/files`, `features/context`).

## In Scope

1. **Shell chrome.** Rebuild `App.tsx`'s outer layout as the macOS three-pane
   shell: unified toolbar (traffic-light decoration, sidebar toggle,
   `Canvas | List | Graph` segmented control, trailing search field + icon
   buttons), source-list sidebar (`WORKSPACE` / `FOLDER SOURCES` / `LIBRARY`
   sections, 28px rows, accent-weak selection, trailing count badges),
   content region, right inspector dock. Apply the `mac-*` tokens, Inter
   typography, and JetBrains Mono for diff/code surfaces.
2. **Per-screen restyle**, matching the 12 reference screens:
   - 01 Canvas, 02 List, 03 Graph — surfaces + their inspectors (relations
     inspector, selection inspector, edge legend). **Canvas nodes must be
     folder/file *objects*, not look-alike cards**; a folder opens on click and fans out the documents it really contains, each sheet showing a faint real-content preview — see "Node objects" below.
   - 04 Batch operations — centred sheet (scrim, shadow, header, scroll body,
     footer secondary+primary) replacing the current panel chrome.
   - 05 Source control (+ 06 DOCX diff, 07 Worktrees) — sidebar change
     groups, unified diff rows, commit bar, worktree table + create sheet.
     (`SourceControlPanel`'s mount into the shell is covered by the separate
     SCM reachability lane — see "Coordination" below; this sprint only
     restyles the component tree, it does not decide whether/where it is
     mounted in `App.tsx`.)
   - 08 Search — toolbar search field + result list + preview pane + filter
     inspector (`exact/fuzzy/semantic` segmented control).
   - 09 Markdown editor — gutter/live-preview editor + outline sidebar +
     frontmatter/backlinks inspector.
   - 10 History & undo — operation list with outcome chips + operation
     detail + storage meter.
   - 11 Share manager — share list + detail (link/expiry/rate limit) +
     members table (no roles) + permissions inspector.
   - 12 Dialogs & overlays — Quick Look, build-context-pack, missing
     resource, undo-N-operations, merge order, PDF collision dialogs restyled
     to the sheet/scrim pattern.
3. **Reusable primitives**: segmented control, search field, depth stepper,
   push button (secondary/primary), sheet/scrim shell, source-list row,
   inspector title/key-value/card — extracted once and reused across panels
   rather than re-implemented per screen.
4. Update colocated tests for every restyled component to assert the new
   structure/roles where the old assertions were purely presentational
   (e.g. a class-name check), while preserving every assertion that checks
   product behavior (gestures, API calls, data shown).

## Node objects — folders and files must be distinguishable objects (owner directive 2026-10-18)

The owner rejected the earlier design where a folder and a file were drawn as the same rounded
box differing only by a type label. The canvas must show a **folder object** and a **file
object** (macOS Finder icon view). Exact geometry and colours are in
`docs/design/UI-DESIGN-BRIEF-MACOS.md` § "Canvas node objects"; the reference render is
`docs/design/screens-mac/01-canvas.png`.

Current code that must change:
- `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.tsx` (~line 115-179) renders a
  `type: 'folder'` object as a white rounded card with a `📁` emoji and a "Folder" caption.
- `src/DocumentIDE.Web/src/features/canvas/nodes/DocumentNode.tsx` renders a document as a
  card (`data-testid="document-paper-sheet"`) with a filename, a DOCX/MD badge and a
  paper-fold accent.

Required:
- folder → folder object (blue folder shape with tab); file → file object (sheet + type-coloured
  accent + folded corner); filename label beneath; **no card border**.
- type accent colour: `.docx` → system blue, `.md` → system green, unknown → neutral.
- graph nodes get a folder/file **icon** in place of the leading dot (keep the tree geometry).
- list rows / sidebar rows / SCM change rows / inspector reference rows get a small folder-or-file
  **icon** (Finder convention: objects on spatial surfaces, small icons in lists).

Non-negotiable: this is a *shape* change only. The existing accessible names, testids, gesture
wiring (`aria-label="Document: <title>"`, `data-testid="document-paper-sheet"`,
`aria-label="Folder: <title>"`, single-click folder → picker, double-click document → open) and
the no-physical-move rule must survive. Prefer keeping the existing testids/labels and swapping
the visual body, so the G-gate tests stay meaningful.

Required test: a canvas rendering test asserts a folder object and a file object are structurally
distinct (e.g. different testids / different type markers), so the two can never silently
regress into the same card.

### Folder interaction — open, fan out, release (owner directive 2026-10-18)

The folder object is **interactive**, not a static glyph. Owner reference:
`uselayouts.com/docs/components/folder-interaction` ("Folder Interaction — a folder
management system with drag-and-drop-like feel"). Reproduce the behaviour with our own code,
tokens and data — do not paste the reference implementation verbatim.

- **Closed** is the default state (the folder object as drawn).
- **Click the folder → open**: the folder front tilts back (`rotateX ≈ -40°`, origin bottom) and
  the documents **inside that folder** fan out as pages rotated `≈ -8° / 0° / +9°` and
  translated up (`y ≈ -55 … -75`). Click again closes. Spring: `duration 0.5-0.6`,
  `bounce 0.12-0.2`, `stiffness 160-190`, `damping 21-24`.
- **The fanned pages are real documents**: the ones that path-belong to that folder. Reuse the
  existing grouping (`groupDocumentsByFolder` / `isWithinFolder`, separator-bounded containment)
  — do not invent a second membership rule and do not use decorative placeholders. A folder with
  no contained documents opens to an empty state (no fabricated pages).
- **Drag a page out = the existing CAN-04 "release" semantic** (surface the referenced document as
  its own canvas object). This is metadata only: **canvas drag never physically moves a file**.
  Preserve the existing release wiring; do not add a new mutation path.
- **Animation library: `motion`** (owner decision, 2026-10-18). This is the first runtime UI
  dependency beyond `react`/`react-dom`. Add it to `src/DocumentIDE.Web/package.json` with
  `npm install motion --prefix src/DocumentIDE.Web` so `package-lock.json` is updated in the same
  commit; do not hand-edit the lockfile.
- Reduced motion: honour `prefers-reduced-motion` — when set, the folder still opens/closes but
  without the spring/fan tween (a plain state change). Not a behaviour change, a11y only.
- Keep the existing folder testids/accessible names (`aria-label="Folder: <title>"`) and keep the
  existing single-click→picker wiring; the open action must not silently replace it. Decide and
  document in the handoff which gesture performs which action (e.g. the object opens on click and
  the existing picker affordance remains reachable), and prove both are still reachable by test.

### Faint page-1 preview on a file object (owner directive 2026-10-18)

A file object's sheet shows a **faint preview of that document's real content** — the current
simulated grey lines are not acceptable as the end state.

- `.md` → render from the real source already served by `GET /api/documents/{documentId}/markdown`.
- `.docx` → render from `UI-02`'s read-only structure route when available; until then keep the
  `LowDetailPaperLayout` fallback (no backend change in this sprint).
- Rendering: faint (low opacity) and clipped to the sheet face so the filename label stays legible.
  It must read as content, not decoration. Pages fanned out of an open folder get the same
  treatment (each page previews its own document).
- **Honest limit to state in the handoff:** this is a *content-derived* preview, **not** a rendered
  page image. A true page-1 raster needs the Word → temp PDF → page-image pipeline that exists in
  no shipped host (the same missing pipeline that blocks `QuickLook`/WORD-04) and is a separate
  later sprint. The existing `thumbnailUrl` / `thumbnailContent` / `thumbnailState` props are the
  seam a future renderer will feed, so the node needs no redesign then.

Required tests (Red → Green → Refactor each):
- [ ] Clicking a folder object opens it (fanned pages present) and clicking again closes it; with
      `prefers-reduced-motion` the open/close still works.
- [ ] The fanned pages are exactly the documents that belong to that folder (fixture with one
      folder holding ≥2 documents plus one document outside it — the outsider must not appear).
- [ ] Releasing a fanned page out preserves the existing CAN-04 semantics (metadata only; assert no
      filesystem mutation path is invoked).
- [ ] A `.md` file object renders a faint preview derived from real source content; the fallback
      path is still used when content is unavailable (loading/failed/missing states).

### Dependency note

`motion` is added only to the web app; no backend/project file changes. If the repo's build embeds
production web assets, run the existing frontend build to confirm the dependency is bundled (the
sprint's own `npx vitest run` / `npx tsc -b` / `npx oxlint` gates plus the existing web-asset
embedding test cover this).

## Out of Scope (non-goals)

- Changing any API contract, route, or payload shape.
- Changing any gesture semantics already proven by a passing test (e.g. what
  a click vs. double-click does, what triggers a fetch, drag-never-moves-file,
  destructive-op review gating, Word COM concurrency=1).
- Mounting `SourceControlController`/the SCM pane into the shipped desktop
  host or `App.tsx` for the first time — that is the SCM-surface-reachability
  gap lane (tracked separately under the SCM program). This sprint restyles
  SCM components so they are ready to be mounted in that design, but does not
  perform the mount itself. If SCM reachability lands first, this sprint
  restyles whatever is mounted at merge time instead of mounting it.
- Implementing `QuickLook`/WORD-04 (still deliberately unimplemented; screen
  12's Quick Look dialog is restyled as a dialog shell only, not wired to a
  real page-render source).
- Any backend (`DocumentIDE.Api`/`Core`/`Infrastructure`) change. In particular the read-only
  `.docx` preview-content route is **UI-02's** deliverable
  (`docs/sprints/ui/UI-02-document-preview-content-api.md`) — do not add it here; use the existing
  low-detail fallback until it lands.
- Adding features/behaviors not present in either the current app or the
  12-screen design.
- Editing `docs/quality/v1-readiness-report.md`, `scripts/verify-release.ps1`,
  `docs/sprints/SPRINT-STATUS.md` rows other than this sprint's own, or any
  sprint manifest/roadmap entry other than this sprint's own addition.

## Coordination with the SCM program

`integration/parallel-batches` is advancing an independent SCM workstream
(W26–W31) that will, at W30, mount the Source Control surface into `App.tsx`
and the desktop host allow-list for the first time. This sprint runs on its
own branch off the current tip and must be merged by the controller with an
explicit reconciliation pass against whatever landed in `App.tsx` meanwhile —
not a blind `git merge` of two independent `App.tsx` rewrites. The controller
(not this lane) owns resolving that merge once both sides are reviewed.

## Required Tests

Every one of the following must stay green, unmodified in intent (assertions
may be updated for new DOM structure, not for new behavior):

- [ ] All existing `App.shell.test.tsx` cases (G1,G3,G4,G5,G6,G7,G8 reachability).
- [ ] All existing `App.test.tsx` cases.
- [ ] Every colocated component test under `features/**` touched by the
      restyle continues to pass (behaviorally) after the DOM-structure update.
- [ ] Every test required by the "Node objects", "Folder interaction" and
      "Faint page-1 preview" sections above.

New tests required by this sprint (Red → Green → Refactor per behavior):

- [ ] The toolbar renders the `Canvas | List | Graph` segmented control and
      switching selection still drives `useViewStore` exactly as before.
- [ ] The sidebar renders `WORKSPACE` / `FOLDER SOURCES` / `LIBRARY` sections
      from the same data the current panels already use (no new data source).
- [ ] The right inspector mounts the correct inspector content for the active
      view/selection (e.g. relations inspector on canvas selection).
- [ ] Each extracted primitive (segmented control, sheet shell, source-list
      row, inspector card) has its own colocated unit test.

At minimum, the first behavior must be demonstrated by a failing test before
implementation:

> All existing `App.shell.test.tsx` cases keep passing through the shell
> rewrite (write/adjust the harness first, confirm it fails against the new
> shell skeleton for the right reason, then build the skeleton to green).

## Failure / Edge Cases

- A panel with no corresponding reference screen yet (e.g. a dialog not in
  the 12-screen series) keeps its current layout, restyled only with `mac-*`
  tokens/typography — do not invent new layout for it.
- Narrow viewport / small window: sidebar and inspector must be collapsible
  (the design's "sidebar toggle" affordance) rather than overflowing.
- Empty states (no folder sources, no shares, no history) must render the
  sidebar/content/inspector sections without placeholder data.

## Security & File-Safety Gate

Before completion, explicitly confirm:

- [ ] no user file was mutated through an unreviewed/bypass path (this sprint
      touches no file-mutation code at all)
- [ ] no API contract was loosened or widened to make a visual change easier
- [ ] no existing privileged-route guard, review-before-apply gate, or
      destructive-action confirmation was removed or weakened for styling
      convenience
- [ ] relevant higher-level safety rules remain stronger than convenience

## Planned Files / Artifacts

- `src/DocumentIDE.Web/src/App.tsx` (shell rewrite)
- `src/DocumentIDE.Web/src/features/shell/*` (new: `Toolbar.tsx`, `Sidebar.tsx`, `Inspector.tsx`, and shared primitives: `SegmentedControl.tsx`, `SheetShell.tsx`, `SourceListRow.tsx`, `InspectorCard.tsx`, `SearchField.tsx`, `DepthStepper.tsx`, `PushButton.tsx` — each with a colocated test)
- `src/DocumentIDE.Web/src/features/canvas/nodes/FolderObject.tsx` (new: interactive folder object — open/close, fanned real documents, release-on-drag) + colocated test
- `src/DocumentIDE.Web/src/features/canvas/nodes/FilePreview.tsx` (new: faint content-derived page-1 preview + fallback) + colocated test
- `src/DocumentIDE.Web/src/index.css` — add the `mac-*` design tokens as a Tailwind v4 `@theme` block (see "Styling mechanism" below)
- `src/DocumentIDE.Web/package.json` + `src/DocumentIDE.Web/package-lock.json` — add `motion` (`npm install motion --prefix src/DocumentIDE.Web`)
- Restyled, not relocated: every component listed in "In Scope" item 2, plus their colocated tests
- `docs/sprints/handoffs/UI-01-HANDOFF.md`

If an existing file already provides an equivalent primitive, extend/rename
it with a documented mapping in the handoff rather than creating a duplicate.

## Styling mechanism (verified by the controller — do not re-invent)

- The app uses **Tailwind v4** via `@tailwindcss/vite`; the only global stylesheet
  is `src/DocumentIDE.Web/src/index.css`, whose entire content is
  `@import "tailwindcss";`. Declare the design tokens there in a
  `@theme { --color-mac-toolbar: #F7F7F8; … }` block so Tailwind generates
  `bg-mac-toolbar` / `text-mac-text` / `border-mac-hairline` utilities.
- `src/DocumentIDE.Web/src/App.css` is dead Vite-scaffold CSS (`.counter`,
  `.hero`, `#center`, …) referenced by nothing in the shipped shell. Do not
  build on it; do not delete it in this sprint unless it proves to be imported
  by a live file (verify with a grep first) — if it is dead, note it in the
  handoff for a separate cleanup.
- Match the brief's typography: **Inter** for UI, **JetBrains Mono** for diff
  gutter/code, keeping the product's existing small density (10–15px).

## Shell navigation structure (from the reference screens)

The sidebar's `LIBRARY` section is the app's feature navigation and replaces
the current flat top-nav buttons. Reference items and their meaning:

| Sidebar row | Meaning in the shipped app |
| --- | --- |
| `WORKSPACE` → Canvas view / List view / Graph view | the existing `Canvas \| List \| Graph` view switch (`useViewStore`) |
| `FOLDER SOURCES` → Documents / Projects / Archive (+ per-source rows) | the existing folder-source data/documents the shell already loads |
| `LIBRARY` → Source control (badge) | SCM surface (mounted by the SCM reachability lane; restyle only here) |
| `LIBRARY` → Worktrees | SCM-15/16 worktree UI |
| `LIBRARY` → Shares (badge) | the existing owner `SharePanel` surface |
| `LIBRARY` → History | the existing `HistoryPanel` surface |
| `LIBRARY` → Search | the existing `SearchPanel` surface |

**Gate constraint:** `App.shell.test.tsx` drives these surfaces by
`getByRole('button', { name: 'Search' })`, `name: 'Shares'`, `name: 'PDF'`,
and by `getByTestId('pdf-export-panel')` / `getByTestId('share-panel')`
beyond the earlier state. The redesign must keep every one of those accessible
names and test ids working (a sidebar row may render as a button with the same
accessible name; a toolbar icon button may too — but at least one element per
name must remain reachable). Do not "solve" a failing gate by renaming it away.

## Acceptance Criteria

- [ ] `App.tsx` renders the macOS three-pane shell matching the design brief's
      layout metrics and tokens.
- [ ] All 12 reference screens have a corresponding restyled surface in the
      app (or, for SCM screens, a restyled component tree ready to be mounted).
- [ ] Zero regressions: full existing frontend suite passes (`npx vitest run`),
      `npx tsc -b` exit 0, `npx oxlint` exit 0.
- [ ] `App.shell.test.tsx` passes unmodified in intent.
- [ ] Canvas folder and file objects are structurally distinct; the folder opens on click and fans
      out the **real** documents contained in it; releasing a fanned page preserves the existing
      CAN-04 semantics with no filesystem mutation.
- [ ] File objects (and fanned pages) show a faint preview derived from **real** content — `.md`
      from the existing markdown route, `.docx` via UI-02 when present, with the low-detail
      fallback intact otherwise.
- [ ] No backend file changed; no API contract changed.
- [ ] Handoff documents every DOM-structure test adjustment and why it is
      presentational, not behavioral.
