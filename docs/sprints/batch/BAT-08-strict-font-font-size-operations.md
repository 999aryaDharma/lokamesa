# BAT-08 — Strict Font & Font-Size Operations

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/bat-08-strict-font-font-size-operations`

## Goal

Change only requested font family or size on reviewed DOCX targets without changing unrelated paragraph/page formatting.

## Depends On

- `BAT-04` — Transactional Safe Apply Engine
- `DOCX-03` — Strict DOCX Text Mutation

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/07-batch-operation-engine.md`
- `docs/specs/06-docx-openxml-word-integration.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Batch/Operations/SetFontOperation.cs`
- `src/DocumentIDE.Core/Batch/Operations/SetFontSizeOperation.cs`
- `tests/DocumentIDE.IntegrationTests/Batch/FontOperationsTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `BAT-04` — Transactional Safe Apply Engine
- `DOCX-03` — Strict DOCX Text Mutation

**Produces**
- A tested implementation of `BAT-08 — Strict Font & Font-Size Operations`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Define explicit font operation target semantics.
- [ ] **Step 2:** Preview effective current→new formatting.
- [ ] **Step 3:** Implement minimal OOXML run/font mutation.
- [ ] **Step 4:** Implement font-size operation separately.
- [ ] **Step 5:** Add invariant comparisons for margins/spacing/styles.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Font change leaves size/margins unchanged.
- [ ] Size change leaves family/margins unchanged.
- [ ] Mixed formatting preview identifies affected text.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Font change leaves size/margins unchanged.

## Failure / Edge Cases

- File changes after preview.
- One file fails while others are valid.

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

- [ ] No normalize-to-template behavior.

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
feat(batch): strict font & font-size operations
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint BAT-08 — Strict Font & Font-Size Operations only.

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
