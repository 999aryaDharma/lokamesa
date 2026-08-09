# QA-04 — Batch Safety Regression Suite

**Status:** PLANNED  
**Priority:** P0  
**Kind:** safety  
**Recommended branch:** `fix/qa-04-batch-safety-regression-suite`

## Goal

Continuously prove stale/locked/failing/low-disk destructive operations cannot corrupt or silently alter user files.

## Depends On

- `BAT-10` — Strict Batch Rename
- `HIST-02` — Conflict-Aware Undo

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/11-testing-strategy.md`
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

- `tests/DocumentIDE.SafetyTests/BatchSafetyRegressionTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `BAT-10` — Strict Batch Rename
- `HIST-02` — Conflict-Aware Undo

**Produces**
- A tested implementation of `QA-04 — Batch Safety Regression Suite`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Inject mutation validation failure.
- [ ] **Step 2:** Inject destination lock/write failure.
- [ ] **Step 3:** Change file after preview then apply.
- [ ] **Step 4:** Simulate insufficient recovery capacity.
- [ ] **Step 5:** Verify strict-operation non-target invariants.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Original survives every injected failure.
- [ ] Stale plan rejected.
- [ ] No-snapshot capacity blocks apply.
- [ ] Undo conflict does not overwrite newer edit.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Original survives every injected failure.

## Failure / Edge Cases

- Test infrastructure itself fails.
- Failure is intermittent and needs reproduction evidence.

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

- [ ] Runs in release gate.

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
fix(quality): batch safety regression suite
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint QA-04 — Batch Safety Regression Suite only.

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
