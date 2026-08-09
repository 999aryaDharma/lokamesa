# PDF-04 — PDF Finalization UX

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/pdf-04-pdf-finalization-ux`

## Goal

Expose single/batch/merge PDF finalization coherently from selection, inspector, command palette, and error results.

## Depends On

- `CAN-08` — List View & View Switch
- `PDF-03` — Streaming Ordered PDF Merge

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/09-pdf-finalization.md`
- `docs/specs/03-workspace-canvas-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Web/src/features/pdf/PdfActions.tsx`
- `tests/frontend/pdf/PdfActions.test.tsx`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `CAN-08` — List View & View Switch
- `PDF-03` — Streaming Ordered PDF Merge

**Produces**
- A tested implementation of `PDF-04 — PDF Finalization UX`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Add Export PDF actions/context rules.
- [ ] **Step 2:** Show merge ordering UI.
- [ ] **Step 3:** Show output collision decisions.
- [ ] **Step 4:** Show progress/results.
- [ ] **Step 5:** Add command-palette entries.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] One selected DOCX defaults to single export.
- [ ] Multiple docs offer separate/merge choices.
- [ ] Failures identify affected source.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> One selected DOCX defaults to single export.

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

- [ ] PDF remains output-only.

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
feat(pdf): pdf finalization ux
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint PDF-04 — PDF Finalization UX only.

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
