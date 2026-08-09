# FND-02 — Privileged Local API Security

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/fnd-02-privileged-local-api-security`

## Goal

Protect the loopback privileged API from foreign browser origins and unauthenticated localhost requests.

## Depends On

- `FND-01` — Repository Bootstrap

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/10-sharing-security-ngrok.md`
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

- `src/DocumentIDE.Api/Security/PrivilegedRequestGuard.cs`
- `src/DocumentIDE.Desktop/WebView/LocalSessionBridge.cs`
- `tests/DocumentIDE.IntegrationTests/Security/PrivilegedApiSecurityTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-01` — Repository Bootstrap

**Produces**
- A tested implementation of `FND-02 — Privileged Local API Security`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Bind the privileged API to loopback on a runtime-selected port.
- [ ] **Step 2:** Generate a 256-bit per-startup session secret in native host memory.
- [ ] **Step 3:** Implement strict Host + dedicated WebView2 Origin validation and reject `Origin: null`.
- [ ] **Step 4:** Require the startup secret on every privileged request through a native/WebView request bridge.
- [ ] **Step 5:** Disallow wildcard CORS and state-changing GET endpoints.
- [ ] **Step 6:** Add Fetch-Metadata checks as defense-in-depth.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Foreign Origin is rejected.
- [ ] DNS-rebinding-style Host mismatch is rejected.
- [ ] Missing/invalid startup secret is rejected.
- [ ] Valid WebView origin + secret succeeds.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Foreign Origin is rejected.

## Failure / Edge Cases

- Browser omits Fetch Metadata headers.
- Origin is malformed.
- Host header contains unexpected name/port.
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

- [ ] Loopback alone is never treated as authentication.
- [ ] Secret is not persisted to disk/localStorage.
- [ ] Security middleware runs before privileged controllers.

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
security(foundation): privileged local api security
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-02 — Privileged Local API Security only.

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
