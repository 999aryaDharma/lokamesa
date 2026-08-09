# CTX-02 — Explicit Dependency Expansion

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/ctx-02-explicit-dependency-expansion`

## Goal

Optionally expand a context pack using explicit backlinks/dependencies while remaining deterministic and bounded.

## Depends On

- `CTX-01` — Context Pack Core
- `GR-02` — Backlinks & Reference Inspector

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

- `src/DocumentIDE.Core/Context/ContextDependencyExpander.cs`
- `tests/DocumentIDE.Core.Tests/Context/ContextDependencyExpanderTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `CTX-01` — Context Pack Core
- `GR-02` — Backlinks & Reference Inspector

**Produces**
- A tested implementation of `CTX-02 — Explicit Dependency Expansion`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Add Include Explicit Dependencies option.
- [ ] **Step 2:** Traverse only explicit relation origins by default.
- [ ] **Step 3:** Detect cycles and deduplicate documents.
- [ ] **Step 4:** Set maximum relation depth.
- [ ] **Step 5:** Show added-by-dependency documents before export.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Cycle terminates.
- [ ] Semantic edge is not traversed by default.
- [ ] Expanded source list is reviewable.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Cycle terminates.

## Failure / Edge Cases

- Dependency graph contains cycles.
- Pack exceeds desired context budget.

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

- [ ] No hidden infinite graph expansion.

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
feat(context-packs): explicit dependency expansion
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint CTX-02 — Explicit Dependency Expansion only.

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
