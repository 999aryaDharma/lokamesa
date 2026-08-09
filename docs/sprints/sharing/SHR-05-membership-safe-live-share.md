# SHR-05 — Membership-Safe Live Share

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/shr-05-membership-safe-live-share`

## Goal

Refresh content only for existing share members while requiring explicit owner action for membership changes.

## Depends On

- `SHR-04` — Snapshot Share
- `FS-03` — Runtime File Watcher

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

- `src/DocumentIDE.Core/Sharing/LiveSharePublisher.cs`
- `tests/DocumentIDE.IntegrationTests/Sharing/LiveShareMembershipTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `SHR-04` — Snapshot Share
- `FS-03` — Runtime File Watcher

**Produces**
- A tested implementation of `SHR-05 — Membership-Safe Live Share`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Subscribe owner process to changes for current ShareMember set.
- [ ] **Step 2:** Refresh artifacts after saved member file changes.
- [ ] **Step 3:** Do not react to new files as new public members.
- [ ] **Step 4:** Add explicit Add/Remove member owner operation.
- [ ] **Step 5:** Version manifest/artifacts atomically.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] New file in shared folder is not public.
- [ ] Collection membership change does not auto-publish.
- [ ] Existing member update appears after refresh.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> New file in shared folder is not public.

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

- [ ] Live content ≠ live membership.

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
security(sharing): membership-safe live share
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SHR-05 — Membership-Safe Live Share only.

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
