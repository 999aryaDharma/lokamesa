# Agent Prompt Cookbook

## Implement one sprint

```text
Implement sprint <ID> only.

First read:
- AGENTS.md
- docs/sprints/<path-to-sprint>
- every Required Reading document listed by the sprint

Verify dependencies are DONE before editing.

Use TDD. Do not implement downstream sprints.
Do not weaken higher-level specs.

At completion:
1. run all sprint tests,
2. run repository verification,
3. self-review git diff,
4. update SPRINT-STATUS.md,
5. provide changed files, commands/results, acceptance status, deviations,
6. commit using the sprint commit guidance.
```

## Review a sprint

```text
Review sprint <ID> implementation against:
- sprint acceptance criteria,
- relevant master/subsystem specs,
- security/file-safety gates,
- tests.

Do not modify code first.
Report findings by severity with exact file references.
```

## Fix review findings

```text
Fix only accepted findings for sprint <ID>.
For each defect, add/strengthen a failing regression test first.
Do not broaden feature scope.
Run the full affected verification suite afterward.
```
