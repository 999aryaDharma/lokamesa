# FND-08 — Build, Verify & Artifact Scripts

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/fnd-08-build-verify-artifact-scripts`

## Goal

Create repeatable developer and release commands that agents can run from VS Code/PowerShell.

## Depends On

- `FND-01` — Repository Bootstrap
- `FND-07` — Desktop/WebView Production Host

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/12-implementation-plan.md`
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

- `scripts/bootstrap.ps1`
- `scripts/verify.ps1`
- `scripts/build-release.ps1`
- `.vscode/tasks.json`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-01` — Repository Bootstrap
- `FND-07` — Desktop/WebView Production Host

**Produces**
- A tested implementation of `FND-08 — Build, Verify & Artifact Scripts`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create `scripts/bootstrap.ps1`, `scripts/verify.ps1`, and `scripts/build-release.ps1`.
- [ ] **Step 2:** Make frontend build feed desktop publish automatically.
- [ ] **Step 3:** Fail scripts on test/lint/build failure.
- [ ] **Step 4:** Emit artifacts into deterministic `artifacts/` paths.
- [ ] **Step 5:** Document Windows prerequisites and VS Code launch tasks.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Fresh clone bootstrap succeeds with prerequisites installed.
- [ ] Verify script returns nonzero on failing test.
- [ ] Release build embeds production web assets.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Fresh clone bootstrap succeeds with prerequisites installed.

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

- [ ] One command verifies repository health.
- [ ] No manual copy of frontend assets required.

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
feat(foundation): build, verify & artifact scripts
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-08 — Build, Verify & Artifact Scripts only.

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
