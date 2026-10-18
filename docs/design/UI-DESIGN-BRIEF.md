# Document IDE — UI Design Brief (`.pen`)

Source of truth for this brief is the shipped frontend code:
`src/DocumentIDE.Web/src/**` (React 19 + Tailwind CSS v4, slate palette).

Purpose: drive a `.pen` design file (`docs/design/document-ide-ui.pen`) that mirrors the
existing product UI so it can be reviewed visually and used for further design work.

> Status: the `.pen` file must be created inside the pen.dev app (MCP cannot create files).
> Until it exists, this brief is the plan of record.

## 1. Design tokens observed in code

| Token | Value in code |
| --- | --- |
| App shell background | `bg-slate-50` |
| Panel / surface | `bg-white`, `border-slate-200` |
| Text primary / secondary / muted | `text-slate-900` / `text-slate-700` / `text-slate-500` |
| Segmented control | `bg-slate-100`, active `bg-white text-slate-900 shadow-xs`, inactive `text-slate-500` |
| Radius | `rounded-md` (controls), `rounded` (segment) |
| Density | `text-xs` controls, `p-2` nav, `p-4` panels |
| Shell layout | full-screen flex column: top nav (border-b) + `flex-1` main row |
| Side panel | right side, `w-full max-w-xl border-l border-slate-200 bg-white p-4`, scrollable |

## 2. Screen inventory (as implemented)

### 2.1 App shell — `App.tsx`
- Top nav (`aria-label="Tools"`): segmented **Canvas | List | Graph** (`ViewSwitcher`),
  **Batch operations** toggle button (`aria-expanded`).
- Main row: active view (canvas / list / graph) on the left; **Batch operations** panel
  on the right when toggled.

### 2.2 Canvas view — `features/canvas/DocumentCanvas.tsx`
- Infinite-ish canvas surface; document/folder nodes (`DocumentNode`), selection,
  zoom controls (`Zoom out` / `Zoom level %` / `Zoom in`), empty state
  (`aria-label="Empty canvas"`), **FolderPicker** (`Search files…`, per-file select),
  **RelationsInspector**, `EditingInWordBadge`, persisted positions,
  assisted/tidy layout, spatial index (performance-driven).

### 2.3 List view — `features/list/DocumentList.tsx`
- Table-ish list (`aria-label="Document list"`), sortable column headers
  (`Sort by <key>`), name filter (`Filter by name...`), type filter (`Filter by type`),
  select-all-visible, per-row selection, clear selection.

### 2.4 Graph view — `features/graph/DocumentGraph.tsx`
- Graph surface + `GraphScopeControls` (bounded scope), `graphLayoutWorker` bounded render.
- Truthful semantics (master spec §27.2): **explicit relations solid**, inferred/semantic
  relations visually distinct (dashed), select / navigate / open-on-double-click.
- Known gap (REL-03 G1): current component is a placeholder — the `.pen` design should show
  the intended final state (edges, legend, node types, scope controls).

### 2.5 Batch operations panel — `features/batch/BatchPreviewPanel.tsx`
- Sections: preview status + progress (`Preview progress`), expired-plan warning
  (`Plan expired. Re-preview required.`), cancel preview, **SearchReplaceReview**
  (recursive folder scope tree, per-file and per-occurrence checkboxes),
  Apply action (currently disabled in product — see REL-03 G2), history entry point.

### 2.6 History / undo — `features/history/{HistoryPanel,UndoConflictDialog}.tsx`
- `HistoryPanel`: operation history list (`Operation history`), per-file outcomes,
  storage-usage progress (`History storage usage`), undo outcomes per entry.
- `UndoConflictDialog` (`role="dialog"`, `Undo review`): conflicting files list, resolution.

### 2.7 Search — `features/search/{SearchPanel,SearchModeSwitch,MetadataFilterChips}.tsx`
- `SearchPanel` (`aria-label="Search Panel"`), input
  (`Search workspace (e.g. nusa type:docx)...`), clear, mode switch
  (`Search Mode Switch`: exact / fuzzy / semantic), metadata filter chips (removable),
  result list with per-result selection, select-all, **Batch operation** hand-off.

### 2.8 Markdown editing — `features/markdown/MarkdownEditor.tsx`
- Source-first markdown editor surface with preview, wikilinks/backlinks,
  save state / change detection (REL-03 G7: current file is a stub — design the real surface).

### 2.9 Source control — `features/source-control/**`
- `SourceControlPanel`: changes pane — staged / unstaged / untracked / conflict sections,
  read-only branch + ahead/behind placeholders, file selection, open-document seam.
- `ChangeActions`: Stage / Unstage / Stage All / Unstage All with busy + typed error states.
- `CommitBox` (SCM-07): commit message + **Commit** vs visually distinct **Commit All**
  (explicit confirmation), hook-failure typing, commit id on success.
- `MarkdownGitDiff`: read-only unified diff view (escaped text).
- `DocxDiffPanel`: document-aware DOCX change report (text / table / formatting classes),
  binary-change fallback.
- `WorktreeList`: registered worktrees, current branch read-only, open-existing action.
- `WorktreeCreateDialog`: create from existing ref, remove clean worktree, dirty-blocked state,
  path + ref inputs (`C:\projects\app-wt-feature`, `feature-x`).

### 2.10 Sharing — `features/sharing/{ShareDialog,ShareManager,SharePermissions}.tsx`
- `ShareDialog` (`Create share`, `role="dialog"`): Snapshot / Live, TTL, permissions.
- `ShareManager`: share status, member list, remove member, stop/revoke.
- `SharePermissions`: allowed downloads section, shared-document download nav.

