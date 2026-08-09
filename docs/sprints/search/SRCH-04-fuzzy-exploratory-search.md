# SRCH-04 — Fuzzy Exploratory Search

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/srch-04-fuzzy-exploratory-search`

## Goal

Add typo-tolerant exploratory matching while keeping exact results higher and destructive operations isolated.

## Depends On

- `SRCH-02` — Exact Ranked Search

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/05-search-indexing-spec.md`
- `docs/specs/07-batch-operation-engine.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Search/FuzzySearchService.cs`
- `tests/DocumentIDE.Core.Tests/Search/FuzzySearchTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `SRCH-02` — Exact Ranked Search

**Produces**
- A tested implementation of `SRCH-04 — Fuzzy Exploratory Search`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Choose/implement bounded fuzzy matcher suitable for <1,000 docs.
- [ ] **Step 2:** Blend fuzzy score below exact lexical score.
- [ ] **Step 3:** Label fuzzy match source.
- [ ] **Step 4:** Cap candidate set before expensive distance scoring.
- [ ] **Step 5:** Expose fuzzy on/off mode.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Small typo finds expected document.
- [ ] Exact result outranks fuzzy result.
- [ ] Batch/SearchReplace module has no dependency on fuzzy matcher.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Small typo finds expected document.

## Failure / Edge Cases

- Index is stale/incomplete.
- Query syntax is malformed.

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

- [ ] Fuzzy never becomes implicit destructive matching.

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
feat(search): fuzzy exploratory search
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SRCH-04 — Fuzzy Exploratory Search only.

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
