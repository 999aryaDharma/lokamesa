# TDD & Testing Policy

## Required cycle
Red → Green → Refactor.

For a bug:
1. reproduce with failing regression test,
2. implement smallest safe fix,
3. run regression,
4. run neighboring suite.

## Safety-critical areas
Mandatory failure-path tests for:
- file mutation,
- batch apply,
- undo,
- Word locks,
- share authorization,
- path containment,
- DOCX resource budgets.

## No fake green
Do not:
- skip tests to pass CI,
- weaken assertions without justification,
- mock the unit under test,
- accept flaky safety/security tests as normal.

## Test ownership
A sprint is not Done until its acceptance criteria have automated evidence where automation is feasible.
