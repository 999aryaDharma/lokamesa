# Git, Handoff & Review — Always On

## Branch ownership

One sprint has exactly one implementation owner at a time.

Recommended branch:

```text
<type>/<sprint-id>-<slug>
```

Do not let two agents edit the same sprint branch concurrently.

## Handoff

Every completed or paused sprint records:

- implementation agent,
- branch,
- commit(s),
- files changed,
- commands/tests,
- acceptance criteria,
- deviations,
- discovered risk,
- reviewer agent,
- next eligible sprint(s).

Use `docs/templates/SPRINT-HANDOFF-TEMPLATE.md`.

## Independent approval

The implementation agent must not be the final approving reviewer.

Default:

```text
Codex implements
→ Antigravity reviews

Antigravity implements
→ Codex reviews
```

A review finding is not automatically accepted. The implementer should verify the finding against code/spec/test evidence before fixing it.

## Merge

Do not merge merely because implementation says DONE.

Final merge readiness requires:
- independent review approval,
- required verification,
- no unresolved blocking findings.
