# QA-05 — Public Share Security Audit

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/qa-05-public-share-security-audit`

## Goal

Attack token scope, traversal, membership, XSS/CSP, rate limits, dependency boundary, and revoke behavior.

## Depends On

- `SHR-08` — Share CSP, Sanitization & Rate Limits
- `SHR-10` — ShareHost OS Isolation Spike

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/11-testing-strategy.md`
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

- `tests/DocumentIDE.SecurityTests/PublicShareAttackSurfaceTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `SHR-08` — Share CSP, Sanitization & Rate Limits
- `SHR-10` — ShareHost OS Isolation Spike

**Produces**
- A tested implementation of `QA-05 — Public Share Security Audit`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Test token cross-share access.
- [ ] **Step 2:** Test traversal/absolute-path payloads.
- [ ] **Step 3:** Test Live membership leakage.
- [ ] **Step 4:** Test sanitizer/CSP script payloads.
- [ ] **Step 5:** Test download bypass/rate-limit bypass.
- [ ] **Step 6:** Check ShareHost architecture dependencies.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] No private document becomes reachable.
- [ ] Injected script does not execute.
- [ ] Revoked share fails.
- [ ] ShareHost lacks privileged services.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> No private document becomes reachable.

## Failure / Edge Cases

- Test infrastructure itself fails.
- Failure is intermittent and needs reproduction evidence.

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

- [ ] Residual same-user OS risk documented consistently.

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
security(quality): public share security audit
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint QA-05 — Public Share Security Audit only.

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
