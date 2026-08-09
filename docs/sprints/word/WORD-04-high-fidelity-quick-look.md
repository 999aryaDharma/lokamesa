# WORD-04 — High-Fidelity Quick Look

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/word-04-high-fidelity-quick-look`

## Goal

Provide Space/Preview read-only multi-page Quick Look using lazy Word-rendered PDF/image artifacts.

## Depends On

- `WORD-03` — Dedicated STA Word Automation Queue
- `DOCX-04` — Document Lens Metadata

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/06-docx-openxml-word-integration.md`
- `docs/specs/03-workspace-canvas-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Preview/QuickLookService.cs`
- `src/DocumentIDE.Web/src/features/quick-look/QuickLook.tsx`
- `tests/DocumentIDE.IntegrationTests/Preview/QuickLookTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `WORD-03` — Dedicated STA Word Automation Queue
- `DOCX-04` — Document Lens Metadata

**Produces**
- A tested implementation of `WORD-04 — High-Fidelity Quick Look`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Define preview cache key by DocumentId/fingerprint/renderer version.
- [ ] **Step 2:** Queue Word-to-temp-PDF render lazily.
- [ ] **Step 3:** Render pages for Quick Look and cache safely.
- [ ] **Step 4:** Invalidate cache after source change.
- [ ] **Step 5:** Implement Space open/Escape close UI.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Preview never edits source.
- [ ] Cache invalidates on fingerprint change.
- [ ] Public viewer does not directly call Word queue.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Preview never edits source.

## Failure / Edge Cases

- Word is unavailable, busy, modal, or user closes it.
- COM call fails.

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

- [ ] No eager Word render for all files at startup.

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
feat(word): high-fidelity quick look
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint WORD-04 — High-Fidelity Quick Look only.

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
