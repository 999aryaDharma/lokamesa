# Two-Agent Coordination Protocol

## Default loop

```text
User/Product Owner
      ↓
Antigravity selects/validates READY sprint
      ↓
Codex implements
      ↓
Codex self-verifies + handoff
      ↓
Antigravity independent review
      ↓
  ┌───────────────┐
  │               │
APPROVE      CHANGES REQUIRED
  │               │
  ↓               ↓
DONE         Codex validates/fixes
                  ↓
             Antigravity re-review
```

## Never

- both agents modify the same sprint at the same time,
- one agent approves its own sprint,
- reviewer silently fixes code before reporting findings,
- implementation agent blindly accepts questionable review advice,
- either agent edits status to DONE before verification/review approval.

## If Antigravity implements

Swap roles:

```text
Antigravity implements
→ Codex reviews
```

## Parallel development

Two agents may implement different sprints simultaneously only if:

- both sprints are READY,
- neither depends on the other,
- planned files/module boundaries do not materially overlap,
- isolated branches/worktrees are used,
- shared schema/public interfaces are already stable,
- integration verification runs after merge/rebase.

When uncertain, serialize the work.
