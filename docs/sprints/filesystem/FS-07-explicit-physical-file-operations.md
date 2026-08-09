# FS-07 — Explicit Physical File Operations

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/fs-07-explicit-physical-file-operations`

## Goal

Implement safe explicit rename, move, duplicate, create-folder/create-file, and Recycle-Bin delete operations.

## Depends On

- `FS-02` — Stable Document Identity
- `FND-02` — Privileged Local API Security

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/04-filesystem-sync-spec.md`
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

- `src/DocumentIDE.Core/Filesystem/PhysicalFileOperations.cs`
- `src/DocumentIDE.Infrastructure/Filesystem/WindowsRecycleBin.cs`
- `tests/DocumentIDE.Core.Tests/Filesystem/PhysicalFileOperationsTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FS-02` — Stable Document Identity
- `FND-02` — Privileged Local API Security

**Produces**
- A tested implementation of `FS-07 — Explicit Physical File Operations`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement basename-only rename validation.
- [ ] **Step 2:** Implement explicit `MovePhysicalFile` separate from canvas movement.
- [ ] **Step 3:** Implement duplicate/create folder/create Markdown and template-DOCX hooks.
- [ ] **Step 4:** Implement Windows Recycle Bin delete.
- [ ] **Step 5:** Journal app-originated operations for watcher correlation.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Rename rejects separators, `..`, rooted path, reserved names, ADS syntax.
- [ ] Delete enters Recycle Bin.
- [ ] Canvas drag code path never calls this service.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Rename rejects separators, `..`, rooted path, reserved names, ADS syntax.

## Failure / Edge Cases

- File disappears between stat and action.
- Permission or lock changes during operation.

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

- [ ] No arbitrary path write endpoint.
- [ ] Destination collision is explicit.

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
security(filesystem): explicit physical file operations
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FS-07 — Explicit Physical File Operations only.

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
