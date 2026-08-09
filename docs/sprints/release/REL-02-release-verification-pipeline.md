# REL-02 — Release Verification Pipeline

**Status:** PLANNED  
**Priority:** P0  
**Kind:** release  
**Recommended branch:** `chore/rel-02-release-verification-pipeline`

## Goal

Create a release gate that runs all safety/security/golden/performance checks before producing final artifacts.

## Depends On

- `REL-01` — Windows Installer Packaging
- `QA-01` — Golden DOCX Regression Corpus
- `QA-02` — Filesystem & Indexing Soak
- `QA-04` — Batch Safety Regression Suite
- `QA-05` — Public Share Security Audit
- `QA-06` — Performance Benchmark Gate
- `QA-07` — Crash & Interrupted-Operation Recovery

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/11-testing-strategy.md`
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

- `scripts/verify-release.ps1`
- `.github/workflows/release.yml`
- `docs/quality/release-checklist.md`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `REL-01` — Windows Installer Packaging
- `QA-01` — Golden DOCX Regression Corpus
- `QA-02` — Filesystem & Indexing Soak
- `QA-04` — Batch Safety Regression Suite
- `QA-05` — Public Share Security Audit
- `QA-06` — Performance Benchmark Gate
- `QA-07` — Crash & Interrupted-Operation Recovery

**Produces**
- A tested implementation of `REL-02 — Release Verification Pipeline`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Create release verification orchestration.
- [ ] **Step 2:** Run backend/frontend unit/integration tests.
- [ ] **Step 3:** Run Windows Word/COM suite on capable runner.
- [ ] **Step 4:** Run security/safety/golden gates.
- [ ] **Step 5:** Attach test summary and checksums to artifacts.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Any release-gate failure blocks packaging/release.
- [ ] Artifact checksum generated.
- [ ] Verification report names exact commit.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Any release-gate failure blocks packaging/release.

## Failure / Edge Cases

- Clean-machine prerequisite missing.
- Release gate failure blocks artifact.

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

- [ ] No 'green by skipping tests' policy.

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
chore(release): release verification pipeline
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint REL-02 — Release Verification Pipeline only.

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
