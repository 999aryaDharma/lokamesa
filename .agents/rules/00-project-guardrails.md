# Project Guardrails — Always On

Read `AGENTS.md` first.

These constraints apply to every coding/review/debugging task.

## Do not

- Do not implement more than the assigned sprint unless the user explicitly authorizes a scope change.
- Do not begin a sprint whose dependencies are not DONE.
- Do not change product requirements to make implementation easier.
- Do not treat cache/database/index as the document source of truth.
- Do not physically move a file because a canvas object moved.
- Do not introduce cloud/account requirements into core functionality.
- Do not create a custom rich DOCX editor; Microsoft Word remains the deep editor.
- Do not use semantic/fuzzy matching for destructive Search & Replace.
- Do not silently broaden a batch scope.
- Do not expose arbitrary local paths through public endpoints.
- Do not claim a task is complete when required tests are skipped/failing.

## Required source order

When instructions conflict:

1. `AGENTS.md`
2. master product spec
3. subsystem spec
4. implementation plan
5. sprint spec
6. this rule set
7. code/comments

If a lower-level document contradicts a higher-level safety requirement, follow the higher-level requirement and report the conflict.
