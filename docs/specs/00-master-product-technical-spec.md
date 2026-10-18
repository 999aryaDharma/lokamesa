# Document IDE — Master Product & Technical Specification

**Status:** Design baseline / implementation-ready specification  
**Date:** 2026-08-07  
**Primary platform:** Windows desktop  
**Secondary surface:** Local web UI + read-only public sharing  
**Primary file formats:** `.docx`, `.md`  
**Final output format:** `.pdf`  
**Product working name:** **Document IDE**

---

# 1. Purpose of This Document

This document is the single source of truth for implementing **Document IDE**, a local-first Windows application for visually organizing, searching, inspecting, editing, transforming, and finalizing document-heavy work.

The application is intended to cover two primary user workflows inside one coherent product:

1. **Administrative document operations**
   - Managing BA / berita acara, reports, official letters, templates, recurring document batches, and similar office work.
   - Working primarily with Microsoft Word `.docx`.
   - Searching across many files.
   - Performing safe and strict batch changes.
   - Finalizing documents to PDF.

2. **Programmer / AI-agent knowledge work**
   - Managing product briefs, PRDs, technical specifications, ADRs, architecture documents, research notes, dataset specifications, implementation notes, and AI-agent context.
   - Working primarily with Markdown `.md`.
   - Navigating references, backlinks, graph relationships, and semantic connections.
   - Maintaining a visual knowledge workspace while keeping files portable and Git-friendly.

The product is **not** intended to replace Microsoft Word. Microsoft Word remains the authoritative rich DOCX editor and rendering engine. Document IDE provides the operating environment around Word.

---

# 2. Product Thesis

Document IDE should feel like a combination of ideas from:

- Figma
- Notion
- Obsidian
- Excalidraw
- Microsoft Word
- Windows Explorer
- a programmer IDE

However, it must not become a loose collection of copied features.

The core product thesis is:

> **A local-first Document IDE where folders and documents become searchable, visual, inspectable, refactorable objects while the original files remain the source of truth.**

The core workflow is:

```text
SEARCH
  ↓
SELECT
  ↓
INSPECT
  ↓
TRANSFORM
  ↓
VERIFY
  ↓
FINALIZE
```

The key differentiation is not merely displaying files on a canvas. The canvas is the operating surface for two high-value capabilities:

1. **Find and understand documents extremely quickly.**
2. **Perform safe, strict transformations across multiple documents.**

---

# 3. Non-Negotiable Product Principles

These requirements are locked and should not be casually changed by an implementation agent.

## 3.1 Local filesystem is the source of truth

The application must not require users to import files into a proprietary project database.

Example:

```text
D:\Kerjaan\BA\BA-NUSA-DUA.docx
D:\Projects\indodax_bot\docs\prd.md
```

These original files remain the real documents.

The application stores only derived/workspace state such as:

- search index,
- thumbnail cache,
- metadata cache,
- workspace layout,
- object positions,
- groups,
- collections,
- relations,
- history/recovery data,
- semantic embeddings,
- share metadata.

Uninstalling Document IDE must not make the user's DOCX or Markdown files unusable.

---

## 3.2 DOCX is edited by Microsoft Word

Document IDE must not attempt to build a full Microsoft Word replacement.

DOCX interaction model:

```text
Preview / inspect / search / batch operations
    → Document IDE

Deep document editing
    → Microsoft Word Desktop
```

Microsoft Word is expected to be installed for the full DOCX workflow.

Word must open as its own normal desktop window.

**Do not embed Word inside the Document IDE window using OLE/ActiveX/Win32 window hosting.**

Reasons:

- fragile focus behavior,
- keyboard shortcut conflicts,
- ribbon problems,
- modal dialog problems,
- DPI scaling issues,
- Office version compatibility,
- multiple-document complexity,
- crash coupling,
- long-term maintenance risk.

---

## 3.3 Markdown remains plain Markdown

Markdown files remain ordinary `.md` files.

The editor is source-first.

Required modes:

```text
Edit | Preview | Split
```

Do not make a Notion-like block database the source of truth.

The Markdown must remain:

- readable in any text editor,
- Git-friendly,
- diff-friendly,
- usable by AI coding agents,
- portable outside Document IDE.

---

## 3.4 Save means save to local storage

From the user's perspective:

```text
Save
→ local file changes immediately
```

Internally, writes must be safe and transactional.

Do not implement naive destructive overwrites.

---

## 3.5 Dragging on the canvas never means moving the physical file

Canvas positioning and filesystem positioning are separate concepts.

```text
Drag object on canvas
→ only visual/workspace organization changes
```

Physical filesystem operations must always be explicit:

- Move physical file
- Rename
- Delete
- Duplicate
- Create folder
- Create document

---

## 3.6 Batch operations are strict

The application must follow:

> User requests property X to change → only property X changes.

Example:

```text
Change font size:
11 pt → 12 pt
```

Must not also change:

- font family,
- paragraph spacing,
- styles,
- margins,
- alignment,
- unrelated OOXML.

There is **no default “normalize everything to template” workflow**.

---

## 3.7 Destructive operations must be deterministic

Global exploratory search may use:

- fuzzy matching,
- semantic search,
- ranking.

Search & Replace and other destructive batch operations must not.

For destructive operations:

- fuzzy matching OFF by default,
- semantic matching OFF,
- deterministic matching required,
- preview required before apply.

---

# 4. Product Scope

## 4.1 Supported source formats

### Required

- `.docx`
- `.md`

### Output-only

- `.pdf`

PDF is not an editable source format in this product.

---

## 4.2 Explicit non-goals for initial implementation

Do not expand initial scope into:

- XLSX editing
- PPTX editing
- PDF editing
- collaborative editing
- cloud-first document storage
- user account system
- mobile application
- plugin marketplace
- custom DOCX WYSIWYG editor
- embedded Microsoft Word
- unlimited group nesting
- AI chat injected into every screen
- proprietary Markdown storage
- automatic document normalization

These may be reconsidered only after the core product is stable.

---

# 5. Core Mental Model

The system must clearly separate these concepts.

## 5.1 Physical File

Real file on disk.

Example:

```text
D:\Kerjaan\BA\BA Januari.docx
```

---

## 5.2 Physical Folder

Real filesystem directory.

Example:

```text
D:\Kerjaan\BA
```

---

## 5.3 Workspace

A named working environment that references one or more local folders.

Examples:

```text
Administrasi
Indodax Bot
UAS Keamanan Siber
Project Freelance
```

A workspace stores:

- folder sources,
- canvas layout,
- positions,
- pins,
- groups,
- collections,
- graph layout,
- manual relations,
- current view,
- local UI preferences,
- history references.

The same physical folder may be referenced by multiple workspaces.

---

## 5.4 Group

A virtual organizational container inside a workspace.

A group does not move physical files.

Examples:

```text
Need Review
Final
August
Project Telkom
```

Groups may be nested, but nesting should remain intentionally shallow.

Recommended maximum:

```text
3 levels
```

Example:

```text
Project
└── Month
    └── Status
```

---

## 5.5 Smart Group

A rule-based virtual group.

Examples:

```text
paper != A4
font != "Times New Roman"
margin == mixed
```

Documents appear automatically when they satisfy a rule.

A Smart Group is not manually populated.

---

## 5.6 Collection

A persistent set of references that may span physical folders.

Example:

```text
Collection: BA Agustus Final

D:\BA\Site-A.docx
D:\BA\Site-B.docx
E:\Revisi\Site-C.docx
D:\Notes\agustus.md
```

No physical move occurs.

---

# 6. Workspace Lifecycle

## 6.1 Create workspace

User creates a named workspace.

Example:

```text
Administrasi
```

Then selects one or more local folder sources.

The UI should discourage opening an excessive number of source folders at once.

Recommended UX:

- soft warning after approximately 5 active folder sources,
- no unnecessarily low hard limit at the storage layer.

The actual expected scale is under 1,000 files per workspace.

---

## 6.2 Workspace persistence

Closing the application must preserve:

