# Git / Branch / Commit Policy

## Branch
One sprint per branch by default.

Naming:
- `feat/<sprint-id>-<slug>`
- `fix/<sprint-id>-<slug>`
- `security/<sprint-id>-<slug>`
- `perf/<sprint-id>-<slug>`
- `test/<sprint-id>-<slug>`

## Commit size
Prefer commits that are independently testable and reviewable.

Typical sprint:
1. failing tests/fixture where useful,
2. implementation,
3. UI/integration,
4. docs/handoff.

Do not create meaningless `wip` commits on the final branch history.

## Message
Use:
`type(scope): description`

Examples:
`security(api): authenticate privileged localhost requests`
`feat(filesystem): preserve document identity across rename`
`test(docx): add split-run replacement regression`
