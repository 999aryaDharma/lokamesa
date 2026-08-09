# Document IDE — Agent Development Pack

This package turns the approved/audited Document IDE specifications into an execution system for AI coding agents.

## Contents

- `AGENTS.md` — mandatory first-read rules.
- `docs/specs/` — audited product + subsystem specifications.
- `docs/agent/` — agent workflow, TDD, safety, review, Git, ADR, performance policies.
- `docs/sprints/` — 98 dependency-aware implementation sprint specs.
- `docs/sprints/SPRINT-STATUS.md` — single operational status board.
- `docs/sprints/sprint-manifest.json` — machine-readable sprint/dependency manifest.
- `docs/templates/` — handoff, review, ADR, change-request templates.
- `docs/decisions/` — ADR destination.
- `docs/quality/` — benchmark/readiness/release evidence destination.

## Start development

1. Put this pack into the repository root.
2. Read `AGENTS.md`.
3. Open `docs/sprints/SPRINT-STATUS.md`.
4. Start with a READY sprint, normally `FND-01`.
5. Use the Ready-to-Run Agent Prompt embedded in that sprint.
6. Do not mark DONE until Definition of Done is satisfied.
7. Repeat.

## Why the sprints are intentionally small

The purpose is to keep an AI agent's context bounded and reviewable. The agent should not be asked to “build Document IDE”; it should be asked to implement one exact behavior with explicit dependencies, tests, safety rules, and exit criteria.

## First recommended sequence

```text
FND-01 Repository Bootstrap
→ FND-02 Privileged Local API Security
→ FND-03 SQLite Persistence Foundation
→ FND-04 Background Job Runtime
→ FND-05 Settings & Secret Storage
→ FND-07 Desktop/WebView Production Host
→ FND-08 Build, Verify & Artifact Scripts
```

Other dependency-ready work can branch after these foundations.

## Conflict rule

`docs/specs/00-master-product-technical-spec.md` wins over lower-level docs until explicitly revised.
