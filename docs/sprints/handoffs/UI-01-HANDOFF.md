# Sprint Handoff — UI-01 (macOS Three-Pane Shell Redesign)

**Status:** DONE, including the controller's review fix-up round F1–F7 (§10). Documented deviations: screen 04's layout (§6.2), the deliberately non-modal screen-12 surfaces (§6.11), the inert/unmounted surfaces (§6.3, §6.4), and the contract tension over the folder gesture (§6.12).
**Branch:** `feat/ui-01-macos-shell-redesign` (worktree `ui-01-shell`, base `641c7d3`, integration tip `96134f2` merged in)
**Commits:**
- docs merge of `integration/parallel-batches` tip (`96134f2`: brief + sprint doc + UI-02 sprint doc + board + `01-canvas.png`), before the commits below
- `a9d5ffd` — Phase A: `mac-*` tokens + shell primitives
- `eb84a9b` — `feat(ui-01): canvas folder/file objects, folder interaction and faint content preview`
- `5ae25a5` — `feat(ui-01): macOS three-pane shell chrome (toolbar, sidebar, inspector)`
- `27e437d` — `refactor(ui-01): restyle every remaining surface with mac-* tokens (phases C-E)`
- `8e70d36` — docs: first handoff revision
- `b9b7d9c` — `feat(ui-01): SheetShell can render inline without a scrim (non-modal surfaces)` — F6
- `79a5a5b` — `fix(ui-01): hoist folder picker affordance out of the object button` — F3 + F4 + F5
- `8fa7182` — `refactor(ui-01): restyle the screen-12 sheet surfaces` — F1
- `c0dabb4` — `refactor(ui-01): restyle the screen-10 history & undo surfaces` — F1
- final commit — this handoff correction — F1 + F2 + F5 + F6 + F7

No backend file was touched:
`git diff --stat 641c7d3..HEAD -- src/DocumentIDE.Api src/DocumentIDE.Core src/DocumentIDE.Infrastructure src/DocumentIDE.Desktop` is empty.

---

## 1. Files changed, by phase

### Phase A — tokens + primitives (`a9d5ffd`) and the folder accents (`eb84a9b`)
- `src/DocumentIDE.Web/src/index.css` — Tailwind v4 `@theme` block with the `mac-*` tokens (surfaces, hairlines, text, accent/state, `--color-mac-folder`, `--color-mac-folder-front`) plus Inter/JetBrains Mono font stacks with system fallbacks (no web-font fetch; the app stays offline-capable).
- `src/DocumentIDE.Web/src/features/shell/` — `SegmentedControl`, `SheetShell`, `SourceListRow`, `InspectorCard` (+`InspectorField`/`InspectorKeyValue`), `SearchField`, `DepthStepper`, `PushButton`, `NodeIcon` (+`documentIconKind`, `NODE_ICON_COLORS`, `NODE_ICON_PATHS`), each with a colocated test. `b9b7d9c` added `SheetShell`'s `scrim?: boolean` opt-out (default `true`): the same header/body/footer chrome can be reused inline, without the scrim and without claiming modality (§10/F6).

### Phase B — shell chrome (`5ae25a5`)
- `App.tsx` — rewritten as toolbar + sidebar + content + inspector, **mounting exactly the same surfaces with the same wiring as the flat top nav**: `SearchPanel`, `SharePanel`, `AddFolderSource`, `BatchPreviewPanel`, `PdfExportPanel`, `MarkdownEditor` overlay, status-message banner, `bootstrapWorkspace` load, the two `useEffect`s and every callback are carried over verbatim.
- `features/shell/Toolbar.tsx` — traffic lights, sidebar toggle, `Canvas | List | Graph` segmented control, search field, `Batch operations` / `PDF` / inspector icon buttons.
- `features/shell/Sidebar.tsx` — `WORKSPACE` / `FOLDER SOURCES` / `LIBRARY` sections, 236px, 28px rows.
- `features/shell/Inspector.tsx` — 260px dock, real data only.
- `features/workspace/ViewSwitcher.tsx` — now delegates to `SegmentedControl` (same `radiogroup`/`radio`/`aria-checked` semantics, same `view-*-btn` testids).

### Phase C — per-view surfaces (`eb84a9b` + `27e437d`)
- `features/canvas/nodes/DocumentNode.tsx` — rewritten as the **file object** (sheet 52x66 r5, type accent bar 50x7 r[4,4,0,0], folded corner 14x14, faint content, filename label beneath, no card border).
- `features/canvas/nodes/FilePreview.tsx` (new) — faint page-1 preview + `LowDetailPaperLayout` (moved here and exported).
- `features/canvas/nodes/FolderObject.tsx` (new) — interactive folder object.
- `features/canvas/DocumentCanvas.tsx` — folder branch delegates to `FolderObject`; deferred single-click arbitration removed; canvas surface/dot-grid moved to `mac-*`.
- `features/list/DocumentList.tsx` — small folder/file icon in the name cell + `mac-*` chrome.
- `features/graph/DocumentGraph.tsx` — node glyph drawn from the shared icon paths, type-coloured, `data-kind` on the node group.
- `features/graph/GraphScopeControls.tsx` — macOS scope bar (`PushButton` + labelled `DepthStepper`).

