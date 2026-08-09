# PDF-02 — Batch Separate PDF Export

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/pdf-02-batch-separate-pdf-export`

## Goal

Export multiple DOCX files as separate PDFs with progress, cancellation between files, and partial results.

## Depends On

- `PDF-01` — Single DOCX PDF Export
- `FND-04` — Background Job Runtime

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/09-pdf-finalization.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Pdf/BatchPdfExportService.cs`
- `src/DocumentIDE.Web/src/features/pdf/BatchExportProgress.tsx`
- `tests/DocumentIDE.IntegrationTests/Pdf/BatchPdfExportTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `PDF-01` — Single DOCX PDF Export
- `FND-04` — Background Job Runtime

**Produces**
- A tested implementation of `PDF-02 — Batch Separate PDF Export`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create ordered export job.
- [ ] **Step 2:** Serialize Word renders through WORD-03.
- [ ] **Step 3:** Expose progress/current filename.
- [ ] **Step 4:** Apply collision policy per file.
- [ ] **Step 5:** Return succeeded/skipped/failed result list.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Multiple exports remain Word-concurrency=1.
- [ ] Cancellation stops before next file.
- [ ] Partial failures are visible.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Multiple exports remain Word-concurrency=1.

## Failure / Edge Cases

- Word export fails for one document.
- Output path collides or disk fills.

Failures must produce typed/inspectable outcomes rather than silent fallback where the subsystem spec requires determinism.

## Security & File-Safety Gate

Before completion, explicitly confirm:

- [ ] no user file was mutated through an unreviewed/bypass path
- [ ] no arbitrary path or secret is exposed
- [ ] dependency/security boundaries remain intact
- [ ] no destructive action silently broadened its scope
- [ ] relevant higher-level safety rules remain stronger than convenience

Mark an item N/A in the handoff only with a one-sentence justification.

## Acceptance Criteria

- [ ] Completed outputs are not deleted on unrelated later failure.

## Definition of Done

- [ ] all implementation steps completed
- [ ] required tests pass
- [ ] repository affected test suite passes
- [ ] no required test skipped
- [ ] no unresolved sprint-required placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] `docs/sprints/SPRINT-STATUS.md` updated
- [ ] sprint handoff written using `docs/templates/SPRINT-HANDOFF-TEMPLATE.md`
- [ ] coherent commit created

## Commit Guidance

Recommended final commit:

```text
feat(pdf): batch separate pdf export
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint PDF-02 — Batch Separate PDF Export only.

Read AGENTS.md, this sprint document, and every Required Reading document first.
Verify every Depends On sprint is DONE before editing.

Use TDD. Do not implement downstream features and do not weaken higher-level specs.

At completion:
1. run all Required Tests and affected repository verification,
2. self-review git diff,
3. map Acceptance Criteria to evidence,
4. update SPRINT-STATUS.md,
5. write the sprint handoff,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the sprint commit guidance.
```
