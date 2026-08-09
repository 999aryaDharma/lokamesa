# IDX-03 — Markdown Structural Parser

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/idx-03-markdown-structural-parser`

## Goal

Parse Markdown headings, frontmatter, links, wikilinks, and searchable body while preserving source text.

## Depends On

- `FND-04` — Background Job Runtime
- `FS-02` — Stable Document Identity

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/08-markdown-graph-spec.md`
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

- `src/DocumentIDE.Core/Markdown/MarkdownStructureParser.cs`
- `tests/DocumentIDE.Core.Tests/Markdown/MarkdownStructureParserTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-04` — Background Job Runtime
- `FS-02` — Stable Document Identity

**Produces**
- A tested implementation of `IDX-03 — Markdown Structural Parser`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Parse YAML frontmatter into structured metadata.
- [ ] **Step 2:** Extract headings and normal links.
- [ ] **Step 3:** Extract `[[wikilinks]]` and aliases.
- [ ] **Step 4:** Preserve raw source separately from derived parse result.
- [ ] **Step 5:** Version parser output.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Frontmatter fixture parses.
- [ ] Relative link/wikilink fixture parses.
- [ ] Malformed frontmatter does not destroy body indexing.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Frontmatter fixture parses.

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

- [ ] Parser never rewrites Markdown.
- [ ] Ambiguous link resolution is deferred to relation service.

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
feat(indexing): markdown structural parser
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint IDX-03 — Markdown Structural Parser only.

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
