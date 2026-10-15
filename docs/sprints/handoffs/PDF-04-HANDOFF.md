# Sprint Handoff — PDF-04

**Status:** DONE
**Branch:** `feat/pdf-04-pdf-finalization-ux`
**Commits:** `<filled at commit time>`

## What changed

Coherent PDF finalization UX over the PDF-01 (single export), PDF-02
(batch separate export), and PDF-03 (ordered merge) backends, plus the
CAN-08 selection context. New presentational `PdfActions` web component
that wires the two existing PDF-03/PDF-02 components instead of
duplicating them: merge ordering reuses `MergeOrderDialog` verbatim,
progress/results reuse `BatchExportProgress` verbatim.

File mapping note (planned names reused with colocated-test adjustment
per the CAN-05 ruling — no duplicate abstraction created):
- planned `src/DocumentIDE.Web/src/features/pdf/PdfActions.tsx` → created as planned
- planned `tests/frontend/pdf/PdfActions.test.tsx` → created as colocated
  `src/DocumentIDE.Web/src/features/pdf/PdfActions.test.tsx`, matching the
  PDF-02/PDF-03 precedent (`BatchExportProgress.test.tsx`,
  `MergeOrderDialog.test.tsx` live next to their components)

Behavior rules (spec 09):
- Single selected DOCX defaults to single export (`resolveDefaultExportMode`
  returns `'single'`; the `Export PDF` button is the only action offered).
- Multiple DOCX offer exactly `Export as separate PDFs` and `Merge to PDF`
  (never a silent default; merge opens `MergeOrderDialog` seeded with
  selection order, confirm emits the explicit order).
- Only `kind: 'docx'` is exportable. PDF selections (and empty selection)
  offer no export action — PDF remains output-only.
- Collisions offer Overwrite / Rename / Skip / Cancel plus `Apply choice to
  remaining collisions`; no callback fires without an explicit user
  decision, so existing files are never overwritten silently (spec 09 §8).
- Progress renders through `BatchExportProgress`; results list every file
  with its source name, typed status, and message — failures always name
  the affected source (spec 09 §12).
- Command-palette entries (`category: 'PDF'`): `pdf.export-single`,
  `pdf.export-separate`, `pdf.merge`, derived from the same action rules.

## Files changed

- `src/DocumentIDE.Web/src/features/pdf/PdfActions.tsx` (new: action
  rules, command-palette entries, `PdfActions` component reusing
  `MergeOrderDialog` + `BatchExportProgress`, collision UI, results list)
- `src/DocumentIDE.Web/src/features/pdf/PdfActions.test.tsx` (new: 6 tests)
- `docs/sprints/SPRINT-STATUS.md` (PDF-04 row READY → DONE)

## Verification

- `npx vitest run src/features/pdf/PdfActions.test.tsx` → PASS (6/6)
- `npx vitest run src/features/pdf` → PASS (10/10: 2 BatchExportProgress + 2 MergeOrderDialog + 6 PdfActions)
- `npx vitest run` (DocumentIDE.Web, full) → PASS (218/218 across 37 files, 0 failed)
- `npx tsc -b` (DocumentIDE.Web) → clean
- `npm run lint` (oxlint) → warnings only, no errors (the two
  `react(only-export-components)` notes on PdfActions match the pre-existing
  FolderPicker.tsx pattern; colocated helpers are the repo convention)
- TDD Red proven: pre-implementation run failed with
  `Failed to resolve import "./PdfActions" ... Does the file exist?`
- No backend C# code touched (frontend-only sprint)

## Acceptance criteria

- [x] PDF remains output-only — non-DOCX (incl. `.pdf`) selections expose
  zero export actions and zero palette entries; component performs no
  mutation, only invokes caller callbacks; `isPdfExportable` gates on
  `kind === 'docx'` (test: `non-DOCX selection offers no export`)
- [x] One selected DOCX defaults to single export — `resolveDefaultExportMode`
  + `Export PDF` button test
- [x] Multiple docs offer separate/merge choices — action-rule + UI flow test
  (merge confirm emits the explicit order via reused `MergeOrderDialog`)
- [x] Failures identify affected source — results-list test asserts the
  failing source filename and message render together

## Security & file-safety gate

- [x] no user file was mutated through an unreviewed/bypass path — pure
  presentational component; all exports go through caller callbacks to the
  reviewed PDF-01/02/03 backends
- [x] no arbitrary path or secret is exposed — collision target paths render
  as caller-supplied display strings only; no fetch/session-token code added
- [x] dependency/security boundaries remain intact — no new packages, no API
  calls, no ShareHost/COM changes; only imports the two existing pdf components
- [x] no destructive action silently broadened its scope — Overwrite fires
  only via explicit radio + confirm click; default decision is the
  non-destructive `Rename`; `applyToRemaining` defaults to false
- [x] relevant higher-level safety rules remain stronger than convenience —
  merge order is explicit user order only (reused dialog never sorts);
  failures render typed per-source outcomes, never silent fallback

## Deviations / spec conflicts

None. Test-file location differs from the sprint plan (`tests/frontend/pdf/…`)
by the CAN-05 colocated-test ruling recorded above; behavior matches the plan.

## Risks discovered

- `PdfActions` is presentational: selection plumbing (canvas/list context
  menus, inspector, real command-palette host) and backend wiring are left
  to the app shell / REL-03 consumer. Callbacks and palette entries are the
  integration seam — verify the host passes stable document ids.
- Collision `targetPath` is caller-supplied; the host must supply the real
  backend-computed target (PDF-01/02 collision semantics) so the displayed
  path matches what Overwrite/Rename would act on.

## Next eligible sprint(s)

- REL-03 (V1 Readiness Gate) — PDF-04 leg now DONE (REL-03 still BLOCKED on REL-02, SHR-11, CTX-04, AI-04 per board).
