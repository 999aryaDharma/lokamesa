# Sprint Handoff — UI-01 (macOS Three-Pane Shell Redesign)

**Status:** DONE (one documented deviation: screen 04's centred-sheet+scrim layout — see *Deviations*)
**Branch:** `feat/ui-01-macos-shell-redesign` (worktree `ui-01-shell`, base `641c7d3`, integration tip `96134f2` merged in)
**Commits:**
- `eb84a9b` — `feat(ui-01): canvas folder/file objects, folder interaction and faint content preview`
- `5ae25a5` — `feat(ui-01): macOS three-pane shell chrome (toolbar, sidebar, inspector)`
- `27e437d` — `refactor(ui-01): restyle every remaining surface with mac-* tokens (phases C-E)`
- docs merge of `integration/parallel-batches` tip (`96134f2`: brief + sprint doc + UI-02 sprint doc + board + `01-canvas.png`), before the three commits above

Phase A (tokens + primitives) is `a9d5ffd`. No backend file was touched:
`git diff --stat 641c7d3..HEAD -- src/DocumentIDE.Api src/DocumentIDE.Core src/DocumentIDE.Infrastructure src/DocumentIDE.Desktop` is empty.

---

## 1. Files changed, by phase

### Phase A — tokens + primitives (`a9d5ffd`) and the folder accents (`eb84a9b`)
- `src/DocumentIDE.Web/src/index.css` — Tailwind v4 `@theme` block with the `mac-*` tokens (surfaces, hairlines, text, accent/state, `--color-mac-folder`, `--color-mac-folder-front`) plus Inter/JetBrains Mono font stacks with system fallbacks (no web-font fetch; the app stays offline-capable).
- `src/DocumentIDE.Web/src/features/shell/` — `SegmentedControl`, `SheetShell`, `SourceListRow`, `InspectorCard` (+`InspectorField`/`InspectorKeyValue`), `SearchField`, `DepthStepper`, `PushButton`, `NodeIcon` (+`documentIconKind`, `NODE_ICON_COLORS`, `NODE_ICON_PATHS`), each with a colocated test.

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

### Phase D — dialogs / sheets / panes (`27e437d`)
`BatchPreviewPanel`, `SearchPanel`, `SharePanel`, `PdfExportPanel`, `MarkdownEditor`, `LocateMovedFileDialog`, `MissingResourceCard`, `MetadataFilterChips`, `SearchModeSwitch`, `ContextPackDialog`, `QuickLook`, `MergeOrderDialog`, `BatchExportProgress`, `PdfActions`, `AddFolderSource`, `ReconciliationStatus`, `HistoryPanel`, `UndoConflictDialog`, `RelationsInspector`, `DocxInspector`, `EditingInWordBadge` — chrome colours swapped to `mac-*` tokens.

### Phase E — SCM component tree 05/06/07, restyled but **not mounted** (`27e437d`)
`SourceControlPanel`, `CommitBox`, `ChangeActions`, `CommitCorrelationPanel`, `DocxDiffPanel`, `MarkdownGitDiff`, `WorktreeList`, `WorktreeCreateDialog`.

### Phase F
- `docs/sprints/handoffs/UI-01-HANDOFF.md` (this file).

`docs/sprints/SPRINT-STATUS.md`, `docs/quality/v1-readiness-report.md`, `scripts/verify-release.ps1` and every manifest/roadmap were **not** edited by this lane (the `SPRINT-STATUS.md` change in the merge commit came from `integration/parallel-batches`). `docs/design/screens-mac/01-canvas.png` was taken from the integration branch, never re-exported or edited here. No `.pen` file was touched.

---

## 2. Verification (exact numbers, this worktree, `src/DocumentIDE.Web`)

| Command | Result |
|---|---|
| `npx vitest run` | **PASS — 71 files, 477 tests, 0 skipped** (baseline before this sprint: 56 files / 402 tests) |
| `npx vitest run src/features/shell` | PASS — 11 files, 50 tests |
| `npx vitest run src/features/canvas` | PASS — 12 files, 84 tests |
| `npx vitest run src/App.shell.test.tsx src/App.test.tsx src/App.shell.redesign.test.tsx` | PASS — 3 files, 14 tests |
| `npx tsc -b` | **exit 0** |
| `npx oxlint` | **exit 0** — 29 warnings, all pre-existing `react(only-export-components)` (files that export a non-component next to a component); none are new classes of finding |

`npm ci` was run once at the start of the sprint; `motion@^14.3.0` was added with `npm install motion --prefix src/DocumentIDE.Web` and `package.json` + `package-lock.json` were committed together in `eb84a9b` (lockfile never hand-edited).

### TDD evidence (Red ➔ Green ➔ Refactor)

- **Folder/file objects, folder interaction, faint preview.** Red: the three new suites failed with `Failed to resolve import "./FilePreview"` / `"./FolderObject"` (3 files failed) before the components existed; then `Failed to resolve import "./FolderObject"` from `DocumentCanvas`. Green: `npx vitest run src/features/canvas/nodes/FilePreview.test.tsx …` → 4 files / 25 tests pass, then `src/features/canvas` → 12 files / 84 tests.
- One self-caught Red-stage correction: the initial release assertions claimed *no fetch at all*, which was dishonest — a fanned `.md` page legitimately GETs its own preview (`GET /api/documents/{id}/markdown`). The assertion was narrowed to the honest claim (**no non-GET request at all** ⇒ no filesystem-mutation path invoked) while still asserting `onReleaseDocument`/`onReleaseDocuments` receive exactly `folderId` + `[documentId]`. The unit-level release test injects a `loadMarkdown` stub, so it asserts a strictly stronger *zero-request* claim.
- **Shell rewrite.** Red: `Sidebar.test.tsx` / `Inspector.test.tsx` / `Toolbar.test.tsx` failed on unresolved imports before `Sidebar.tsx` / `Inspector.tsx` existed. Green: `src/features/shell` → 11 files / 50 tests.
- **Shell gates.** `App.shell.test.tsx` + `App.test.tsx` were kept green through the whole rewrite (7 + 1 tests) and a new `App.shell.redesign.test.tsx` (6 tests) pins the new structure. One real regression was caught by an existing test and fixed at the source, not by weakening it: `BatchApp.test.tsx` > *leaves Tools Space default activation available with real canvas mounted*. The old flat nav carried an `onKeyDown` guard so a focused chrome control keeps default Space activation while the canvas owns window-level Space for pan mode. The guard was re-applied to the toolbar, sidebar and inspector regions (`event.stopPropagation()`), i.e. the behaviour was preserved rather than the assertion relaxed.

---

## 3. Acceptance criteria

- [x] **`App.tsx` renders the macOS three-pane shell matching the brief's metrics/tokens.** Toolbar 52px (`h-[52px]`, `bg-mac-toolbar`), sidebar 236px (`bg-mac-sidebar`), inspector 260px (`bg-mac-inspector`), content `bg-mac-content`; `mac-*` tokens declared once in `index.css` `@theme`; Inter/JetBrains Mono stacks; both side panes collapsible via the toolbar's `Toggle sidebar` / `Toggle inspector` (failure/edge-case clause). Evidence: `App.shell.redesign.test.tsx` asserts `banner[name=Toolbar]`, `complementary[name=Sidebar]`, `main[name=Workspace content]`, `complementary[name=Inspector]`.
- [x] **All 12 reference screens have a corresponding restyled surface (SCM = restyled, unmounted, component tree).** 01/02/03 canvas+list+graph surfaces and their inspectors; 04 batch; 05/06/07 SCM tree restyled; 08 search; 09 markdown editor; 10 history & undo; 11 share; 12 dialog/overlay components. **Deviation:** screen 04 is restyled as the shipped right-side sheet, not the centred scrim sheet (see *Deviations* #2).
- [x] **Zero regressions:** full suite 71 files / 477 tests pass, `tsc -b` exit 0, `oxlint` exit 0.
- [x] **`App.shell.test.tsx` passes unmodified in intent** (G1/G3/G4/G5/G6/G7/G8 as before; asserted DOM updated only where structure changed — see §5).
- [x] **Canvas folder and file objects are structurally distinct; the folder opens on click and fans out the *real* contained documents; releasing a page preserves CAN-04 semantics with no filesystem mutation.** Evidence: `FolderInteraction.test.tsx` (containment + outsider excluded + CAN-04 release metadata-only + distinctness), `FolderObject.test.tsx` (open/close incl. reduced motion, fan = exactly the folder's documents, release args, no non-GET request).
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
| double click on the object | the **existing "open all" release** (`onReleaseDocuments(folderId, supportedIds)`), folder ends closed |
| click a fanned page | nothing (page click is stopped) |
| **drag a fanned page out** (≥12px) | **CAN-04 release of that one document** — `onReleaseDocuments(folderId, [documentId])`, metadata only |
| keyboard: Enter/Space on the object | same as single click (open/close) |

Consequences that are behavioural and are therefore stated plainly, not hidden:

1. The 250 ms *single-click/double-click arbitration timer* in `DocumentCanvas` was **deleted**. It existed only because a bare first click opened a popover that would swallow the second click of a real double click. With the picker behind its own affordance that hazard is gone, and the arbitration produced a 250 ms lag on every folder click. `FolderPicker.test.tsx` was re-pointed at the affordance; **every CAN-04 contract assertion was kept** (no dumping files on single click, picker contents, unsupported extension excluded, open-selected releases its ids, open-all releases supported ids, escape/close, folder-removal closes the picker, release is metadata-only).
2. A folder click now **selects** the folder (previously selection happened only after the deferred timer). This is the canvas's normal click semantics; the old test's `selectedIds.has(folder)` → `false` assertion was updated to `true` and the change is recorded here.

`prefers-reduced-motion` is honoured: with it set, `useReducedMotion()` returns true and the spring is replaced by `{ duration: 0 }`; the folder still opens, fans and closes.

---

## 5. DOM-structure test adjustments (presentational, not behavioural)

| Test | Old assertion | New assertion | Why it is presentational |
|---|---|---|---|
| `nodes/DocumentNode.test.tsx` | `getByText('DOCX')` / `getByText('MD')` type badges | root `data-testid="document-paper-sheet"` has `data-kind="docx"`/`"md"` and contains `[data-part="file-sheet"]` + `[data-part="type-accent"]` | The owner directive replaced the type *chip* with the Finder file object; the type is now carried by the accent bar's colour and machine-readably by `data-kind`. No product behaviour changed; the file identity (`aria-label="Document: <title>"`, filename text) is asserted unchanged. |
| `canvas/FolderPicker.test.tsx` | "single click is deferred, then opens the picker" | "a single click opens the folder object instead of dumping files" (asserts `data-open`, fanned pages, picker absent, nothing released) | Direct consequence of the owner's folder-interaction directive (§4). The CAN-04 assertions in the same file are untouched. |
| `canvas/FolderPicker.test.tsx` | after a realistic double click: `selectedIds.has(folder) === false` | `=== true` (folder ends `data-open="false"`, no picker) | Click-selects-object is the canvas's existing rule (§4.2). |
| `canvas/FolderInteraction.test.tsx` | (new file) | release asserts `folderId` + `[documentId]` and that **no request used a non-GET method** | The old-style "no fetch at all" claim would have been false: fanned `.md` pages read their own real content. The honest claim is "no mutation path is invoked", which is what CAN-04 requires. |
| `features/batch/BatchApp.test.tsx` | unchanged | unchanged | The guard that makes it pass was restored in the product (chrome regions stop Space propagation), not relaxed in the test. |

No pre-existing test asserted a CSS class name, so the `mac-*` restyle is test-transparent. The only two files in the whole suite that assert a class name are this sprint's own new tests — `features/canvas/nodes/FilePreview.test.tsx` (`opacity-40`, `overflow-hidden` on the faint preview) and `features/shell/PushButton.test.tsx` (`bg-mac-accent` on the primary variant) — and both are asserting the *new* design contract, not a legacy one.

---

## 6. Deviations / spec conflicts

1. **Accessible-name uniqueness was enforced, not renamed away.** The brief's nav table wants a sidebar row per feature; the gate tests need exactly one `Search` / `Shares` / `PDF` / `Add folder` / `Batch operations` control. Resolution: `Search` and `Shares` are LIBRARY sidebar buttons; `Batch operations` and `PDF` are toolbar icon buttons; `Add folder` is the FOLDER SOURCES header button. `App.shell.redesign.test.tsx` asserts `getAllByRole('button', { name }) === 1` for each of the five, so a future duplicate fails loudly instead of making the gate queries ambiguous.
2. **Screen 04 is a right-side sheet, not a centred scrim sheet.** `BatchPreviewPanel`'s full-screen scrim redesign was not adopted: its own acceptance test (`BatchApp.test.tsx`, pre-existing) asserts the panel opens **beside a usable canvas** (`canvas-surface` still present) and toggles from the toolbar. The panel was restyled with `mac-*` tokens and keeps its layout. `SheetShell` (with scrim/header/body/footer) exists and is unit-tested, ready for a follow-up that also updates that acceptance test. This is the one place where the design's layout idiom is not fully realised.
3. **`Source control`, `Worktrees`, `History` are inert presentational rows**, not buttons. `SourceControlPanel`, `HistoryPanel` and `QuickLook` are not mounted by this app (mounting SCM is a separate reachability lane per the sprint's *Out of Scope*; QuickLook needs a page-render source that no shipped host exposes). Rendering them as buttons would advertise actions that cannot happen, so they are plain rows (`Sidebar.test.tsx` asserts the labels exist and that no button carries those names).
4. **`RelationsInspector` / `DocxInspector` are restyled but not mounted.** Neither has a data source (no relations endpoint, no document-metadata endpoint wired to the web surface). The mounted inspector is built only from real data: canvas selection, workspace object counts, graph node/edge counts and the edge legend. No invented badges or placeholder values.
5. **Inspector content is per-view, not a fixed 3-card stack.** Canvas/List show `Selection` when there is a selection and `Workspace state` otherwise; Graph shows `Scope` + `Edge legend`. Selection and workspace state are mutually exclusive so a count row and a status row can never both display the same string.
6. **Graph stays a radial layout, not the reference's orthogonal tree.** GR-03/GR-05's layout engine and its tests (`graph-node-*`, `graph-edge-*`, `graph-svg`, dashed-inferred semantics) are unchanged; the owner directive's "folder/file icon in place of the leading dot, keep the tree geometry" was satisfied by drawing the shared icon paths inside the node circle, coloured by type, with `data-kind` for tests/AT. Rebuilding the layout is a separate sprint.
7. **`Top-K semantic edges` is presented as a labelled stepper** rather than a raw number input (the `DepthStepper` primitive). The control's behaviour and its `graph-scope-controls` testid are unchanged. The reference's `Workspace / Folder / Selection` scope segments are deliberately **not** rendered: the graph store has no per-scope source, and the failure/edge-case clause forbids inventing controls that do nothing.
8. **Persisted object geometry is unchanged.** `DOCUMENT_CARD_WIDTH/HEIGHT` (160x220) and `FOLDER_CARD_WIDTH/HEIGHT` (160x120) are what the canvas store, assisted placement and released-object upsert already use; changing them would move every existing workspace's layout. The brief's 140x122 object frame is used for the *object drawing* inside the frame and for components mounted without explicit geometry (`FolderObject`/`DocumentNode` defaults).
9. **`.docx` preview is the low-detail fallback** (`LowDetailPaperLayout`, now a 3-line drawing per the brief). The read-only content route is UI-02's deliverable; no backend route was added.
10. **`App.css` is dead** (verified: no importer in `src/**`, `index.html` or `main.tsx`; `main.tsx` imports only `./index.css`). It was not deleted and not built on — it is left for a separate cleanup, as the sprint's styling section instructs.

---

## 7. Honest limits stated for the owner

- **The faint preview is content-derived, not a rendered page image.** A true page-1 raster needs the Word → temp PDF → page-image pipeline that exists in no shipped host (the same missing pipeline that blocks `QuickLook`/WORD-04). `.md` shows real source text; `.docx` shows the low-detail layout until UI-02 lands. `thumbnailUrl` / `thumbnailContent` / `thumbnailState` are the untouched public seam a future renderer feeds, so the node needs no redesign then.
- **Word COM concurrency, the review-gated destructive-op path, and "canvas drag never moves a file" are untouched.** This sprint added no mutation path of any kind.
- **Known a11y follow-up (not a regression, present before in spirit):** the folder's picker affordance is a real `<button>` nested inside the folder's `role="button"` container. It is keyboard reachable and labelled, but a later pass should hoist it out of the role-button subtree so AT does not announce nested buttons.

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
3. **Nested interactive element in `FolderObject`** (§7).
4. **Token swap is mechanical** across the Phase D/E panels: chrome colours only, `dark:` variants deliberately left alone. Panels that had no reference screen (e.g. `LocateMovedFileDialog`, `ContextPackDialog` in full) keep their current layout, per the failure/edge-case clause.
5. **Test-time `matchMedia`**: `motion`'s `useReducedMotion()` needs a `matchMedia` stub in jsdom; the folder tests stub it. If a future test renders `FolderObject` without the stub it will fail for an environment reason, not a product one.

## Next eligible sprint(s)

- `UI-02` — document preview content API (unblocks the `.docx` faint preview; the node already consumes the seam).
- A follow-up for screen 04's centred-sheet+scrim layout (also update `BatchApp.test.tsx`'s "beside a usable canvas" acceptance).
- SCM reachability lane — mount the already-restyled `SourceControlPanel` into the new sidebar and the desktop host allow-list.
