# `.agents/` — Antigravity + Codex Control Plane

This directory is the shared operational layer for the two coding agents used on Document IDE.

## Agent model

Default pairing:

```text
Codex
  primary implementation agent

Antigravity
  orchestrator + independent reviewer
```

This is a default, not a hard capability limit. Either agent may implement a sprint, but:

> The agent that implements a sprint must not be the final reviewer that approves the same sprint.

If Antigravity implements, Codex performs the independent review.
If Codex implements, Antigravity performs the independent review.

## Source-of-truth hierarchy

Both agents must follow:

1. `AGENTS.md`
2. `docs/specs/00-master-product-technical-spec.md`
3. relevant `docs/specs/01..11`
4. `docs/specs/12-implementation-plan.md`
5. active sprint document under `docs/sprints/`
6. `.agents/rules/`
7. code/comments

A `.agents` rule may clarify execution behavior but must never weaken product/security/file-safety requirements.

## Antigravity

Workspace rules live in:

```text
.agents/rules/
```

Reusable workflows live in:

```text
.agents/workflows/
```

Recommended Antigravity setup:

- core rule files `00` through `05`: Always On
- `10-antigravity-role.md`: Always On
- `11-codex-role.md`: Manual / not needed by Antigravity
- workflows: invoke using their filename as workspace workflow/slash command

## Codex

Codex automatically starts from the repository `AGENTS.md`.

The root `AGENTS.md` explicitly tells Codex to consult this `.agents/` directory for shared rules and workflows.

Codex does not need `.agents/workflows/` to be a native workflow system; the Markdown workflow files are reusable task protocols that Codex can read and execute when the user names one.

## Operational files

```text
.agents/
├── README.md
├── rules/
├── workflows/
├── roles/
└── coordination/
```

Do not store:
- secrets,
- transient model thoughts,
- user documents,
- API tokens,
- generated build artifacts.

The sprint status remains authoritative in:

```text
docs/sprints/SPRINT-STATUS.md
```
