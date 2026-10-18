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

---

# Sprint Handoff — UI-03 (part B)

**Status:** part B DONE (with one recorded data gap) · **Branch:** `feat/ui-03-mac-screens-slicing`
**Scope of this commit:** screens **10 History & undo**, **04 Batch operations**, **12 Dialogs**, plus the
architecture-boundary correction for the SCM registration part A added. Part A's commit is untouched;
this is a second commit on the same branch.

## What changed

### 1. Screen 10 — `HistoryScreen` (`features/history/HistoryScreen.tsx`, new)

One History surface, two visibly separate authorities (spec 13 §4.3/§12):

- macOS `SegmentedControl` **`Operations | Git`** (`aria-label="History source"`).
- **Git** = part A's `GitHistoryScreen` unchanged (`GET /api/source-control/history`). It is the
  default tab, preserving the shipped History-row behaviour.
- **Operations** = the shipped `HistoryPanel` (operation rows, per-file outcomes, the real
  storage-capacity meter and its warning) plus the shipped `UndoConflictDialog` for the HIST-02
  conflict review. Rows only come from an injected `OperationHistorySource` (`load` + `undo`);
  there is no sample data in the component.
- The inspector shows the selected operation's real fields (type, status, recorded time, file count,
  snapshot id) and an accurate undo-policy card. The storage meter is not duplicated.

**HONEST DATA GAP — blocking for the owner.** There is no privileged route that lists Document IDE
operations or history storage usage, so the shipped shell passes **no `source`** and the Operations
tab renders a typed unavailable state (`history-operations-unavailable`) with no rows, no meter and
no "no operations recorded" claim. What exists today:

- `POST /api/batch-operations/{planId}/apply`, `POST .../{planId}/undo`,
  `GET .../{planId}/history` — per-plan only; the client has no way to discover plan ids after a
  restart, so they cannot back a History list.
- `HistoryStorage.GetAllMetadataAsync()` / `GetTotalSizeAsync()` / `GetCapacityBytesAsync()` exist in
  Infrastructure and are DI-registered, but no controller exposes them.
- `JsonlHistoryStore` reads apply/undo records **per plan** only (no enumeration API, no
  operation-level status/type record).

Adding a listing route would therefore also require deciding how to project operation type and
operation status (neither is persisted), and per-file undo outcomes (only undo status is persisted).
That is a backend contract decision, not a UI slice, so it is recorded here instead of being invented
in the client. Wiring the eventual route is one `source` prop at the shell; `HistoryScreen` needs no
change. Until then `undo-conflict` is mounted but cannot be triggered in the shipped build.

**Why no backend change in this commit:** the alternative (a route that enumerates `apply-*.jsonl`
and derives "Completed/Partial" plus a generic type) would publish derived semantics the engine does
not persist. The sprint forbids invented data; a typed gap is the honest outcome.

### 2. Screen 04 — centred, non-modal batch sheet

`BatchPreviewPanel` now renders the design's **centred sheet chrome** via the shared `SheetShell`
with `scrim={false}`:

- full-content layer `absolute inset-0 … items-center justify-center pointer-events-none`; the sheet
  panel re-enables pointer events, so the canvas stays usable outside the sheet (**not modal**, no
  scrim, no `aria-modal`);
- sheet header (title + optional `✕` via a new optional `onClose` prop, wired in `App` to the same
  state the toolbar toggle uses), an honest status line (`n files · m occurrences`), scrolling body
  and a footer with secondary `Save selection` + primary `Apply` (and `Cancel preview` while a job is
  running);
- the outer `<section aria-label="Batch operations">` region is preserved, so `BatchApp.test.tsx`
  and `BatchPreviewPanel.test.tsx` keep every existing assertion;
- no batch request, plan, selection, apply/undo or persistence semantics changed.

### 3. Screen 12 — dialogs reachability

| Overlay | Reachable? | Why |
| --- | --- | --- |
| Merge order | **yes** | `PdfExportPanel` → `Merge to PDF` (real selection) |
| PDF progress | **yes** | `PdfExportPanel` → separate/merge response (`BatchExportProgress`) |
| PDF collision | **yes** | typed `OutputCollision` from `/api/pdf/export` → decision fieldset |
| Undo review | **mounted, not triggerable** | host is `HistoryScreen`; no operations list exists yet (screen 10 gap) |
| QuickLook | **no** | needs the WORD-04 Word → temp PDF → page-image pipeline; `QuickLook` is imported only by its own test. Not stubbed. |
| Context pack | **no** | `ContextPackDialog` has no context-pack data source. Not stubbed. |
| Missing resource | **no** | `MissingResourceCard` has **no rendered host** in this build (verified: imported only by `MissingResourceCard.test.tsx`). Not mounted speculatively. |

