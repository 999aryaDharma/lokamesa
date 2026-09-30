# Document IDE — Source Control & Git Worktree Add-on

This package adds the approved Source Control / Git Worktree feature to the existing Document IDE documentation set.

## Scope decision

The feature follows Git's working-tree → staging → commit model and adds explicit Fetch / Pull / Push plus bounded Git Worktree support.

**Branch-management UX is intentionally excluded.**

The current branch may appear as read-only Git context, but Document IDE V1 does not create, delete, rename, switch, merge, or rebase branches.

## New sprint count

17 new sprints: `SCM-01` through `SCM-17`.

The original 98-sprint plan becomes 115 sprints after this addition.

## Integration

Overlay the files in this archive onto the existing documentation tree. The changed existing files are:

- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/02-domain-data-model.md`
- `docs/specs/11-testing-strategy.md`
- `docs/specs/12-implementation-plan.md`
- `docs/specs/README.md`
- `docs/sprints/00-sprint-index.md`
- `docs/sprints/01-dependency-graph.md`
- `docs/sprints/02-execution-waves.md`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/sprint-manifest.json`

New files include:

- `docs/specs/13-source-control-git-worktree-spec.md`
- `docs/sprints/source-control/SCM-01-*` through `SCM-17-*`
- `docs/quality/source-control-release-checklist.md`
- `docs/decisions/ADR-001-source-control-git-scope.md`

`SCM-01` is marked `READY` because its prerequisites are already `DONE` in the supplied status board. Other SCM sprints are initially `BLOCKED` until their dependencies are complete.
