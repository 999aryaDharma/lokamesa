# FND-04 — Background Job Runtime

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/fnd-04-background-job-runtime`

## Goal

Provide cancellable, deduplicated, bounded background jobs for indexing, previews, exports, reconciliation, and other expensive work.

## Depends On

- `FND-03` — SQLite Persistence Foundation

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
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

- `src/DocumentIDE.Core/Jobs/JobContracts.cs`
- `src/DocumentIDE.Infrastructure/Jobs/LocalJobScheduler.cs`
- `tests/DocumentIDE.Core.Tests/Jobs/JobSchedulerTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-03` — SQLite Persistence Foundation

**Produces**
- A tested implementation of `FND-04 — Background Job Runtime`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Define job identity, type, state, progress, cancellation, and retry contract.
- [ ] **Step 2:** Implement bounded worker queues with per-document serialization support.
- [ ] **Step 3:** Implement deduplication by job type + document + fingerprint.
- [ ] **Step 4:** Persist/recover only job state that materially needs crash recovery.
- [ ] **Step 5:** Expose observable progress to local UI.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Duplicate indexing jobs collapse.
- [ ] Cancellation stops before the next safe boundary.
- [ ] Two mutation jobs for the same document never execute concurrently.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Duplicate indexing jobs collapse.

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

- [ ] UI thread is not used for expensive jobs.
- [ ] Retry is limited to safe/idempotent jobs.
- [ ] Failures expose typed diagnostics.

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
feat(foundation): background job runtime
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-04 — Background Job Runtime only.

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