- folder positions,
- document positions,
- group positions,
- opened/collapsed state where useful,
- selected view,
- graph layout,
- manually pinned objects,
- collections,
- workspace relations.

Opening the same workspace later should restore the visual environment.

---

## 6.3 Storage location

Do not pollute user project folders with application metadata.

Store application state under an application-controlled path similar to:

```text
%LOCALAPPDATA%\DocumentIDE\
```

Example internal structure:

```text
DocumentIDE/
├── app.db
├── workspaces/
├── cache/
│   ├── thumbnails/
│   ├── previews/
│   └── semantic/
├── history/
├── shares/
└── logs/
```

The precise physical structure may evolve, but the user's working folders should remain clean.

---

# 7. Canvas UX

# 7.1 Canvas appearance

The home surface of a workspace is a large clean white canvas.

It should not feel like a dashboard full of cards and statistics.

Visual target:

```text
┌──────────────────────────────────────────────────────────────┐
│                                                              │
│       [Folder BA]                [Folder Surat]              │
│       24 files                   17 files                    │
│                                                              │
│                                                              │
│                    [Folder Templates]                        │
│                    9 files                                   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

The canvas should feel:

- spacious,
- quiet,
- visually clean,
- direct-manipulation oriented.

---

# 7.2 Folder visual object

Folder objects use a recognizable folder representation.

Required visible information:

- folder name,
- file count.

The visual may include small document-paper hints inside the folder illustration.

---

# 7.3 File visual object

A file appears as a paper-sheet object.

Visible information:

- exact filename above the paper,
- paper-like thumbnail,
- lightweight type indicator if required.

DOCX thumbnail:

- based primarily on first-page appearance,
- scaled down,
- slightly low-detail / intentionally not fully readable,
- recognizable by visual structure,
- content must not create excessive visual noise.

The thumbnail is for recognition, not full reading.

---

# 7.4 Folder opening behavior

## Single click

Single clicking a folder opens a lightweight file picker/popover.

Example:

```text
BA Agustus
──────────────────────

☑ BA Site A.docx
☐ BA Site B.docx
☑ BA Site C.docx
☐ catatan.md

[Open selected]   [Open all]
```

User can:

- select one file,
- multi-select files,
- open all.

## Double click

Double clicking a folder is a shortcut for:

```text
Open all
```

---

# 7.5 Initial placement of files released from a folder

When files are released from a folder into the canvas:

- their initial position must be near the source folder,
- they must be arranged cleanly,
- they must not overlap,
- they should initially form a compact grid/fan-like layout,
- the source folder remains visible.

After creation on the canvas, the file becomes independent and may be moved anywhere.

The folder remains only the physical origin reference.

---

# 7.6 Free movement after release

Once a file exists on the canvas:

```text
file may be dragged anywhere
```

The application must not continuously force it back near its origin folder.

---

# 7.7 Assisted freeform layout

The canvas is neither:

- strict grid layout,
- nor completely chaotic freeform.

Use **assisted freeform layout**.

Required behaviors:

- collision detection,
- collision avoidance,
- local reflow,
- light snapping,
- intelligent new-object placement,
- preservation of spatial memory,
- `Tidy selection`,
- `Tidy canvas`,
- pinning.

---

# 7.8 Position priority

Objects have effective placement priority.

### Pinned

User explicitly pins position.

Auto-layout must not move it.

### Manually placed

User has manually dragged the object.

Auto-layout should strongly prefer not moving it.

### Existing automatic placement

May move slightly if needed.

### New object

May be placed freely by the layout engine.

Priority:

```text
Pinned
  ↓
Manually placed
  ↓
Existing automatic
  ↓
New object
```

---

# 7.9 Local reflow only

When a new object appears, do not reorganize the entire canvas.

Required:

```text
new file appears
→ find nearby usable space
→ minimally shift local auto-positioned objects
```

Avoid:

```text
new file appears
→ entire canvas reorders
```

Spatial memory is a first-class UX requirement.

---

# 7.10 Reopening folder content

If files from a folder were previously released and manually positioned:

- remember their last canvas positions,
- restore them when practical.

If those positions are now occupied:

- find the nearest good alternative,
- avoid overlap,
- preserve approximate region.

---

# 7.11 Multi-select drag

When dragging multiple selected documents:

- show a ghost bounding box / grouped preview,
- drop as a compact packed arrangement,
- do not stack every file at exactly the cursor coordinate.

---

# 8. Main Views

Required view switcher:

```text
Canvas | List | Graph
```

All three views represent the same workspace dataset.

---

## 8.1 Canvas

Purpose:

- spatial organization,
- visual recognition,
- grouping,
- working-set arrangement.

---

## 8.2 List

Purpose:

- dense management,
- sorting,
- metadata inspection,
- large selection,
- batch workflow.

Expected useful columns:

- filename,
- type,
- physical folder,
- modified date,
- size,
- page count,
- paper size,
- primary font,
- main font size,
- margin summary,
- status.

---

## 8.3 Graph

Purpose:

- knowledge navigation,
- document relationships,
- Markdown backlinks,
- explicit references,
- semantic relations.

Graph is a separate main view, not permanently embedded into the primary canvas.

---

# 9. File Interaction Model

## 9.1 Single click

Single click:

```text
Select file
→ show right-side inspector
```

---

## 9.2 Space / Preview

Pressing `Space` or choosing Preview opens a large **Quick Look**.

Quick Look:

- read-only,
- visually larger than thumbnail,
- supports multi-page DOCX preview,
- supports Markdown rendered preview,
- allows search-match highlighting,
- allows scrolling.

---

## 9.3 Double click DOCX

```text
Double click .docx
→ open Microsoft Word Desktop
```

---

## 9.4 Double click Markdown

```text
Double click .md
→ open internal Markdown editor
```

---

# 10. Inspector

The inspector appears after selecting a document.

For DOCX, show useful information such as:

- filename,
- physical location,
- modified time,
- file size,
- page count,
- section count,
- table count,
- image count,
- paper size,
- orientation,
- margins,
- primary font,
- common font sizes,
- detected headings,
- header/footer presence,
- document warnings.

Actions:

- Open in Word
- Preview
- Reveal in Explorer
- Add to Group
- Add to Collection
- Export PDF
- Inspect metadata
- View history

For Markdown:

- filename,
- location,
- headings,
- frontmatter,
- outbound links,
- backlinks,
- related documents,
- file size,
- modified time.

---

# 11. DOCX Architecture

## 11.1 Source of truth

The `.docx` package itself remains authoritative.

Do not convert DOCX to HTML and regenerate entire documents after minor changes.

---

## 11.2 Minimal OOXML mutation

When performing strict batch edits:

```text
original DOCX
→ identify exact relevant OOXML structures
→ modify minimal required elements
→ preserve untouched package content
```

Do not rewrite unrelated formatting.

---

## 11.3 Recommended technical layer

Use the Open XML SDK or equivalent OOXML-aware package manipulation layer.

Its responsibilities:

- inspect DOCX package,
- extract text,
- extract metadata,
- inspect sections,
- inspect margins,
- inspect fonts,
- inspect tables,
- inspect headers/footers,
- apply strict mutations.

Microsoft Word COM/Interop should be reserved for operations where Word itself is necessary.

---

# 12. Microsoft Word Integration

Word is used for:

- deep DOCX editing,
- authoritative document rendering where required,
- PDF export,
- optional high-fidelity preview generation.

---

## 12.1 Open in Word

The application launches the user's original file in Microsoft Word.

Do not create an editable proprietary duplicate for normal editing.

---

## 12.2 Editing status

While a DOCX is open in Word, Document IDE should visibly indicate:

```text
Editing in Word
```

This can be inferred from:

- file lock state,
- Word process/document integration,
- temporary lock files,
- event observation.

Implementation must not rely on only one fragile signal.

---

## 12.3 Conflicting batch operation

If Document IDE is about to modify a document currently open in Word:

Do not silently overwrite.

Show safe options:

```text
Document is currently open in Word.

