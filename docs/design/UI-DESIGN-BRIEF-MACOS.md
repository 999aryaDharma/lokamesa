# Document IDE — macOS-oriented design series (design of record)

Status: **complete** (12 of 12 screens rendered, 2026-10-17; canvas/graph node objects revised 2026-10-18) · Source file: `D:\document-ide\ui.pen` (git-ignored, opened in the Pen app)
Exports: `docs/design/screens-mac/*.png` (full resolution, 1440×900 per screen)

## Direction (decided 2026-10-17)

The `.pen` file is the **design of record**. The shipped V1 code is *not* the reference for this
series — the code will be adapted to this design later, through a proper spec → sprint change
(AGENTS.md source-of-truth order still applies: a UI overhaul needs a spec/sprint, not an
ad-hoc edit while working another sprint).

Chosen idiom: **macOS three-pane app** — unified toolbar + left source-list sidebar + content +
right inspector (Xcode / Pages / Finder-like). Rejected alternatives: "visual language only"
(keep the old top-nav layout) and "full window chrome + collapsible sidebar".

## Core layout metrics (all screens identical)

| Region | Size | Notes |
| --- | --- | --- |
| Window | 1440 × 900 | `clip: true`, vertical layout |
| Toolbar | full × 52 | traffic lights, sidebar toggle, view switch, search field, 4 action icons |
| Sidebar (source list) | 236 | sections `WORKSPACE` / `FOLDER SOURCES` / `LIBRARY`, 28px rows |
| Content | remaining (924) | content header 44 + surface |
| Inspector | 280 | right dock, `$mac-inspector` fill |

## Tokens added for this series (`mac-*`)

| Token | Value | Use |
| --- | --- | --- |
| `mac-toolbar` | `#F7F7F8` | unified toolbar |
| `mac-sidebar` | `#F0F0F2` | source-list sidebar |
| `mac-inspector` | `#F5F5F7` | right inspector |
| `mac-content` / `mac-fill-2` | `#FFFFFF` | content + cards/controls |
| `mac-canvas` | `#FBFBFD` | canvas / graph surface |
| `mac-hairline` | `#00000014` | 1px separators (inner stroke) |
| `mac-hairline-strong` | `#D9D9DE` | control borders |
| `mac-text` / `mac-text-2` / `mac-text-3` | `#1D1D1F` / `#6E6E73` / `#8E8E93` | primary / secondary / tertiary |
| `mac-accent` / `mac-accent-weak` | `#007AFF` / `#E7F0FF` | system blue + selection |
| `mac-chip` | `#E6E6E9` | segmented control track |
| `mac-success` / `mac-warning` / `mac-danger` | `#34C759` / `#FF9F0A` / `#FF3B30` | status |

Typography: **Inter** (stand-in for SF Pro — SF is not licensable as a web font); monospace
**JetBrains Mono** for diff gutter/code. Density stays small (`10–15px`) to match the product's
existing `text-xs` feel.

## Canvas node objects — folders and files are *objects*, not boxes (revised 2026-10-18)

Owner directive: the canvas must not draw folders and files as look-alike rounded boxes that
differ only by a small type label. They must be **recognisable objects** so the two are
distinguishable at a glance. Chosen idiom: **macOS Finder icon view**.

Applied to every place a folder or file is shown (owner scope decision):

| Surface | Treatment |
| --- | --- |
| Canvas (screen 01) | **Folder object** and **file object** as the node itself; filename label centred underneath; no card border |
| Graph (screen 03) | node row keeps its tree geometry but the leading dot is a **folder / file icon** (tree layout depends on the node bounds, so the object does not replace the row) |
| List (02), sidebar `FOLDER SOURCES`, Source control (05) change rows, inspector reference cards | **small type icon** per row — which is the Finder convention for lists (icon view on spatial surfaces, small icons in lists) |

### Folder object

- folder body `56×48`, `cornerRadius 7`, fill `#3E8EF7`, outer shadow `#0000001F` (0/2/6)
- folder front panel `56×36` at `y+12`, `cornerRadius 7`, fill `#7CB6FF` (the back peeking above the front creates the tab)
- label: Inter 12, weight 500, `$mac-text`, `textAlign: center`, width 140

### File object