So **three** overlays are reachable end-to-end, one is mounted behind an honest missing data source,
and three are deliberately absent. `App.dialogs.test.tsx` asserts the three positives and the
absences, including negative assertions for `quick-look-dialog`, the context-pack dialog and the
missing-resource group so a later stub cannot be added silently.

### 4. Architecture-boundary fix (part A regression)

Part A registered the SCM runtime inline in `src/DocumentIDE.Desktop/WebView/WebAppHost.cs`,
including `IGitProcessRunner -> GitProcessRunner`. That violates
`SourceControlProcessBoundaryTests.ApiCompositionRoot_IsTheOnlyCSharpPlaceWiringTheGitRunner`, which
permits the concrete runner name only in `src/DocumentIDE.Api/Program.cs` or under
`src/DocumentIDE.Infrastructure/SourceControl/`.

- New `src/DocumentIDE.Infrastructure/SourceControl/SourceControlServiceRegistration.cs`:
  `public static IServiceCollection AddSourceControlServices(this IServiceCollection services)`
  containing the same **16** `TryAdd*` registrations part A added (options, mutation queue, runner,
  repository, stage, commit, work-session repository/service, remote, push, pull, history, restore,
  worktree read/mutation, conflicts). `TryAdd*` keeps caller overrides intact.
- `WebAppHost` now calls `builder.Services.AddSourceControlServices();` once and no longer names
  `GitProcessRunner`.
- `DocumentIDE.Api/Program.cs` was **not** touched (it is permitted to name the runner).

**Residual (stated plainly):** the SCM runtime is now registered in two places — the inline block in
`Program.cs` and this new extension. They currently agree, but they can drift. A later cleanup should
move `Program.cs` onto `AddSourceControlServices()` and delete the inline block. That was explicitly
out of scope for this pass.

## Files changed (part B)

**New**

- `src/DocumentIDE.Web/src/features/history/HistoryScreen.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.sheet.test.tsx`
- `src/DocumentIDE.Web/src/App.dialogs.test.tsx`
- `src/DocumentIDE.Infrastructure/SourceControl/SourceControlServiceRegistration.cs`

**Modified**

- `src/DocumentIDE.Web/src/App.tsx` — mounts `HistoryScreen` on the History surface, adds the
  document-name/open handlers, passes `onClose` to the batch sheet. All part-A surfaces remain.
- `src/DocumentIDE.Web/src/features/shell/Sidebar.tsx` — one History row; surface id
  `'git-history'` renamed to `'history'`, test id `sidebar-git-history` → `sidebar-history`.
- `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.tsx` — centred non-modal `SheetShell`.
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` — one `AddSourceControlServices()` call.

**Existing tests changed (deliberate, listed in full)**

1. `src/DocumentIDE.Web/src/features/shell/Sidebar.test.tsx` — one expected navigation value:
   `onNavigate` third call is now `'history'` instead of `'git-history'`. The row is no longer
   Git-only; the intent (the History row navigates to the History surface) is unchanged.
2. `src/DocumentIDE.Web/src/App.shell.test.tsx` — the part-A History test now clicks
   `sidebar-history` (renamed test id) and also asserts `history-screen` renders; title updated to
   "composed History surface on its Git tab". No assertion was removed or weakened. Added one new
   test for the segmented control + typed unavailable state.
3. No other existing test was edited. `BatchPreviewPanel.test.tsx`, `BatchApp.test.tsx`,
   `Sidebar`'s other tests, `HistoryPanel.test.tsx` and `UndoConflictDialog.test.tsx` are untouched
   and pass.

## Verification

- `npx tsc -b` → **exit 0**
- `npx oxlint` → **exit 0**, **35 warnings**, all pre-existing `react(only-export-components)`; no
  finding in any part-B file (same count as part A).
- `npx vitest run` → **85 files / 593 tests passed, 0 failed, 0 skipped**
  (part A baseline: 82 / 580; +3 files, +13 tests).
- `dotnet build DocumentIDE.sln --nologo` → **0 errors, 104 warnings** (incremental build; all
  pre-existing package/analyzer warnings, none from the new registration file or `WebAppHost`).
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj` →
  **12/12 passed, 0 failed, 0 skipped** (the boundary test is green).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControlHostTests"`
  → **1/1 passed, 0 skipped**.