[Skip]
[Open Word]
[Retry after save]
```

A force-overwrite flow should not be a normal operation.

---

# 13. Safe Write Layer

Every application-originated file mutation must use a safe write pipeline.

Required logical workflow:

```text
Original
  ↓
Create temporary working copy
  ↓
Apply strict operation
  ↓
Validate result
  ↓
Create recovery snapshot
  ↓
Atomic replace
  ↓
Updated original path
```

On failure:

```text
Original must remain valid and unchanged.
```

---

## 13.1 Windows atomic replacement

Where practical, use platform-supported atomic replacement behavior.

Example conceptual mechanism:

```text
File.Replace(temp, original, backup)
```

Exact implementation must correctly handle:

- ACL preservation,
- file attributes,
- locks,
- antivirus interference,
- same-volume constraints.

---

# 14. File Synchronization & Reconciliation

The local filesystem always wins.

---

## 14.1 While app is running

Use filesystem watchers.

Detect:

- create,
- modify,
- rename,
- move where detectable,
- delete.

Pipeline:

```text
Filesystem event
→ debounce/coalesce
→ verify file state
→ update identity/path
→ re-index changed document
→ refresh metadata
→ invalidate/update thumbnail
→ update search results
→ update graph if needed
```

---

## 14.2 While app is closed

No real-time monitoring is possible.

When reopened:

```text
show cached workspace immediately
+
run filesystem reconciliation
```

This is the selected hybrid startup strategy.

---

## 14.3 Startup reconciliation

First perform a light scan using:

- path,
- file identity,
- size,
- last modified timestamp.

Only parse/re-index documents when necessary.

Do not fully parse all files on every startup.

---

## 14.4 File identity

Do not identify documents only by path.

On Windows/NTFS, prefer stable filesystem identity such as file ID where available.

Example conceptual record:

```text
DocumentId
FilesystemId
CurrentPath
LastKnownPath
ContentFingerprint
```

Rename should preserve:

- canvas position,
- groups,
- collections,
- graph relations,
- history references.

---

## 14.5 Move handling

If a file is moved within monitored sources and identity can be preserved:

```text
update path
→ preserve workspace identity
```

For cross-volume moves or identity loss:

- compare filename,
- size,
- timestamps,
- content fingerprint,
- last known location,
- possibly short-window operation correlation.

Never silently merge two uncertain identities.

---

## 14.6 Missing files

If a known file disappears:

Do not immediately erase workspace state.

Show:

```text
PRD.md
⚠ File missing

Last known location:
D:\Project\docs\PRD.md

[Locate file]
[Remove from workspace]
```

---

## 14.7 Missing folder source

If a folder path disappears:

```text
Folder source unavailable
```

Allow:

- Locate folder
- Remove source

Do not destroy the workspace layout immediately.

---

# 15. Search Engine

Search is a primary product subsystem.

Expected scale:

```text
< 1,000 documents per workspace
```

The architecture should optimize for:

- local use,
- fast indexing,
- real-time query feedback,
- low resource usage.

Do not introduce unnecessary distributed-search infrastructure.

---

# 16. Real-Time Search Model

Real-time search means:

## 16.1 Real-time query

Every keystroke queries a persistent local search index.

Do not reparse every DOCX during typing.

---

## 16.2 Real-time indexing

When a file changes:

```text
Word Ctrl+S
→ filesystem watcher
→ re-index only changed file
```

The search results should become current automatically.

---

# 17. Indexed Content

For DOCX, index as much useful structured content as practical:

- filename,
- path,
- body text,
- headings,
- tables,
- headers,
- footers,
- section metadata,
- page setup metadata,
- common formatting metadata.

For Markdown:

- filename,
- path,
- body,
- headings,
- frontmatter,
- links,
- wikilinks,
- code block metadata if useful.

---

# 18. Global Search Modes

Global exploratory search supports:

1. exact/lexical search,
2. fuzzy search,
3. metadata filtering,
4. optional semantic search.

---

## 18.1 Query syntax

Examples:

```text
nusa dua
```

```text
"nusa dua"
```

```text
folder:BA "nusa dua"
```

```text
type:docx font:"Arial"
```

```text
paper:Letter
```

```text
margin:mixed
```

```text
modified:this-week
```

```text
"Januari 2026" -template
```

The exact grammar may evolve, but power-user filtering is required.

---

## 18.2 Search ranking

Useful ranking signals may include:

1. filename exact match,
2. filename partial match,
3. heading match,
4. body match,
5. table match,
6. header/footer match,
7. fuzzy score,
8. semantic score when enabled.

---

# 19. Semantic Search

Semantic search is optional and must not replace deterministic search.

Default:

- local-first,
- local embeddings where practical.

Optional advanced configuration:

- user may provide external AI/API credentials,
- external AI is opt-in,
- core product remains fully usable without external API.

UI should clearly differentiate:

```text
Exact / Smart
```

or an equivalent mode distinction.

Do not mix semantic results invisibly into strict exact-search expectations.

---

# 20. Search as Dataset Definition

A major product pattern is:

```text
Search
→ Select results
→ Batch operation
```

Example:

```text
Search:
"NUSA-DUA-1"

17 matched documents
↓
Select all
↓
Batch Replace
↓
Preview
↓
Apply
```

Search is not only navigation. It is a way to define the target document set.

---

# 21. Batch Operations

Batch operations share a common execution framework.

Initial operation families:

- Search & Replace
- Change font
- Change font size
- Change margins
- Change paper size
- Change orientation
- Change alignment
- Rename files

Future operations may be added only if they obey strict-operation principles.

---

# 22. Batch Operation Pipeline

Every destructive batch operation must follow:

```text
Define target
  ↓
Scan
  ↓
Preview
  ↓
Review affected files/occurrences
  ↓
Apply
  ↓
Validate
  ↓
Persist
  ↓
Record history
```

No mass mutation should happen before showing scope and impact.

---

# 23. Batch Scope

Supported scopes:

```text
Current selection
Current folder
Current workspace
```

Rules:

- if user invokes from selected files, default to selection,
- if invoked from a folder, default to current folder,
- do not automatically expand to unrelated workspace folders.

---

# 24. Search & Replace

Search & Replace is a specialized strict batch operation.

---

## 24.1 Default scope

When launched on a folder:

```text
Scope: current folder
Include subfolders: ON by default
```

The UI must show actual recursive scope before apply.

Example:

```text
Scope:
D:\Kerjaan\BA
↳ 6 subfolders included

47 files scanned
13 matched
31 occurrences
```

---

## 24.2 Subfolder exclusions

Even though recursion is ON by default, user must be able to exclude selected branches.

Example:

```text
☑ Januari
☑ Februari
☑ Maret
☐ Archive
```

---

## 24.3 Matching rules

Default:

- exact/deterministic,
- fuzzy OFF,
- semantic OFF.

Optional advanced mode:

```text
Regex
```

Regex must be clearly labeled advanced.

---

## 24.4 Occurrence review

Before apply, show:

```text
13 files matched
31 occurrences

Body       19
Tables      7
Headers     3
Footers     2
```

Allow per-occurrence selection:

```text
☑ occurrence 1
☑ occurrence 2
☐ occurrence 3
```

---

## 24.5 DOCX run fragmentation

DOCX text may be split across multiple OOXML runs.

Example:

```xml
<w:r>NUSA-</w:r>
<w:r>DUA-1</w:r>
```

The user visually sees:

```text
NUSA-DUA-1
```

The search/replace engine must support logical matching across run boundaries.

Mutation must preserve formatting as much as possible.

Do not implement search & replace as raw XML string replacement.

---

# 25. History, Undo & Recovery

Retention:

```text
30 days
```

History is local.

---

## 25.1 Operation history

Example:

```text
Today 16:31

Search & Replace
13 files
31 replacements

