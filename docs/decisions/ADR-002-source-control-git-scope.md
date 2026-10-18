# ADR-002 — Source Control (Git) Scope in V1

**Status:** Accepted

## Context

The Source Control add-on (`docs/SCM-ADDON-README.md`) extends Document IDE V1 with an in-app Git workflow defined by `docs/specs/13-source-control-git-worktree-spec.md` and decomposed into sprints `SCM-01` … `SCM-17`. The add-on overlays new roadmap files onto an existing 98-sprint plan (making it 115 sprints) without weakening the local-file safety, explicit-approval, and separation-of-concerns rules of the master specification.

The add-on package states a deliberate scope decision: the feature follows Git's working-tree → staging → commit model with explicit Fetch / Pull / Push and bounded Git Worktree support, while **branch-management UX is intentionally excluded** from V1.

## Decision

Document IDE V1 implements source control as a controlled in-app Git workflow:

- Git remains the source of truth for Git state; Document IDE History remains the application operation/recovery history (correlated in `SCM-09`, never merged).
- All Git operations are invoked through a controlled backend process runner (`SCM-01`) using the structured Git executable boundary — the frontend never invokes `git.exe` and no arbitrary shell command may be injected through a filename or path.
- Mutating index/working-tree operations are serialized per worktree (`SCM-01`) and destructive operations (discard/restore) require explicit, previewed user action (`SCM-10`).
- Remote operations are explicit and safe-first: Fetch (`SCM-11`), fast-forward-only Pull (`SCM-12`), explicit Push (`SCM-13`), conflict state shell (`SCM-14`).
- Worktree support is bounded to discovery/open (`SCM-15`) and safe create/remove (`SCM-16`).
- Branch-management UX is excluded in V1: no create/delete/rename/switch/merge/rebase from the product UI. The current branch may be shown read-only as Git context.
- No automatic commit, push, or destructive fix; every mutation stays explicit and reviewable.

## Alternatives Considered

- Full branch-management UI in V1: rejected by the add-on scope decision; expands risk surface beyond the approved feature.
- Shell/CLI passthrough or raw command execution from the frontend: rejected; violates argument-injection and privilege boundaries.
- Auto-commit/auto-push background behavior: rejected; breaks the explicit-approval rule for mutations.
- Duplicating Git state in SQLite as a second source of truth: rejected; Git state is read from Git, never mirrored as authoritative.

## Consequences

### Positive

Users get a Git-aware workflow (status, diff, stage, commit, history, remote, worktree) inside the IDE without leaving the local-first model. Structured process boundaries keep Git operations typed, cancellable, and serialized per worktree.

### Negative / residual risk

Branch workflows remain in external Git tools during V1. Conflict resolution is a bounded resolution shell, not a full merge editor. Hook/authentication/remote failures surface as typed outcomes. Live Git process behavior against hostile paths/Unicode is covered by `SCM-17` hardening evidence.

## Security & File-Safety Impact

No user file is mutated through an unreviewed path; destructive actions (discard/restore) are explicit and previewed. Repository escape via path handling, argument injection through filenames, orphaned Git processes, and credential leakage are explicitly gated by the per-sprint Security & File-Safety Gate and the `SCM-17` regression/security suite. The add-on does not weaken any master-specification safety rule.

## Test / Experiment Evidence

Scope evidence: `docs/SCM-ADDON-README.md` (scope decision, sprint count 98 → 115) and `docs/specs/13-source-control-git-worktree-spec.md` (explicit exclusions: create/delete/rename/merge from a branch-management UI, branch comparison workspace). The added roadmap integration (sprint index, status board, dependency graph, execution waves, sprint manifest) is recorded in commit history. Implementation evidence accumulates per sprint:
`SCM-01` required tests (capability detection, injection fixtures, per-worktree serialization) through `SCM-17` (`tests/DocumentIDE.IntegrationTests/SourceControl/SourceControlRegressionSuite.cs`, `tests/DocumentIDE.SecurityTests/SourceControl/GitProcessSecurityTests.cs`, `docs/quality/source-control-release-checklist.md`).

## Supersedes

None.

## Superseded By

None.
