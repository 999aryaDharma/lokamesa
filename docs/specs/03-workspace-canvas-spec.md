# 03 — Workspace & Canvas Interaction Specification

**Project:** Document IDE  
**Status:** UX/behavior baseline  
**Depends on:** `01-system-architecture.md`, `02-domain-data-model.md`

---

## 1. Purpose

This document specifies named workspaces, the clean white canvas, folder/file object behavior, assisted freeform layout, selection, grouping, collections, preview interactions, and persistence rules.

---

## 2. Workspace Model

A user may create named workspaces such as:

```text
Administrasi
Indodax Bot
UAS Keamanan Siber
Project Freelance
```

A workspace references local folder sources without importing files.

Each workspace persists its own:

- folder source list,
- canvas positions,
- groups,
- collections,
- graph layout,
- last active view,
- viewport.

---

## 3. Canvas Visual Philosophy

The canvas MUST NOT look like a dashboard.

Default visual:

- large white surface,
- minimal chrome,
- objects placed directly on canvas,
- left sidebar for tools/navigation,
- right inspector only when selection requires it.

Avoid:

- statistic cards,
- heavy colored panels,
- permanently expanded metadata blocks.

---

## 4. Folder Object

Folder object displays:

- folder icon/illustration,
- exact folder display name,
- number of supported files in source.

Example:

```text
[folder illustration]
BA Agustus
24 files
```

File count should reflect supported `.docx` and `.md` content, with nested count policy made explicit in UI if recursive.

---

## 5. File Object

File object appears as a paper sheet.

Required:

- exact filename above,
- thumbnail derived from document,
- type visually inferable,
- selected state,
- optional small status marker.

DOCX:

- first-page low-detail recognizable thumbnail.

Markdown:

- rendered-paper preview or text-layout thumbnail.

Thumbnail content should not be fully readable at normal canvas scale.

---

## 6. Folder Single Click

Single-click opens lightweight picker/popover.

Capabilities:

- list supported files,
- search within folder picker,
- multi-select,
- `Open selected`,
- `Open all`.

Do not eject all files from a single click.

---

## 7. Folder Double Click

Double-click:

```text
Open all
```

This is intentional fast-path behavior.

If the folder has an unusually large supported-file count, implementation MAY show a lightweight confirmation while preserving the intended shortcut. Typical user folders contain tens, not hundreds, of files.

---

## 8. New File Placement

When files are released from a folder:

1. origin anchor = source folder object,
2. find nearby free region,
3. create compact grid/fan,
4. avoid overlap,
5. minimally reflow nearby auto-positioned objects,
6. preserve manually positioned/pinned objects.

---

## 9. Position Modes

### Auto

System may reposition locally.

### Manual

Set once user drags object.

System should strongly avoid moving it.

### Pinned

System MUST NOT move it automatically.

---

## 10. Local Reflow Algorithm Requirements

The layout engine SHOULD optimize a placement score such as:

```text
score =
  collision_penalty
+ distance_from_origin_penalty
+ movement_of_existing_manual_objects_penalty
+ group_overlap_penalty
+ alignment_penalty
+ viewport_overflow_penalty
```

Hard constraints:

- no overlap with pinned objects,
- do not move pinned objects,
- do not globally reshuffle entire canvas for local insertions.

Soft constraints:

- stay close to source folder for first placement,
- align with nearby row/column rhythm,
- keep reasonable whitespace.

---

## 11. Spatial Memory

Spatial memory is a first-class requirement.

If a file has been manually positioned at a location:

- retain coordinate across app restart,
- retain coordinate across rename,
- retain coordinate across physical move where identity is preserved,
- when hidden/closed and reopened, prefer restoring same region.

---

## 12. Tidy Selection

`Tidy selection`:

- only affects selected objects,
- preserves approximate region,
- resolves overlap,
- improves spacing,
- preserves object order when practical,
- does not move pinned objects.

---

## 13. Tidy Canvas

`Tidy canvas`:

- operates on unpinned objects,
- preserves broad spatial regions,
- preserves groups,
- does not flatten everything into one strict grid,
- resolves accidental overlap/gaps.

Mental model:

> tidy a desk, not rebuild the room.

---

## 14. Multi-Select

Required interactions:

```text
Ctrl + click
Shift + click where range semantics apply
drag selection rectangle
Ctrl + A in contextual result set
```

Multi-selection supports:

- virtual group assignment,
- collection assignment,
- batch operation,
- export,
- multi-object move.

---

## 15. Multi-Drag

Dragging multiple selected documents:

- show ghost bounding region,
- preserve relative positions during drag if already arranged,
- if dropped from picker as a new set, auto-pack into compact arrangement,
- never stack all documents at one coordinate.

---

## 16. Folder Origin vs File Freedom