[Inspect]
[Undo]
```

Other history items:

- margin changes,
- paper changes,
- rename operations,
- Markdown edits where supported,
- filesystem operations where reversible.

---

## 25.2 Snapshot strategy

Do not snapshot entire workspaces.

Snapshot only files changed by an operation.

---

## 25.3 Undo batch

If one batch changed 43 files:

```text
Undo batch
```

should restore all affected files from the operation snapshot, subject to conflict checks.

---

## 25.4 Undo conflict

If a file has changed externally since the recorded operation:

Do not blindly overwrite newer work.

Require conflict-aware recovery behavior.

---

# 26. Markdown Editor

Markdown is edited internally.

Required editing model:

```text
Edit | Preview | Split
```

---

## 26.1 Required Markdown capabilities

Support:

- headings,
- paragraphs,
- lists,
- tables,
- checkboxes,
- fenced code blocks,
- syntax highlighting,
- images,
- regular Markdown links,
- relative links,
- wikilinks,
- frontmatter,
- Mermaid,
- LaTeX/math rendering.

---

## 26.2 Frontmatter

Example:

```yaml
---
type: technical-spec
project: indodax-bot
status: active
depends_on:
  - architecture.md
  - ADR-003.md
tags:
  - trading
  - backend
---
```

Frontmatter should be queryable by search and graph subsystems.

---

## 26.3 Markdown save

Use safe write behavior:

```text
edit
→ temporary write
→ flush/validate
→ atomic replace
```

---

# 27. Knowledge Graph

Graph is especially important for programmer documentation.

---

## 27.1 Relation sources

Graph relations may originate from:

### Explicit

- Markdown links,
- relative links,
- wikilinks,
- frontmatter dependencies,
- manual relation created by user.

### Inferred

- local semantic similarity,
- optional AI-assisted relation suggestions.

---

## 27.2 Truthful graph semantics

The graph must not present inferred similarity as a factual relationship.

Recommended visual semantics:

```text
solid edge
= explicit relation
```

```text
dashed edge
= inferred / semantic relation
```

---

## 27.3 Backlinks

Selecting a document should expose:

```text
Referenced by
References
Potentially related
```

---

# 28. Context Packs

Context Packs are a planned knowledge-work feature.

Purpose:

- select a set of project documents,
- produce an AI-agent-ready combined context.

Example:

```text
Context Pack: Trading Strategy Engine

prd.md
technical-spec.md
ADR-003.md
dataset.md
```

Potential options:

- include backlinks,
- include explicit dependencies,
- include related docs,
- token estimate,
- export combined Markdown,
- copy combined Markdown.

This feature is valuable for AI coding workflows but is not required to block the earliest filesystem/search/canvas milestone.

---

# 29. Metadata as an Actionable Property

Metadata must not merely be displayed.

Example:

Clicking:

```text
Font: Arial
```

may become:

```text
font:"Arial"
```

filter.

Clicking:

```text
Margin: Mixed
```

may become:

```text
margin:mixed
```

This enables:

```text
Metadata
→ Filter
→ Selection
→ Strict operation
```

---

# 30. Filesystem Operations

Document IDE may perform explicit physical operations.

Required operations:

- Rename file
- Rename folder
- Move physical file
- Create folder
- Duplicate file
- Delete file
- Create Markdown
- Create DOCX from template

---

## 30.1 Delete behavior

Default deletion goes to:

```text
Windows Recycle Bin
```

Do not default to permanent delete.

---

## 30.2 Physical move safety

A physical move must be clearly differentiated from:

```text
Move on canvas
```

Use wording such as:

```text
Move physical file...
```

---

# 31. PDF Finalization

Microsoft Word is the preferred DOCX-to-PDF rendering engine for fidelity.

Required operations:

## 31.1 Single export

```text
one DOCX
→ one PDF
```

Default filename follows original document name.

---

## 31.2 Batch export

```text
multiple DOCX
→ separate PDFs
```

---

## 31.3 Merge export

```text
multiple selected DOCX
→ render each
→ merge into one PDF
```

User must explicitly define document order.

Do not infer merge order silently.

---

# 32. Command Palette

Command Palette is required in V1.

Recommended shortcut:

```text
Ctrl + K
```

or a similarly conflict-safe shortcut determined during implementation.

Commands may include:

```text
Search in folder
Open in Word
Preview
Batch replace
Export PDF
Create collection
Open graph
Reveal in Explorer
Tidy selection
Tidy canvas
Create Markdown
Create folder
```

The palette should reduce sidebar clutter and enable fast power-user workflows.

---

# 33. Keyboard UX

Recommended core shortcuts:

```text
Ctrl + P       quick file search
Ctrl + K       command palette
Space          Quick Look
Enter          primary selected action where appropriate
F2             rename
Delete         recycle selected physical item only after clear confirmation context
Ctrl + A       select relevant visible result set
Esc            close preview / cancel mode
```

Exact shortcuts must be reviewed for conflicts with embedded browser controls and Markdown editing.

---

# 34. Read-Only Sharing

The user does not want a paid cloud/server dependency.

Sharing uses the user's machine as the host.

Selected transport for V1:

```text
ngrok
```

Architecture:

```text
Document IDE
    ↓
Local read-only share server
    ↓
ngrok tunnel
    ↓
Public URL
    ↓
Read-only viewer
```

---

# 35. Share Access Model

Anyone with the link may view.

No user account is required.

However, access must use a high-entropy share token.

Example conceptual route:

```text
/share/<unguessable-token>
```

The ngrok domain itself is not the authorization boundary.

---

# 36. Share Permissions

Configurable per share:

```text
Allow original download   OFF by default
Allow PDF download        OFF by default
```

Viewer never receives edit rights.

Edit buttons should not merely be disabled; they should not be rendered in the read-only interface.

---

# 37. Share Modes

Two required modes:

## 37.1 Snapshot

Content is frozen at the moment of publication.

Subsequent local edits do not alter the snapshot.

Store snapshot data locally under application-controlled storage.

---

## 37.2 Live

Saved local changes refresh the shared read-only representation.

Flow:

```text
local save
→ watcher
→ index/preview update
→ viewer sees current version
```

---

# 38. Share Security Rules

Never expose physical local paths such as:

```text
D:\Kerjaan\Secret\file.docx
```

Use internal document IDs.

Bad:

```text
GET /file?path=D:\Secret\password.docx
```

Required model:

```text
GET /share/<token>/document/<document-id>
```

Server must verify:

1. share token,
2. document membership in share,
3. requested capability.

Prevent:

- path traversal,
- arbitrary local-file reads,
- directory enumeration,
- access to non-shared workspace content.

---

# 39. Share Lifetime

Because the user's computer hosts the content:

```text
app stopped
or
laptop offline
or
ngrok tunnel stopped
→ share unavailable
```

This is accepted behavior.

The application must clearly show share status:

```text
Share active
Share stopped
Tunnel unavailable
```

---

# 40. Local Web Architecture

The product may expose its UI through a local web service.

Primary recommended architecture concept:

```text
Windows Desktop Shell
        │
        ├── Web UI
        │
        └── Local Backend
              ├── filesystem
              ├── index
              ├── OpenXML
              ├── Word integration
              ├── history
              └── share server
