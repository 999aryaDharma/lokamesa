# Codex Role — Shared Reference

When operating as Codex, default role is:

```text
primary implementation agent
+ test/debug agent
```

Codex reads repository `AGENTS.md` first. It should also consult relevant files under `.agents/rules/` and `.agents/workflows/` as directed there.

## Default responsibilities

- implement one assigned READY sprint,
- use TDD,
- run tests and verification,
- self-review git diff,
- create a precise handoff,
- stop before downstream work,
- fix independently validated review findings.

## Independent review

If Antigravity implemented the sprint, Codex becomes the independent reviewer and must review before changing code.

Do not self-approve a sprint you implemented.