After a file is released:

- it can be dragged anywhere,
- the source folder remains metadata/breadcrumb only,
- no visual tether is required by default,
- the file is not automatically pulled back toward origin.

---

## 17. Virtual Grouping

Groups are visual/workflow organization.

Dragging a document into a manual group:

- changes virtual membership,
- does not move physical file.

A file MAY exist in multiple virtual groups.

---

## 18. Nested Groups

Maximum supported nesting:

```text
3 levels
```

Example:

```text
Project
└── Month
    └── Status
```

UI must prevent deeper nesting.

---

## 19. Smart Groups

Smart groups are rule-driven.

Examples:

```text
paper != A4
font != "Times New Roman"
margin == mixed
```

Manual drop into a smart group is rejected with explanation.

---

## 20. Collections

Collections are persistent document sets independent from canvas grouping.

Use cases:

- BA final set,
- context pack source,
- ordered PDF merge source,
- project-specific cross-folder set.

Collection membership does not affect canvas position.

---

## 21. Main View Switch

Required:

```text
Canvas | List | Graph
```

Switching views MUST NOT destroy current selection context unnecessarily.

---

## 22. List View

List view should support:

- sorting,
- filtering,
- bulk selection,
- metadata columns,
- direct entry to batch operations.

Canvas position is irrelevant in list view.

---

## 23. File Single Click

Single click:

- select file,
- show inspector.

Do not open Word.

---

## 24. Space / Quick Look

`Space` or Preview action:

- opens large read-only preview,
- supports scrolling,
- highlights active search terms,
- dismiss with Escape.

---

## 25. Double Click

DOCX:

```text
open Microsoft Word
```

Markdown:

```text
open internal Markdown editor
```

---

## 26. Inspector

Right-side inspector appears only when useful.

DOCX fields:

- path (local work mode only),
- modified time,
- size,
- page/section/table/image counts,
- paper,
- margins,
- primary font/size,
- headings,
- header/footer state.

Markdown:

- headings,
- frontmatter,
- outbound links,
- backlinks,
- semantic relations,
- modified time.

---

## 27. Search on Canvas

While global search is active:

- preserve true canvas coordinates,
- matching objects emphasize,
- nonmatching objects may fade/hide,
- ending search restores visual state exactly.

Search filtering MUST NOT permanently trigger canvas reflow.

---

## 28. Sidebar

Keep sidebar restrained.

Recommended sections:

```text
Workspace
Canvas
Search
Groups
Collections
Batch Operations
History
Shares
Settings
```

Do not duplicate every command as a permanent button; rely on command palette/context menus.

---

## 29. Command Palette

Required in V1.

Recommended command categories:

- navigation,
- search,
- document actions,
- workspace actions,
- batch operations,
- graph actions,
- PDF,
- share.

---

## 30. Context Menus

File context menu may include:

```text
Preview
Open in Word / Open Markdown
Reveal in Explorer
Add to Group
Add to Collection
Batch Operation
Export PDF
Rename
Move physical file...
Duplicate
Delete to Recycle Bin
History
```

Folder context menu:

```text
Open selected...
Open all
Search in folder
Batch operation in folder
Reveal in Explorer
Rename physical folder
Detach from workspace
```

---

## 31. Physical Move Warning

Canvas move and physical move must be visually distinct.

Use explicit wording:

```text
Move physical file...
```

Never use ambiguous `Move` if it may alter filesystem path.

---

## 32. Empty / Missing States

Missing document:

```text
[paper placeholder]
PRD.md
⚠ File missing
```

Actions:

- Locate
- Remove reference

Missing folder:

```text
[folder placeholder]
Source unavailable
```

Actions:

- Locate folder
- Remove source

---

## 33. Accessibility

Required:

- keyboard-navigable object selection,
- visible focus,
- semantic labels for folder/file objects,
- non-color-only state indication,
- reduced-motion option for reflow animations.

---

## 34. Acceptance Criteria

Canvas is acceptable when:

- normal workspace opens as a clean white canvas,
- folder single click shows picker,
- folder double click opens all,
- new documents appear near origin,
- manual files may move anywhere,
- local insertions do not globally reshuffle canvas,
- pinned objects never auto-move,
- closing/reopening preserves manual positions,
- drag does not physically move source files,
- search does not permanently alter layout.

---

# 35. Post-Audit Canvas Performance Requirements

Collision and placement MUST use a benchmarked spatial acceleration structure such as a uniform spatial hash/grid, R-tree, or quadtree. Do not use a steady-state algorithm that repeatedly scans every object for each candidate position.

Benchmark at minimum:

```text
100 objects
500 objects
1,000 objects
```

for open-all, drag, multi-drag, tidy, and local reflow. Existing spatial-memory rules remain higher priority than global re-layout.