### Phase D — dialogs / sheets / panes (`27e437d`, plus this fix-up round)
Chrome colours swapped to `mac-*` tokens in `27e437d`: `BatchPreviewPanel`, `SearchPanel`, `SharePanel`, `PdfExportPanel`, `MarkdownEditor`, `LocateMovedFileDialog`, `MissingResourceCard`, `MetadataFilterChips`, `SearchModeSwitch`, `AddFolderSource`, `ReconciliationStatus`, `RelationsInspector`, `DocxInspector`, `EditingInWordBadge`.

**Correction (review finding F1).** The first revision of this handoff *also* listed `ContextPackDialog`, `QuickLook`, `MergeOrderDialog`, `BatchExportProgress`, `PdfActions`, `HistoryPanel` and `UndoConflictDialog` as restyled. That claim was false: all seven are absent from `git diff --name-only 96134f2..8e70d36` and each had **0** `mac-` matches at `8e70d36` (`QuickLook` used `className="quick-look-overlay"` — an undefined class — and `ContextPackDialog` inline `overlayStyle`/`modalStyle`; the other five had no chrome classes at all, so the mechanical token swap had nothing to swap). They are restyled for real now:

| File | `mac-` matches: `8e70d36` → now | Form taken |
|---|---|---|
| `features/quick-look/QuickLook.tsx` | 0 → 9 | `SheetShell` **scrim** — it already carried `aria-modal="true"` + Escape-close, so the scrim matches its existing modality |
| `features/context/ContextPackDialog.tsx` | 0 → 18 | `SheetShell` **scrim** — it was already a full-screen `fixed` `rgba(0,0,0,0.5)` overlay |
| `features/pdf/MergeOrderDialog.tsx` | 0 → 3 | `SheetShell scrim={false}` — rendered inline, *not* modal today |
| `features/pdf/BatchExportProgress.tsx` | 0 → 7 | panel; keeps `role="region"` + `role="progressbar"` |
| `features/pdf/PdfActions.tsx` | 0 → 10 | panel; collision `fieldset` restyled, radios/checkbox unchanged |
| `features/history/HistoryPanel.tsx` | 0 → 41 | panel: operation cards, outcome chips, storage meter |
| `features/history/UndoConflictDialog.tsx` | 0 → 8 | `SheetShell scrim={false}` — rendered inline, *not* modal today |

Per-file evidence: `git diff --name-only 96134f2..8e70d36 -- <file>` → empty; `git show 8e70d36:<file> | Select-String 'mac-'` → 0 matches. Every testid, accessible name, label, warning string and callback was kept; **no existing assertion was weakened** to make these pass.

### Phase E — SCM component tree 05/06/07, restyled but **not mounted** (`27e437d`)
`SourceControlPanel`, `CommitBox`, `ChangeActions`, `CommitCorrelationPanel`, `DocxDiffPanel`, `MarkdownGitDiff`, `WorktreeList`, `WorktreeCreateDialog`.

### Phase F
- `docs/sprints/handoffs/UI-01-HANDOFF.md` (this file).

`docs/sprints/SPRINT-STATUS.md`, `docs/quality/v1-readiness-report.md`, `scripts/verify-release.ps1` and every manifest/roadmap were **not** edited by this lane (the `SPRINT-STATUS.md` change in the merge commit came from `integration/parallel-batches`). `docs/design/screens-mac/01-canvas.png` was taken from the integration branch, never re-exported or edited here. No `.pen` file was touched.

---

## 2. Verification (exact numbers, this worktree, `src/DocumentIDE.Web`)

| Command | Result |
|---|---|
| `npx vitest run` | **PASS — 71 files, 481 tests, 0 skipped** (before this sprint: 56 files / 402 tests; after the first revision: 71/477 — the fix-up round adds 4 tests) |
| `npx vitest run src/features/shell` | PASS — 11 files, 52 tests |
| `npx vitest run src/features/canvas` | PASS — 12 files, 86 tests |
| `npx vitest run src/features/pdf` | PASS — 5 files, 25 tests |
| `npx vitest run src/features/history` | PASS — 2 files, 21 tests |
| `npx vitest run src/features/context` | PASS — 1 file, 6 tests |
| `npx vitest run src/features/quick-look` | PASS — 1 file, 7 tests |
| `npx vitest run src/App.shell.test.tsx src/App.test.tsx src/App.shell.redesign.test.tsx` | PASS — 3 files, 14 tests |
| `npx tsc -b` | **exit 0** |
| `npx oxlint` | **exit 0** — 29 warnings, all pre-existing `react(only-export-components)` (files that export a non-component next to a component); none are new classes of finding, and the count is unchanged by this round |

Fix-up round invariants, checked by command:

