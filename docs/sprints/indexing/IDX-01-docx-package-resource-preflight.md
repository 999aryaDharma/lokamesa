# IDX-01 — DOCX Package Resource Preflight

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/idx-01-docx-package-resource-preflight`

## Goal

Reject hostile/oversized DOCX ZIP/XML packages before deep OpenXML parsing.

## Depends On

- `FND-04` — Background Job Runtime
- `FS-02` — Stable Document Identity

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
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

- `src/DocumentIDE.Infrastructure/Docx/DocxPackagePreflight.cs`
- `tests/DocumentIDE.IntegrationTests/Docx/DocxResourceBudgetTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-04` — Background Job Runtime
- `FS-02` — Stable Document Identity

**Produces**
- A tested implementation of `IDX-01 — DOCX Package Resource Preflight`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Inspect compressed size, ZIP entry count, declared uncompressed sizes, and per-part size.
- [ ] **Step 2:** Enforce V1 limits from spec.
- [ ] **Step 3:** Prohibit DTD/external entity behavior in XML readers.
- [ ] **Step 4:** Return typed `ResourceBudgetExceeded` warning.
- [ ] **Step 5:** Benchmark/decide whether hostile parsing needs worker-process isolation.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Entry-count bomb is rejected.
- [ ] Oversized single part is rejected.
- [ ] Highly compressed hostile fixture does not exhaust process.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Entry-count bomb is rejected.

## Failure / Edge Cases

- Parser fails on malformed input.
- Job is cancelled or duplicated.

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

- [ ] Source remains untouched.
- [ ] Rejected file remains visible with indexing warning.

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
security(indexing): docx package resource preflight
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint IDX-01 — DOCX Package Resource Preflight only.

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
