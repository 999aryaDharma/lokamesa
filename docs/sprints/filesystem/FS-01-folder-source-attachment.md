# FS-01 — Folder Source Attachment

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/fs-01-folder-source-attachment`

## Goal

Let a workspace attach one or more local folders without importing/copying documents.

## Depends On

- `FND-03` — SQLite Persistence Foundation
- `FND-07` — Desktop/WebView Production Host

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/02-domain-data-model.md`
- `docs/specs/03-workspace-canvas-spec.md`
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

- `src/DocumentIDE.Core/Workspaces/FolderSourceService.cs`
- `src/DocumentIDE.Web/src/features/workspace/AddFolderSource.tsx`
- `tests/DocumentIDE.IntegrationTests/Filesystem/FolderSourceTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-03` — SQLite Persistence Foundation
- `FND-07` — Desktop/WebView Production Host

**Produces**
- A tested implementation of `FS-01 — Folder Source Attachment`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement native folder picker.
- [ ] **Step 2:** Canonicalize and validate selected path.
- [ ] **Step 3:** Persist FolderSource and availability.
- [ ] **Step 4:** Enumerate supported `.docx`/`.md` documents recursively with containment rules.
- [ ] **Step 5:** Expose supported file count to UI.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Attach valid folder.
- [ ] Reject nonexistent/inaccessible path with typed error.
- [ ] Reopening workspace restores source reference.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Attach valid folder.

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

- [ ] No `.documentide` metadata is written into source folder.
- [ ] Attaching a folder does not mutate contents.

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
feat(filesystem): folder source attachment
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FS-01 — Folder Source Attachment only.

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
