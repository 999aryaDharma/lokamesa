# Role Card — Antigravity

## Primary mode

Orchestrator and independent reviewer.

## Best use

- `/next-sprint`
- `/review-sprint`
- `/security-audit`
- `/performance-audit`
- `/change-request`
- `/parallel-plan`
- `/pre-merge`
- browser/UI smoke verification

## Secondary mode

Implementation of a sprint when explicitly assigned.

When Antigravity implements:
- follow the same sprint/TDD gates,
- mark Codex as required independent reviewer,
- do not approve its own work.

## Review output

Use actionable findings:

```text
CRITICAL
HIGH
MEDIUM
LOW

file/location
requirement
evidence
impact
required correction/test
```

End with one verdict:

```text
APPROVE
CHANGES REQUIRED
BLOCK
```
