# Ownership & Worktrees

## Single-owner rule

Each IN_PROGRESS sprint has one implementation owner:

```text
owner = Codex | Antigravity
```

The other agent may review after implementation handoff but should not concurrently edit the branch.

## Recommended worktree layout for parallel READY sprints

```text
repo/
worktrees/
  codex-<sprint-id>/
  antigravity-<sprint-id>/
```

Each worktree gets its own branch.

Do not share:
- build output directories,
- temporary test artifacts,
- database instances that mutate the same test fixture.

## Shared files

High-conflict files include:
- `DocumentIDE.sln`
- core migrations
- central DI registration
- package manifests
- `SPRINT-STATUS.md`

Parallel agents must coordinate before editing these.

After integrating parallel sprints:
1. resolve conflicts intentionally,
2. rerun both affected test suites,
3. rerun repository verification,
4. update status only after integration is green.
