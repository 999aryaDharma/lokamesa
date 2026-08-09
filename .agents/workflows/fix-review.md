# Fix Review Findings

## Description

Validate and fix accepted independent-review findings.

## Steps

1. Read all review findings.
2. For each finding mark `ACCEPTED` or `REJECTED_WITH_EVIDENCE`.
3. For accepted defects:
   - reproduce/prove,
   - add failing regression coverage first,
   - implement smallest root-cause fix,
   - run regression and neighboring suites.
4. Do not fix speculative/rejected findings.
5. Run full sprint verification again.
6. Update handoff with finding disposition/evidence.
7. Commit fixes.
8. Return to the independent reviewer for re-review.
