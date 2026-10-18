# UI-05 — Context Pack & Missing-Resource Surfaces

**Status:** READY
**Priority:** P0
**Kind:** ui
**Recommended branch:** `feat/context-pack-missing-resource-surfaces`

## Goal

Mount the two remaining design-12 overlays on top of thin, typed, read-mostly routes over services that
already exist — without inventing product semantics. This is the last V1 UI work.

## Why

UI-04 stopped on both, with concrete blockers:

- **Context pack.** `ContextPackService` / `ContextExportService` exist (CTX-04 DONE), and
  `ContextPackDialog` is presentation-only. There is **no route** that builds a pack, and no full-text
  source for `.docx` beyond the bounded **preview** route (60 lines / 8 KB) — publishing a preview as
  document text would be false.
- **Missing resources.** `availabilityStatus` already ships on documents and `MissingResourceCard` /
  `LocateMovedFileDialog` exist, but `Remove reference` has **no route**, and the locate dialog fetches
  **without the session token**, so it cannot reach the loopback API from `https://appassets.html2sav`
  (a real bug in a shipped component).

## In Scope

### A. Context pack
1. A bounded **full-text source** per document, honest about format:
   - `.md` → its real source;
   - `.docx` → the **indexed** text (`SearchableBody` produced by the DOCX parser), explicitly labelled
     as extracted text, not the document's own bytes; if the index has no text for it, the item is
     reported as unavailable rather than silently empty.
   - Per-document and total byte caps; a pack that would exceed them is refused typed.
2. `POST api/context/pack` over an explicit ordered list of document ids → combined Markdown + the real
   token estimate from `ContextExportService`. Typed outcomes for: unknown id, no text available,
   over budget, empty selection.
3. Mount `ContextPackDialog` behind a real trigger driven by the current selection.
   **Copy-only unless `onExportToFile` can be honoured.** If the dialog renders an export control even
   when no export callback is supplied, do **not** mount it with a lying button — stop and report.

### B. Missing resources
1. `DELETE api/documents/{documentId}/reference` (or equivalent) that removes **only** the database
   reference and never touches the file. Typed outcomes; no file-system mutation anywhere.
2. Fix `LocateMovedFileDialog` to use the same authenticated fetch path as the rest of the app (session
   token header). Pin it with a test that would fail against the current unauthenticated fetch.
3. Mount `MissingResourceCard` where documents are already rendered (list/canvas/inspector), using the
   real `availabilityStatus`; the locate flow routes into the fixed dialog.

## Out of Scope

- QuickLook (WORD-04 page pipeline is deliberately unimplemented).
- Any new persistence model, export destination picker, or save-dialog plumbing.
- Changing document mutation, recovery, or SCM semantics.
- Redesigning the two components.

## Hard Rules

- No fabricated rows, counts, text or export success. Absent beats fake.
- If an item needs a product decision (where a file is written, what "reference" means beyond the DB
  row), **stop and report** instead of choosing.
- Backend additions are append-only, typed, privileged-guarded, bounded, and tested.
- One owner of `App.tsx`.

## Required Tests

- [ ] Pack of two `.md` documents returns their real text and a real token estimate.
- [ ] A `.docx` item contributes its indexed text and is labelled as extracted; a document with no
      indexed text is typed unavailable, not empty.
- [ ] Over-cap / over-budget / unknown-id / empty-selection are typed.
- [ ] The dialog mounts from a selection, and **no export control appears** unless export is honoured.
- [ ] `DELETE .../reference` removes the row and leaves the file byte-identical (assert the file still
      exists and hashes the same).
- [ ] `LocateMovedFileDialog` sends the session token (test fails against the pre-fix fetch).
- [ ] Missing documents render the card with the real availability status; acting on it uses the routes.
- [ ] Guards: `401` missing/invalid token, `403` hostile Origin/Host, on every new route.
- [ ] No inert controls; negative test that QuickLook remains absent.

## Acceptance Criteria

- [ ] Design screen 12 reaches 5 of 6 overlays (QuickLook excepted by design).
- [ ] Every new route is typed, bounded and guarded; no file is mutated by the reference route.
- [ ] No fabricated content anywhere.

## Definition of Done

- [ ] required tests pass; affected suites green; no skipped required test
- [ ] `git diff` self-reviewed; handoff written (`docs/sprints/handoffs/UI-05-HANDOFF.md`)
- [ ] board row left to the controller

## Commit Guidance

```text
feat(ui): mount the context pack and missing-resource surfaces
```
