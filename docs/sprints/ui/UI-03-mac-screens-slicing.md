# UI-03 — macOS Screen Slicing (remaining screens, adapted to real features)

**Status:** READY
**Priority:** P1
**Kind:** ui
**Branch:** `feat/ui-03-mac-screens-slicing`

## Goal

Turn the remaining macOS design screens into shipped, reachable UI, composed from the components that
already exist and wired only to features this product actually has. `ui.pen` (and its exports in
`docs/design/screens-mac/`) is the design of record; this sprint is the slicing pass, not a new design.

## Context — what UI-01 already shipped

UI-01 (`0d6893b`) delivered the three-pane shell, `mac-*` tokens, the `features/shell/*` primitives,
folder/file objects and the restyle of the screens below. **Reachability is the gap**, not styling:
`App.tsx` imports no `features/source-control/*`, and `Sidebar.tsx` documents the SCM surfaces as
"inert labels".

| Screen | Component exists | Reachable today |
| --- | --- | --- |
| 01 canvas, 02 list, 03 graph, 08 search, 09 markdown, 11 share | yes | **yes** |
| 04 batch operations | yes | mounted, but a right-side sheet (design: centred sheet) |
| 05 source control, 06 DOCX diff, 07 worktrees | yes | **no** (no App.tsx mount; controller not in the desktop allow-list) |
| 10 history & undo | yes | **no** (no host) |
| 12 dialogs | yes | **partial** (merge order + PDF progress via `PdfActions`; QuickLook / context pack / undo-conflict unreachable) |

## Design → feature mapping (the "adapt to our features" part)

**Screen 05 — Source control** → `SourceControlScreen` composed of `SourceControlPanel`, `MarkdownGitDiff`,
`DocxDiffPanel`, `CommitBox`, `RemoteStatus`, `PushButton`, `ChangeActions`, `GitHistoryPanel`,
`ConflictPanel` (from SCM-14), `DiscardRestoreDialog`, `PullDialog`.

- Sidebar groups `Staged / Changes / Untracked / Conflicts` come from the real `GET status` snapshot.
- Header counters (`main · n ahead · m behind`) come from `GET remote`.
- **Adaptation:** a `.docx` row must **not** render a text diff. Selecting it shows the document-aware
  summary and routes to screen 06. Our DOCX diff is structural; the design's unified text diff for a
  `.docx` is not implementable honestly.
- **Adaptation:** `Commit staged` = `POST commit` with the staged paths; `Commit all` = `POST commit-all`
  behind an explicit confirmation (the design's own footer says so).
- Branch is **read-only** — no branch switching/creation anywhere in this screen.
- Inspector: Repository card + "Open worktrees" (routes to screen 07).

**Screen 06 — DOCX document-aware diff** → `DocxDiffScreen` around the existing `DocxDiffPanel`, with an
inspector whose primary action is the existing `openInWord`. The design's chips map to our change
classes; no rendered page image is claimed (WORD-04 remains unimplemented).

**Screen 07 — Worktrees** → `WorktreesScreen` around `WorktreeList` + `WorktreeCreateDialog`.
**This needs HTTP routes that do not exist yet**: SCM-15/16 shipped
`IGitWorktreeService` / `IGitWorktreeMutationService` (list, open, create, remove, prune) and the
components, but **no route**. Add them append-only, then compose:
- `Prune` = `RefreshWorktreesAsync` (safe metadata refresh; never deletes valid worktrees).
- `Open` = `OpenWorktreeAsync`; `RequiresAttachmentFlow` must be surfaced as a typed action that routes
  the user through the existing FS-01 attachment flow — never auto-attach.
- `Create` accepts an **existing** ref only (branch/tag/commit); no new-vs-existing choice.
- Rules card is accurate: main not removable, clean required, existing ref, canvas never moved.

**Screen 10 — History & undo** → `HistoryScreen` around `HistoryPanel` + `UndoConflictDialog`, with the
storage-capacity meter from the real History storage.

**Screen 04 — Batch operations** → adopt the design's **centred** `SheetShell`, but with
`scrim={false}`. **Deliberate adaptation, recorded:** a scrim makes batch modal, which changes shipped
behaviour; the centred layout matches the design without that change.

**Screen 12 — Dialogs** → mount only overlays that have a real data source and a real trigger:
merge order + PDF progress (already via `PdfExportPanel`), undo-conflict (screen 10), missing-resource
(if `MissingResourceCard` is mounted). **QuickLook stays unavailable** until WORD-04 renders pages;
`ContextPackDialog` stays unmounted until a context-pack data source exists. Do not fake either.

## Out of scope

- New product features the design implies but we do not have (page-image QuickLook, context packs,
  DOCX text diff, branch management, cloud sync).
- Redesigning screens 01/02/03/08/09/11 — they are already shipped.
- Changing file-safety, SCM or batch semantics.

## Hard rules

- **No invented data.** Every row, counter and badge must come from a real route or be absent.
- **No inert controls.** A nav label that opens nothing is a defect (this is exactly the current
  "Source control" label). Either it works or it is not rendered.
- **`.pen` is read-only design of record**; access it only through the pencil MCP.
- One lane owns `App.tsx` at a time; never blind-merge two App.tsx rewrites.
- Add the API surface append-only; do not reorder existing controller members.
- `SourceControlController` must be added to `WebAppHost.AllowedControllers` as part of making the
  surface actually reachable — otherwise the desktop product 404s the whole screen.

## Acceptance criteria

- [ ] Sidebar `Source control`, `Worktrees`, `History` are real navigations; none is inert.
- [ ] Screen 05 renders real staged/changes/untracked/conflicts groups, a per-file diff (text for `.md`,
      document-aware for `.docx`), the commit bar, and the repository inspector.
- [ ] Screen 06 opens from a `.docx` row and its primary action opens Word through the existing path.
- [ ] Screen 07 lists real worktrees and supports create-from-existing-ref, remove-when-clean, prune,
      and open-with-attachment-flow.
- [ ] Screen 10 renders real operations with outcome chips and the real storage meter; undo works.
- [ ] Screen 04 is a centred sheet and **non-modal** (no scrim), and batch behaviour is otherwise unchanged.
- [ ] No screen claims a feature we do not have; QuickLook and context pack are absent, not stubbed.
- [ ] Frontend gates green (`tsc`, `oxlint`, `vitest`), backend suite green, no skipped required test.

## Commit guidance

```text
feat(ui): slice remaining macOS screens into the shipped shell
```
