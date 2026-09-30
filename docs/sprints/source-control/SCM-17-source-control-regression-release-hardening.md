# SCM-17 — Source Control Regression & Release Hardening

**Status:** BLOCKED  
**Priority:** P0  
**Kind:** quality  
**Recommended branch:** `feat/source-control-regression-release-hardening`

## Goal

Make the Source Control feature release-safe with fixture repositories, concurrency tests, Windows path/security tests, remote/worktree coverage, and release-gate evidence.

## Depends On

- SCM-04
- SCM-05
- SCM-09
- SCM-12
- SCM-13
- SCM-14
- SCM-16
- QA-02

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/11-testing-strategy.md`
- `docs/specs/13-source-control-git-worktree-spec.md`
- `docs/specs/12-implementation-plan.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- branch-management UI or branch lifecycle unless this sprint explicitly says otherwise,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `tests/DocumentIDE.IntegrationTests/SourceControl/SourceControlRegressionSuite.cs`
- `tests/DocumentIDE.SecurityTests/SourceControl/GitProcessSecurityTests.cs`
- `docs/quality/source-control-release-checklist.md`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- SCM-04
- SCM-05
- SCM-09
- SCM-12
- SCM-13
- SCM-14
- SCM-16
- QA-02

**Produces**
- A tested implementation of `SCM-17 — Source Control Regression & Release Hardening`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Build fixture repository matrix.
- [ ] **Step 2:** Run Windows path/Unicode and injection tests.
- [ ] **Step 3:** Run concurrency tests.
- [ ] **Step 4:** Run remote/worktree integration suite.
- [ ] **Step 5:** Write release evidence and residual-risk notes.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Full SCM fixture suite passes on Windows.
- [ ] No shell injection or repository escape regression exists.
- [ ] Stage/commit mutation queue shows no index corruption.
- [ ] Remote/worktree fixture suite passes.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Full SCM fixture suite passes on Windows.

## Failure / Edge Cases

- Git is not installed or is not discoverable.
- Repository/worktree disappears during an operation.
- File path contains spaces, Unicode, or special characters.
- Git operation is already running elsewhere and index lock is unavailable.
- Word has changed a DOCX between status/diff/stage operations.
- Repository reports a hook, permission, authentication, conflict, or remote rejection failure.

Failures must produce typed/inspectable outcomes rather than silent fallback where the subsystem spec requires determinism.

## Security & File-Safety Gate

Before completion, explicitly confirm:

- [ ] no user file was mutated through an unreviewed/bypass path
- [ ] no arbitrary shell command can be injected through a filename/path
- [ ] no repository escape is possible through path handling
- [ ] no secret/credential output is exposed in UI logs
- [ ] no destructive action silently broadened its scope
- [ ] relevant higher-level safety rules remain stronger than convenience

Mark an item N/A in the handoff only with a one-sentence justification.

## Acceptance Criteria

- [ ] All SCM release-gate tests are automated or explicitly justified manual Windows tests.
- [ ] No branch-management scope has leaked into V1.
- [ ] Source Control can be disabled without breaking non-Git Document IDE workflows.

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
test(scm): source-control-regression-release-hardening
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SCM-17 — Source Control Regression & Release Hardening only.

Read AGENTS.md, this sprint document, and every Required Reading document first.
Verify every Depends On sprint is DONE before editing.

Use TDD. Do not implement downstream features and do not weaken higher-level specs.
Do not add branch-management UI or behavior that is outside this sprint's scope.

At completion:
1. run all Required Tests and affected repository verification,
2. self-review git diff,
3. map Acceptance Criteria to evidence,
4. update SPRINT-STATUS.md,
5. write the sprint handoff,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the sprint commit guidance.
```
