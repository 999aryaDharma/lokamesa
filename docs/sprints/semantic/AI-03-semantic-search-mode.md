# AI-03 — Semantic Search Mode

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/ai-03-semantic-search-mode`

## Goal

Add clearly labeled semantic search as an optional exploratory mode independent from Exact.

## Depends On

- `AI-02` — Local Embedding Provider
- `SRCH-05` — Realtime Search UI & Selection

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/05-search-indexing-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Search/SemanticSearchService.cs`
- `src/DocumentIDE.Web/src/features/search/SearchModeSwitch.tsx`
- `tests/DocumentIDE.Core.Tests/Search/SemanticSearchTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `AI-02` — Local Embedding Provider
- `SRCH-05` — Realtime Search UI & Selection

**Produces**
- A tested implementation of `AI-03 — Semantic Search Mode`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement semantic query embedding and nearest-neighbor lookup.
- [ ] **Step 2:** Render Semantic/Smart mode separately.
- [ ] **Step 3:** Label result source/score.
- [ ] **Step 4:** Keep Exact mode deterministic and unchanged.
- [ ] **Step 5:** Handle missing/stale embeddings gracefully.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Semantic mode finds conceptually related fixture.
- [ ] Exact mode results unchanged.
- [ ] SearchReplace does not depend on semantic service.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Semantic mode finds conceptually related fixture.

## Failure / Edge Cases

- Model/provider unavailable.
- Embedding is stale relative to content.

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

- [ ] No invisible semantic contamination of destructive actions.

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
feat(semantic): semantic search mode
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint AI-03 — Semantic Search Mode only.

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
