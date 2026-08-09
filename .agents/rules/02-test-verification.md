# Test & Verification — Always On

Evidence is required before completion.

## For new behavior

1. Add a failing test.
2. Run it and confirm failure for the intended reason.
3. Implement the minimum correct behavior.
4. Run the test until green.
5. Refactor only after green.
6. Run neighboring/affected suites.

## For bugs

A regression test must reproduce the defect before the fix where automation is feasible.

## Never

- delete or weaken assertions merely to make CI green,
- skip required security/safety tests,
- mock the unit actually being tested,
- convert an integration requirement into a meaningless unit-only test.

## Completion report

Report exact commands and outcomes.

A build passing alone is not sufficient evidence for a sprint whose behavior requires tests.
