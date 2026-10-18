# DATA-01 — Reference Hygiene and Missing-Status Refresh

**Status:** READY
**Priority:** P1
**Kind:** fix + data migration
**Recommended branch:** `fix/data-hygiene-missing-status`

## Goal

Close the two data-hygiene residuals from the v1.0.0 readiness report: an orphaned search-index row when a
document reference is removed, and a missing-document status that is only as fresh as the workspace
bootstrap.

## Depends On

- UI-05 (context pack + missing-resource surfaces) — DONE, merged

## In Scope

### 1. Reference removal must not orphan the index

`DELETE api/documents/{documentId}/reference` removes only the database reference — correct, it must never
touch the file — but it leaves the `search_content` FTS row behind, so the removed document can still
surface in search.

Fix: remove the index entry as part of the same operation, in the same transaction as the reference
deletion (or immediately after with a proven idempotent cleanup). `FtsIndex` already exposes a delete by
`document_id`; reuse it rather than writing SQL in the controller.

Also add a **backfill** for rows already orphaned: a migration or a bounded startup cleanup that deletes
`search_content` rows with no matching document. It must be idempotent and must not touch documents that
still exist.

### 2. Missing-status refresh

The missing/available status is computed at workspace bootstrap, so a file removed afterwards stays
"available" until reload. Add a real refresh path:

- backend: reuse the existing probe (`MissingResourceService.ProbeDocumentAsync` /
  `ProbeSourceAsync`) behind the existing guarded surface; do not invent a new status model;
- frontend: refresh on an explicit user action **and** on window focus/visibility, without fabricating a
  status while the probe is in flight;
- failures stay typed; "unknown because the probe failed" must not render as "available".

### 3. Migration numbering

The next free migration is **013**. Use `013_...` and register it in `MigrationRunner`. If another lane
lands 013 first, renumber yours to the next free number at merge time rather than editing theirs.

## Out of Scope

- Changing what "reference" means, deleting files, recovery/undo semantics.
- Redesigning the missing-resource card.
- `docs/quality/**`, `scripts/verify-release.ps1`, `.pen`.

## Required Tests

- [ ] Removing a reference deletes its `search_content` row and the document no longer appears in search.
- [ ] The file still exists with an identical SHA-256 after removal.
- [ ] The backfill removes only orphaned rows; a document that still exists keeps its index row.
- [ ] Refresh updates a document that became missing after bootstrap, and a probe failure renders a typed
      unknown state rather than "available".
- [ ] Guards unchanged: `401`/`403` on the touched routes.

## Definition of Done

- [ ] required tests pass; Integration/Core/Security green; no skipped required test
- [ ] handoff written (`docs/sprints/handoffs/DATA-01-HANDOFF.md`)
- [ ] board row left to the controller

## Commit Guidance

```text
fix(data): delete index rows with the reference and refresh missing status
```