- `git diff --check` → clean.

### TDD evidence (Red → Green → Refactor)

1. **Screen 10 RED:** `HistoryScreen.test.tsx` failed at module resolution
   (`Failed to resolve import "./HistoryScreen"`). After implementation, a wrong default import
   surfaced as "Element type is invalid"; after fixing the named `UndoConflictDialog` import the
   focused run was **6/6**.
2. **Screen 04 RED:** `BatchPreviewPanel.sheet.test.tsx` failed with
   `Unable to find … [data-testid="batch-sheet-layer"]`; after the sheet refactor it was **2/2** and
   the existing batch suites stayed green (26/26 combined).
3. **App wiring RED:** the new App History test failed with
   `Unable to find … [data-testid="sidebar-history"]`; after the Sidebar/App wiring,
   `App.shell.test.tsx` + `Sidebar.test.tsx` were **19/19**.
4. **Screen 12:** merge order/progress/collision were already reachable from part A/G8 and are now
   pinned by positive assertions; the new negative assertions are guards. The genuinely new overlay
   (undo review) has its Red evidence in the screen-10 test above.
5. **Refactor:** tab switching now dismisses transient undo state; import grouping kept; no
   behavioural change to batch or Git code. All gates re-run after the final edits.

## Acceptance criteria — part B

- [x] Screen 10 is reachable from the existing single History row and has a working
      `Operations | Git` segmented control. Evidence: `HistoryScreen.test.tsx` (6),
      `App.shell.test.tsx` (2 History tests).
- [~] **Screen 10 renders real operations with outcome chips and the real storage meter; undo
      works.** The composed `HistoryPanel` + `UndoConflictDialog` do this for a real payload and are
      fully tested, **but the shipped build has no operations-listing route**, so the tab renders a
      typed unavailable state. This is a **blocking data gap**, not a fake list. Evidence:
      `HistoryScreen.test.tsx` + `history-operations-unavailable`.
- [x] Screen 04 is a centred sheet and **non-modal** (no scrim, no `aria-modal`), footer actions
      work, and every existing batch assertion still passes. Evidence:
      `BatchPreviewPanel.sheet.test.tsx` (2) + untouched `BatchPreviewPanel.test.tsx` /
      `BatchApp.test.tsx`.
- [x] QuickLook and the context pack are absent, not stubbed; missing-resource is absent because its
      host is not rendered. Evidence: `App.dialogs.test.tsx` negative assertions.
- [x] Frontend gates green; backend build green; architecture suite **12/12**; no skipped required
      test.

## Deviations / residual risks

1. **Screen 10 data gap (main residual).** A read-only route that lists operations + storage usage
   is required before the Operations tab can show real rows. The UI contract (`OperationHistorySource`)
   is ready and the typed unavailable state names the missing route.
2. **Undo review is not triggerable end-to-end** until (1) lands; the overlay itself is mounted and
   tested with a real-shaped undo result.
3. **SCM registration duplication** between `Program.cs` and `AddSourceControlServices()` (see §4).
4. **Batch is now an overlay, not a column.** It covers the content centre while open (design
   fidelity), instead of shrinking the canvas. No canvas object, selection, batch request or
   persistence behaviour changed; the canvas remains mounted and interactive outside the sheet.
5. The batch sheet's `✕` is new chrome wired to the same close state as the toolbar toggle; when the
   panel is rendered without `onClose` (unit tests) no close button is rendered.
6. Screen 10's default tab is Git, not Operations. This preserves part A's shipped History behaviour
   and avoids landing every History visit on a data-unavailable surface. The design's Operations
   default should be restored when the listing route exists.

## Next eligible work

- **Backend follow-up (owner decision needed):** expose a read-only operations + history-usage route
  (and decide whether operation type/status are persisted or derived), then pass a real
  `OperationHistorySource` in `App.tsx`. That single wiring makes screen 10 fully real and makes the
  undo-conflict overlay reachable.
- **Cleanup:** consolidate `DocumentIDE.Api/Program.cs` SCM registrations onto
  `AddSourceControlServices()`.
- **Unchanged from part A:** SCM-04/05 diff routes for screens 05/06.
