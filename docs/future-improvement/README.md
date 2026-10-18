# Document IDE — Future Improvement Roadmap

**Label:** `future-improvement`
**Scope:** post-V1 roadmap
**Planned range:** `1.1.0 → 1.10.x`
**Status model:** FUTURE / SPECIFIED / CANDIDATE / PROMOTED

This directory contains approved product direction that is intentionally **outside the active V1 sprint board**. It must not be added to `docs/sprints/SPRINT-STATUS.md` until the owner explicitly promotes a version into an implementation cycle.

## Product Rule

Future improvements must extend the Document IDE model without weakening the existing source-of-truth, safety, deterministic mutation, local-first, and separation-of-concerns rules.

The ten planned improvement lines are:

1. `1.1.0` — Command Palette + Quick Open
2. `1.2.0` — Review Workspace
3. `1.3.0` — Saved Views / Smart Collections
4. `1.4.0` — Document Health / Linter
5. `1.5.0` — Work Session
6. `1.6.0` — Document Comments / Review Notes as canvas note objects
7. `1.7.0` — Wildboard Core (`SPECIFIED` — second-brain workspace kind, `WB-01…WB-05`)
8. `1.8.0` — Rich Writing & Organization (`CANDIDATE` — Medium-style editor over Markdown)
9. `1.9.0` — Media & References (`CANDIDATE` — images, video, links, webclips, PDF)
10. `1.10.0` — Diagram Boards (`SPECIFIED` — native diagram engine, `DB-01…DB-07`; ADR-004)

## V1 Boundary

These features are consumers/extensions of the V1 platform. They do not replace or redefine:

- local filesystem as document-content source of truth,
- Git state as Git source of truth,
- Document IDE History as application operation/recovery history,
- canvas position semantics,
- DOCX fidelity and safe mutation rules,
- Markdown source-first editing,
- explicit user approval for destructive operations.

## Versioning Policy

A version here is a **product milestone**, not a promise of immediate release. Minor versions may contain multiple implementation sprints. Patch versions (`1.x.1`, `1.x.2`, etc.) are reserved for hardening, UX refinement, accessibility, and regression fixes within the already-approved feature scope.

## Promotion Rule

When a future version is promoted to active implementation:

1. validate dependencies against the current V1/V1.x architecture,
2. add its sprints to the active sprint manifest,
3. assign real repository branches/worktrees according to agent policy,
4. update status through the normal `READY → IN_PROGRESS → DONE` lifecycle,
5. add handoffs and verification evidence,
6. update the active implementation plan.

Future docs are otherwise planning artifacts and do not change the current V1 execution board.
