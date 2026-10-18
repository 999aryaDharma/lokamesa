# SCM-15 — Git Worktree Discovery & Open Existing

**Status:** BLOCKED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/git-worktree-discovery-open-existing`

## Goal

Expose registered Git worktrees and let the user open an existing worktree as the active Document IDE context.

## Depends On

- SCM-01
- FS-03
- FND-07

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/04-filesystem-sync-spec.md`
- `docs/specs/13-source-control-git-worktree-spec.md`
- `docs/specs/03-workspace-canvas-spec.md`

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

- `src/DocumentIDE.Infrastructure/SourceControl/GitWorktreeService.cs`
- `src/DocumentIDE.Web/src/features/source-control/WorktreeList.tsx`
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeDiscoveryTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- SCM-01
- FS-03
- FND-07

**Produces**
- A tested implementation of `SCM-15 — Git Worktree Discovery & Open Existing`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement worktree list parser.
- [ ] **Step 2:** Resolve worktree paths safely.
- [ ] **Step 3:** Wire open-existing-worktree action to workspace/source context.
- [ ] **Step 4:** Preserve existing canvas spatial memory when context changes.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Main and linked worktrees are listed.
- [ ] Open existing worktree updates active filesystem context.
- [ ] No branch switch occurs when opening a worktree.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Main and linked worktrees are listed.

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

- [ ] Worktree list is derived from Git.
- [ ] Current branch metadata is read-only.
- [ ] Opening worktree does not create a branch or alter Git history.

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
feat(scm): git-worktree-discovery-open-existing
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SCM-15 — Git Worktree Discovery & Open Existing only.

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