```

This enables:

- Windows-native launcher/integration,
- web technology for rich canvas UI,
- optional browser access on localhost,
- shared frontend architecture.

---

# 41. Recommended Implementation Architecture

This section is a technical recommendation for the coding agent.

The exact versions should be pinned during implementation.

## 41.1 Windows host/backend

Recommended:

- C# / .NET
- desktop shell using WinUI 3 or WPF
- WebView2 for frontend hosting
- ASP.NET Core/Kestrel for local APIs where useful

Why:

- strong Windows filesystem support,
- strong COM/Word interoperability,
- good background service/task handling,
- simple SQLite integration,
- Windows-native installer ecosystem.

Alternative frameworks should require a clear justification.

---

## 41.2 Frontend

Recommended:

- React
- TypeScript
- Vite
- Tailwind CSS
- a dedicated canvas/graph rendering library selected after a proof-of-concept

The canvas subsystem must support:

- large coordinate space,
- drag,
- multi-select,
- collision awareness,
- viewport transforms,
- persisted positions,
- selection box,
- keyboard interaction.

Avoid choosing a graph/canvas library before testing:

- 100+ objects,
- pan/zoom,
- large thumbnails,
- local reflow,
- nested groups.

---

## 41.3 Local database

Recommended:

```text
SQLite
```

Responsibilities:

- workspaces,
- folder sources,
- document identities,
- paths,
- metadata,
- canvas positions,
- groups,
- collections,
- relations,
- history records,
- search index,
- share definitions.

---

## 41.4 Full-text search

Recommended baseline:

```text
SQLite FTS5
```

Because expected scale is under 1,000 files.

Add fuzzy matching at the application layer or via a suitable local extension.

Do not introduce Elasticsearch/OpenSearch for this scale.

---

## 41.5 Semantic search

Recommended architecture:

```text
EmbeddingProvider interface
├── LocalEmbeddingProvider
└── OptionalExternalEmbeddingProvider
```

Semantic vectors may use:

- a local vector index,
- SQLite-compatible vector extension,
- another embedded vector store.

Exact implementation requires benchmarking.

---

# 42. Core Domain Model

Recommended conceptual entities.

---

## 42.1 Workspace

```text
Workspace
- id
- name
- created_at
- updated_at
- active_view
- canvas_viewport
```

---

## 42.2 FolderSource

```text
FolderSource
- id
- workspace_id
- physical_path
- filesystem_identity
- display_name
- availability_status
- added_at
```

---

## 42.3 Document

```text
Document
- id
- filesystem_identity
- current_path
- last_known_path
- file_type
- filename
- size_bytes
- modified_at
- content_fingerprint
- index_status
- availability_status
```

---

## 42.4 WorkspaceDocument

Allows same physical document to appear differently across workspaces.

```text
WorkspaceDocument
- workspace_id
- document_id
- canvas_x
- canvas_y
- position_mode
- pinned
- visible_on_canvas
- last_canvas_x
- last_canvas_y
```

---

## 42.5 Group

```text
Group
- id
- workspace_id
- parent_group_id
- name
- group_type
- canvas_x
- canvas_y
- width
- height
```

Group type:

```text
manual
smart
```

---

## 42.6 GroupMember

```text
GroupMember
- group_id
- document_id
```

Smart groups may derive membership instead of persisting manual members.

---

## 42.7 Collection

```text
Collection
- id
- workspace_id
- name
- created_at
```

---

## 42.8 CollectionMember

```text
CollectionMember
- collection_id
- document_id
- sort_order
```

---

## 42.9 Relation

```text
Relation
- id
- workspace_id
- source_document_id
- target_document_id
- relation_type
- relation_origin
- confidence
```

Origins:

```text
explicit_link
frontmatter
manual
semantic
```

---

## 42.10 OperationHistory

```text
OperationHistory
- id
- workspace_id
- operation_type
- parameters
- created_at
- affected_file_count
- affected_occurrence_count
- undo_status
```

---

## 42.11 RecoverySnapshot

```text
RecoverySnapshot
- id
- operation_id
- document_id
- snapshot_path
- original_fingerprint
- post_operation_fingerprint
- expires_at
```

---

## 42.12 Share

```text
Share
- id
- workspace_id
- secret_token_hash
- mode
- allow_original_download
- allow_pdf_download
- created_at
- active
```

---

## 42.13 ShareMember

```text
ShareMember
- share_id
- document_id
- snapshot_reference
```

---

# 43. File Change State Machine

Recommended conceptual states:

```text
Indexed
Modified
Indexing
Ready
Missing
Locked
Conflict
Error
```

Example:

```text
Ready
  ↓ external save
Modified
  ↓ index job
Indexing
  ↓ success
Ready
```

---

# 44. Background Job Model

Use an internal local job queue for expensive work.

Job types may include:

- parse DOCX,
- parse Markdown,
- render thumbnail,
- render preview,
- generate embedding,
- update graph,
- export PDF,
- apply batch operation,
- reconciliation scan.

Jobs must support:

- cancellation,
- progress,
- retry where safe,
- deduplication,
- per-document serialization where required.

---

# 45. Thumbnail Pipeline

DOCX thumbnail fidelity does not need to equal Word editing fidelity.

Recommended staged approach:

## Fast thumbnail

Extract enough document structure to render a recognizable low-detail page preview.

Cache result.

## High-fidelity Quick Look

Generate lazily when user asks for preview.

Possible strategy:

```text
Word render/export to temporary PDF
→ render PDF pages as images
→ cache preview
```

Do not launch Word rendering for every file on every startup.

---

# 46. Quick Look Cache

Cache high-quality previews using a key derived from:

```text
document identity
+
content fingerprint
+
render version
```

When source changes:

```text
invalidate preview cache
```

---

# 47. Search Index Update Strategy

Use incremental indexing.

Initial import:

```text
scan source
→ queue parse
→ store index
```

Subsequent modifications:

```text
file watcher
→ debounce
→ fingerprint check
→ re-index only changed file
```

---

# 48. File Watcher Reliability

Windows filesystem watcher events can:

- duplicate,
- arrive in bursts,
- overflow,
- report sequences of rename/create/delete.

Therefore:

- debounce,
- coalesce events,
- verify actual filesystem state,
- periodically reconcile monitored sources,
- never trust a single watcher event as authoritative.

---

# 49. Search UI Behavior

Search results should update without leaving the workspace.

Possible behavior:

```text
normal canvas
→ user types query
→ non-matching objects fade/hide
→ matching documents become emphasized
```

For larger result sets, a dedicated search result presentation may be used.

Show snippets and match origin:

```text
Body
Table
Header
Footer
Filename
```

---

# 50. Canvas Search Interaction

When search is active:

- preserve original object positions,
- do not permanently reflow the workspace merely because results are filtered,
- exiting search restores the prior visual state.

---

# 51. Graph Layout Persistence

User-adjusted graph positions should be persisted independently from canvas positions.

Do not reuse the same coordinates for Canvas and Graph.

---

# 52. Smart Group Examples

Examples:

```text
Format Mismatch
paper != A4
OR font != "Times New Roman"
```

```text
Mixed Margin
margin == mixed
```

```text
Needs Review
tag == "review"
```

Smart Groups must make their rules inspectable.

---

# 53. Strict Formatting Operations

Examples of valid strict operations:

## Set Paper Size

Only mutate section page size.

## Set Margin

Only mutate requested section margin properties.

## Set Font

Only mutate targeted text/font references in selected scope.

## Set Font Size

Only mutate targeted size values.

Do not use a “format painter for entire document” model unless added as a separate explicit feature later.

---

# 54. Word Fidelity Testing

DOCX handling needs dedicated regression fixtures.

Maintain a corpus containing:

- simple paragraphs,
- multiple fonts,
- tables,
- merged cells,
- headers,
- footers,
- section breaks,
- page orientation changes,
- numbered lists,
- bullets,
- images,
- floating images,
- fields,
- hyperlinks,
- custom styles,
- mixed runs,
- complex search/replace cases.

Tests should verify:

```text
open
→ apply no-op / minimal edit
→ save
→ document remains valid
```

---

# 55. Batch Operation Failure Model

Batch operations must support partial operational failure without uncertainty.

Before changing documents:

- build operation plan,
- identify targets,
- validate lock state,
- prepare snapshots.

If one file fails during apply:

Preferred behavior:

- clearly report file-level success/failure,
- do not hide partial completion,
- allow undo of files already changed,
- optionally support all-or-nothing mode later.

For V1, prioritise transparent per-file result plus undo.

---

# 56. Operation Preview

Preview must include enough information to understand blast radius.

Examples:

```text
Files scanned
Files affected
Occurrences
Subfolders included
Headers affected
Footers affected
Tables affected
```

For formatting:

```text
Current
→ New
```

---

# 57. External Edit Conflict

Scenario:

```text
Document IDE prepares batch operation
↓
user edits file in Word
↓
file fingerprint changes
↓
Document IDE tries apply
```

Required:

- detect stale operation plan,
- block automatic mutation,
- rescan/recompute affected changes,
- require updated preview if semantics changed.

---

# 58. Export Safety

PDF export must not modify the DOCX unless explicitly required.

Output collision behavior should be explicit:

```text
overwrite
rename
skip
```

Do not silently replace unrelated PDFs.

---

# 59. Merge PDF

Merge flow:

```text
Selected documents
→ user orders documents
→ each DOCX rendered to PDF
→ PDFs merged locally
→ final output path selected
```

Store intermediate output in temporary application storage and clean it after completion.

---

# 60. Share Snapshot Storage

Snapshot share should never mutate original files.

Create a publish package containing only allowed content.

Possible contents:

- rendered preview,
- sanitized metadata,
- optional original if download explicitly allowed,
- Markdown rendered representation,
- graph data relevant to shared set.

---

# 61. Local Share Server Isolation

The public read-only server should be a restricted surface separate from internal privileged APIs.

Recommended separation:

```text
Internal local API
→ full local capabilities

