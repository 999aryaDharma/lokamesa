# CAN-03 — Document Paper Object & Thumbnail Shell

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/can-03-document-paper-object-thumbnail-shell`

## Goal

Render DOCX/Markdown as paper-sheet objects with exact filename and cached low-detail preview.

## Depends On

- `IDX-04` — Metadata Persistence
- `CAN-01` — Canvas Foundation

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/03-workspace-canvas-spec.md`
- `docs/specs/06-docx-openxml-word-integration.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Web/src/features/canvas/nodes/DocumentNode.tsx`
- `src/DocumentIDE.Core/Preview/ThumbnailContracts.cs`
- `tests/frontend/canvas/DocumentNode.test.tsx`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `IDX-04` — Metadata Persistence
- `CAN-01` — Canvas Foundation

**Produces**
- A tested implementation of `CAN-03 — Document Paper Object & Thumbnail Shell`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Render paper object + filename.
- [ ] **Step 2:** Define thumbnail cache API contract.
- [ ] **Step 3:** Show loading/stale/missing states.
- [ ] **Step 4:** Use recognizable but intentionally low-detail document image.
- [ ] **Step 5:** Keep type/status decoration visually restrained.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Filename is readable at normal zoom.
- [ ] Missing doc is visibly distinct.
- [ ] Thumbnail failure falls back without breaking canvas.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Filename is readable at normal zoom.

## Failure / Edge Cases

- Object state changes while layout action runs.
- Viewport contains many objects.

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

- [ ] Thumbnail is derived/cache data only.

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
feat(canvas): document paper object & thumbnail shell
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint CAN-03 — Document Paper Object & Thumbnail Shell only.

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
