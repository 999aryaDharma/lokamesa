# IDX-06 — Incremental Index Scheduler

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/idx-06-incremental-index-scheduler`

## Goal

Connect filesystem changes to minimal parse/index/cache invalidation work.

## Depends On

- `IDX-05` — FTS5 Search Index
- `FS-03` — Runtime File Watcher

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/05-search-indexing-spec.md`
- `docs/specs/04-filesystem-sync-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Indexing/IncrementalIndexCoordinator.cs`
- `tests/DocumentIDE.Core.Tests/Indexing/IncrementalIndexCoordinatorTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `IDX-05` — FTS5 Search Index
- `FS-03` — Runtime File Watcher

**Produces**
- A tested implementation of `IDX-06 — Incremental Index Scheduler`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Map verified file changes to parse jobs.
- [ ] **Step 2:** Skip deep parse when fingerprint unchanged.
- [ ] **Step 3:** Invalidate relevant metadata/preview/embedding cache when changed.
- [ ] **Step 4:** Deduplicate repeated indexing requests.
- [ ] **Step 5:** Publish ready/error index state.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] One modified file reindexes only itself.
- [ ] Repeated event for same fingerprint does not duplicate work.
- [ ] Deleted file is removed from active index but retained as missing metadata.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> One modified file reindexes only itself.

## Failure / Edge Cases

- Parser fails on malformed input.
- Job is cancelled or duplicated.

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

- [ ] No DB writer transaction wraps expensive parsing.
- [ ] Index failure does not break other search results.

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
feat(indexing): incremental index scheduler
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint IDX-06 — Incremental Index Scheduler only.

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