Public share API
→ read-only, token-scoped capabilities only
```

Do not expose the same privileged endpoints through ngrok.

---

# 62. ngrok Integration

Wrap ngrok behind an abstraction.

```text
ShareTransport
└── NgrokTransport
```

Do not spread ngrok-specific logic throughout the application.

Required lifecycle:

```text
Start Share
→ ensure local share server
→ start ngrok endpoint
→ obtain public URL
→ combine with secret share path
→ display share link
```

Stop:

```text
Stop Share
→ deactivate share
→ stop/release tunnel where appropriate
```

The user may be required to configure their ngrok credentials according to ngrok requirements.

The product must not assume third-party free-tier policies will remain unchanged forever.

---

# 63. Security Requirements

Even though this is primarily a local app, security matters.

Required protections:

- path canonicalization,
- path traversal prevention,
- symlink/junction awareness,
- share scope validation,
- high-entropy share tokens,
- secrets stored securely,
- no plaintext external API keys where avoidable,
- local API restricted to loopback by default,
- CSRF/origin protections where relevant,
- public share endpoints read-only,
- file-type validation,
- operation audit logs,
- no arbitrary command execution from Markdown.

---

# 64. Markdown Security

Rendered Markdown must sanitize unsafe HTML where required.

Mermaid and math renderers must not create arbitrary local file access.

Do not automatically execute code fences.

---

# 65. Word Automation Safety

Word COM automation should be isolated behind a service/interface.

Example:

```text
IWordAutomationService
- OpenDocument
- ExportPdf
- RenderPreview
- DetectOpenDocument
```

Do not call COM directly throughout UI code.

Operations should be serialized where Word's automation model requires it.

Always clean up COM references/process state correctly.

---

# 66. Architecture Boundaries

Recommended backend modules:

```text
Workspace
Filesystem
DocumentIdentity
Indexing
Search
Docx
Markdown
Thumbnail
Preview
CanvasState
Graph
BatchOperations
History
WordAutomation
Pdf
Sharing
Semantic
Settings
```

Each module should expose a clear interface.

Avoid a giant “DocumentService” handling everything.

---

# 67. Frontend Feature Modules

Recommended:

```text
workspace/
canvas/
files/
folders/
search/
inspector/
quick-look/
markdown-editor/
graph/
batch-operations/
history/
share/
command-palette/
settings/
```

---

# 68. API Design Guidance

Use task-oriented APIs.

Good:

```text
POST /workspaces/{id}/batch-operations/preview
POST /workspaces/{id}/batch-operations/apply
GET  /documents/{id}/preview
GET  /documents/{id}/metadata
```

Avoid generic unsafe endpoints like:

```text
POST /filesystem/write-anything
```

---

# 69. Preview / Apply Separation

Batch APIs must separate:

```text
Preview operation
```

from:

```text
Apply operation
```

The apply request should reference a validated operation plan/version.

If file fingerprints changed since preview:

```text
reject stale plan
```

---

# 70. Search & Replace Operation Plan

Conceptual structure:

```text
OperationPlan
- id
- scope
- query
- replacement
- matching_mode
- recursive
- created_at
- files[]
```

Per file:

```text
FilePlan
- document_id
- expected_fingerprint
- matches[]
```

Per match:

```text
Match
- location_type
- context
- logical_offset
- structural_reference
- selected
```

---

# 71. Logging

Maintain local structured logs.

Log:

- indexing failures,
- Word automation errors,
- share tunnel failures,
- batch operation results,
- reconciliation anomalies.

Do not log full document contents unnecessarily.

---

# 72. Privacy

Core processing is local.

Default:

- no document telemetry,
- no document upload,
- no semantic API call.

External AI must be opt-in.

Public share sends content only because the user explicitly starts sharing through ngrok.

---

# 73. Performance Targets

Given expected workspace size <1,000 files:

## Search

Target:

- visible search response should feel immediate,
- local FTS query should generally complete within tens of milliseconds after index warmup.

## Canvas

Should remain smooth with tens to low hundreds of visible objects.

## Startup

- cached workspace appears quickly,
- reconciliation continues without blocking full UI.

## Re-index

- changed file re-indexed incrementally.

Exact numeric budgets should be measured in profiling tests rather than guessed.

---

# 74. Concurrency

Do not mutate the same physical document concurrently from multiple internal jobs.

Use document-level locks.

Allow unrelated documents to index concurrently within reasonable resource limits.

Word COM jobs may require stricter serialization.

---

# 75. Error UX

Errors should name the affected document and action.

Bad:

```text
Operation failed.
```

Good:

```text
BA-NUSA-DUA.docx could not be updated because it is currently locked by Microsoft Word.
```

For batch operations:

```text
42 succeeded
2 skipped
1 failed
```

with inspectable details.

---

# 76. Offline Behavior

All core functionality must work offline:

- workspaces,
- canvas,
- indexing,
- exact search,
- fuzzy search,
- DOCX inspection,
- Markdown editing,
- graph explicit relations,
- batch operations,
- history,
- Word integration,
- PDF export.

Internet is required only for:

- public ngrok sharing,
- optional external AI.

---

# 77. Application Modes

## Work Mode

Full local privileges.

Available only on user's machine.

Features:

- edit,
- batch,
- filesystem operations,
- Word launch,
- Markdown editing,
- PDF,
- share management.

## Share Mode

Public read-only UI.

Features:

- browse allowed shared content,
- search within share,
- view previews,
- view graph if included,
- download only if explicitly allowed.

No mutation.

---

# 78. Proposed Main Navigation

Left sidebar should remain restrained.

Possible items:

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

Do not fill the canvas with dashboard widgets.

---

# 79. Search Scope UX

Search should make scope visible.

Possible scopes:

```text
Workspace
Folder
Selection
Collection
```

Global exploratory search may default to workspace.

Destructive search must use explicit strict scope.

---

# 80. Document Lens / Quick Information

A document can be understood without opening Word.

Example inspector summary:

```text
Laporan Januari.docx

14 pages
2 sections
4 tables
7 images

Paper       A4
Margins     4/4/3/3
Main font   TNR 12

