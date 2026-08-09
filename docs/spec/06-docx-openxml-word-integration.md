# 06 — DOCX, OpenXML & Microsoft Word Integration Specification

**Project:** Document IDE  
**Status:** Fidelity/safety baseline  
**Depends on:** `04-filesystem-sync-spec.md`, `07-batch-operation-engine.md`

---

## 1. Purpose

Defines DOCX structural processing, metadata extraction, preview strategy, strict OOXML mutation, Microsoft Word integration, locking, and fidelity regression requirements.

---

## 2. Core Rule

DOCX remains the authoritative source file.

Do not:

```text
DOCX → HTML editor state → regenerated DOCX
```

for ordinary operations.

Prefer:

```text
DOCX package
→ inspect exact OOXML
→ mutate only requested structures
```

---

## 3. Microsoft Word Role

Word Desktop is:

- deep editor,
- authoritative familiar editing surface,
- high-fidelity PDF rendering engine,
- optional high-fidelity preview engine.

Word is NOT embedded inside Document IDE.

---

## 4. OpenXML Layer

Recommended:

```text
Open XML SDK
```

Responsibilities:

- open package without Word,
- parse main body,
- tables,
- headers,
- footers,
- section properties,
- styles,
- numbering references,
- theme/font references,
- relationships,
- images metadata,
- page setup.

---

## 5. DOCX Text Model

Search and mutation must operate on a logical document text model while retaining mapping back to OOXML runs.

Conceptual:

```text
LogicalParagraph
- text
- run_segments[]
```

Each run segment maps logical character ranges to:

- part,
- paragraph,
- run,
- text node,
- formatting context.

---

## 6. Run Fragmentation

Example:

```xml
<w:r><w:t>NUSA-</w:t></w:r>
<w:r><w:t>DUA-1</w:t></w:r>
```

Logical text:

```text
NUSA-DUA-1
```

Search must find this as one match.

Replacement must avoid flattening unrelated formatting.

---

## 7. Replacement Strategy

For strict replacement across runs:

1. build logical text,
2. identify matched logical range,
3. identify affected runs,
4. preserve formatting from appropriate anchor run,
5. mutate minimum required text nodes,
6. remove only now-empty redundant text/runs when safe,
7. preserve unrelated child elements.

This logic requires dedicated tests.

---

## 8. Metadata Extraction

Required baseline:

- page size per section,
- orientation,
- margins,
- primary/most-common fonts,
- common font sizes,
- section count,
- table count,
- image count,
- heading candidates,
- header/footer presence.

Page count may require Word/rendered metadata for full fidelity; absence of exact page count must not block indexing.

---

## 9. Primary Font

`primary_font` is derived metadata, not a DOCX fact.

Define deterministic heuristic, e.g.:

- count effective font usage across body text,
- ignore empty runs,
- resolve styles where practical,
- report most frequent effective font.

Expose mixed formatting separately.

---

## 10. Margin Summary

If all sections share same margins:

```text
4 / 4 / 3 / 3 cm
```

If sections differ:

```text
mixed
```

Strict margin operation must specify whether it applies:

- all sections,
- selected sections if future UI supports it.

V1 may use all sections only when user explicitly requests the margin operation.

---

## 11. DOCX Validation

After mutation:

- reopen package,
- verify required parts/relationships,
- ensure package can be parsed,
- optionally ask Word to open/validate for high-risk operations.

No mutation may replace original until validation passes.

---

## 12. Preview Strategy

### Canvas thumbnail

Fast low-detail cached preview.

### Quick Look

Higher-fidelity, multi-page, read-only.

Recommended high-fidelity pipeline:

```text
DOCX
→ Word export to temporary PDF
→ PDF pages rendered to images
→ cache
```

Use lazily; do not render every DOCX through Word at startup.

---

## 13. Preview Cache Key

Cache key:

```text
DocumentId
+ content_fingerprint
+ renderer_version
```

Source change invalidates cache.

---

## 14. Open in Word

Backend action:

