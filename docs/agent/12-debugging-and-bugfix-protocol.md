# Debugging & Bugfix Protocol

When a test/behavior fails:

1. reproduce consistently,
2. identify smallest failing layer,
3. inspect logs/state rather than guessing,
4. write regression test,
5. fix root cause,
6. verify neighboring invariants,
7. run full affected suite.

For file corruption/security defects:
- stop feature work,
- mark sprint blocked,
- add regression/release-gate coverage,
- review whether related previously-completed sprints are affected.
