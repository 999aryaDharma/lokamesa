# DOCX-03 — Strict DOCX Text Mutation

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/docx-03-strict-docx-text-mutation`

## Goal

Replace a selected logical match while changing the minimum OOXML text structures and preserving unrelated formatting.

## Depends On

- `DOCX-02` — Logical Search Across Runs

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

- `src/DocumentIDE.Infrastructure/Docx/StrictTextMutator.cs`
- `tests/DocumentIDE.Core.Tests/Docx/StrictTextMutatorTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `DOCX-02` — Logical Search Across Runs

**Produces**
- A tested implementation of `DOCX-03 — Strict DOCX Text Mutation`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Copy working DOCX to temp fixture.
- [ ] **Step 2:** Apply replacement to mapped runs.
- [ ] **Step 3:** Preserve appropriate anchor formatting.
- [ ] **Step 4:** Remove only safely redundant emptied text/run structures.
- [ ] **Step 5:** Reopen/validate resulting package.
- [ ] **Step 6:** Compare unrelated formatting fixture invariants.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Split-run replacement produces correct visible text.
- [ ] Unrelated paragraph formatting unchanged.
- [ ] Invalid result never replaces original.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Split-run replacement produces correct visible text.

## Failure / Edge Cases

- OOXML contains unexpected structure.
- Package validation fails.

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

- [ ] Never raw-string replace XML.
- [ ] No whole-document HTML regeneration.

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
feat(docx): strict docx text mutation
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint DOCX-03 — Strict DOCX Text Mutation only.

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
