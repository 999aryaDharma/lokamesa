# SCM-18 — Worktree API Surface

**Status:** READY
**Priority:** P0
**Kind:** feature
**Recommended branch:** `feat/worktree-api-surface`

## Goal

Expose the already-tested worktree services (SCM-15 discovery/open, SCM-16 create/remove/prune) over the
privileged Source Control API so screen 07 of the macOS design can be composed from real data.

## Why this sprint exists

SCM-15 and SCM-16 are `DONE` and shipped `IGitWorktreeService` (`ListWorktreesAsync`,
`OpenWorktreeAsync`) and `IGitWorktreeMutationService` (`CreateWorktreeAsync`, `RemoveWorktreeAsync`,
`RefreshWorktreesAsync`), plus `WorktreeList.tsx` / `WorktreeCreateDialog.tsx`. A route scan across
`src/DocumentIDE.Api` finds **no worktree HTTP route**, so the components have nothing to call and
screen 07 cannot be mounted. This is a small, purely additive surface sprint — no new Git behaviour.

## Depends On

- `SCM-15` — Git Worktree Discovery & Open Existing (`DONE`)
- `SCM-16` — Safe Git Worktree Create & Remove (`DONE`)

## Required Reading

- `AGENTS.md`
- `docs/specs/13-source-control-git-worktree-spec.md` (§16)
- `src/DocumentIDE.Core/SourceControl/IGitWorktreeService.cs`
- `src/DocumentIDE.Core/SourceControl/IGitWorktreeMutationService.cs`
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (the append-only region pattern)
- the existing SCM route tests in `tests/DocumentIDE.IntegrationTests/SourceControl/`

## In Scope

Append-only routes on `SourceControlController`:

| Method | Route | Service call |
| --- | --- | --- |
| `GET` | `api/source-control/worktrees` | `ListWorktreesAsync` |
| `POST` | `api/source-control/worktrees/open` | `OpenWorktreeAsync` |
| `POST` | `api/source-control/worktrees` | `CreateWorktreeAsync` |
| `DELETE` | `api/source-control/worktrees` | `RemoveWorktreeAsync` |
| `POST` | `api/source-control/worktrees/prune` | `RefreshWorktreesAsync` |

- Typed response DTOs mirroring the existing SCM conventions (`status`/`isSuccess`/`errorCode`/`message`).
- Route convention: `400` only for input rejected before any Git ran; `200` with a typed body for
  inspectable outcomes (`NotARepository`, `GitUnavailable`, `RequiresAttachmentFlow`, dirty/locked/main
  refusal, not-registered, parse error).
- `RequiresAttachmentFlow` is **not** an error: it is the typed signal that the caller must route the
  user through the existing FS-01 attachment flow. Never attach automatically.

## Out of Scope

- Any change to worktree services or Git invocations.
- Branch management, branch switching, force-removal.
- Mounting the UI (that is `UI-03`).
- Adding the controller to `WebAppHost.AllowedControllers` — `UI-03` owns reachability, so the two
  changes land together and are verified together.
- Unrelated refactors; reordering existing controller members.

## Planned Files

- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (append-only region: 5 routes + DTOs)
- `src/DocumentIDE.Api/Program.cs` (append-only DI for the two worktree services, if not already registered)
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeApiTests.cs`

## Required Tests

- [ ] `GET worktrees` lists real worktrees from a real repository (main + one created worktree), and
      returns typed `NotARepository` for a plain folder.
- [ ] `POST worktrees` creates a worktree from an **existing** branch/tag/commit; a non-existent ref is
      refused typed and creates nothing.
- [ ] `DELETE worktrees` removes only a clean, registered, non-main worktree; a dirty worktree is refused
      and still exists afterwards; the main worktree is refused.
- [ ] `POST worktrees/open` returns `RequiresAttachmentFlow` for a registered-but-unattached worktree and
      attaches nothing.
- [ ] `POST worktrees/prune` returns the re-read registry and deletes no valid worktree.
- [ ] Missing/invalid session token is `401`; a hostile Origin/Host is `403` (same guard as every other
      SCM route).
- [ ] An unsafe path (absolute, rooted, `..`, or outside the resolved worktree) is rejected before Git.

At minimum the first behaviour must be demonstrated by a failing test before implementation.

## Failure / Edge Cases

- Git missing; not a repository; malformed porcelain; worktree path disappears mid-operation.
- Index/lock contention during create/remove.
- Path with spaces / Unicode / non-ASCII.
- `RemoveWorktreeAsync` must never be forced: a locked or dirty worktree is a typed refusal.

## Security & File-Safety Gate

- [ ] no user file mutated through an unreviewed path (removal is explicit and never forced)
- [ ] no arbitrary shell command is built from a path
- [ ] no repository escape through path handling
- [ ] no secret/credential output
- [ ] no destructive action broadened in scope (no branch management, no force)
- [ ] higher-level safety rules stay stronger than convenience

## Acceptance Criteria

- [ ] Every worktree capability the UI needs is reachable over the privileged API with typed outcomes.
- [ ] No new Git behaviour is introduced — the routes are a thin, tested surface over SCM-15/16.
- [ ] The route set is append-only and does not disturb any existing SCM route.

## Definition of Done

- [ ] all steps complete; required tests pass; affected suite green; no skipped required test
- [ ] `git diff` self-reviewed; no unintended deletions
- [ ] handoff written (`docs/sprints/handoffs/SCM-18-HANDOFF.md`)
- [ ] board row left to the controller

## Commit Guidance

```text
feat(scm): expose worktree services over the privileged API
```
