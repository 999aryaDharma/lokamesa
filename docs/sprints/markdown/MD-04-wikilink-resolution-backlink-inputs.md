# MD-04 — Wikilink Resolution & Backlink Inputs

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/md-04-wikilink-resolution-backlink-inputs`

## Goal

Resolve `[[wikilinks]]` deterministically and surface ambiguity rather than silently picking a duplicate filename.

## Depends On

- `MD-03` — Frontmatter & Explicit Link Index

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/08-markdown-graph-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Markdown/WikilinkResolver.cs`
- `tests/DocumentIDE.Core.Tests/Markdown/WikilinkResolverTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `MD-03` — Frontmatter & Explicit Link Index

**Produces**
- A tested implementation of `MD-04 — Wikilink Resolution & Backlink Inputs`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Implement relative exact-path resolution first.
- [ ] **Step 2:** Implement unique filename-stem fallback.
- [ ] **Step 3:** Return ambiguity list when multiple candidates.
- [ ] **Step 4:** Support wikilink alias syntax.
- [ ] **Step 5:** Expose unresolved/ambiguous diagnostics.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Unique stem resolves.
- [ ] Two same-stem docs are ambiguous.
- [ ] Alias preserves target identity.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Unique stem resolves.

## Failure / Edge Cases

- Markdown contains malformed/unsafe HTML.
- External save conflicts with editor state.

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

- [ ] No silent arbitrary candidate choice.

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
feat(markdown): wikilink resolution & backlink inputs
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint MD-04 — Wikilink Resolution & Backlink Inputs only.

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
