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
   - 01 Canvas, 02 List, 03 Graph — card/table/tree surfaces + their
     inspectors (relations inspector, selection inspector, edge legend).
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
- Any backend (`DocumentIDE.Api`/`Core`/`Infrastructure`) change.
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
- `src/DocumentIDE.Web/src/styles/mac-tokens.css` (or equivalent token source consistent with the project's existing styling approach — inspect current styling mechanism before choosing)
- Restyled, not relocated: every component listed in "In Scope" item 2, plus their colocated tests
- `docs/sprints/handoffs/UI-01-HANDOFF.md`

If an existing file already provides an equivalent primitive, extend/rename
it with a documented mapping in the handoff rather than creating a duplicate.

## Acceptance Criteria

- [ ] `App.tsx` renders the macOS three-pane shell matching the design brief's
      layout metrics and tokens.
- [ ] All 12 reference screens have a corresponding restyled surface in the
      app (or, for SCM screens, a restyled component tree ready to be mounted).
- [ ] Zero regressions: full existing frontend suite passes (`npx vitest run`),
      `npx tsc -b` exit 0, `npx oxlint` exit 0.
- [ ] `App.shell.test.tsx` passes unmodified in intent.
- [ ] No backend file changed; no API contract changed.
- [ ] Handoff documents every DOM-structure test adjustment and why it is
      presentational, not behavioral.
