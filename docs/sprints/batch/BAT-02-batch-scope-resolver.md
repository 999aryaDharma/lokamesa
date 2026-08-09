# BAT-02 — Batch Scope Resolver

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/bat-02-batch-scope-resolver`

## Goal

Resolve Selection/Folder/Workspace/Collection scopes deterministically with recursive folder inclusion and branch exclusions.

## Depends On

- `FS-01` — Folder Source Attachment
- `SRCH-05` — Realtime Search UI & Selection
- `BAT-01` — Batch Operation Plan Domain

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
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

- `src/DocumentIDE.Core/Batch/BatchScopeResolver.cs`
- `tests/DocumentIDE.Core.Tests/Batch/BatchScopeResolverTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FS-01` — Folder Source Attachment
- `SRCH-05` — Realtime Search UI & Selection
- `BAT-01` — Batch Operation Plan Domain

**Produces**
- A tested implementation of `BAT-02 — Batch Scope Resolver`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement Selection scope by explicit DocumentIds.
- [ ] **Step 2:** Implement Folder scope with subfolders ON by default.
- [ ] **Step 3:** Apply excluded branches.
- [ ] **Step 4:** Implement Workspace/Collection scopes.
- [ ] **Step 5:** Return scanned file counts before content matching.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Folder scope recursively includes nested files.
- [ ] Excluded Archive branch is absent.
- [ ] Selection never silently expands.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Folder scope recursively includes nested files.

## Failure / Edge Cases

- File changes after preview.
- One file fails while others are valid.

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

- [ ] Containment policy from FS-08 is reused.

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
feat(batch): batch scope resolver
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint BAT-02 — Batch Scope Resolver only.

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
