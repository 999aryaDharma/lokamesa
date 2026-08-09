# SHR-09 — ngrok Transport

**Status:** PLANNED  
**Priority:** P1  
**Kind:** feature  
**Recommended branch:** `feat/shr-09-ngrok-transport`

## Goal

Expose ShareHost through ngrok using an adapter owned by the desktop app and a user-configured protected credential.

## Depends On

- `SHR-02` — Separate ShareHost Process
- `FND-05` — Settings & Secret Storage

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/10-sharing-security-ngrok.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `src/DocumentIDE.Core/Sharing/IShareTransport.cs`
- `src/DocumentIDE.Infrastructure/Sharing/NgrokTransport.cs`
- `tests/DocumentIDE.IntegrationTests/Sharing/NgrokTransportTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `SHR-02` — Separate ShareHost Process
- `FND-05` — Settings & Secret Storage

**Produces**
- A tested implementation of `SHR-09 — ngrok Transport`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Define IShareTransport.
- [ ] **Step 2:** Implement NgrokTransport start/stop/status.
- [ ] **Step 3:** Use protected ngrok credential reference.
- [ ] **Step 4:** Build final public URL + secret path.
- [ ] **Step 5:** Handle tunnel failure/restart cleanly.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Start returns public endpoint.
- [ ] Stop invalidates active transport.
- [ ] Credential never appears in logs.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Start returns public endpoint.

## Failure / Edge Cases

- Tunnel/server stops.
- Attacker supplies malformed token/path/input.

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

- [ ] Provider logic stays isolated.

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
feat(sharing): ngrok transport
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SHR-09 — ngrok Transport only.

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
