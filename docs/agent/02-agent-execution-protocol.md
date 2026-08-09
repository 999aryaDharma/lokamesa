# Agent Sprint Execution Protocol

For every sprint:

## 1. Intake
- Read `AGENTS.md`.
- Read sprint spec.
- Read all required specs.
- Verify dependencies are DONE in `docs/sprints/SPRINT-STATUS.md`.

## 2. Baseline
- Run existing relevant tests.
- Record existing failures before changing code.
- Inspect actual repository patterns; do not duplicate equivalent abstractions.

## 3. Branch
Use:
`<type>/<sprint-id>-<slug>`

Examples:
`feat/fs-03-runtime-file-watcher`
`security/fnd-02-privileged-local-api-security`

## 4. TDD
For each behavior:
1. write failing test,
2. prove it fails for the expected reason,
3. implement minimal behavior,
4. prove test passes,
5. refactor,
6. run affected suite.

## 5. Integration
Run sprint acceptance tests and repository verification.

## 6. Self-review
Review `git diff` for:
- scope creep,
- unsafe file paths,
- bypasses,
- secrets,
- skipped tests,
- dead abstractions,
- accidental source rewrite.

## 7. Commit
Commit a coherent reviewable unit.

## 8. Handoff
Update sprint status and create a handoff using the template.
