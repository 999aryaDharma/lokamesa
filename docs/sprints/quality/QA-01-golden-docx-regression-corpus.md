# QA-01 — Golden DOCX Regression Corpus

**Status:** PLANNED  
**Priority:** P0  
**Kind:** quality  
**Recommended branch:** `test/qa-01-golden-docx-regression-corpus`

## Goal

Build the canonical fixture corpus and structural invariance tests for DOCX safety.

## Depends On

- `DOCX-03` — Strict DOCX Text Mutation

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/11-testing-strategy.md`
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

- `tests/fixtures/docx/README.md`
- `tests/DocumentIDE.GoldenTests/DocxGoldenTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `DOCX-03` — Strict DOCX Text Mutation

**Produces**
- A tested implementation of `QA-01 — Golden DOCX Regression Corpus`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Add simple/split-runs/mixed-format/tables/headers/footers/sections/images/fields/tracked-changes fixtures.
- [ ] **Step 2:** Add hostile/resource fixtures.
- [ ] **Step 3:** Create helper assertions for package validity and unrelated-property invariance.
- [ ] **Step 4:** Run no-op fingerprint tests.
- [ ] **Step 5:** Document fixture purpose.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] All fixtures open/parse as expected.
- [ ] Strict replacement invariant suite passes.
- [ ] Hostile fixtures fail safely.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> All fixtures open/parse as expected.

## Failure / Edge Cases

- Test infrastructure itself fails.
- Failure is intermittent and needs reproduction evidence.

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

- [ ] Corpus is version controlled and deterministic.

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
test(quality): golden docx regression corpus
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint QA-01 — Golden DOCX Regression Corpus only.

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
