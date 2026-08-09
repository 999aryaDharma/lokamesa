# Two-Agent Handoff

## Description

Transfer a sprint from implementation agent to independent reviewer.

## Steps

1. Implementation agent stops editing after verification.
2. Handoff must include:
   - sprint ID,
   - implementation agent,
   - branch/commit,
   - changed files,
   - tests/commands/results,
   - acceptance mapping,
   - deviations/risks.
3. Reviewer checks out/opens the exact implementation revision.
4. Reviewer runs `/review-sprint`.
5. If findings exist, implementation owner receives `/fix-review`.
6. Same reviewer runs `/re-review`.
7. Only after APPROVE may the sprint become DONE/merge-ready.
