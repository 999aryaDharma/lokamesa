# WORD-02 — Word Editing/Lock Detection

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/word-02-word-editing-lock-detection`

## Goal

Detect likely Word editing state and block unsafe app-originated mutation of open/changed DOCX files.

## Depends On

- `WORD-01` — Open DOCX in Microsoft Word
- `FS-03` — Runtime File Watcher

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/04-filesystem-sync-spec.md`
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

- `src/DocumentIDE.Infrastructure/Word/WordEditingStateDetector.cs`
- `src/DocumentIDE.Web/src/features/inspector/EditingInWordBadge.tsx`
- `tests/DocumentIDE.IntegrationTests/Word/WordEditingStateTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `WORD-01` — Open DOCX in Microsoft Word
- `FS-03` — Runtime File Watcher

**Produces**
- A tested implementation of `WORD-02 — Word Editing/Lock Detection`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Combine Word document-list probe, lock/access signal, and Word temp-file hints.
- [ ] **Step 2:** Expose `Editing in Word` state.
- [ ] **Step 3:** Refresh state on relevant filesystem/process changes.
- [ ] **Step 4:** Define safe false-positive/false-negative handling.
- [ ] **Step 5:** Integrate state into mutation preflight.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Open Word doc shows editing state.
- [ ] Closed Word doc clears state.
- [ ] Batch preflight can block/skip open doc.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Open Word doc shows editing state.

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

- [ ] No single heuristic is treated as infallible.

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
feat(word): word editing/lock detection
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint WORD-02 — Word Editing/Lock Detection only.

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
