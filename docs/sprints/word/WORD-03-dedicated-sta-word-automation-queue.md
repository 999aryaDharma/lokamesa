# WORD-03 — Dedicated STA Word Automation Queue

**Status:** PLANNED  
**Priority:** P0  
**Kind:** performance  
**Recommended branch:** `perf/word-03-dedicated-sta-word-automation-queue`

## Goal

Serialize all Word COM automation on one dedicated STA worker with concurrency exactly one.

## Depends On

- `FND-04` — Background Job Runtime
- `WORD-01` — Open DOCX in Microsoft Word

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/06-docx-openxml-word-integration.md`
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

- `src/DocumentIDE.Infrastructure/Word/WordAutomationQueue.cs`
- `tests/DocumentIDE.IntegrationTests/Word/WordAutomationQueueTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-04` — Background Job Runtime
- `WORD-01` — Open DOCX in Microsoft Word

**Produces**
- A tested implementation of `WORD-03 — Dedicated STA Word Automation Queue`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create dedicated STA worker thread/process boundary for Word automation adapter.
- [ ] **Step 2:** Queue ExportPdf/RenderPreview/etc.
- [ ] **Step 3:** Guarantee max COM concurrency `1`.
- [ ] **Step 4:** Implement cancellation between documents, not mid-unsafe COM write.
- [ ] **Step 5:** Release COM references deterministically.
- [ ] **Step 6:** Test concurrent callers are serialized.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Max observed COM operations = 1.
- [ ] Worker is STA.
- [ ] No repeated orphan Word process in integration run.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Max observed COM operations = 1.

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

- [ ] Indexing/OpenXML remains independently concurrent.

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
perf(word): dedicated sta word automation queue
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint WORD-03 — Dedicated STA Word Automation Queue only.

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
