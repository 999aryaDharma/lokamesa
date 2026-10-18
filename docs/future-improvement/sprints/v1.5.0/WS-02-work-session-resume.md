# WS-02 — Work Session Resume & Context Restoration

**Label:** `future-improvement`  
**Roadmap version:** `v1.5.0`  
**Status:** SPECIFIED  
**Priority:** `P0`  
**Kind:** feature  

## Goal

Work Session Resume & Context Restoration

## Depends On

- `WS-01`

**Promotion rule:** this sprint is not eligible for the active V1 board until the owner promotes its roadmap version.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md`
- `docs/future-improvement/specs/00-shared-future-contracts.md`
- Relevant feature specification for `v1.5.0`

Also read `AGENTS.md` and the agent policies under `docs/agent/`.

## In Scope

This sprint implements only the documented goal and the concrete steps below. Changes needed to satisfy tests/acceptance criteria are in scope.

## Out of Scope

- unrelated feature work,
- product-level branch-management behavior,
- weakening existing source-of-truth or safety rules,
- implementation of later roadmap versions.

## Planned Files / Artifacts

Names below are planning targets; adapt to the repository's established paths if an equivalent boundary already exists.

- application/domain or infrastructure files for the feature
- frontend feature files
- targeted unit/integration tests
- sprint handoff under `docs/future-improvement/handoffs/` once promoted

## Interfaces & Contracts

**Consumes**
- Existing V1 services named in Depends On
- Feature specification for the roadmap version

**Produces**
- Tested implementation of `WS-02 — Work Session Resume & Context Restoration`
- Stable behavior for downstream future sprints
- Regression tests for acceptance criteria

## Implementation Steps

- [ ] Capture/restore active workspace context.
- [ ] Restore selection, view, review position, and canvas viewport where safe.
- [ ] Detect missing sources without destructive fallback.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at the end.

## Required Tests

- [ ] persistence/restart tests
- [ ] activity attribution tests
- [ ] workspace isolation tests

## Failure / Edge Cases

- dependency becomes unavailable,
- workspace/document disappears during the operation,
- Unicode/long/special paths,
- concurrent external edits,
- stale indexed state,
- permission denied,
- feature metadata is missing or corrupt.

Failures must remain typed/inspectable and must not silently broaden scope.

## Security & File-Safety Gate

Before completion, explicitly confirm:

- [ ] feature metadata cannot mutate document contents outside approved services
- [ ] no arbitrary shell command can be injected through filenames or metadata
- [ ] physical paths do not become public/share identifiers
- [ ] destructive actions retain confirmation/recovery checks
- [ ] no new feature creates a second source of truth for document contents

## Acceptance Criteria

- [ ] Resume restores safe UI context
- [ ] Missing files remain missing, not recreated
- [ ] No document bytes are copied into session state

## Definition of Done

- [ ] implementation steps complete
- [ ] required tests pass
- [ ] affected repository verification passes
- [ ] no required test skipped
- [ ] no unresolved sprint-required placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] future-improvement status updated when promoted
- [ ] sprint handoff written using the standard handoff template when promoted
- [ ] coherent implementation commit created

## Commit Guidance

Recommended final commit:

```text
feat(future): ws-02-work-session-resume
```

Multiple commits are allowed when independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement WS-02 — Work Session Resume & Context Restoration only.

Read AGENTS.md, this sprint document, the shared future contracts, and the feature specification first.
This sprint is part of roadmap v1.5.0; do not implement later roadmap versions.
Use TDD. Preserve all existing V1 source-of-truth, filesystem, document-fidelity, Git, and safety semantics.
Do not add product-level branch-management features.

At completion:
1. run the required tests and affected repository verification,
2. self-review git diff,
3. map acceptance criteria to evidence,
4. write the handoff when this roadmap version is promoted,
5. report changed files, exact commands/results, deviations/risks,
6. commit using the sprint commit guidance.
```
