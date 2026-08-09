# PDF-01 — Single DOCX PDF Export

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/pdf-01-single-docx-pdf-export`

## Goal

Export one DOCX to PDF through the serialized Word automation queue without modifying source.

## Depends On

- `WORD-03` — Dedicated STA Word Automation Queue

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

- `src/DocumentIDE.Core/Pdf/PdfExportService.cs`
- `tests/DocumentIDE.IntegrationTests/Pdf/SinglePdfExportTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `WORD-03` — Dedicated STA Word Automation Queue

**Produces**
- A tested implementation of `PDF-01 — Single DOCX PDF Export`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement output path/name selection.
- [ ] **Step 2:** Queue Word ExportAsFixedFormat.
- [ ] **Step 3:** Handle output collision explicitly.
- [ ] **Step 4:** Fingerprint source before/after.
- [ ] **Step 5:** Return typed export result.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Generated PDF exists.
- [ ] DOCX fingerprint unchanged.
- [ ] Existing PDF is not silently overwritten.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Generated PDF exists.

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

- [ ] Word COM only via queue.

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
feat(pdf): single docx pdf export
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint PDF-01 — Single DOCX PDF Export only.

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