Headings:
- Pendahuluan
- Kondisi Site
- Hasil Pemeriksaan
- Kesimpulan
```

This is a core “understand documents quickly” feature.

---

# 81. AI Coding-Agent Compatibility

Markdown project documentation should be especially easy to export/use as coding-agent context.

Prioritize:

- clean source files,
- relative links,
- deterministic paths,
- frontmatter,
- explicit relations,
- context packs,
- token estimates where available.

Do not hide knowledge in a proprietary internal note database.

---

# 82. Acceptance Criteria — Core File Safety

Implementation is not acceptable unless:

- opening workspace never rewrites source documents,
- canvas drag never moves physical files,
- failed strict edit leaves original valid,
- batch preview occurs before batch apply,
- undo data exists before destructive mutation,
- deleting defaults to Recycle Bin,
- open-in-Word conflict is handled,
- share cannot read arbitrary local paths.

---

# 83. Acceptance Criteria — Workspace

Must support:

- create named workspace,
- add multiple local folder sources,
- persist canvas layout,
- reopen workspace,
- reconcile source changes,
- preserve renamed file identity where possible,
- show missing files/folders without destroying layout.

---

# 84. Acceptance Criteria — Canvas

Must support:

- white clean canvas,
- folder object with name/count,
- paper-style file thumbnail,
- folder picker on single click,
- open all on double click,
- new files initially near source folder,
- free movement after release,
- local collision avoidance,
- persisted manual positions,
- pinning,
- tidy selection/canvas.

---

# 85. Acceptance Criteria — DOCX

Must support:

- inspect metadata,
- index text,
- Quick Look,
- open in Word,
- strict safe mutation,
- detect/open-file conflict,
- PDF export,
- incremental re-index after Word save.

---

# 86. Acceptance Criteria — Markdown

Must support:

- source editing,
- preview,
- split mode,
- save to local file,
- headings,
- code,
- tables,
- links,
- wikilinks,
- frontmatter,
- Mermaid,
- math,
- graph participation.

---

# 87. Acceptance Criteria — Search

Must support:

- real-time indexed search,
- exact matching,
- fuzzy matching,
- metadata filters,
- search snippets,
- per-folder filtering,
- current index updated after file changes.

Semantic search may be staged after exact/fuzzy search, but architecture must allow it.

---

# 88. Acceptance Criteria — Search & Replace

Must support:

- current folder scope,
- subfolders included by default,
- branch exclusion,
- exact deterministic matching,
- regex advanced mode,
- DOCX logical matches across runs,
- preview counts,
- occurrence review,
- strict mutation,
- safe write,
- 30-day recovery record.

---

# 89. Acceptance Criteria — Graph

Must support:

- Canvas/List/Graph view switch,
- explicit links,
- backlinks,
- manual relations,
- graph position persistence.

Semantic dashed relations may follow in a later milestone.

---

# 90. Acceptance Criteria — Sharing

Must support:

- create Snapshot share,
- create Live share,
- start read-only local share server,
- expose through ngrok,
- anyone-with-link access,
- secret route token,
- configurable original download,
- configurable PDF download,
- no write APIs exposed,
- stop/revoke share.

---

# 91. Recommended Delivery Phases

This specification contains the full vision, but implementation should be phased.

---

## Phase 0 — Technical spikes

Before building the whole UI, prove:

1. reliable DOCX metadata extraction,
2. strict OOXML search/replace across runs,
3. Word open/export integration,
4. Windows file identity tracking,
5. filesystem watcher reconciliation,
6. canvas library performance,
7. local web + desktop shell architecture.

Deliver small test harnesses, not product UI.

---

## Phase 1 — Local workspace foundation

Implement:

- desktop shell,
- local backend,
- SQLite,
- workspace CRUD,
- folder sources,
- reconciliation,
- document identity,
- basic indexing,
- cached metadata.

---

## Phase 2 — Canvas

Implement:

- white canvas,
- folder objects,
- file paper thumbnails,
- folder picker,
- open selected/open all,
- drag,
- multi-select,
- persisted positions,
- collision avoidance,
- local reflow,
- pin,
- tidy actions.

---

## Phase 3 — Search

Implement:

- full-text indexing,
- exact search,
- metadata filters,
- fuzzy search,
- snippets,
- realtime incremental updates,
- search-driven selection.

---

## Phase 4 — DOCX document lens

Implement:

- inspector,
- metadata,
- Quick Look,
- Word launch,
- editing status,
- external-save re-index.

---

## Phase 5 — Strict batch operations

Start with:

1. Search & Replace,
2. Font size,
3. Font,
4. Margin,
5. Paper size.

Implement:

- preview plans,
- fingerprints,
- snapshots,
- atomic write,
- undo/history.

---

## Phase 6 — Markdown knowledge workflow

Implement:

- source editor,
- Preview/Split,
- frontmatter,
- links,
- wikilinks,
- backlinks,
- graph.

---

## Phase 7 — PDF finalization

Implement:

- single export,
- batch separate export,
- ordered merge.

---

## Phase 8 — Semantic search

Implement:

- local embedding provider,
- semantic search mode,
- semantic graph suggestions,
- dashed inferred relations.

---

## Phase 9 — Sharing

Implement:

- share packaging,
- Snapshot/Live,
- restricted read-only web server,
- ngrok transport,
- permissions,
- revoke.

---

## Phase 10 — Context Packs

Implement:

- selected document pack,
- dependency inclusion,
- backlink inclusion,
- token estimate,
- combined Markdown export.

---

# 92. Testing Strategy

Required test layers:

## Unit

- path handling,
- query parser,
- metadata parsing,
- OOXML mutation,
- graph extraction,
- operation planning,
- file identity logic.

## Integration

- SQLite/index,
- filesystem watcher,
- safe write,
- Word COM,
- PDF export,
- ngrok adapter mocked boundary.

## Golden DOCX regression

Maintain fixture corpus and expected structural outcomes.

## End-to-end

Example workflow:

```text
create workspace
→ attach folder
→ index
→ open folder on canvas
→ search
→ preview search/replace
→ apply
→ open in Word
→ save
→ re-index
→ export PDF
→ undo
```

---

# 93. Architecture Quality Rules for Coding Agents

AI coding agents implementing this system must follow these constraints:

1. Do not collapse all functionality into giant services.
2. Do not mutate physical files from UI components directly.
3. Do not bypass the safe-write layer.
4. Do not bypass operation preview for batch actions.
5. Do not use semantic search for destructive matching.
6. Do not identify files only by path when stable identity is available.
7. Do not expose internal filesystem APIs to public share mode.
8. Do not regenerate whole DOCX files for simple strict edits.
9. Do not treat application cache as source of truth.
10. Do not add cloud requirements to core workflows.
11. Do not build custom Word-style DOCX editing.
12. Do not change product scope without updating this specification.

---

# 94. Important Rejected Designs

The following ideas were explicitly rejected or deprioritized.

## Embedded Word

Rejected.

Use external Microsoft Word window.

## Full freeform chaos

Rejected.

Use assisted freeform.

## Full automatic grid

Rejected.

Preserve spatial freedom.

## Normalize-to-template batch edit

Rejected as core behavior.

Use strict operations only.

## Notion-style Markdown source of truth

Rejected.

Use plain Markdown source.

## Cloud/server dependency for core app

Rejected.

Local-first.

## Paid application infrastructure requirement

Rejected.

Core app should not require paid server/cloud infrastructure.

## Public share hosted on proprietary backend

Rejected.

Use user's machine + ngrok for V1.

---

# 95. Product Identity

The best mental model is:

> **Document IDE**

Programmer IDE analogy:

```text
Code IDE                Document IDE
────────────────────────────────────────
Files                   Folders/Documents
Search                  Global document search
Find References         Backlinks/References
Refactor                 Batch document operations
Code Inspection         Document metadata inspection
Editor                   Word / Markdown editor
Build                    Finalize to PDF
Workspace                Document Workspace
Symbols/Graph            Document Graph
```

This analogy should guide product decisions.

---

# 96. Primary User Stories

## Administrative

> I can open my Administrasi workspace, see several local folders visually, open selected BA documents onto the canvas, search across them instantly, inspect formatting inconsistencies, run a strict Search & Replace recursively inside one folder, preview every affected occurrence, apply safely, continue editing individual files in Microsoft Word, then export selected final files to PDFs.

## Programmer

> I can open a project workspace containing Markdown specifications, visually browse the docs, edit the Markdown source directly, navigate backlinks and graph relations, search the entire knowledge base, create collections, and assemble a context pack for my AI coding agent without moving or importing my files.

## Sharing

> I can select a collection of documents, start a read-only Snapshot or Live share, receive a public ngrok link, and let anyone with the link browse permitted content without exposing write capabilities or arbitrary files on my machine.

---

# 97. Definition of Done for Product Foundation

The initial product foundation can be considered coherent when:

- local filesystem remains authoritative,
- named workspaces work,
- canvas is visually clean,
- file/folder identity survives normal rename/move cases,
- search is real-time,
- DOCX opens in Word,
- Markdown edits internally,
- strict batch mutation is safe and previewable,
- history supports 30-day recovery,
- PDF output works,
- graph supports knowledge workflow,
- sharing is read-only and isolated.

---

# 98. Final Product Rules Summary

```text
SOURCE OF TRUTH
Local filesystem

