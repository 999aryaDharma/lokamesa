# FND-07 — Desktop/WebView Production Host

**Status:** PLANNED  
**Priority:** P0  
**Kind:** feature  
**Recommended branch:** `feat/fnd-07-desktop-webview-production-host`

## Goal

Make the WPF + WebView2 application run the production React bundle and local privileged API without terminal processes.

## Depends On

- `FND-02` — Privileged Local API Security
- `FND-05` — Settings & Secret Storage

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/03-workspace-canvas-spec.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Desktop/MainWindow.xaml`
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/WebAppHostTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-02` — Privileged Local API Security
- `FND-05` — Settings & Secret Storage

**Produces**
- A tested implementation of `FND-07 — Desktop/WebView Production Host`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Host the built React bundle via a dedicated application origin/virtual host mapping.
- [ ] **Step 2:** Start/stop privileged local backend with desktop lifecycle.
- [ ] **Step 3:** Inject runtime API endpoint/session authorization through native bridge.
- [ ] **Step 4:** Implement development Vite URL mode and production static mode.
- [ ] **Step 5:** Handle WebView2 missing/runtime failure with a useful local error.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Production build opens without Vite dev server.
- [ ] Foreign navigation loses privileged bridge capability.
- [ ] Closing desktop stops privileged local host cleanly.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Production build opens without Vite dev server.

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

- [ ] User launches one Windows app.
- [ ] React static assets are local in production.
- [ ] WebView origin remains deterministic for Origin validation.

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
feat(foundation): desktop/webview production host
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-07 — Desktop/WebView Production Host only.

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
