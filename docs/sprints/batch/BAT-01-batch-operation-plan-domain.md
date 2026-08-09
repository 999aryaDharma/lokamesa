# BAT-01 — Batch Operation Plan Domain

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/bat-01-batch-operation-plan-domain`

## Goal

Define typed previewable operation plans with file fingerprints, occurrence/change IDs, lifecycle, and idempotency.

## Depends On

- `FND-03` — SQLite Persistence Foundation
- `DOCX-03` — Strict DOCX Text Mutation

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/07-batch-operation-engine.md`
- `docs/specs/02-domain-data-model.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Batch/OperationPlan.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/BatchPlanRepository.cs`
- `tests/DocumentIDE.Core.Tests/Batch/OperationPlanTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-03` — SQLite Persistence Foundation
- `DOCX-03` — Strict DOCX Text Mutation

**Produces**
- A tested implementation of `BAT-01 — Batch Operation Plan Domain`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Define operation types/statuses.
- [ ] **Step 2:** Create BatchOperationPlan + per-file plan persistence.
- [ ] **Step 3:** Store expected fingerprint and selected change IDs.
- [ ] **Step 4:** Implement plan expiry/idempotency.
- [ ] **Step 5:** Disallow apply parameters that were not previewed.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Plan can be created/read.
- [ ] Completed plan cannot apply twice.
- [ ] Plan records expected fingerprint per file.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Plan can be created/read.

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

- [ ] Apply endpoint references reviewed plan, not arbitrary mutation payload.

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
feat(batch): batch operation plan domain
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint BAT-01 — Batch Operation Plan Domain only.

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