SOURCE FORMATS
DOCX + Markdown

DOCX EDITOR
Microsoft Word Desktop

MARKDOWN EDITOR
Internal source-first editor

FINAL FORMAT
PDF

HOME
Large clean white canvas

FOLDER CLICK
Picker

FOLDER DOUBLE CLICK
Open all files

NEW FILE POSITION
Near source folder

AFTER RELEASE
Free canvas movement

CANVAS LAYOUT
Assisted freeform

VIEWS
Canvas | List | Graph

SEARCH
Realtime local indexed

SEARCH MODES
Exact + fuzzy + metadata + optional semantic

BATCH EDIT
Strict only

SEARCH & REPLACE
Exact deterministic by default
Recursive subfolders ON
Regex advanced
Preview mandatory

SAVE
Direct to local through safe-write layer

HISTORY
30 days

FILESYSTEM CHANGES
Live watcher + startup reconciliation

PDF
Single + batch + ordered merge

COMMAND PALETTE
Required in V1

SEMANTIC AI
Local default, external optional

SHARING
Read-only
Anyone with link
Snapshot or Live
ngrok
Downloads configurable, default OFF

CLOUD DEPENDENCY
None for core app

TARGET SCALE
Under 1,000 documents per workspace
```

---

# 99. Next Engineering Documents Recommended

After this master specification is approved, create the following implementation documents:

```text
docs/
├── 00-master-product-technical-spec.md
├── 01-system-architecture.md
├── 02-domain-data-model.md
├── 03-workspace-canvas-spec.md
├── 04-filesystem-sync-spec.md
├── 05-search-indexing-spec.md
├── 06-docx-openxml-word-integration.md
├── 07-batch-operation-engine.md
├── 08-markdown-graph-spec.md
├── 09-pdf-finalization.md
├── 10-sharing-security-ngrok.md
├── 11-testing-strategy.md
└── 12-implementation-plan.md
```

The master document must remain the product-level source of truth. Lower-level technical documents may elaborate implementation details but must not silently contradict the decisions recorded here.

---

# 100. Final Instruction to AI Coding Agents

When implementing Document IDE:

> Prioritize file safety, deterministic behavior, local-first architecture, fast search, and a clean canvas over adding more features.

If a design choice creates tension between convenience and protecting user documents, prefer protecting the documents.

If a design choice creates tension between “smart AI behavior” and deterministic destructive operations, prefer deterministic behavior.

If a design choice creates tension between proprietary internal state and portable local files, prefer portable local files.

If a design choice creates tension between global auto-layout and preserving the user's spatial memory, preserve spatial memory.

The application should feel powerful because it gives users control over their documents—not because it hides complexity behind automation.

---

# 101. Security & Performance Hardening Baseline (Post-Audit)

This section is normative and overrides any weaker or ambiguous wording earlier in the master specification.

## 101.1 Privileged localhost API

Binding to loopback is necessary but not sufficient. The privileged local API MUST use a runtime-selected port where practical and enforce all of the following:

- expected loopback `Host`/port allowlist,
- dedicated WebView2 application `Origin` allowlist,
- reject foreign origins and `Origin: null` for privileged browser-originated calls,
- Fetch Metadata / `Sec-Fetch-Site` checks as defense-in-depth where available,
- a cryptographically random **256-bit per-startup session secret** on every privileged API request,
- no wildcard CORS,
- no state-changing GET routes,
- strict method/content-type/schema validation.

Preferred implementation: keep the session secret only in native process memory and attach it through the WebView2 native request/bridge boundary. Do not persist it in localStorage, disk, or an ordinary long-lived cookie.

## 101.2 Separate public ShareHost

The ngrok-facing public server MUST run as a separate executable:

```text
DocumentIDE.exe
  privileged local work process
        ↓ publishes sanitized artifacts/manifests
%LOCALAPPDATA%\DocumentIDE\shares\<share-id>\
        ↓
DocumentIDE.ShareHost.exe
  public read-only process
        ↓
ngrok
        ↓
Internet
```

ShareHost MUST NOT load Word automation, batch mutation, unrestricted filesystem mutation, or privileged workspace-secret services. It consumes only explicit share manifests and application-owned sanitized artifacts.

**Residual risk:** a second process running as the same Windows user is not automatically an OS sandbox. A technical spike MUST evaluate restricted token/AppContainer/ACL-limited access. Until verified, do not claim ShareHost process separation alone prevents arbitrary local-file reads after full code execution.

## 101.3 Live Share membership

`Live` means live content for existing members, not live membership. New files appearing in a shared physical folder or later added to a source Collection MUST NOT become public automatically. Adding/removing members requires an explicit owner action.

## 101.4 Share token

- 256 bits of cryptographically secure entropy,
- fixed encoding and accepted length,
- malformed/oversized input rejected before expensive work,
- persist only a one-way hash where practical,
- verify with fixed-time comparison.

## 101.5 Basename-only rename

Rename APIs accept a basename, never a path. Reject path separators, `.`, `..`, rooted paths, traversal, Windows reserved names, alternate data stream/path syntax, invalid trailing normalization cases, and destination collisions. Physical movement is a separate `MovePhysicalFile` operation.

## 101.6 DOCX package resource budget

Automatic DOCX indexing treats OOXML ZIP/XML as untrusted input. Recommended V1 automatic-index limits:

```text
compressed source size       <= 100 MiB
ZIP entry count              <= 10,000
total declared uncompressed  <= 512 MiB
single package part          <= 128 MiB
DTD/external entities        prohibited
automatic parsing budget     bounded
```

Files exceeding limits are marked `skipped for safety`. If reliable timeout/cancellation cannot be enforced in-process, evaluate a parser worker process.

## 101.7 Word COM serialization

All Word COM automation runs through one dedicated STA worker queue:

```text
WordAutomationQueue
concurrency = 1
```

## 101.8 SQLite concurrency baseline

```sql
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;
```

Parse/compute outside writer transactions; keep database writes short.

## 101.9 Recovery storage

Retain history for 30 days without silent early eviction. Recommended V1 defaults:

```text
history budget = 5 GiB
warning        = 80%
```

Before destructive apply, estimate required snapshot bytes. If complete recovery snapshots cannot be safely created, BLOCK the operation.

## 101.10 Public Share rate limits

Initial configurable V1 defaults:

```text
general reads        120 requests/minute/IP
search                30 requests/minute/IP
downloads             10 requests/minute/IP
concurrent downloads   2/IP
```

Return `429` with retry information. Public viewer requests MUST NOT directly trigger Word COM rendering.

## 101.11 CSP and sanitization

Start from:

```text
default-src 'none'
script-src 'self'
connect-src 'self'
img-src 'self' data:
font-src 'self'
object-src 'none'
base-uri 'none'
frame-ancestors 'none'
form-action 'none'
```

`script-src` MUST NOT use `unsafe-eval` or generic `unsafe-inline`. Bundle scripts locally. Sanitize rendered Markdown/HTML with a maintained sanitizer such as DOMPurify or equivalent.

## 101.12 Junction/symlink containment

Canonicalize configured roots and resolved reparse-point targets. A junction/symlink escaping a configured source root MUST NOT be silently traversed by indexing, sharing, batch scope, or filesystem operations.

## 101.13 Cross-volume move ambiguity

Never auto-bind when multiple plausible candidates exist, including duplicate fingerprints. Identical content does not prove identical document identity.

## 101.14 Canvas scaling

Use a spatial acceleration structure (uniform grid/spatial hash, R-tree, or quadtree chosen by benchmark) for collision and placement. Avoid repeated all-object scans.

## 101.15 Graph scaling

For more than approximately 250 visible nodes, default to selected-neighborhood/filtered view. Limit semantic edges to top-K neighbors (`K=5` recommended) plus a confidence threshold. Run layout off the interactive UI thread.

## 101.16 PDF merge memory

Merge sequentially from files/streams with bounded buffers. Do not load all source PDFs/pages into memory simultaneously.
