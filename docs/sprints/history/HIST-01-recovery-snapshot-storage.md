# HIST-01 — Recovery Snapshot Storage

**Status:** PLANNED  
**Priority:** P0  
**Kind:** safety  
**Recommended branch:** `fix/hist-01-recovery-snapshot-storage`

## Goal

Create 30-day per-operation snapshots with 5 GiB budget, 80% warning, and fail-closed capacity preflight.

## Depends On

- `FND-03` — SQLite Persistence Foundation
- `FS-02` — Stable Document Identity

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

- `src/DocumentIDE.Core/History/RecoverySnapshotService.cs`
- `src/DocumentIDE.Infrastructure/History/HistoryStorage.cs`
- `tests/DocumentIDE.IntegrationTests/History/RecoveryStorageTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-03` — SQLite Persistence Foundation
- `FS-02` — Stable Document Identity

**Produces**
- A tested implementation of `HIST-01 — Recovery Snapshot Storage`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Persist snapshot metadata including size/fingerprints/expiry.
- [ ] **Step 2:** Copy only files affected by approved operation.
- [ ] **Step 3:** Implement history budget/free-disk preflight.
- [ ] **Step 4:** Warn at 80% and block destructive apply if complete snapshots cannot fit.
- [ ] **Step 5:** Clean only expired snapshots unless owner explicitly clears history.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Snapshot created before mutation.
- [ ] Insufficient capacity blocks operation.
- [ ] Unexpired snapshot not silently evicted.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Snapshot created before mutation.

## Failure / Edge Cases

- Disk capacity is insufficient.
- File was edited after operation.

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

- [ ] Retention target remains 30 days.
- [ ] Failure to snapshot means failure to mutate.

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
fix(history): recovery snapshot storage
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint HIST-01 — Recovery Snapshot Storage only.

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
