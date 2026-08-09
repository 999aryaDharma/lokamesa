# 05 — Search & Indexing Specification

**Project:** Document IDE  
**Status:** Core subsystem baseline  
**Depends on:** `02-domain-data-model.md`, `04-filesystem-sync-spec.md`

---

## 1. Purpose

Defines real-time local search, incremental indexing, query syntax, ranking, metadata filters, fuzzy behavior, semantic search boundaries, and search-driven selection.

---

## 2. Performance Context

Expected workspace:

```text
< 1,000 supported documents
```

Use embedded local infrastructure.

Recommended baseline:

```text
SQLite + FTS5
```

Do not introduce Elasticsearch/OpenSearch.

---

## 3. Real-Time Definition

### Query realtime

Every keystroke queries existing index.

### Index realtime

Changed files re-index incrementally after save.

Do NOT:

```text
keypress
→ reparse all DOCX
```

---

## 4. Indexing Pipeline

```text
document changed
→ parser selected by type
→ structured extraction
→ metadata normalization
→ FTS update
→ relation extraction
→ optional embedding queue
```

---

## 5. DOCX Indexed Fields

At minimum:

- filename,
- path label,
- body text,
- headings,
- table text,
- headers,
- footers.

Metadata:

- paper size,
- orientation,
- margins,
- primary font,
- primary font size,
- page count where available,
- section/table/image counts.

---

## 6. Markdown Indexed Fields

- filename,
- path label,
- body,
- headings,
- frontmatter,
- link labels/targets,
- wikilinks.

---

## 7. Query Modes

### Exact/Lexical

Primary deterministic mode.

### Fuzzy

For exploratory search only.

### Metadata

Structured filtering.

### Smart/Semantic

Optional additional mode.

---

## 8. Query Syntax

Support a practical grammar.

Examples:

```text
nusa dua
"nusa dua"
folder:BA "nusa dua"
type:docx
type:md
font:"Arial"
paper:Letter
margin:mixed
modified:this-week
"Januari 2026" -template
```

Potential fields:

```text
folder
type
font
font-size
paper
margin
modified
name
heading
```

---

## 9. Query Parser

Parser output should be structured:

```text
SearchQuery
- terms[]
- exact_phrases[]
- excluded_terms[]
- filters[]
- mode
- scope
```

Do not pass raw query strings directly into ad-hoc SQL.

---

## 10. Scope

Exploratory search supports:

```text
Workspace
Folder
Selection
Collection
```

Default global search:

```text
Current workspace
```

---

## 11. Search Result Model

```text
SearchResult
- document_id
- filename
- score
- match_sources[]
- snippets[]
- metadata_summary
```

Match sources:

```text
filename
heading
body
table
header
footer
frontmatter
metadata
semantic
```

---

## 12. Ranking

Suggested priority:

1. exact filename,
2. filename prefix/substring,
3. exact heading,
4. exact body,
5. table,
6. header/footer,
7. fuzzy,
8. semantic.

Weights MUST be testable/configurable.

Semantic results must be visibly identified when they enter ranking.

---

## 13. Snippets

Show contextual snippets.

Example:

```text
BA-NUSA-DUA.docx
Body
"...perangkat pada site NUSA DUA telah diperiksa..."
```

For headers/tables:

```text
Header
"BERITA ACARA NUSA DUA"
```

---

## 14. Fuzzy Search

Fuzzy is for discovery.

Requirements:

- tolerance for minor typo,
- lower rank than exact,
- no hidden fuzzy substitution in Search & Replace.

---

## 15. Semantic Search

Default implementation:

```text
local embedding provider
```

Architecture:

```text
EmbeddingProvider
├── LocalEmbeddingProvider
└── ExternalEmbeddingProvider (optional)
```

Semantic search should be a separate mode or clearly indicated result source.

---

## 16. External AI

Optional only.

If enabled:

- user provides credentials,
- product must disclose which content is sent,
- secrets stored using OS protection,
- core search remains usable without credentials.

---

## 17. Embedding Granularity

V1 recommendation:

- one document-level embedding from normalized title/headings/body summary or bounded content,
- optionally section/chunk embeddings later.

Avoid unnecessary chunk explosion for <1,000-file workspaces.

---

## 18. Index Versioning

Store parser/index version.

If parser logic changes:

```text
index version mismatch
→ schedule rebuild
```

Source files remain untouched.

---

## 19. Incremental Update

Use content fingerprint.

If same fingerprint:

- avoid full reparse.

If changed:

- update FTS,
- metadata,
- embeddings if enabled.

---

## 20. Search on Canvas

Search must not destroy layout.

During active query:

- emphasize matches,
- fade/hide nonmatches,
- preserve actual coordinates,
- restore previous visual state on exit.

---

## 21. Search-Driven Selection

Result set can be selected.

Example:

```text
Search "NUSA-DUA-1"
→ 17 documents
→ Select all results
→ Batch Operation
```

Selection must carry the exact result document IDs into operation scope.

---

## 22. Metadata-as-Filter

Inspector metadata fields can launch search filters.

Example:

```text
click "Arial"
→ font:"Arial"
```

```text
click "Mixed"
→ margin:mixed
```

---

## 23. Destructive Search Boundary

Search & Replace MUST NOT use:

- semantic matching,
- implicit fuzzy matching.

Its match engine is specified in `07-batch-operation-engine.md`.

---

## 24. Search History

Optional but useful V1:

- recent queries,
- recent folder-scoped queries.

Do not sync externally.

---

## 25. Rebuild

Provide:

```text
Rebuild search index
```

This operation:

- deletes derived index,
- reparses current workspace sources,
- does not alter documents.

---

## 26. Failure Handling

If one document parser fails:

- mark index error for that document,
- keep rest of index usable,
- expose retry/inspect error.

Search result UI should not pretend failed documents were indexed.

---

## 27. Acceptance Criteria

Must satisfy:

- query updates while typing,
- Word save changes results after incremental re-index,
- exact results outrank fuzzy,
- metadata filters work,
- semantic mode is distinguishable,
- entire index can be rebuilt,
- search selection can feed batch operations,
- canvas positions do not change permanently due to filtering.

---

# 28. Post-Audit Search/SQLite Concurrency Requirements

Search/index persistence uses WAL, foreign keys, and `busy_timeout=5000` per the domain specification. Expensive DOCX parsing and embedding generation occur outside SQLite writer transactions; the indexer opens a short transaction only to persist prepared results. Interactive FTS reads must remain responsive while background indexing writes occur.