- `git rev-parse 96134f2:src/DocumentIDE.Web/src/App.shell.test.tsx` → `3d2d60af…`; `git rev-parse HEAD:…` → `3d2d60af…` (identical — the gate file is byte-for-byte the `96134f2` version).
- `git diff --stat 641c7d3..HEAD -- src/DocumentIDE.Api src/DocumentIDE.Core src/DocumentIDE.Infrastructure src/DocumentIDE.Desktop` → **empty**.
- This round touched only 13 files: the 7 restyled surfaces above, `features/shell/SheetShell.tsx` (+test), `features/canvas/nodes/FolderObject.tsx` (+test), `features/canvas/FolderPicker.test.tsx`, `features/canvas/FolderInteraction.test.tsx` (comment only) and this handoff.

`npm ci` was run once at the start of the sprint; `motion@^14.3.0` was added with `npm install motion --prefix src/DocumentIDE.Web` and `package.json` + `package-lock.json` were committed together in `eb84a9b` (lockfile never hand-edited).

### TDD evidence (Red ➔ Green ➔ Refactor)

- **Folder/file objects, folder interaction, faint preview.** Red: the three new suites failed with `Failed to resolve import "./FilePreview"` / `"./FolderObject"` (3 files failed) before the components existed; then `Failed to resolve import "./FolderObject"` from `DocumentCanvas`. Green: `npx vitest run src/features/canvas/nodes/FilePreview.test.tsx …` → 4 files / 25 tests pass, then `src/features/canvas` → 12 files / 84 tests.
- One self-caught Red-stage correction: the initial release assertions claimed *no fetch at all*, which was dishonest — a fanned `.md` page legitimately GETs its own preview (`GET /api/documents/{id}/markdown`). The assertion was narrowed to the honest claim (**no non-GET request at all** ⇒ no filesystem-mutation path invoked) while still asserting `onReleaseDocument`/`onReleaseDocuments` receive exactly `folderId` + `[documentId]`. The unit-level release test injects a `loadMarkdown` stub, so it asserts a strictly stronger *zero-request* claim.
- **Shell rewrite.** Red: `Sidebar.test.tsx` / `Inspector.test.tsx` / `Toolbar.test.tsx` failed on unresolved imports before `Sidebar.tsx` / `Inspector.tsx` existed. Green: `src/features/shell` → 11 files / 50 tests.
- **Shell gates.** `App.shell.test.tsx` + `App.test.tsx` were kept green through the whole rewrite (7 + 1 tests) and a new `App.shell.redesign.test.tsx` (6 tests) pins the new structure. One real regression was caught by an existing test and fixed at the source, not by weakening it: `BatchApp.test.tsx` > *leaves Tools Space default activation available with real canvas mounted*. The old flat nav carried an `onKeyDown` guard so a focused chrome control keeps default Space activation while the canvas owns window-level Space for pan mode. The guard was re-applied to the toolbar, sidebar and inspector regions (`event.stopPropagation()`), i.e. the behaviour was preserved rather than the assertion relaxed.

---

## 3. Acceptance criteria

