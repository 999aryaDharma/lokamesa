# REL-01 — Windows Installer Packaging

**Status:** PLANNED  
**Priority:** P0  
**Kind:** release  
**Recommended branch:** `chore/rel-01-windows-installer-packaging`

## Goal

Package the self-contained win-x64 desktop app and ShareHost into a normal Windows installer without bundling user documents.

## Depends On

- `FND-08` — Build, Verify & Artifact Scripts
- `QA-03` — Privileged Local API Security Audit

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/12-implementation-plan.md`
- `docs/specs/01-system-architecture.md`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements exactly the goal and concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- downstream sprint features,
- unrelated refactors,
- product behavior not required by this sprint,
- weakening a higher-level specification to make implementation easier.

## Planned Files / Artifacts

- `installer/DocumentIDE.wxs`
- `scripts/build-release.ps1`
- `tests/DocumentIDE.InstallTests/InstallerSmoke.md`

If an earlier completed sprint established an equivalent file with a different but documented name, modify that existing file rather than creating a duplicate abstraction. Record the mapping in the handoff.

## Interfaces & Contracts

**Consumes**
- `FND-08` — Build, Verify & Artifact Scripts
- `QA-03` — Privileged Local API Security Audit

**Produces**
- A tested implementation of `REL-01 — Windows Installer Packaging`.
- Stable behavior required by downstream sprints.
- Regression tests for the acceptance criteria below.

## Implementation Steps

- [ ] **Step 1:** Publish self-contained win-x64.
- [ ] **Step 2:** Package WPF/WebView assets/ShareHost/runtime dependencies.
- [ ] **Step 3:** Use WiX or chosen full-trust installer.
- [ ] **Step 4:** Check/install WebView2 Evergreen prerequisite.
- [ ] **Step 5:** Create Start Menu/uninstall entries.
- [ ] **Step 6:** Verify uninstall never removes source documents.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] Clean test VM installs/runs.
- [ ] Uninstall preserves `%USERPROFILE%` source documents.
- [ ] App starts without dev server/.NET install.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> Clean test VM installs/runs.

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

- [ ] Word remains external prerequisite for Word-dependent features.

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
chore(release): windows installer packaging
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint REL-01 — Windows Installer Packaging only.

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
