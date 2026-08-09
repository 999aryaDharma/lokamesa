# Debug Bug

## Description

Systematically fix a defect without workaround-first editing.

## Steps

1. Identify owning subsystem/sprint.
2. Reproduce reliably.
3. Inspect logs/state and isolate the smallest failing layer.
4. State root-cause hypothesis supported by evidence.
5. Add a failing regression test.
6. Implement smallest safe root-cause fix.
7. Run regression and neighboring tests.
8. If defect touches corruption/security/share authorization:
   - stop unrelated feature work,
   - add safety/security release-gate coverage,
   - assess already-completed related sprints.
9. Report root cause and evidence.
