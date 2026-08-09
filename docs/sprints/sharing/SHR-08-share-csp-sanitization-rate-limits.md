# SHR-08 — Share CSP, Sanitization & Rate Limits

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/shr-08-share-csp-sanitization-rate-limits`

## Goal

Harden the public viewer with strict CSP, HTML sanitization, and concrete endpoint/concurrency rate limits.

## Depends On

- `SHR-03` — Share Token Authentication
- `MD-02` — Markdown Preview & Sanitization

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
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

- `src/DocumentIDE.ShareHost/Security/ViewerSecurityHeaders.cs`
- `src/DocumentIDE.ShareHost/Security/ShareRateLimits.cs`
- `tests/DocumentIDE.IntegrationTests/Sharing/ShareWebSecurityTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `SHR-03` — Share Token Authentication
- `MD-02` — Markdown Preview & Sanitization

**Produces**
- A tested implementation of `SHR-08 — Share CSP, Sanitization & Rate Limits`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Apply CSP baseline from spec.
- [ ] **Step 2:** Set nosniff/referrer/frame/form policies.
- [ ] **Step 3:** Reuse maintained sanitizer for rendered Markdown.
- [ ] **Step 4:** Configure general 120/min/IP, search 30/min/IP, download 10/min/IP, concurrent download 2/IP defaults.
- [ ] **Step 5:** Return 429 with retry information.
- [ ] **Step 6:** Cap global expensive public work.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Injected script is neutralized.
- [ ] CSP lacks unsafe-eval/generic unsafe-inline script.
- [ ] Rate-limit tests return 429.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Injected script is neutralized.

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

- [ ] Public request cannot trigger Word COM.

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
security(sharing): share csp, sanitization & rate limits
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SHR-08 — Share CSP, Sanitization & Rate Limits only.

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
