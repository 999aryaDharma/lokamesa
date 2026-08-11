# AGENTS.md — Document IDE Agent Instructions

This file is the first instruction file for any AI coding agent working in this repository.

## Source-of-truth order

When requirements conflict, obey in this order:

1. `docs/specs/00-master-product-technical-spec.md`
2. relevant subsystem spec in `docs/specs/01..11`
3. `docs/specs/12-implementation-plan.md`
4. current sprint spec in `docs/sprints/**`
5. implementation code/tests
6. comments

A sprint may make implementation detail more concrete, but it may not silently weaken a higher-level safety/product requirement.

## Work exactly one sprint

Default rule:

Do not implement downstream features “while you are here”.

Before starting a sprint:

1. read its `Depends On`,
2. verify each dependency is DONE,
3. read every document in its `Required Reading`,
4. inspect existing implementation and tests,
5. create/follow the sprint branch,
6. use TDD for behavior changes.

## Strict TDD Workflow (Red ➔ Green ➔ Refactor)

Every AI coding agent producing or implementing a sprint in this repository MUST follow strict TDD for all behavioral changes:

1. **Red Stage**:
   - Write a failing unit or integration test FIRST before writing any production code.
   - Run the test suite and confirm that the test fails explicitly for the expected reason.
2. **Green Stage**:
   - Write the minimum necessary implementation code required to satisfy the failing test.
   - Run the test suite and verify that the test passes cleanly.
3. **Refactor Stage**:
   - Clean up, simplify, and refactor the code and structure without breaking any existing behavior.
   - Re-run the full test suite to guarantee zero regressions.

**Rule**: Never write production code first and batch test writing at the end. Every behavioral step within a sprint must follow the Red ➔ Green ➔ Refactor cycle.

## Non-negotiable product rules

- Local filesystem remains source of truth.
- Canvas drag never physically moves files.
- DOCX deep editing remains Microsoft Word Desktop.
- Markdown remains plain `.md`.
- Destructive batch operations require reviewed deterministic plans.
- No semantic/fuzzy matching in destructive Search & Replace.
- Source mutation requires recovery snapshot + temp mutation + validation + atomic replace.
- If recovery cannot be created, mutation is blocked.
- Word COM automation concurrency is exactly `1`.
- Privileged localhost API requires Origin/Host/session authorization.
- Live Share content may update; membership never expands automatically.
- Public ShareHost is separate from privileged app and has no privileged mutation/Word dependencies.
- Do not claim ShareHost is an OS sandbox unless verified by the isolation ADR/tests.
- Never expose arbitrary local paths through public sharing.

## Before claiming completion

Run the sprint's required commands/tests plus repository verification.

Report:

- files changed,
- tests/commands run and results,
- acceptance criteria status,
- deviations/spec conflicts,
- remaining risks,
- commit hash if committed.

Do not claim “done” if a required test is skipped or failing.

