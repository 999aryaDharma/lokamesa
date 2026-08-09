# 09 — PDF Finalization Specification

**Project:** Document IDE  
**Status:** Final-output baseline  
**Depends on:** `06-docx-openxml-word-integration.md`

---

## 1. Purpose

Defines DOCX-to-PDF finalization using Microsoft Word, batch export, ordered PDF merge, output naming, conflicts, temporary artifacts, and error handling.

---

## 2. Scope

PDF is output-only.

Document IDE does not edit PDF.

Supported:

1. one DOCX → one PDF,
2. multiple DOCX → separate PDFs,
3. ordered multiple DOCX → one merged PDF.

Markdown-to-PDF may be added later but is not a locked core requirement from the product discussion.

---

## 3. Rendering Engine

Preferred:

```text
Microsoft Word Desktop automation
```

Reason:

- maximum DOCX rendering fidelity,
- user expects Word-equivalent layout.

---

## 4. Single Export

Flow:

```text
select DOCX
→ Export PDF
→ choose output directory/name
→ Word render
→ write PDF
→ report success
```

Default name:

```text
BA-NUSA-DUA.docx
→ BA-NUSA-DUA.pdf
```

---

## 5. Batch Separate Export

Flow:

```text
select multiple DOCX
→ Export as separate PDFs
→ choose output folder
→ render each
→ per-file result
```

---

## 6. Merge Export

Flow:

```text
select DOCX
→ Merge to PDF
→ order documents explicitly
→ render each to temporary PDF
→ merge in selected order
→ choose final filename
```

Do not infer order silently from canvas position unless user explicitly selects “use canvas order” as a future option.

---

## 7. Ordering UI

Required:

- drag reorder list,
- visible filenames,
- final count.

Example:

```text
1. Cover.docx
2. BA Site A.docx
3. BA Site B.docx
4. Lampiran.docx
```

---

## 8. Output Collision

If target exists:

```text
Overwrite
Rename
Skip
Cancel
```

Never silently overwrite.

Batch UI may offer:

```text
Apply choice to remaining collisions
```

---

## 9. Word Lock

Export may render a document that is open in Word if Word automation can safely access saved state.

Important rule:

- export uses saved document content,
- unsaved Word changes cannot be assumed present.

If unsaved state is detectable, warn:

```text
This document may contain unsaved Word changes.
```

---

## 10. Temporary Files

Use app temp directory:

```text
%LOCALAPPDATA%\DocumentIDE\temp\pdf-jobs\<job-id>\
```

Clean intermediates after success/failure.

---

## 11. PDF Merge Library

Use a local PDF library that:

- preserves page dimensions,
- does not rasterize pages,
- merges without quality loss.

The implementation agent must verify license compatibility.

---

## 12. Batch Result

Example:

```text
12 PDFs generated
1 skipped
1 failed
```

Show per-file failure details.

---

## 13. Cancellation

Cancellation:

- stops before next queued document,
- cleans incomplete temporary artifacts,
- does not delete already completed user output unless explicitly chosen.

---

## 14. Source Integrity

PDF export MUST NOT mutate DOCX.

Test by fingerprinting DOCX before/after export.

---

## 15. Naming Sanitization

When deriving filenames:

- preserve meaningful source name,
- replace invalid Windows filename characters only where necessary,
- avoid hidden collision.

---

## 16. Share Integration

`Allow PDF download` may generate or expose a PDF artifact for shared documents.

Share generation must obey same source-integrity rule.

---

## 17. Acceptance Criteria

Must pass:

- DOCX fingerprint unchanged after export,
- output visually matches Word-generated PDF,
- merge order matches user order,
- collisions never overwrite silently,
- temp artifacts cleaned,
- partial batch results are transparent.

---

# 18. Post-Audit PDF Merge Performance Requirements

The PDF merge implementation MUST support sequential file/stream-based append and MUST NOT require all input PDFs/pages in memory simultaneously. Memory usage should scale primarily with the current input/buffer rather than total merged corpus size. Add a large-merge peak-memory regression test.
