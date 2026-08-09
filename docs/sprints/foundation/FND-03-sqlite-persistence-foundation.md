# FND-03 — SQLite Persistence Foundation

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/fnd-03-sqlite-persistence-foundation`

## Goal

Create the authoritative application metadata database, migrations, repositories, and concurrency baseline.

## Depends On

- `FND-01` — Repository Bootstrap

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/02-domain-data-model.md`
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

- `src/DocumentIDE.Infrastructure/Persistence/AppDb.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/001_Initial.cs`
- `tests/DocumentIDE.IntegrationTests/Persistence/SqliteConcurrencyTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-01` — Repository Bootstrap

**Produces**
- A tested implementation of `FND-03 — SQLite Persistence Foundation`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create migration runner and initial tables for Workspace, FolderSource, Document, and WorkspaceDocument.
- [ ] **Step 2:** Configure `journal_mode=WAL`, `foreign_keys=ON`, and `busy_timeout=5000`.
- [ ] **Step 3:** Create transaction/repository abstractions with short write transactions.
- [ ] **Step 4:** Add database versioning and startup migration flow.
- [ ] **Step 5:** Add integration tests for concurrent reader/writer behavior.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Migrations apply on empty database.
- [ ] Foreign-key violations fail.
- [ ] FTS-like read can continue during unrelated short write transaction.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Migrations apply on empty database.

## Failure / Edge Cases

- Startup fails partway through initialization.
- Dependency is unavailable or misconfigured.

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

- [ ] Database lives under application-controlled local state.
- [ ] No document contents become source-of-truth database blobs.
- [ ] WAL is verified at runtime.

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
feat(foundation): sqlite persistence foundation
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-03 — SQLite Persistence Foundation only.

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