- sheet `52×66`, `cornerRadius 5`, fill `$mac-fill-2`, stroke `$mac-hairline-strong` 1, outer shadow `#0000001F` (0/2/6)
- type accent bar `50×7` at the top, `cornerRadius [4,4,0,0]`, fill = **type colour**: `.docx` → `$mac-accent` (#007AFF), `.md` → `$mac-success` (#34C759)
- folded corner `14×14`, `cornerRadius [0,5,0,0]`, fill `$mac-chip`
- three content lines (36/29/34 × 3, radius 1.5) in `$mac-chip`
- label: as above

Object frame is `140×122` in both cases (icon area on top, label beneath).

**Implementation note for code:** this is a change of *node shape*, not of behaviour — the
canvas object model (`CanvasObject.type: 'folder' | 'document'`) already distinguishes the two,
so no API/contract change is implied by the shape itself.

### Folder interaction (owner directive, 2026-10-18)

Reference the owner gave: `uselayouts.com` "Folder Interaction" — a folder that opens on click and
lets its contents fan out with a spring. Reproduced here as a *behaviour* (not copied code):

- **Closed** (default): the folder object as drawn above.
- **Open** (click): the folder front tilts back (`rotateX ≈ -40°`, origin bottom) and the documents
  **inside that folder** fan out — three or more pages, rotated `≈ -8° / 0° / +9°` and translated
  up (`y ≈ -55 … -75`), spring `duration 0.5-0.6`, `bounce 0.12-0.2`, `stiffness 160-190`,
  `damping 21-24`. Click again closes.
- **What fans out is real**: the pages are the documents that actually belong to that folder,
  derived from existing path containment (`groupDocumentsByFolder` / `isWithinFolder`) — not
  decorative placeholders.
- **Drag-out = the existing "release" semantic** (CAN-04): it surfaces the referenced document as
  its own canvas object. It is metadata only — *canvas drag never physically moves a file*.
- Screen 01 shows the **open** state (frozen) as the interaction reference; the closed state is
  the same object without the fanned pages.

Animation library: **`motion`** (owner decision — first runtime UI dependency; the repo otherwise
ships only `react` + `react-dom`).

### Faint page-1 preview on a file object (owner directive, 2026-10-18)

The file object's sheet should show a **faint preview of the document's real content**, not the
current simulated grey lines.

- Source of truth, by type:
  - `.md` → the real source, already served by `GET /api/documents/{documentId}/markdown`.
  - `.docx` → the real extracted structure/text, served by a **new read-only route** built on the
    existing `DocxStructureParser` (see sprint `UI-02`). Until that route exists, the DOCX sheet
    falls back to the current low-detail layout.
- Rendering: faint (low opacity) and clipped to the sheet face so the filename label stays
  legible; it must read as *content*, not as decoration.
- **Honest limit:** this is a *content-derived* preview, **not** a rendered page image. A true
  page-1 raster needs the Word → temp PDF → page-image pipeline, which exists in **no** shipped
  host (the same missing pipeline that blocks `QuickLook`/WORD-04). That pipeline is a separate
  later sprint; when it lands it feeds the existing `thumbnailUrl` seam, so the node needs no
  redesign.
- Code seam already present: `DocumentNode` accepts `thumbnailUrl` / `thumbnailContent` /
  `thumbnailState` (`'loading' | 'ready' | 'stale' | 'missing' | 'failed'`) and already has the
  `LowDetailPaperLayout` fallback + `document-low-detail-fallback` testid. Nothing presently
  populates them with real data — that is the gap this directive closes.

## macOS patterns used

- **Unified toolbar**: traffic lights, sidebar toggle, segmented control (`Canvas | List | Graph`),
  trailing search field + icon buttons (Shares, Batch, Inspector, More).
- **Source list sidebar**: uppercase section headers (10px, `letterSpacing 0.06`), 28px rows,
  accent-weak selection, trailing count badges.
- **Right inspector**: title row + key/value rows + reference cards.
- **Sheets**: modal work (Batch operations) is a centred sheet with a scrim, shadow, header,
  scroll body and a footer with secondary + primary actions.
- **Segmented control**, **search field**, **depth stepper**, **push buttons** (secondary = white +
  border, primary = accent) as reusable inline patterns.

## Screens delivered

| # | Screen | Export | Highlights |
| --- | --- | --- | --- |
| 01 | Canvas view | `screens-mac/01-canvas.png` | **folder/file objects** (Finder icon-view: folder object, file object with type-coloured accent + folded corner, filename beneath — no card box), explicit/inferred edge hint, relations legend, floating zoom pill, relations inspector |
| 02 | List view | `screens-mac/02-list.png` | filter bar (name field, type filter, selection counter), table `NAME/TYPE/STATUS/MODIFIED` with status dots + selection checkboxes, selection inspector |
| 03 | Graph view | `screens-mac/03-graph.png` | scope bar (Workspace/Folder/Selection, depth stepper, include-inferred), orthogonal relation tree with **folder/file icons** on each node, 3 edge classes (explicit / inferred / missing), edge legend |
| 04 | Batch operations | `screens-mac/04-batch-operations.png` | modal sheet: preview status, expiry warning, progress bar, review tree with per-occurrence before/after, missing-file exclusion, `Apply 34 changes` primary |
| 05 | Source control | `screens-mac/05-source-control.png` | sidebar change groups (Staged/Changes/Untracked/Conflicts), unified diff with typed add/del rows, commit bar with message field + `Commit all` / `Commit staged` |

| 06 | DOCX document-aware diff | `screens-mac/06-docx-diff.png` | read-only document report: change-class chips (`Changed`/`Moved`/`Inserted`/`Deleted`), paragraph-level before → after blocks, tables/formatting summaries, inspector document metadata (`Open in Word` primary) |
| 07 | Worktrees | `screens-mac/07-worktrees.png` | `PATH/BRANCH/STATE` table with per-row actions, `+ New worktree`, create sheet whose ref input accepts an **existing** branch/tag/commit only, inspector repository summary |
| 08 | Search | `screens-mac/08-search.png` | active toolbar search, result list with highlighted match snippets beside a preview pane, inspector filters with `exact/fuzzy/semantic` segmented control + metadata chips |
| 09 | Markdown editor | `screens-mac/09-markdown-editor.png` | editor with line gutter + live preview, sidebar document outline, inspector frontmatter/metadata + backlinks |
| 10 | History & undo | `screens-mac/10-history-undo.png` | operation list with per-operation outcome chips (`Applied`/`Undone`/`Failed`), inspector operation detail + `History storage: x of y (pct%)` meter |
| 11 | Share manager | `screens-mac/11-share-manager.png` | share list + selected share detail (link, expiry, rate limit), statuses limited to `Active`/`Stopped`/`Tunnel error`, actions `Copy link`/`Stop`/`Revoke`/`Regenerate link`/`Add member`, members table **with no roles**, permissions inspector |
| 12 | Dialogs & overlays | `screens-mac/12-dialogs.png` | dimmed window with six reviewed-plan overlays: Quick Look, Build context pack, Missing resource, Undo N operations?, Merge order, PDF collision (`Overwrite`/`Rename`/`Skip`) |

Frames in `ui.pen` for this series are named `App — <Screen> (macOS)` (plus `Dialogs & overlays
(macOS)`) and sit in one row at `y = -160`, `x = 4670 … 21510`; the v1 series is untouched.

## Known gaps / caveats in this series

- Icons available to the Pen sandbox are a subset of Lucide; a few concepts (expiry clock, close ✕,
  warning triangle) are drawn as text glyphs (`⚠`, `✕`) instead of icons.
- Edge styling: pen.dev has no dashed stroke, so inferred edges are distinguished by **colour +
  legend** rather than dash pattern. If the implementation needs dashes, that is a code-side
  decision this design does not settle.
- Screens 01–05 use the **post-V1-fix** surface set (submit-on-enter search field, graph envelope,
  owner share lifecycle, commit box) — i.e. they assume the V1 gap-fix work landed.
- Screen 12 shows the six overlays together on one dimmed window (2×3 grid) rather than six separate
  windows; the sheet behind the scrim is the base frame's dimmed canvas, used for depth only. The
  sidebar therefore has no highlighted nav row — overlays are global, and a global overlay must not
  claim ownership of one section.
- Nothing here is wired to the sprint board; a UI overhaul sprint must be authored before any code
  change follows this design.

## Pen MCP operational notes (for future sessions)

- `.pen` files are only reachable through the pencil MCP `execute` tool; run snippets with
  `tools.pencil.execute({ filePath, input })`. `Insert`/`Update`/`Copy`/`Delete`/`Get`/`Export`/
  `FindEmptySpace`/`SetVariables` are the sandbox globals; `Print` writes to the tool output.
- Nodes created by `Insert` in the same call often export **blank**; `Copy`ing the finished frame in
  a later call and deleting the original renders correctly. Exports/`TakeScreenshot` taken in the
  same call as a change are stale — always export in a following call.
- After changing a frame's `layout` from `vertical` to `none`, children coordinates can appear
  offset; give the surface explicit `width`/`height` numbers to stabilise it.
- `Number.prototype.toFixed` is unavailable in the sandbox (`Math.round` instead). `rotation` on
  rectangles did not behave as documented — use axis-aligned connectors or SVG `path` nodes instead.
- Exports are named by node id (`<nodeId>.png`); rename after exporting.
