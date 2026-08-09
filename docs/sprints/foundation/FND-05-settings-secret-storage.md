# FND-05 — Settings & Secret Storage

**Status:** PLANNED  
**Priority:** P1  
**Kind:** security  
**Recommended branch:** `security/fnd-05-settings-secret-storage`

## Goal

Persist non-sensitive settings and protect ngrok/external-AI secrets using Windows-protected storage.

## Depends On

- `FND-02` — Privileged Local API Security
- `FND-03` — SQLite Persistence Foundation

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/02-domain-data-model.md`
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

- `src/DocumentIDE.Core/Settings/AppSettings.cs`
- `src/DocumentIDE.Infrastructure/Security/WindowsSecretStore.cs`
- `tests/DocumentIDE.IntegrationTests/Security/SecretStoreTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-02` — Privileged Local API Security
- `FND-03` — SQLite Persistence Foundation

**Produces**
- A tested implementation of `FND-05 — Settings & Secret Storage`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create typed settings model with defaults from specs.
- [ ] **Step 2:** Separate secret references from normal SQLite settings.
- [ ] **Step 3:** Implement Windows-protected secret storage adapter.
- [ ] **Step 4:** Add settings validation and migration/versioning.
- [ ] **Step 5:** Ensure logs and exception messages redact secrets.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Round-trip normal settings.
- [ ] Secret can be stored/read by current user context.
- [ ] Secret never appears in structured logs.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Round-trip normal settings.

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

- [ ] No plaintext long-lived API/ngrok credential in app database.
- [ ] Startup session secret is not persisted here.

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
security(foundation): settings & secret storage
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint FND-05 — Settings & Secret Storage only.

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
