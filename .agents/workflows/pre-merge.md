# Pre-Merge

## Description

Final verification after independent review approval.

## Steps

1. Confirm independent reviewer verdict is APPROVE.
2. Confirm no unresolved blocking findings.
3. Run sprint Required Tests.
4. Run repository affected verification.
5. Run required security/safety/golden suites.
6. Inspect final diff for secrets, skipped tests, scope creep, placeholder behavior, unsafe paths.
7. Confirm handoff and status evidence.
8. Return only:
   - `MERGE READY`, or
   - `NOT MERGE READY` with blockers.
