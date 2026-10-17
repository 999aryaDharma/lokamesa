# Document IDE — macOS-oriented design series (design of record)

Status: **in progress** (5 of 12 screens rendered) · Source file: `D:\document-ide\ui.pen` (git-ignored, opened in the Pen app)
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
| 01 | Canvas view | `screens-mac/01-canvas.png` | 4 document cards on canvas, explicit/inferred edge hint, relations legend, floating zoom pill, relations inspector |
| 02 | List view | `screens-mac/02-list.png` | filter bar (name field, type filter, selection counter), table `NAME/TYPE/STATUS/MODIFIED` with status dots + selection checkboxes, selection inspector |
| 03 | Graph view | `screens-mac/03-graph.png` | scope bar (Workspace/Folder/Selection, depth stepper, include-inferred), orthogonal relation tree, 3 edge classes (explicit / inferred / missing), edge legend |
| 04 | Batch operations | `screens-mac/04-batch-operations.png` | modal sheet: preview status, expiry warning, progress bar, review tree with per-occurrence before/after, missing-file exclusion, `Apply 34 changes` primary |
| 05 | Source control | `screens-mac/05-source-control.png` | sidebar change groups (Staged/Changes/Untracked/Conflicts), unified diff with typed add/del rows, commit bar with message field + `Commit all` / `Commit staged` |

## Screens still to build

06 DOCX document-aware diff · 07 Worktrees (+ create sheet) · 08 Search & inspector filters ·
09 Markdown editor (outline + metadata) · 10 History & undo (outcome list + storage meter) ·
11 Share manager (members + permissions) · 12 Dialogs & overlays (Quick Look, context pack,
missing resource, undo review, merge order, PDF collision).

## Known gaps / caveats in this series

- Icons available to the Pen sandbox are a subset of Lucide; a few concepts (expiry clock, close ✕,
  warning triangle) are drawn as text glyphs (`⚠`, `✕`) instead of icons.
- Edge styling: pen.dev has no dashed stroke, so inferred edges are distinguished by **colour +
  legend** rather than dash pattern. If the implementation needs dashes, that is a code-side
  decision this design does not settle.
- Screens 01–05 use the **post-V1-fix** surface set (submit-on-enter search field, graph envelope,
  owner share lifecycle, commit box) — i.e. they assume the V1 gap-fix work landed.
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
