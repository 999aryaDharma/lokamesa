# SHR-04 — Snapshot Share

**Status:** PLANNED  
**Priority:** P0  
**Kind:** security  
**Recommended branch:** `security/shr-04-snapshot-share`

## Goal

Publish a frozen read-only share package containing only explicit member artifacts as they existed at publication time.

## Depends On

- `SHR-03` — Share Token Authentication
- `WORD-04` — High-Fidelity Quick Look
- `MD-02` — Markdown Preview & Sanitization

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

- `src/DocumentIDE.Core/Sharing/SnapshotSharePublisher.cs`
- `src/DocumentIDE.ShareHost/Endpoints/SnapshotEndpoints.cs`
- `tests/DocumentIDE.IntegrationTests/Sharing/SnapshotShareTests.cs`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `SHR-03` — Share Token Authentication
- `WORD-04` — High-Fidelity Quick Look
- `MD-02` — Markdown Preview & Sanitization

**Produces**
- A tested implementation of `SHR-04 — Snapshot Share`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Prepare DOCX preview artifacts owner-side.
- [ ] **Step 2:** Prepare sanitized Markdown rendered artifact.
- [ ] **Step 3:** Copy only allowed public metadata.
- [ ] **Step 4:** Freeze manifest/version.
- [ ] **Step 5:** Serve through ShareHost viewer.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Later local edit does not change Snapshot.
- [ ] Nonmember ID returns not found/denied.
- [ ] ShareHost does not invoke Word.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Later local edit does not change Snapshot.

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

- [ ] Original download absent unless permission later enables it.

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
security(sharing): snapshot share
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint SHR-04 — Snapshot Share only.

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
