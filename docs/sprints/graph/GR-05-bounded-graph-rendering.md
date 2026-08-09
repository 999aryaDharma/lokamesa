# GR-05 — Bounded Graph Rendering

**Status:** PLANNED  
**Priority:** P1  
**Kind:** performance  
**Recommended branch:** `perf/gr-05-bounded-graph-rendering`

## Goal

Keep graph usable for large workspaces through neighborhood/filter default, top-K semantic edges, and off-UI-thread layout.

## Depends On

- `GR-03` — Graph View

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/08-markdown-graph-spec.md`
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

- `src/DocumentIDE.Web/src/features/graph/graphLayout.worker.ts`
- `src/DocumentIDE.Web/src/features/graph/GraphScopeControls.tsx`
- `tests/frontend/graph/GraphPerformance.test.ts`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `GR-03` — Graph View

**Produces**
- A tested implementation of `GR-05 — Bounded Graph Rendering`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Move layout computation off interactive UI thread/worker.
- [ ] **Step 2:** Default >250 visible nodes to selected-neighborhood/filter mode.
- [ ] **Step 3:** Add explicit Full Graph action.
- [ ] **Step 4:** Prepare API for top-K semantic edges (`K=5` default).
- [ ] **Step 5:** Benchmark representative 1,000-document graph.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Large graph does not freeze UI.
- [ ] Default mode is bounded above threshold.
- [ ] Full graph remains explicit user action.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Large graph does not freeze UI.

## Failure / Edge Cases

- Relation target is missing/ambiguous.
- Graph size exceeds comfortable full view.

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

- [ ] No all-to-all semantic rendering.

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
perf(graph): bounded graph rendering
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint GR-05 — Bounded Graph Rendering only.

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
