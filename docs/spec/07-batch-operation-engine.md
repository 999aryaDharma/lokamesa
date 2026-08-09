# 07 — Strict Batch Operation Engine Specification

**Project:** Document IDE  
**Status:** Safety-critical  
**Depends on:** `04-filesystem-sync-spec.md`, `06-docx-openxml-word-integration.md`

---

## 1. Purpose

Defines the deterministic preview/apply engine for mass document transformations.

The key rule:

> Batch operations are strict. Only the explicitly requested property/content is changed.

---

## 2. Initial Operation Types

V1:

```text
SearchAndReplace
SetFont
SetFontSize
SetMargins
SetPaperSize
SetOrientation
SetAlignment
RenameFiles
```

Operations may expand later, but each new operation needs:

- precise target definition,
- preview model,
- strict mutation rules,
- tests,
- undo strategy.

---

## 3. Common Pipeline

```text
Target selection
→ Preview request
→ Deterministic scan
→ Operation plan
→ User review
→ Apply request
→ Stale check
→ Lock check
→ Snapshot
→ Mutation
→ Validation
→ Atomic replace
→ Re-index
→ History
```

---

## 4. Scope Resolution

Supported scopes:

```text
Selection
Folder
Workspace
Collection
```

Default:

- selected documents → Selection,
- folder context → Folder,
- global batch UI → explicit user choice.

Never silently widen scope.

---

## 5. Folder Scope

For folder operations:

```text
Include subfolders = ON by default
```

Before preview/apply, UI MUST show:

- root folder,
- included subfolder count/tree,
- excluded branches,
- files scanned.

---

## 6. Search & Replace Matching

Default:

```text
Exact / deterministic
Fuzzy = OFF
Semantic = OFF
Regex = OFF
```

Advanced mode:

```text
Regex = ON
```

Regex UI must clearly indicate advanced behavior.

---

## 7. Search & Replace Locations

Search these logical locations:

- body,
- table cells,
- headers,
- footers.

V1 should exclude field instructions and unsafe generated constructs unless explicitly supported.

Each match stores origin.

---

## 8. Preview Model

Example:

```text
47 files scanned
13 files matched
31 occurrences

Body    19
Tables   7
Headers  3
Footers  2
```

Per file:

```text
BA-NUSA-DUA.docx
☑ Page/Body ...NUSA-DUA-1...
☑ Header    ...NUSA-DUA-1...
```

User may deselect individual occurrences.

---

## 9. OperationPlan

Suggested contract:

```text
OperationPlan
- id
- workspace_id
- operation_type
- scope
- parameters
- created_at
- expires_at
- files[]
```

---

## 10. FilePlan

```text
FilePlan
- document_id
- expected_fingerprint
- lock_state_at_preview
- matches/changes[]
- selected
```

---

## 11. Apply Staleness

At apply:

```text
current_fingerprint != expected_fingerprint
→ reject file as stale
```

Do not automatically apply an old plan to newer content.

UI must request re-preview.

---

## 12. Snapshot Timing

Recovery snapshot MUST be created:

```text
after apply is approved
before original is replaced
```

No destructive change before snapshot.

---

## 13. Safe Mutation

For each file:

```text
copy original to temp
→ mutate temp
→ validate
→ atomic replace original
```

If mutation/validation fails:

```text
original remains unchanged
```

---

## 14. Partial Failure

V1 chosen behavior:

- transparent per-file result,
- successful files may remain changed,
- failed/skipped files reported,
- undo can revert successfully changed files.

Example:

```text
42 succeeded
2 skipped (open in Word)
1 failed (invalid package)
```

---

## 15. Search & Replace Across Runs

Use logical DOCX text mapping defined in `06-docx-openxml-word-integration.md`.

Never use raw zip/XML string replacement.

---

## 16. Regex

Regex preview must show exact captured results.

Replacement syntax should use a documented standard.

Do not allow regex replace without preview.

Protect against pathological regex patterns with:

- timeout,
- bounded input execution.

---

## 17. Set Font

Strict semantics must be defined before implementation.

Recommended V1:

- selected documents,
- target effective font match optionally specified,
- apply only to text runs selected by operation,
- do not change font size/paragraph formatting.

UI examples:

```text
Set all direct Arial text → Times New Roman
```

or:

```text
Set all body text font → Times New Roman
```

The second is broader and must preview scope clearly.

---

## 18. Set Font Size

Only font size.

Do not change family, style, spacing.

---

## 19. Set Margins

Only section margin values.

Preview:

```text
Document A
Section 1: 3/3/3/3 → 4/4/3/3
Section 2: unchanged
```

If operation targets all sections, state so.

---

## 20. Set Paper Size

Only page size/orientation properties requested.

Changing to A4 must not alter margins.

---

## 21. Rename Files

Preview every old/new path.

Validation:

- illegal Windows characters,
- reserved names,
- duplicate target,
- path length,
- existing destination.

Rename never modifies document content.

---

## 22. Undo

Operation-level undo:

```text
Undo batch
```

For each affected document:

- compare current fingerprint to post-operation fingerprint.

If unchanged since operation:

- restore snapshot.

If changed later:

- mark conflict,
- require user decision.

---

## 23. History Retention

Snapshots/history:

```text
30 days
```

Cleanup job removes expired snapshots safely.

---

## 24. Apply API

Conceptual:

```text
POST /batch-operations/preview
POST /batch-operations/{planId}/apply
POST /history/{operationId}/undo
```

Apply endpoint does not accept arbitrary new mutation parameters; it applies reviewed plan with selected occurrence IDs.

---

## 25. Operation Idempotency

Repeated apply request for already-completed plan must not apply twice.

Store plan status.

---

## 26. Cancellation

Preview can be cancelled.

Apply cancellation:

- may stop before next file,
- must never abort halfway through writing a single file unsafely.

---

## 27. Logging

Record:

- operation ID,
- user-visible parameters,
- affected document IDs,
- result statuses.

Avoid logging full document contents.

---

## 28. Acceptance Criteria

Must pass:

- recursive folder preview shows real scope,
- fuzzy/semantic never used by destructive replace,
- occurrence-level selection works,
- stale plans are rejected,
- open Word documents are skipped/blocked safely,
- failed file remains original,
- successful operation is undoable for 30 days,
- strict margin change does not alter font,
- strict font-size change does not alter margins.

---

# 29. Post-Audit Batch Safety Requirements

### Rename

Rename accepts `newBaseName` only, never an arbitrary path. Reject `\\`, `/`, `.`, `..`, rooted paths, traversal components, illegal characters, reserved Windows device names, alternate-data-stream/path syntax, invalid trailing dot/space cases, and collisions. Require the basename invariant equivalent to:

```text
Path.GetFileName(newBaseName) == newBaseName
```

Physical movement is a separate `MovePhysicalFile` operation.

### Recovery capacity

Recommended V1 history budget is 5 GiB, warning at 80%, retention 30 days. Before the first destructive mutation, estimate complete snapshot bytes and free disk. If recovery cannot be fully created, BLOCK apply. Never silently delete unexpired snapshots to proceed.

### Acceptance additions

- rename cannot escape the current directory,
- destructive apply fails closed when recovery capacity is insufficient.
