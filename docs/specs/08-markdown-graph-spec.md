# 08 — Markdown Editor, Knowledge Graph & Context Specification

**Project:** Document IDE  
**Status:** Knowledge-work baseline  
**Depends on:** `05-search-indexing-spec.md`, `02-domain-data-model.md`

---

## 1. Purpose

Defines source-first Markdown editing, preview, parsing, frontmatter, links, backlinks, graph relations, semantic relations, and context-pack behavior for programmer/AI-agent documentation.

---

## 2. Source Rule

The `.md` file on disk is the source of truth.

No hidden block database.

No required proprietary sidecar for document contents.

---

## 3. Editor Modes

Required:

```text
Edit
Preview
Split
```

Default mode may be remembered per workspace/document.

---

## 4. Editing Experience

Editor should provide:

- syntax highlighting,
- line numbers optional,
- find/replace,
- heading navigation,
- code block support,
- Markdown-aware indentation,
- autosave optional but explicit in settings,
- standard `Ctrl+S`.

---

## 5. Rendering Support

Required:

- Common Markdown syntax,
- tables,
- task checkboxes,
- fenced code,
- syntax-highlighted code,
- images,
- relative links,
- frontmatter,
- Mermaid,
- LaTeX/math,
- wikilinks.

---

## 6. Security

Preview must not execute arbitrary code.

Raw HTML should be sanitized according to chosen Markdown renderer policy.

Mermaid/math must not gain arbitrary filesystem access.

---

## 7. Frontmatter

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

Parser must retain raw frontmatter and expose structured values.

---

## 8. Wikilinks

Syntax:

```text
[[architecture]]
[[architecture|Architecture Overview]]
```

Resolution order:

1. exact relative path if included,
2. exact filename stem within workspace,
3. disambiguation UI when multiple matches.

Do not silently bind ambiguous wikilinks.

---

## 9. Relative Links

Support normal Markdown links:

```text
[Architecture](./architecture.md)
```

Resolve based on physical source path.

---

## 10. Link Extraction

For each Markdown file extract:

- outbound Markdown links,
- wikilinks,
- frontmatter dependencies.

Update graph after save.

---

## 11. Backlinks

For selected document:

```text
Referenced by
```

should list explicit inbound relations.

Backlinks are deterministic.

---

## 12. Manual Relations

User may create explicit manual relations from graph/list/inspector.

Manual relation stores:

- source,
- target,
- relation type.

Manual relations are workspace metadata and do not modify Markdown unless a future explicit “write link into document” command is added.

---

## 13. Graph View

Graph is one of:

```text
Canvas | List | Graph
```

Graph node positions persist independently.

---

## 14. Edge Semantics

Solid edge:

```text
explicit
```

Sources:

- Markdown link,
- wikilink,
- frontmatter,
- manual relation.

Dashed edge:

```text
semantic / inferred
```

Never render inferred relation as factual solid relation.

---

## 15. Semantic Related Documents

Local semantic system may provide:

```text
Potentially related
- execution-engine.md 87%
- risk-management.md 76%
```

These are suggestions only.

---

## 16. Graph Filters

Useful filters:

- explicit only,
- inferred only,
- by relation type,
- by folder/source,
- by document type,
- selected neighborhood depth.

---

## 17. Graph Focus

Selecting a node can show:

```text
References
Referenced by
Potentially related
```

Double click Markdown node opens editor.

DOCX nodes may also exist if explicit/manual relations point to DOCX.

---

## 18. DOCX Participation

DOCX may participate in graph through:

- manual relations,
- semantic relations,
- future extracted explicit references.

Do not pretend normal DOCX body text creates explicit backlinks unless a deterministic linking syntax/process is defined.

---

## 19. Save Behavior

Markdown save:

```text
temp write
→ validation/flush
→ atomic replace
→ watcher/index update
```

External edits must reconcile like any local file.

---

## 20. Git Compatibility

Markdown files remain ordinary files.

Document IDE should not insert noisy metadata automatically into source Markdown.

Frontmatter modifications occur only on explicit user action.

---

## 21. Context Packs

Planned capability:

```text
select documents
→ Create Context Pack
```

Inputs can include DOCX and Markdown.

Output baseline:

- combined Markdown representation,
- ordered source list,
- token estimate where tokenizer available.

---

## 22. Context Pack Options

Potential:

```text
Include backlinks
Include explicit dependencies
Include semantic related docs
Max token budget
```

Default should favor explicit user-selected documents and deterministic dependencies.

---

## 23. DOCX in Context Pack

For DOCX:

- use extracted text/structured Markdown-like representation,
- identify source filename,
- do not mutate DOCX.

---

## 24. Context Pack Export

Support:

- copy combined Markdown,
- export `context.md`.

Future direct integrations with AI agents are optional.

---

## 25. Search Integration

Markdown frontmatter fields should be searchable.

Examples:

```text
type:technical-spec
project:indodax-bot
status:active
```

Exact grammar can reuse search metadata framework.

---

## 26. Acceptance Criteria

Must pass:

- Markdown stays portable,
- Edit/Preview/Split work,
- save updates physical `.md`,
- wikilinks resolve deterministically,
- backlinks update after save,
- graph positions persist,
- explicit and semantic edges look different,
- semantic relation never becomes destructive operation input automatically.

---

# 27. Post-Audit Graph Scaling Requirements

Store all explicit relations, but do not render an expensive/unreadable full graph by default as size grows.

Recommended V1:

- above approximately 250 visible nodes, default to selected-neighborhood or filtered view,
- semantic relations limited to top `K = 5` neighbors per document by default,
- apply a configurable minimum confidence threshold,
- graph layout runs off the interactive UI thread,
- full graph remains an explicit user action,
- never generate an all-to-all semantic graph.