```text
OpenDocument(document_id)
```

Resolution:

- document must be available,
- canonical path resolved,
- shell/Word opens original file.

---

## 15. Word Automation Interface

Suggested:

```text
IWordAutomationService
- OpenDocument(DocumentId)
- IsDocumentOpen(DocumentId)
- ExportPdf(DocumentId, OutputPath)
- RenderPreview(DocumentId, PreviewOptions)
```

Implementation details stay behind adapter.

---

## 16. COM Safety

Requirements:

- do not retain unnecessary COM proxies,
- release references deterministically,
- handle Word not installed,
- handle Word busy/modal state,
- serialize operations when needed,
- do not kill arbitrary user Word processes.

---

## 17. Word Not Installed

Application can still:

- browse,
- index,
- search,
- inspect OpenXML,
- edit Markdown.

Features requiring Word show:

```text
Microsoft Word is required for this operation.
```

---

## 18. Open/Lock Conflict

Before strict DOCX mutation:

- check expected fingerprint,
- inspect lock/edit state.

If open in Word:

```text
Skip
Open Word
Retry after save
```

Do not overwrite silently.

---

## 19. No-Op Round Trip

Opening/indexing a DOCX MUST NOT modify it.

A no-op operation must not rewrite package bytes.

---

## 20. Strict Mutation Examples

### Font size

Modify only targeted `w:sz`/effective direct formatting selected by operation semantics.

### Paper size

Modify only requested section page size properties.

### Margin

Modify only requested `w:pgMar` values.

### Alignment

Modify only target paragraph justification.

No unrelated normalization.

---

## 21. Styles

Do not flatten style-based formatting into direct formatting unless the operation explicitly targets effective formatting and the design requires it.

For V1, define each operation's scope precisely.

---

## 22. Headers / Footers Search

Search must include text from referenced header/footer parts.

Search & Replace preview must report origin:

```text
Header
Footer
```

---

## 23. Tables Search

Table cell text participates in logical matching.

Preview reports:

```text
Table
```

---

## 24. Fields and Complex Content

Do not blindly replace inside field code or generated content.

V1 should:

- distinguish regular text vs field instructions,
- exclude field-code text from destructive replace by default unless explicitly supported.

---

## 25. Track Changes

If DOCX contains tracked changes:

- detect presence,
- report warning in inspector/operation preview,
- avoid destructive operations that cannot preserve semantics safely.

Exact tracked-change editing can be staged after V1.

---

## 26. Golden Fixture Corpus

Maintain DOCX fixtures for:

- split runs,
- styled runs,
- tables,
- headers,
- footers,
- section breaks,
- different margins,
- different orientations,
- images,
- hyperlinks,
- lists,
- custom styles,
- fields,
- tracked changes.

---

## 27. Acceptance Criteria

Must pass:

- index without modifying source,
- logical search finds split-run text,
- strict replace preserves unrelated formatting,
- Word double-click opens original,
- Word save triggers re-index,
- open Word document is not silently batch-overwritten,
- PDF export uses Word when available,
- invalid mutation never replaces original.

---

# 28. Post-Audit DOCX Resource & Word Automation Requirements

### DOCX package preflight

Before deep OpenXML parsing, enforce configurable automatic-index limits. Recommended V1 defaults:

```text
compressed source size       <= 100 MiB
ZIP entry count              <= 10,000
total declared uncompressed  <= 512 MiB
single package part          <= 128 MiB
DTD/external entities        prohibited
automatic parsing budget     bounded
```

If exceeded, keep the file visible but mark indexing as `skipped for safety`. Add hostile fixtures for entry explosion, oversized parts, high compression ratio, DTD/external entity attempts, and parser-budget exhaustion.

### Word COM

All Word automation calls use one dedicated STA worker queue with concurrency exactly `1`. PDF export and Word-rendered previews must never invoke the Word object model concurrently.

### Acceptance additions

- hostile/oversized DOCX fails safely without source mutation or process exhaustion,
- Word COM maximum concurrency is exactly 1.
