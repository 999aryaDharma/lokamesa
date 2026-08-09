# DOCX-04 — Document Lens Metadata

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/docx-04-document-lens-metadata`

## Goal

Expose useful DOCX structure and format metadata without opening Word.

## Depends On

- `IDX-04` — Metadata Persistence

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/06-docx-openxml-word-integration.md`
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

- `src/DocumentIDE.Core/Documents/DocumentLensService.cs`
- `src/DocumentIDE.Web/src/features/inspector/DocxInspector.tsx`
- `tests/DocumentIDE.Core.Tests/Documents/DocumentLensTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `IDX-04` — Metadata Persistence

**Produces**
- A tested implementation of `DOCX-04 — Document Lens Metadata`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Compute page/section/table/image counts where reliable.
- [ ] **Step 2:** Compute paper/orientation/margin summaries.
- [ ] **Step 3:** Compute deterministic primary font/font-size summaries.
- [ ] **Step 4:** Expose heading list and header/footer presence.
- [ ] **Step 5:** Render right-side Inspector.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Inspector displays fixture metadata.
- [ ] Mixed sections show mixed summaries.
- [ ] Metadata click can invoke search filter where supported.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Inspector displays fixture metadata.

## Failure / Edge Cases

- OOXML contains unexpected structure.
- Package validation fails.

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

- [ ] Derived heuristic is labeled as summary, not source fact.

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
feat(docx): document lens metadata
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint DOCX-04 — Document Lens Metadata only.

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
