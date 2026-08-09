# IDX-02 — DOCX Structural Parser

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/idx-02-docx-structural-parser`

## Goal

Extract searchable text and document structure from safe DOCX packages without modifying them.

## Depends On

- `IDX-01` — DOCX Package Resource Preflight

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/06-docx-openxml-word-integration.md`
- `docs/specs/05-search-indexing-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Infrastructure/Docx/DocxStructureParser.cs`
- `tests/DocumentIDE.Core.Tests/Docx/DocxStructureParserTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `IDX-01` — DOCX Package Resource Preflight

**Produces**
- A tested implementation of `IDX-02 — DOCX Structural Parser`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Parse body paragraphs, tables, headers, footers, sections, and relationships needed by metadata.
- [ ] **Step 2:** Extract structured text without raw XML search hacks.
- [ ] **Step 3:** Record table/header/footer origin.
- [ ] **Step 4:** Return parser version and diagnostics.
- [ ] **Step 5:** Verify source fingerprint unchanged.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Simple/multi-section fixtures parse.
- [ ] Headers/tables are separated by origin.
- [ ] No-op parse leaves byte fingerprint unchanged.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Simple/multi-section fixtures parse.

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

- [ ] Field instructions are not treated as ordinary destructive text.
- [ ] Malformed package gives typed error.

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
feat(indexing): docx structural parser
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint IDX-02 — DOCX Structural Parser only.

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
