# AI-01 — Embedding Provider Abstraction

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/ai-01-embedding-provider-abstraction`

## Goal

Define local-first semantic provider contracts without coupling core search/graph to a specific model or external API.

## Depends On

- `FND-05` — Settings & Secret Storage
- `IDX-06` — Incremental Index Scheduler

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/05-search-indexing-spec.md`
- `docs/specs/08-markdown-graph-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Semantic/IEmbeddingProvider.cs`
- `src/DocumentIDE.Core/Semantic/EmbeddingCoordinator.cs`
- `tests/DocumentIDE.Core.Tests/Semantic/EmbeddingCoordinatorTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-05` — Settings & Secret Storage
- `IDX-06` — Incremental Index Scheduler

**Produces**
- A tested implementation of `AI-01 — Embedding Provider Abstraction`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Define EmbeddingProvider interface and model identity.
- [ ] **Step 2:** Define normalized input preparation.
- [ ] **Step 3:** Persist provider/model/content fingerprint with vectors.
- [ ] **Step 4:** Add cancellation/bounded batch generation.
- [ ] **Step 5:** Define optional external provider boundary with explicit opt-in.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Mock provider round-trip.
- [ ] Changing content/model invalidates embedding.
- [ ] Core exact search works with no provider configured.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Mock provider round-trip.

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

- [ ] External AI is optional.

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
feat(semantic): embedding provider abstraction
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint AI-01 — Embedding Provider Abstraction only.

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
