# PDF-03 — Streaming Ordered PDF Merge

**Status:** PLANNED  
**Priority:** P1  
**Kind:** performance  
**Recommended branch:** `perf/pdf-03-streaming-ordered-pdf-merge`

## Goal

Merge user-ordered PDFs using sequential file/stream processing without rasterization or loading the whole corpus into RAM.

## Depends On

- `PDF-02` — Batch Separate PDF Export

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/09-pdf-finalization.md`
- `docs/specs/11-testing-strategy.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Pdf/PdfMergeService.cs`
- `src/DocumentIDE.Web/src/features/pdf/MergeOrderDialog.tsx`
- `tests/DocumentIDE.IntegrationTests/Pdf/PdfMergeMemoryTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `PDF-02` — Batch Separate PDF Export

**Produces**
- A tested implementation of `PDF-03 — Streaming Ordered PDF Merge`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement explicit drag-order model.
- [ ] **Step 2:** Render intermediate DOCX PDFs first.
- [ ] **Step 3:** Select license-compatible streaming merge library.
- [ ] **Step 4:** Append inputs sequentially with bounded buffers.
- [ ] **Step 5:** Clean temp job directory on success/failure.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Output page order matches user order.
- [ ] Pages are not rasterized.
- [ ] Peak memory does not scale linearly with total source bytes.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Output page order matches user order.

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

- [ ] No silent inferred merge order.

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
perf(pdf): streaming ordered pdf merge
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint PDF-03 — Streaming Ordered PDF Merge only.

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