### 2.11 PDF — `features/pdf/{PdfActions,MergeOrderDialog,BatchExportProgress}.tsx`
- `PdfActions` (`PDF export actions`): export/merge/finalize actions, results list.
- `MergeOrderDialog` (`Merge document order`): ordered file list, move up/down.
- `BatchExportProgress` (`Batch PDF export progress`): per-file progress region.

### 2.12 Document surfaces
- `QuickLook` (`Quick Look preview of <file>`): preview overlay + close.
- `DocxInspector` (`DOCX inspector`): headings outline, filter by heading, metadata.
- `ContextPackDialog`: context pack with token budget (`e.g. 8000`).
- `MissingResourceCard` / `LocateMovedFileDialog`: missing file/folder recovery
  (`Enter full target file path...`).
- `ReconciliationStatus`: filesystem reconciliation state.
- `AddFolderSource`: attach local folder (`Enter local folder path (e.g. C:\MyDocuments)`).

## 3. `.pen` deliverable plan

Top-level frames (screens) to build in `docs/design/document-ide-ui.pen`:

1. `App — Canvas view` (default shell, zoom controls, document nodes, inspector).
2. `App — List view` (filters, sort, selection).
3. `App — Graph view` (explicit solid / inferred dashed, legend, scope controls).
4. `App — Batch operations open` (with Apply enabled per the V1 gap fix).
5. `App — Source control changes` (sections + ChangeActions + CommitBox).
6. `App — Source control diff` (markdown diff + DOCX document-aware diff).
7. `App — Worktrees` (list + create dialog states).
8. `App — Search` (modes, chips, results, batch hand-off).
9. `App — Markdown editor` (real editing surface).
10. `Dialogs` (Share, PDF merge order, Quick Look, Context pack, Missing resource,
    Undo conflict, Worktree create).

Plus a small `Design tokens` frame documenting the palette/typography above.

## 4. Delivered design

The `.pen` file was created in the pen.dev app and lives at **`D:\document-ide\ui.pen`**
(open in Pen; generated through the pencil MCP `execute` tool).

### Design system

- `NavBar (component)` — reusable top bar (product name, Canvas/List/Graph segmented switch,
  search / source-control / share icon buttons, Batch operations toggle). Screens reference it
  and override the active segment per screen.
- Variables: `bg`, `surface`, `border`, `text-primary`, `text-secondary`, `text-muted`,
  `accent`, `accent-weak`, `canvas`, `success`, `warning`, `danger`, `edge`, `shadow-soft`,
  `font-body` (Inter), `font-mono` (JetBrains Mono).

### Screens (1440 × 900, 3 × 4 grid)

| # | Frame | Covers |
| --- | --- | --- |
| 1 | `App — Canvas view` | document/folder nodes, explicit (solid) vs inferred (dashed) relation edges, relation legend, zoom controls, relations inspector, Editing-in-Word badge, missing/stale node states |
| 2 | `App — List view` | name/type filters, sortable columns, selection + select-all, type chips, state column (indexed / stale / missing), footer selection summary |
| 3 | `App — Graph view` | bounded graph scope panel, explicit vs inferred edges, focus node, missing targets, scope depth stepper |
| 4 | `App — Batch operations` | preview-ready status, plan expiry timer, preview progress, recursive review tree with per-file/per-occurrence checkboxes, before/after occurrence rows, missing-file exclusion, Apply (enabled) / Cancel / History |
| 5 | `App — Source control` | staged / changes / untracked / conflicts sections, stage + unstage actions, branch + ahead/behind, worktree count, commit box with `Commit staged` vs distinct `Commit all` |
| 6 | `App — DOCX document diff` | document-aware change report (text / table / formatting / binary classes), read-only note that deep DOCX editing stays in Word |
| 7 | `App — Worktrees` | worktree table (path / branch / state / actions), dirty worktree blocked state, create-worktree dialog with base ref, new-vs-existing branch choice |
| 8 | `App — Search` | query syntax, exact/fuzzy/semantic mode switch, removable metadata filter chips, result list with highlighted hits, send-to-batch hand-off |
| 9 | `App — Markdown editor` | source-first editor with toolbar, line numbers, wikilinks, save state, outline/backlinks panel, "plain .md" note |
| 10 | `App — History & undo` | history storage usage, operation entries with per-file outcomes, undo affordance, blocked/partial outcomes |
| 11 | `App — Share manager` | live share status + expiry, link copy/regenerate, permissions, members with roles, add member, lifecycle (stop / revoke) |
| 12 | `Dialogs & overlays` | Create share, Quick Look, Context pack, Missing resource, Undo review, Merge document order + batch export progress |

### Verification loop used

Screens were rendered at full resolution with `Export([frameId], "png", <dir>, {scale:1})`
and inspected; layout problems found this way (nav child order, legend overflow, table column
collapse, badge overflow, dialog footer overflow) were fixed and re-exported.

### Notes / caveats

- Two surfaces are drawn in their **intended post-fix state** rather than the pre-V1 state:
  the graph view (G1 placeholder) and the batch panel Apply button (G2 disabled in product).
- Naming/ids in the design are illustrative; the visual system (slate palette, `text-xs`
  control density, `rounded-md` controls, bordered white panels) mirrors the shipped Tailwind
  classes in `src/DocumentIDE.Web/src/**`.