- [x] **`App.tsx` renders the macOS three-pane shell matching the brief's metrics/tokens.** Toolbar 52px (`h-[52px]`, `bg-mac-toolbar`), sidebar 236px (`bg-mac-sidebar`), inspector 260px (`bg-mac-inspector`), content `bg-mac-content`; `mac-*` tokens declared once in `index.css` `@theme`; Inter/JetBrains Mono stacks; both side panes collapsible via the toolbar's `Toggle sidebar` / `Toggle inspector` (failure/edge-case clause). Evidence: `App.shell.redesign.test.tsx` asserts `banner[name=Toolbar]`, `complementary[name=Sidebar]`, `main[name=Workspace content]`, `complementary[name=Inspector]`.
- [x] **Screen-by-screen inventory (honest, per screen).** 01 canvas, 02 list, 03 graph, 08 search, 09 markdown editor, 11 share — restyled surfaces, mounted. 05/06/07 SCM — restyled component tree, **unmounted** (reachability is a separate lane). 10 history & undo — `HistoryPanel` + `UndoConflictDialog` restyled **in this fix-up round** (F1). 12 dialogs & overlays — `QuickLook`, `ContextPackDialog`, `MergeOrderDialog`, `BatchExportProgress`, `PdfActions` restyled **in this fix-up round** (F1); the two that are already modal adopt `SheetShell`'s scrim, the three that are inline today deliberately do not (§6.11). **Not matched to the design:** 04 (batch operations) is restyled but keeps the shipped right-side sheet instead of the centred scrim sheet — deferred on purpose (§6.2). No screen outside this list is claimed.
- [x] **Zero regressions:** full suite 71 files / 481 tests pass, `tsc -b` exit 0, `oxlint` exit 0.
- [x] **`App.shell.test.tsx` passes unmodified in intent** (G1/G3/G4/G5/G6/G7/G8 as before; asserted DOM updated only where structure changed — see §5).
- [x] **Canvas folder and file objects are structurally distinct; the folder opens on click and fans out the *real* contained documents; releasing a page preserves CAN-04 semantics with no filesystem mutation.** Evidence: `FolderInteraction.test.tsx` (containment + outsider excluded + CAN-04 release metadata-only + distinctness), `FolderObject.test.tsx` (open/close incl. reduced motion, fan = exactly the folder's documents, release args, no non-GET request, plus the F4 a11y invariants: `aria-pressed`, picker affordance not nested).
- [x] **File objects (and fanned pages) show a faint preview derived from real content** — `.md` from the existing `GET /api/documents/{documentId}/markdown`; `.docx` keeps `LowDetailPaperLayout`; loading/missing/failed/stale states fall back. Evidence: `FilePreview.test.tsx`, `DocumentNode.test.tsx`, `FolderObject.test.tsx`.
- [x] **No backend file changed; no API contract changed.** No `src/DocumentIDE.Api|Core|Infrastructure|Desktop` file in any commit; the only network calls are the pre-existing routes.
- [x] **Handoff documents every DOM-structure test adjustment and why it is presentational** — §5.
- [x] **Every sprint-required new test exists**: segmented control drives `useViewStore` (toolbar *and* sidebar rows); sidebar sections from existing data; inspector follows view/selection; each extracted primitive has a colocated test.

---

## 4. Gesture decision (owner directive: "the open action must not silently replace the picker")

Recorded exactly as implemented by `DocumentCanvas` + `FolderObject`, and as asserted by tests:

| Gesture on a folder object | Action |
|---|---|
| single click on the object body | **opens/closes** the folder (front tilts back `rotateX -40°` origin bottom, contained documents fan out as pages ≈ -8°/0°/+9° lifted 55–75px) **and selects the object** like any other canvas object |
| single click on the object's `⌄` affordance (`aria-label="Open documents in <title>"`, `data-testid="folder-picker-<id>"`) | the **existing CAN-04 folder picker** opens — the old wiring is intact, it just moved behind its own affordance instead of being the bare-click action |
| double click on the object | the **existing "open all" release** (`onReleaseDocuments(folderId, supportedIds)`), folder ends closed. Stated exactly (F5): a real double click is `click, click, dblclick`, so `handleClick` runs **twice** first (select + open, then select + close) and `handleDoubleClick` runs **last** (`stopPropagation`, close again — idempotent — then release). End state is deterministic: closed, selected, exactly one release, no picker |
| click a fanned page | nothing (page click is stopped) |
| **drag a fanned page out** (≥12px) | **CAN-04 release of that one document** — `onReleaseDocuments(folderId, [documentId])`, metadata only |
| keyboard: Enter/Space on the object | same as single click (open/close) |

Consequences that are behavioural and are therefore stated plainly, not hidden:

1. The 250 ms *single-click/double-click arbitration timer* in `DocumentCanvas` was **deleted**. It existed only because a bare first click opened a popover that would swallow the second click of a real double click. With the picker behind its own affordance that hazard is gone, and the arbitration produced a 250 ms lag on every folder click. `FolderPicker.test.tsx` was re-pointed at the affordance; **every CAN-04 contract assertion was kept** (no dumping files on single click, picker contents, unsupported extension excluded, open-selected releases its ids, open-all releases supported ids, escape/close, folder-removal closes the picker, release is metadata-only).
2. A folder click now **selects** the folder (previously selection happened only after the deferred timer). This is the canvas's normal click semantics: `FolderObject.handleClick` → `onSelect` → `applyClick`. The first revision of this document claimed the old test's `selectedIds.has(folder) === false` assertion had been "updated to `true`" — it had actually been **deleted** (review finding F3, which was correct). Both assertions are now genuinely restored, as `=== true`, in the single-click test and the realistic-double-click test, and they pass against the unmodified product.

`prefers-reduced-motion` is honoured: with it set, `useReducedMotion()` returns true and the spring is replaced by `{ duration: 0 }`; the folder still opens, fans and closes.

---

## 5. DOM-structure test adjustments (presentational, not behavioural)

| Test | Old assertion | New assertion | Why it is presentational |
|---|---|---|---|
| `nodes/DocumentNode.test.tsx` | `getByText('DOCX')` / `getByText('MD')` type badges | root `data-testid="document-paper-sheet"` has `data-kind="docx"`/`"md"` and contains `[data-part="file-sheet"]` + `[data-part="type-accent"]` | The owner directive replaced the type *chip* with the Finder file object; the type is now carried by the accent bar's colour and machine-readably by `data-kind`. No product behaviour changed; the file identity (`aria-label="Document: <title>"`, filename text) is asserted unchanged. |
| `canvas/FolderPicker.test.tsx` | "single click is deferred, then opens the picker" | "a single click opens the folder object instead of dumping files" (asserts `data-open`, fanned pages, **`selectedIds.has(folder) === true`**, picker absent, nothing released) | Direct consequence of the owner's folder-interaction directive (§4). Every other CAN-04 contract assertion in the same file is untouched. |
| `canvas/FolderPicker.test.tsx` | after a realistic double click: `selectedIds.has(folder) === false` | `selectedIds.has(folder) === true` (folder ends `data-open="false"`, no picker, exactly one release) | Click-selects-object is the canvas's existing rule (§4.2). The first revision **deleted** this assertion while this document described it as an update; it is genuinely restored now (F3). |
| `canvas/nodes/FolderObject.test.tsx` | (new assertions) | `folder-object` carries `aria-pressed`, and **not** `aria-selected`; `folder.querySelector('button') === null`; the picker affordance is a **sibling** of the object button | F4: `aria-selected` is not a supported state on `role="button"`, and a real `<button>` inside a `role="button"` subtree is a nested interactive control. Open/close, fan-out, release and picker behaviour are asserted unchanged. |
| `canvas/FolderInteraction.test.tsx` | (new file) | release asserts `folderId` + `[documentId]` and that **no request used a non-GET method** | The old-style "no fetch at all" claim would have been false: fanned `.md` pages read their own real content. The honest claim is "no mutation path is invoked", which is what CAN-04 requires. |
| `features/batch/BatchApp.test.tsx` | unchanged | unchanged | The guard that makes it pass was restored in the product (chrome regions stop Space propagation), not relaxed in the test. |

No pre-existing test asserted a CSS class name, so the `mac-*` restyle is test-transparent. The only two files in the whole suite that assert a class name are this sprint's own new tests — `features/canvas/nodes/FilePreview.test.tsx` (`opacity-40`, `overflow-hidden` on the faint preview) and `features/shell/PushButton.test.tsx` (`bg-mac-accent` on the primary variant) — and both are asserting the *new* design contract, not a legacy one.

---

## 6. Deviations / spec conflicts

1. **Accessible-name uniqueness was enforced, not renamed away.** The brief's nav table wants a sidebar row per feature; the gate tests need exactly one `Search` / `Shares` / `PDF` / `Add folder` / `Batch operations` control. Resolution: `Search` and `Shares` are LIBRARY sidebar buttons; `Batch operations` and `PDF` are toolbar icon buttons; `Add folder` is the FOLDER SOURCES header button. `App.shell.redesign.test.tsx` asserts `getAllByRole('button', { name }) === 1` for each of the five, so a future duplicate fails loudly instead of making the gate queries ambiguous.
2. **Screen 04 (batch operations) keeps the shipped right-side sheet; the centred scrim sheet is deliberately deferred — on modality grounds, not because a test forbids it.** Review finding F2 was correct and the first revision's justification was wrong. A centred sheet over a scrim makes batch operations **modal**, so the canvas is no longer interactive beside it: that is a change to already-shipped behaviour, which this sprint's own non-goal forbids. `BatchApp.test.tsx` does **not** forbid the centred sheet — its only assertion is `expect(screen.getByTestId('canvas-surface')).toBeInTheDocument()` (`BatchApp.test.tsx:9`), a DOM-presence check, and in `App.tsx` the canvas is rendered by `{activeView === 'canvas' && <DocumentCanvas …>}` independently of `{batchOpen && <BatchPreviewPanel/>}`, so `canvas-surface` stays in the tree under a centred overlay too.

   **Deferred item, stated explicitly.** *What* is deferred: moving batch operations to the design's centred `SheetShell` scrim sheet. *Why*: it would make a shipped, currently non-modal surface modal. *What is ready*: `SheetShell` (scrim + header/body/footer, unit-tested) is the drop-in, and `b9b7d9c` now also gives it an inline (`scrim={false}`) form; the follow-up must additionally decide whether `BatchApp.test.tsx`'s "beside a usable canvas" acceptance is still the intended product behaviour. **Screen 04 does not match the design and is not claimed to.**
3. **`Source control`, `Worktrees`, `History` are inert presentational rows**, not buttons. `SourceControlPanel`, `HistoryPanel` and `QuickLook` are not mounted by this app (mounting SCM is a separate reachability lane per the sprint's *Out of Scope*; QuickLook needs a page-render source that no shipped host exposes). Rendering them as buttons would advertise actions that cannot happen, so they are plain rows (`Sidebar.test.tsx` asserts the labels exist and that no button carries those names).
4. **`RelationsInspector` / `DocxInspector` are restyled but not mounted.** Neither has a data source (no relations endpoint, no document-metadata endpoint wired to the web surface). The mounted inspector is built only from real data: canvas selection, workspace object counts, graph node/edge counts and the edge legend. No invented badges or placeholder values.
5. **Inspector content is per-view, not a fixed 3-card stack.** Canvas/List show `Selection` when there is a selection and `Workspace state` otherwise; Graph shows `Scope` + `Edge legend`. Selection and workspace state are mutually exclusive so a count row and a status row can never both display the same string.
6. **Graph stays a radial layout, not the reference's orthogonal tree.** GR-03/GR-05's layout engine and its tests (`graph-node-*`, `graph-edge-*`, `graph-svg`, dashed-inferred semantics) are unchanged; the owner directive's "folder/file icon in place of the leading dot, keep the tree geometry" was satisfied by drawing the shared icon paths inside the node circle, coloured by type, with `data-kind` for tests/AT. Rebuilding the layout is a separate sprint.
7. **`Top-K semantic edges` is presented as a labelled stepper** rather than a raw number input (the `DepthStepper` primitive). The control's behaviour and its `graph-scope-controls` testid are unchanged. The reference's `Workspace / Folder / Selection` scope segments are deliberately **not** rendered: the graph store has no per-scope source, and the failure/edge-case clause forbids inventing controls that do nothing.
8. **Persisted object geometry is unchanged.** `DOCUMENT_CARD_WIDTH/HEIGHT` (160x220) and `FOLDER_CARD_WIDTH/HEIGHT` (160x120) are what the canvas store, assisted placement and released-object upsert already use; changing them would move every existing workspace's layout. The brief's 140x122 object frame is used for the *object drawing* inside the frame and for components mounted without explicit geometry (`FolderObject`/`DocumentNode` defaults).
9. **`.docx` preview is the low-detail fallback** (`LowDetailPaperLayout`, now a 3-line drawing per the brief). The read-only content route is UI-02's deliverable; no backend route was added.
10. **`App.css` is dead** (verified: no importer in `src/**`, `index.html` or `main.tsx`; `main.tsx` imports only `./index.css`). It was not deleted and not built on — it is left for a separate cleanup, as the sprint's styling section instructs.
11. **The screen-12 sheet idiom is applied only where the surface is already modal (F1/F2 consistency).** `scrim` follows **existing modality**, so no shipped surface silently became modal:
    - `QuickLook` (`aria-modal="true"` + Escape-close, unstyled class names only) and `ContextPackDialog` (`position: fixed`, `rgba(0,0,0,0.5)` overlay) were already modal overlays → they adopt `SheetShell` with the scrim. Neither is mounted by any shipped host yet (QuickLook needs the WORD-04 page-render pipeline; the context pack has no host), so no shipped behaviour can change.
    - `MergeOrderDialog`, `UndoConflictDialog`, `BatchExportProgress`, `PdfActions` are rendered **inline** inside already-mounted panels (the PDF panel is mounted at `App.tsx:341`) → restyled in place; the two dialog-shaped ones reuse `SheetShell`'s panel chrome with `scrim={false}`. Adding a scrim would have made them modal — the same mistake §6.2 describes. Two details worth keeping: `BatchExportProgress` keeps `role="region"` (it is a progress region, not a dialog) and `PdfActions`'s collision `fieldset`/radios/checkbox are unchanged, so the "never overwrite silently" decision UI is untouched.
    - Side effect worth knowing: `ContextPackDialog`'s old header `×` button was replaced by `SheetShell`'s header `✕` (same action, same `onClose`); its footer `Close` button is unchanged.
12. **Undisclosed contract tension, now disclosed (F7).** This branch **replaced** the contract's original "single click on a folder → CAN-04 picker" with "single click → open/close, picker behind the `⌄` affordance". Two clauses are in tension: the contract's *Folder interaction* clause literally allows "the object opens on click and the existing picker affordance remains reachable", while the *Out of Scope* list forbids changing gesture semantics proven by a passing test (`FolderPicker.test.tsx`'s "single click is deferred, then opens the picker"). The first revision presented this as a pure owner directive and did not name the conflict. Resolution actually taken: the **owner directive of 2026-10-18 wins**, because it is later, explicit, and the clause above sanctions exactly this shape; the picker stays reachable, the 250 ms arbitration timer is deleted, and every other CAN-04 assertion is kept. **Controller action requested:** amend the contract so the *Out of Scope* gesture rule and the *Folder interaction* clause agree, rather than leaving the resolution to be inferred from this handoff.

---

## 7. Honest limits stated for the owner

- **The faint preview is content-derived, not a rendered page image.** A true page-1 raster needs the Word → temp PDF → page-image pipeline that exists in no shipped host (the same missing pipeline that blocks `QuickLook`/WORD-04). `.md` shows real source text; `.docx` shows the low-detail layout until UI-02 lands. `thumbnailUrl` / `thumbnailContent` / `thumbnailState` are the untouched public seam a future renderer feeds, so the node needs no redesign then.
- **Word COM concurrency, the review-gated destructive-op path, and "canvas drag never moves a file" are untouched.** This sprint added no mutation path of any kind.
- **Folder a11y — fixed in this round (F4).** The folder's picker affordance is no longer nested inside the object's `role="button"` subtree: it is a sibling, still keyboard reachable, same `aria-label` and `data-testid`. The object now exposes selection with `aria-pressed` instead of the invalid `aria-selected`. **Still open, deliberately not touched:** the *file* object (`DocumentNode.tsx:119`) carries `aria-selected` on `role="button"` too — the same defect class — but that attribute is asserted by a pre-existing test (`DocumentList.test.tsx:443`), so changing it is a test-visible change outside F4's scope and belongs in its own lane. It is a follow-up, not a regression.

---

## 8. Security & file-safety gate

- [x] **No user file was mutated through an unreviewed/bypass path** — this sprint touches no file-mutation code; the canvas release path is the pre-existing metadata-only `onReleaseDocuments` → `upsertObject`, and `FolderInteraction.test.tsx` asserts no non-GET request is issued by the drag.
- [x] **No API contract was loosened or widened** — no backend file changed; every call is a pre-existing route (`/markdown`, `/folder-sources`, `/documents`, `/graph`, `/word/open`, PDF, share, batch).
- [x] **No privileged-route guard, review-before-apply gate or destructive confirmation was removed or weakened** — `BatchPreviewPanel`'s preview → save → apply gating is untouched (only chrome colours changed); it is still review-before-apply.
- [x] **No arbitrary local path is exposed through public sharing** — untouched; the sidebar shows `displayName` only.
- [x] **Relevant higher-level safety rules remain stronger than convenience** — local filesystem stays the source of truth; DOCX deep editing remains Word Desktop; nothing here changes recovery-snapshot, temp-mutation or atomic-replace rules (no mutation code touched).

---

## 9. Remaining risks

1. **Controller-owned merge.** `App.tsx` was rewritten by this lane; `integration/parallel-batches` W30 will mount the Source Control surface into `App.tsx` and the desktop host allow-list. This needs the explicit reconciliation pass the sprint's *Coordination* section requires — not a blind merge. The SCM tree is already restyled, so the reconciliation should be "mount the restyled component into the new sidebar's LIBRARY section" (and delete the inert `Source control` row).
2. **`motion` is the first runtime UI dependency** beyond react/react-dom. The repo's web-asset embedding path should be re-verified by whoever owns the release build (this sprint's gates do not bundle the app).
3. **A11y:** `FolderObject`'s nested-button/`aria-selected` defect is fixed (§7); `DocumentNode` (file object) still carries `aria-selected` on `role="button"` and is asserted by `DocumentList.test.tsx:443`, so that one is a bounded follow-up (§7).
4. **Screen 04 remains a controller decision** (§6.2): the centred sheet is deferred for modality reasons; adopting it is a product call about whether batch operations should be modal.
5. **Token swap is mechanical** across the Phase D/E panels: chrome colours only, `dark:` variants deliberately left alone. Panels that had no reference screen (e.g. `LocateMovedFileDialog`) keep their current layout, per the failure/edge-case clause.
6. **Test-time `matchMedia`**: `motion`'s `useReducedMotion()` needs a `matchMedia` stub in jsdom; the folder tests stub it. If a future test renders `FolderObject` without the stub it will fail for an environment reason, not a product one.

---

## 10. Controller review fix-up round (F1–F7)

An independent reviewer returned **REQUEST CHANGES** with seven findings. All seven are addressed. Nothing was argued away, and no gate assertion was weakened, deleted or redirected to make something pass.

### F1 (major) — seven components claimed as restyled had never been touched → **restyled for real**
- `features/quick-look/QuickLook.tsx:182` (`<SheetShell>`, scrim); `features/context/ContextPackDialog.tsx:125` (`<SheetShell>`, scrim; inline `overlayStyle`/`modalStyle` deleted); `features/pdf/MergeOrderDialog.tsx:58` (`scrim={false}`); `features/pdf/BatchExportProgress.tsx` (mac panel; `role="region"`/`role="progressbar"` kept); `features/pdf/PdfActions.tsx:148,157,165,185,220` (`PushButton`s + restyled collision `fieldset`); `features/history/HistoryPanel.tsx:197` (+ following — cards, chips, storage meter); `features/history/UndoConflictDialog.tsx:59` (`scrim={false}`).
- Evidence: at `8e70d36`, `git diff --name-only 96134f2..8e70d36 -- <file>` is empty **and** `git show 8e70d36:<file> | Select-String 'mac-'` returns **0** for all seven; now each has `mac-` matches (QuickLook 9, ContextPackDialog 18, MergeOrderDialog 3, BatchExportProgress 7, PdfActions 10, HistoryPanel 41, UndoConflictDialog 8). Commits `8fa7182` + `c0dabb4`. Per-testid/name/callback preservation verified by the untouched colocated tests: `src/features/pdf` 5/25, `src/features/history` 2/21, `src/features/context` 1/6, `src/features/quick-look` 1/7 — all green.
- Consequence: the "all 12 screens" acceptance is replaced by the honest inventory in §3 and §1/Phase D.

### F2 (major) — the screen-04 justification was false → **handoff corrected; behaviour deliberately unchanged**
- Finding verified independently: `BatchApp.test.tsx:9` is only `expect(screen.getByTestId('canvas-surface')).toBeInTheDocument()` (a DOM-presence check), and `App.tsx:289-299`/`:338` render the canvas independently of the batch panel, so that test does not forbid a centred sheet.
- Change: §6.2 now states the real reason — a centred scrim sheet makes batch operations **modal**, which changes already-shipped behaviour and is forbidden by this sprint's own non-goal — and carries an explicit deferred item (what/why/what-is-ready). §3 no longer claims screen 04 matches the design. No behaviour changed: adopting the centred sheet *would* be the change.

### F3 (major) — a gate assertion had been silently deleted and described as an "update" → **restored**
- `features/canvas/FolderPicker.test.tsx:142` and `:192` — `expect(useCanvasStore.getState().selectedIds.has(FOLDER_ID)).toBe(true)` after the single click and at the end of the realistic-double-click case; `:151` — still selected after the closing click.
- Evidence: the assertions pass against the **unmodified** product (`FolderObject.handleClick` → `onSelect` → `applyClick`), i.e. the previous revision deleted a true assertion. `src/features/canvas` 12 files / 86 tests green. §4.2 and §5 now say "deleted, not updated".

### F4 (minor) — nested interactive control + invalid `aria-selected` → **fixed**
- `features/canvas/nodes/FolderObject.tsx:182` (new sibling positioning frame), `:193` (`aria-pressed`, was `aria-selected`), `:304` (affordance now a **sibling** of the `role="button"` element).
- New tests: `features/canvas/nodes/FolderObject.test.tsx:189` (the object button contains no `<button>`; the affordance is outside it, still a labelled keyboard-reachable button) and `:210` (`aria-pressed` `false`→`true`, and `aria-selected` never present).
- Red evidence: those two tests failed first — the object button did contain the picker `<button>` and carried no `aria-pressed`. Green after the change; the whole canvas suite stayed green (`FolderPicker`, `FolderInteraction`, `DocumentCanvas`, list/graph suites).

### F5 (minor) — imprecise gesture description → **description corrected, behaviour untouched**
- `features/canvas/nodes/FolderObject.tsx:19-31` (doc comment), `features/canvas/FolderPicker.test.tsx` (file header), `features/canvas/FolderInteraction.test.tsx` (file header), §4 table.
- Now stated exactly: `click, click, dblclick` → `handleClick` runs twice (select + open, select + close) then `handleDoubleClick` runs last (`stopPropagation`, closes again — idempotent — then one release); end state closed, selected, one release, no picker. No timer arbitration was re-added.

### F6 (minor) — `SheetShell` was dead code → **resolved**
- Consumers now: `QuickLook.tsx:182`, `ContextPackDialog.tsx:125`, `MergeOrderDialog.tsx:58`, `UndoConflictDialog.tsx:59`. `b9b7d9c` added the `scrim?: boolean` opt-out with its own Red→Green test (`features/shell/SheetShell.test.tsx`, now 6 tests).
- No other extracted primitive is unused: `SegmentedControl` (Toolbar, ViewSwitcher), `SourceListRow` (Sidebar), `InspectorCard` (Inspector), `SearchField` (Toolbar), `DepthStepper`/`PushButton` (GraphScopeControls, plus the new PDF/history surfaces), `NodeIcon` (7 files).

### F7 (minor) — undisclosed contract tension → **disclosed**
- §6.12 added: both clauses quoted verbatim in effect, the owner directive of 2026-10-18 named as the winner and why (later, explicit, and sanctioned by the *Folder interaction* clause), and an explicit controller action requested — amend the contract so the *Out of Scope* gesture rule and the *Folder interaction* clause agree.

### Round-level invariants
- `src/DocumentIDE.Web/src/App.shell.test.tsx` sha at `96134f2` = sha at HEAD = `3d2d60af…` (byte-identical).
- `npx vitest run` → 71 files / **481** tests, 0 skipped (was 477 before this round; +2 SheetShell, +2 FolderObject).
- `npx tsc -b` → exit 0. `npx oxlint` → exit 0, 29 warnings (count unchanged; all pre-existing `react(only-export-components)`).
- `git diff --stat 641c7d3..HEAD -- src/DocumentIDE.Api src/DocumentIDE.Core src/DocumentIDE.Infrastructure src/DocumentIDE.Desktop` → empty.

## Next eligible sprint(s)

- `UI-02` — document preview content API (unblocks the `.docx` faint preview and `QuickLook`; the node already consumes the seam, and `QuickLook` now consumes `SheetShell`).
- A follow-up for screen 04's centred-sheet+scrim layout — deferred **because it makes batch operations modal** (§6.2), not because a test forbids it. The follow-up must be an explicit product decision about modality, and `SheetShell` is the drop-in.
- DocumentNode a11y follow-up (§7/§9.3): the *file* object still carries `aria-selected` on `role="button"`; changing it is test-visible (`DocumentList.test.tsx:443`).
- SCM reachability lane — mount the already-restyled `SourceControlPanel` into the new sidebar and the desktop host allow-list.
