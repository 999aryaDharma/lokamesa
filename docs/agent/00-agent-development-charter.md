# Agent Development Charter

## Mission

Implement Document IDE exactly as specified, incrementally and safely.

The agent optimizes for:

1. user file safety,
2. deterministic behavior,
3. local-first architecture,
4. test evidence,
5. maintainable module boundaries,
6. performance at the documented target scale,
7. only then development speed.

## Forbidden shortcuts

An agent may not:

- skip recovery because it is “temporary”,
- make Search & Replace fuzzy/semantic,
- replace an explicit strict operation with broad normalization,
- move physical files when the user only dragged canvas objects,
- expose a public filesystem path endpoint,
- run Word COM concurrently,
- use cached metadata as filesystem authority,
- silently broaden batch scope,
- auto-publish newly created Live Share files,
- add cloud/user-account dependencies to core app,
- reinterpret a failed test as acceptable without updating an approved spec/ADR.

## Session size

Default one sprint per branch/session. Two sprints may be combined only if:
- one is a tiny prerequisite of the other,
- both are marked Ready,
- their combined review remains coherent,
- the agent states the combination before editing.

## Evidence over confidence

Every completion claim must contain command/test evidence.
