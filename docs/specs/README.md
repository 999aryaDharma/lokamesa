# Document IDE — Technical Documentation Index

This directory contains the implementation-facing technical specifications derived from the approved master product specification.

## Read order

1. `00-master-product-technical-spec.md` — product-level source of truth.
2. `01-system-architecture.md` — process/module/security architecture.
3. `02-domain-data-model.md` — persistent entities and invariants.
4. `03-workspace-canvas-spec.md` — workspace/canvas UX and layout rules.
5. `04-filesystem-sync-spec.md` — local file identity, watcher, reconciliation.
6. `05-search-indexing-spec.md` — FTS, fuzzy, metadata, semantic search.
7. `06-docx-openxml-word-integration.md` — DOCX fidelity and Word adapter.
8. `07-batch-operation-engine.md` — strict preview/apply/undo engine.
9. `08-markdown-graph-spec.md` — Markdown, backlinks, graph, context.
10. `09-pdf-finalization.md` — PDF export and merge.
11. `10-sharing-security-ngrok.md` — read-only public share security.
12. `11-testing-strategy.md` — verification and release gates.
13. `12-implementation-plan.md` — phased agent execution plan.
14. `13-source-control-git-worktree-spec.md` — source control (Git) status, diff, stage, commit, remote, conflict and worktree behavior.

## Conflict rule

If a lower-level technical document conflicts with `00-master-product-technical-spec.md`, the master spec wins until the master is explicitly revised.

## Agent rule

Do not treat a subsystem spec as permission to widen product scope. Implement only the documented behavior and preserve local-file safety above convenience.

---

## Post-Audit Hardening

The specs now include the accepted security/performance audit refinements. `00-master-product-technical-spec.md` section 101 is the consolidated normative hardening baseline. Lower-level specs include matching requirements/tests for localhost API authentication, separate ShareHost, frozen Live Share membership, strict rename, DOCX resource budgets, Word COM serialization, SQLite WAL, recovery storage, CSP/rate limiting, junction containment, canvas/graph scaling, and streaming PDF merge.
