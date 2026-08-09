# Sprint Execution — Always On

Default unit of work:

```text
1 agent session
→ 1 sprint
→ tests
→ self-review
→ handoff
→ independent review
```

Before editing:

1. read `AGENTS.md`,
2. read `docs/sprints/SPRINT-STATUS.md`,
3. verify the assigned sprint is READY or explicitly already IN_PROGRESS by this agent,
4. verify every dependency is DONE,
5. read the sprint's Required Reading,
6. inspect current repository state,
7. run relevant baseline tests.

During implementation:

- use Red → Green → Refactor for behavioral changes,
- keep edits within sprint scope,
- reuse existing abstractions rather than creating duplicates,
- do not implement downstream convenience features.

Before handoff:

- run all sprint-required tests,
- run affected repository verification,
- inspect complete git diff,
- map acceptance criteria to evidence,
- update sprint status/handoff only when evidence supports it.

Never auto-start the next sprint after completion.
