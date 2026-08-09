# FND-01 — Repository Bootstrap

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/fnd-01-repository-bootstrap`

## Goal

Create the greenfield repository/solution structure so backend, desktop shell, frontend, tests, and docs build from one checkout.

## Depends On

- None. This is a root sprint.

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/12-implementation-plan.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `DocumentIDE.sln`
- `src/DocumentIDE.Desktop/App.xaml`
- `src/DocumentIDE.Web/package.json`
- `scripts/verify.ps1`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- None. This is a root sprint.

**Produces**
- A tested implementation of `FND-01 — Repository Bootstrap`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create `DocumentIDE.sln` and projects for Core, Infrastructure, Api, Desktop, ShareHost, and tests.
- [ ] **Step 2:** Create `src/DocumentIDE.Web` using React + TypeScript + Vite + Tailwind.
- [ ] **Step 3:** Wire WebView2 desktop shell to a development URL and a production static-asset mode.
- [ ] **Step 4:** Add formatting/linting/editorconfig and deterministic restore commands.
- [ ] **Step 5:** Add backend/frontend smoke tests and a repository-level verification script.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] `dotnet build` succeeds from repository root.
- [ ] Backend smoke test passes.
- [ ] Frontend unit smoke test and production build pass.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `dotnet build` succeeds from repository root.

## Failure / Edge Cases

- Startup fails partway through initialization.
- Dependency is unavailable or misconfigured.

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

- [ ] One checkout can build backend + frontend.
- [ ] Desktop shell opens the React surface.
- [ ] No business feature is implemented yet.

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
feat(foundation): repository bootstrap
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-01 — Repository Bootstrap only.

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
