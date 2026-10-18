# Sprint Handoff — UI-03 (part A)

**Status:** part A DONE · **part B outstanding** (screens 10 History & undo, 04 Batch operations, 12 Dialogs)
**Branch:** `feat/ui-03-mac-screens-slicing`
**Scope of this commit:** screens **05 Source control**, **06 DOCX diff**, **07 Worktrees**, plus real
sidebar reachability and the desktop host allow-list. Screens 10/04/12 are deliberately not in this
commit; they remain a later commit on the same branch.

## What changed

### 1. Desktop reachability

- `SourceControlController` is now in `WebAppHost.AllowedControllers`.
- The host test `SourceControlHostTests.SourceControlRoute_IsReachable_WhileNonAllowedControllerStays404`
  proves `GET /api/source-control/status` reaches the controller while `GET /api/diagnostics/latest`
  is still 404 (not on the allow-list).
- **Necessary host wiring beyond the allow-list:** the desktop host did not have the SCM-01..18
  service registrations (they lived only in the development API host's `Program.cs`), so adding the
  controller to the allow-list alone produced an unconstructible controller. `WebAppHost` now
  registers the same service factories with `TryAdd*`. No service, Git command, route or DTO was
  changed; no SCM semantics moved. This is disclosed as a deviation from the literal
  "allow-list only" boundary because the required host test cannot pass without it.

### 2. Real sidebar navigation (`features/shell/Sidebar.tsx`, `App.tsx`)

- `Source control`, `Worktrees` and `History` are real buttons driven by the shell's
  `activeSurface` state. No `sidebar-inert-*` test ids remain and no inert label is rendered.
- `activeSurface` is `'workspace' | 'source-control' | 'worktrees' | 'docx-diff' | 'git-history'`.
  The toolbar's Canvas/List/Graph switch returns to the workspace surface.
- The workspace right inspector is only rendered on the workspace surface; the SCM screens own
  their right inspectors, so the inspector toggle remains meaningful on every surface.
- No count badges were added: the shell has no real SCM count at sidebar level, so the design's
  trailing badges are absent rather than fabricated.

### 3. Screen 05 — `SourceControlScreen`

Composed from the shipped components: `SourceControlPanel`, `MarkdownGitDiff`, `CommitBox`,
`RemoteStatus`, `PushButton`, `ChangeActions`, `GitHistoryPanel`, `ConflictPanel`,
`DiscardRestoreDialog`, `PullDialog`.

- Groups (`Staged` / `Changes` / `Untracked` / `Conflicts`) come from `GET /api/source-control/status`.
- Header `branch · n ahead · m behind` comes from `GET /api/source-control/remote`; a missing
  upstream renders the explicit "No upstream" state, never a fake 0/0.
- Repository inspector: repository path, branch, upstream, ahead, behind, worktree count (from
  `GET worktrees`), staged count and changes count (from the status snapshot), conflicts count,
  plus `Open worktrees`.
- Remote card hosts the shipped `RemoteStatus` (Fetch) and `PushButton`; `Pull…` opens `PullDialog`.
- `Discard changes…` opens the reviewed `DiscardRestoreDialog` for an unstaged, non-conflicted
  Markdown change.
- `GitHistoryPanel` is mounted behind an explicit "Show git history" toggle in the inspector (so the
  screen does not fetch history until asked).
- `ConflictPanel` mounts only when the status snapshot has conflict entries; its manual-open action
  routes DOCX to Word and Markdown to the editor.
- Commit bar uses `CommitBox` with the design labels **Commit staged** (`POST /commit`) and
  **Commit all** (`POST /commit-all`) behind the explicit confirmation UI.
- **`.md` diff:** the SCM-04 HTTP diff route (`GET /api/source-control/diff`) is **not exposed by
  this build's controller**. Rather than invent it, the screen composes the exact
  HEAD ↔ working-tree text diff from two real read-only routes:
  `GET /api/source-control/file-version?commit=HEAD` and
  `GET /api/documents/{id}/markdown`. `buildUnifiedDiff` is a pure exact-line formatter (common
  prefix/suffix context, removed block, added block; no fuzzy/semantic matching).
- **`.docx` diff:** never a text diff. The row shows a document-aware change summary (state, index
  vs working tree, format) and a control that opens screen 06.
- Other file types show an explicit "no preview" state, not a faked text diff.
- Empty / loading / transport-error / no-repository / Git-unavailable states are real component
  states; no rows are synthesized.

### 4. Screen 06 — `DocxDiffScreen`

- Wraps the shipped `DocxDiffPanel` and the existing `openInWord` action.
- Inspector shows file, folder, format, Git status, change, index and working-tree state, and the
  primary `Open in Word` action. Unregistered paths get an explicit "not a registered document"
  message and no arbitrary-path opener.
- **Adaptation / limitation:** the SCM-05 document-aware comparison provider is not exposed over
  HTTP in this build. The panel renders the typed limitation rather than a fabricated change list;
  no text diff and no page image are claimed. The screen still opens from a `.docx` row and its
  primary action reaches `POST /api/word/open`.

### 5. Screen 07 — `WorktreesScreen`

- Composed from `WorktreeList` + `WorktreeCreateDialog` on the SCM-18 routes:
  `GET worktrees`, `POST worktrees`, `DELETE worktrees`, `POST worktrees/open`,
  `POST worktrees/prune`.
- `Prune` = `RefreshWorktreesAsync` (safe metadata refresh; never deletes a valid worktree).
- `Open` = `OpenWorktreeAsync`. `RequiresAttachmentFlow` is surfaced as a typed action with an
  explicit `Attach folder source` button that opens the existing FS-01 flow with the worktree path
  prefilled. Nothing is attached automatically; the test asserts no `POST /folder-sources`.
- `Create` accepts an existing ref only (the shipped dialog copy/policy is unchanged).
- `Remove` is a two-step reviewed action and uses the SCM-18 `DELETE` route; main/locked/prunable
  worktrees are shown as not removable and the backend still refuses dirty worktrees.
- Repository inspector + accurate rules card: main not removable, clean required, existing ref,
  canvas files never moved.
- `WorktreeList` gained an **optional** `onRemoveWorktree` prop; when absent the original read-only
  SCM-15 surface and all its tests are unchanged.

### 6. `History` sidebar row

Points to `GitHistoryScreen`, which mounts the real SCM-08 `GitHistoryPanel`
(`GET /api/source-control/history`). It is clearly labelled as **Git** history. Design 10
(Document IDE operation history + undo + storage meter) is part B and is **not** claimed here.

## Files changed

**New**

- `src/DocumentIDE.Web/src/features/source-control/SourceControlScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/source-control/DocxDiffScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/source-control/WorktreesScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/source-control/GitHistoryScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/source-control/sourceControlApi.ts`
- `src/DocumentIDE.Web/src/features/source-control/sourceControlFileKind.ts`
- `src/DocumentIDE.Web/src/features/source-control/markdownDiff.ts` / `.test.ts`
- `tests/DocumentIDE.IntegrationTests/Desktop/SourceControlHostTests.cs`
- `docs/sprints/handoffs/UI-03-HANDOFF.md` (this file)

**Modified**

- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` — allow-list + SCM DI factories (disclosed above).
- `src/DocumentIDE.Web/src/App.tsx` — surface switching + screen mounts; the existing canvas/list/
  graph/search/share/PDF/batch/markdown paths are preserved (no removals).
- `src/DocumentIDE.Web/src/features/shell/Sidebar.tsx` — SCM rows navigate.
- `src/DocumentIDE.Web/src/features/source-control/CommitBox.tsx` — optional labels + optional
  commit-all confirmation (defaults preserve SCM-07 behaviour).
- `src/DocumentIDE.Web/src/features/source-control/ConflictPanel.tsx` — optional DOCX-specific open
  handler (default behaviour unchanged).
- `src/DocumentIDE.Web/src/features/source-control/WorktreeList.tsx` — optional remove action
  (absent by default; SCM-15 behaviour unchanged).
- `src/DocumentIDE.Web/src/features/workspace/AddFolderSource.tsx` — optional `initialPath` so the
  worktree attachment signal can prefill the existing FS-01 form.

**Existing tests changed (deliberately)**

- `src/DocumentIDE.Web/src/features/shell/Sidebar.test.tsx` — the old assertion
  *"lists the not-yet-mounted SCM surfaces as inert labels, not fake buttons"* encoded the defect
  UI-03 fixes. It was replaced with two assertions that the three rows navigate to
  `source-control` / `worktrees` / `git-history` and that only the active row is pressed. No other
  Sidebar assertion was deleted.
- `src/DocumentIDE.Web/src/App.shell.test.tsx` — **additive only**: SCM route fixtures plus four
  UI-03 reachability tests (Source control, `.docx` → screen 06 → Word, Worktrees, History). No
  existing assertion was removed or weakened.

No file-safety, SCM, batch, sharing, service, controller (other than the allow-list entry), `.pen`,
`docs/quality/**`, `scripts/verify-release.ps1`, `SPRINT-STATUS.md`, manifest or roadmap file was
changed.

## Verification

- `npx tsc -b` → **exit 0**
- `npx oxlint` → **exit 0**, 35 warnings, all pre-existing (`only-export-components`); no finding in
  any new/changed UI-03 file.
- `npx vitest run` → **82 files / 580 tests passed, 0 failed, 0 skipped**.
- `dotnet build DocumentIDE.sln --nologo` → **0 errors, 105 warnings** (all pre-existing package/
  analyzer warnings; no new warning from UI-03).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControlHostTests"`
  → **1/1 passed, 0 skipped**.
- `git diff --check` → clean.

### TDD evidence (Red → Green → Refactor)

1. **Host RED:** `SourceControlHostTests` was written first and failed with `Expected: Not NotFound /
   Actual: NotFound` on `/api/source-control/status`. After the allow-list + DI wiring it passed 1/1.
2. **Screens RED:** `SourceControlScreen.test.tsx`, `DocxDiffScreen.test.tsx`,
   `WorktreesScreen.test.tsx` and `GitHistoryScreen.test.tsx` were written before the screen files;
   the first run failed at module resolution (`Failed to resolve import "./SourceControlScreen"`,
   etc.). After implementation the focused run was **29/29**, and the final full run is 580/580.
3. **Behaviour tests that drove implementation:** SCM groups from a mocked status payload; `.md`
   text diff from the two read-only routes; `.docx` never invokes a text diff; commit vs commit-all
   route + body; commit-all confirmation blocks the request until confirmed; `RequiresAttachmentFlow`
   surfaces and does **not** auto-attach; `Prune` posts the prune route; sidebar rows navigate.
4. **Refactor:** `fileKind` was moved out of the screen component to
   `sourceControlFileKind.ts` so the new screen adds no `only-export-components` warning; exact-line
   diff edge cases (empty side, no trailing newline, CRLF) were isolated in `markdownDiff.ts` with
   their own tests. All gates were re-run after the refactor.

Honesty note: the Sidebar test was updated before the Sidebar implementation but its red run was not
captured separately; the red evidence above is from the host test and the new screen/component tests.

## Acceptance criteria — part A

- [x] Sidebar `Source control`, `Worktrees`, `History` are real navigations; none is inert.
      Evidence: `Sidebar.test.tsx` (2 tests) + `App.shell.test.tsx` UI-03 tests.
- [x] Screen 05 renders real staged/changes/untracked/conflicts groups, a per-file diff (text for
      `.md`, document-aware for `.docx`), the commit bar and the repository inspector.
      Evidence: `SourceControlScreen.test.tsx`; `markdownDiff.test.ts`; 580/580 suite.
- [x] Screen 06 opens from a `.docx` row and its primary action opens Word through the existing path.
      Evidence: `SourceControlScreen.test.tsx` (routes callback), `DocxDiffScreen.test.tsx`
      (`POST /api/word/open` body), `App.shell.test.tsx` (end-to-end row → screen 06 → Word).
- [x] Screen 07 lists real worktrees and supports create-from-existing-ref, remove-when-clean, prune,
      and open-with-attachment-flow.
      Evidence: `WorktreesScreen.test.tsx` (6 tests).
- [x] Host reachability: Source Control route served by the desktop host; non-allowed controller
      still 404. Evidence: `SourceControlHostTests` 1/1.
- [ ] Screen 10 (History & undo), screen 04 (centred batch sheet), screen 12 dialogs — **part B,
      outstanding**. The `History` sidebar row deliberately opens the real Git history surface, not
      design 10.

## Deviations / adaptations

1. **No SCM-04/05 HTTP diff route.** `GET /api/source-control/diff` is listed in spec 13 §18 but is
   not implemented in `SourceControlController`. The task forbade backend/controller changes, so:
   - `.md` uses the two real read-only routes (`file-version` + `markdown`) and exact line
     formatting in `markdownDiff.ts`;
   - `.docx` shows the typed document-aware limitation plus `Open in Word`, never a fake diff.
   This is the main residual gap and the reason screen 06 does not show a change list yet.
2. **Commit staged body.** The real `SourceControlCommitRequest` is `{ path, message }`; there is no
   `stagedPaths` field in the API contract. `Commit staged` therefore posts the repository path and
   lets Git commit the actual index (the only staging authority), exactly as SCM-07 defined. The
   screen does not invent a path list.
3. **Host DI wiring.** See §1: the desktop host needed the SCM service factories before the
   controller could be constructed. No service behaviour changed.
4. **`History` = Git history, not design 10.** Design 10 is part B; mounting a fake operations list
   would violate "no invented data". The row is a real navigation to a real Git history surface and
   is labelled accordingly.
5. **DOCX screen body.** `DocxDiffPanel` is mounted with the typed "comparison unavailable" state.
   This is honest but means the design's change-class chips are not rendered yet.
6. **Worktree `Active` state.** SCM-18 exposes no "active worktree" read route, so the screen does
   not invent an Active badge. The main worktree shows `Not removable`; Open remains available for
   every registered worktree.
7. **Markdown diff scope.** It requires an indexed `documentId` to read the working-tree source; an
   unregistered path shows a typed "not a registered Markdown document" state rather than an
   arbitrary-path read.
8. **Hunk shape.** The client formatter emits one whole-file hunk (common prefix/suffix context +
   removed block + added block) rather than the minimal multi-hunk output `git diff` would produce.
   It is exact, complete and never fuzzy; it is not byte-identical to Git's hunk partitioning.

## Residual risks

- **Screen 06 is data-limited until the SCM-05 diff route exists.** The wrapper, inspector and Word
  action are real, but the document-aware change list cannot be displayed through the API yet.
- The Markdown diff is a UI-side composition. If the backend later exposes the spec diff route, the
  screen should switch to it and delete `markdownDiff.ts` (or keep it only as a test oracle).
- `activeWorktreePath` is not observable over SCM-18, so no "Active worktree" state is shown.
- The rendered worktree `Remove` action is enabled only for non-main/non-locked/non-prunable rows;
  dirty state is unknown to the list and is refused typed by the backend (no force).
- `WorktreeList` still renders an `Open` button when a caller does not pass `onOpenWorktree`
  (pre-existing behaviour); `WorktreesScreen` always passes a working handler.

## Next eligible work

- **Part B of UI-03:** screen 10 (`HistoryScreen` + `UndoConflictDialog` + real storage meter),
  screen 04 (centred non-modal `SheetShell`), screen 12 overlays with real data sources only.
- **Follow-up (not this sprint):** expose the SCM-04/05 diff routes (`GET /api/source-control/diff`)
  so screen 05/06 can render the typed document-aware report directly and the Markdown UI-side
  composition can be retired.
