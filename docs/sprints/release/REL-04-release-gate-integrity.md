# REL-04 — Release Gate Integrity & Markdown Preview Sanitizer Coverage

**Status:** READY
**Priority:** P0
**Kind:** release
**Recommended branch:** `fix/rel-04-release-gate-integrity`

## Goal

Restore the release verification pipeline's honesty (REL-02) and give the
`MarkdownPreview` gate real evidence, by (a) making it impossible for a named
release gate to report `Passed` when its suite executed **zero** tests, and
(b) filling the currently empty `DocumentIDE.MarkdownPreview.Tests` project with
direct tests for the real `MarkdownPreviewService` sanitizer (MD-02), which sits
on the public-share rendering path.

## Why this sprint exists

Found by the controller while doing REL-03 release-gate reconnaissance. Three
facts, each verified by running a command:

1. `tests/DocumentIDE.MarkdownPreview.Tests` contains **only a `.csproj`** and has
   never contained a test file in its whole history (`git log --all
   --diff-filter=A` shows one commit adding the csproj, `bce9f11`, and nothing
   else). `dotnet test` on it prints *"No test is available"* and **exits 0**.
2. `scripts/verify-release.ps1` therefore records
   `[Passed] MarkdownPreview — MarkdownPreview suite passed` while executing
   **zero tests**. The gate body only calls `Assert-LastExit`; it never inspects
   the trx, so it cannot fail on a missing suite. The adjacent `$trxGateMap` loop
   guards against a *missing* trx (`$rows.Count -eq 0`) but not against a trx with
   `total = 0`, so the same hole is latent for every mapped suite.
3. `MarkdownPreviewService.RenderAndSanitize` (Markdig + `Ganss.Xss`, registered as
   the real `ISnapshotMarkdownRenderer` used by frozen/live share rendering) is
   invoked by **no test at all**. The XSS tests that do exist
   (`ShareWebSecurityTests`, `PublicShareAttackSurfaceTests`) exercise the
   ShareHost *serving* path and/or use `FakeMarkdownRenderer`; none instantiate the
   real service.

This is precisely the "green by skipping" failure mode the script's own header
promises to refuse ("No 'green by skipping' policy: a skipped gate fails the
release"). REL-02's handoff item 4 and
`docs/quality/v1-readiness-report.md`'s accepted-risk row both present "the gate
runs its csproj explicitly" as the mitigation for this suite not being in the
solution. That mitigation provides **no evidence**, because there is no suite. A
V1 `GO` resting partly on that row would be overstated.

**Do not rewrite history:** `docs/sprints/handoffs/REL-02-HANDOFF.md` is the
record of a completed sprint and must **not** be edited to remove its claim.
Record the correction in this sprint's handoff and in the re-issued readiness
report instead.

## Depends On

- `REL-02` — Release Verification Pipeline

**Hard rule:** Do not start this sprint until all dependencies are marked `DONE`
in `docs/sprints/SPRINT-STATUS.md`. (`REL-02` is `DONE`.)

## Required Reading

- `AGENTS.md`
- `docs/specs/11-testing-strategy.md` (sections on gate honesty / skipped tests)
- `docs/specs/12-implementation-plan.md` (completion gates)
- `scripts/verify-release.ps1` — in particular `Get-TrxSummaries` (line ~116),
  the `$trxGateMap` loop (lines ~254-279) and the `MarkdownPreview` gate
  (lines ~281-290)
- `docs/sprints/handoffs/REL-02-HANDOFF.md` — item 4 (the claim this sprint
  corrects)
- `docs/quality/v1-readiness-report.md` — the accepted-risk row for this suite
- `tests/DocumentIDE.IntegrationTests/Scripts/V1ReadinessGateTests.cs`
- `src/DocumentIDE.Infrastructure/Markdown/MarkdownPreviewService.cs`
- `src/DocumentIDE.Infrastructure/Sharing/SnapshotMarkdownRendererAdapter.cs`

## In Scope

- Adding direct tests to `tests/DocumentIDE.MarkdownPreview.Tests/` for the real
  `MarkdownPreviewService`.
- Hardening `scripts/verify-release.ps1` so a gate whose suite executed zero tests
  **cannot** report `Passed`.
- Correcting the false rationale comment above the `MarkdownPreview` gate.
- Recording the corrected claim in this sprint's handoff (the readiness-report row
  is corrected when REL-03 re-issues the report; do not edit REL-02's handoff).

## Out of Scope

- Any new share/publishing feature, or any change to share behavior.
- Adding `DocumentIDE.MarkdownPreview.Tests` to `DocumentIDE.sln` — REL-02
  deliberately keeps it out so the explicit gate is its only runner. Adding it
  would double-run it under `BackendFull`. Keep it out; make the explicit gate
  honest instead.
- Rewriting `docs/sprints/handoffs/REL-02-HANDOFF.md`.
- Any sanitization *policy* change beyond the minimum a RED test proves necessary.
- Unrelated refactors and downstream features.

## Planned Files / Artifacts

- `tests/DocumentIDE.MarkdownPreview.Tests/*.cs` (new test files)
- `scripts/verify-release.ps1`
- `docs/sprints/handoffs/REL-04-HANDOFF.md`

## Interfaces & Contracts

**Consumes**
- `REL-02` — Release Verification Pipeline, as amended.

**Produces**
- A `MarkdownPreview` gate that carries real evidence (an executed-test count).
- Direct regression tests for `MarkdownPreviewService.RenderAndSanitize`.
- A documented correction of the REL-02 / readiness-report mitigation claim.

## Implementation Steps

- [ ] **Step 1 (RED):** Add `MarkdownPreviewServiceTests` to
      `tests/DocumentIDE.MarkdownPreview.Tests` covering, at minimum:
      - `<script>` payloads removed;
      - event-handler attributes (`onerror`, `onload`, `onclick`) removed;
      - `javascript:` URLs in `<a href>` / `src` neutralized;
      - `<iframe>`, `<form>`, `<meta http-equiv=refresh>` and similar removed;
      - raw HTML embedded in markdown is neutralized (Markdig's default pipeline
        passes it through, so the sanitizer is the control being tested);
      - legitimate markdown still renders (headings, emphasis, `http(s)` links,
        lists) — guards against over-sanitization;
      - HTML inside a fenced code block stays **escaped text**, not live markup;
      - `null`/empty input returns without throwing.
      Run them. Any vector that leaks is a real security finding: fix it minimally
      and keep its test as the pin. If every vector already passes, the tests are
      the deliverable and no production change is made — say so plainly in the
      handoff rather than manufacturing a change.

- [ ] **Step 2 (RED, gate integrity):** Prove the vacuous gate, reversibly.
      Record the current behaviour (gate reports `Passed` with 0 tests), then prove
      the hardened gate rejects it. Keep the experiment reversible and report both
      directions.

- [ ] **Step 3 (GREEN):** In `scripts/verify-release.ps1`:
      - in the `$trxGateMap` loop, require `$tot -gt 0` (a suite that executed no
        tests must fail its gate, not pass it);
      - in the `MarkdownPreview` gate, read the trx written by the run and fail
        unless `total -gt 0` **and** `failed -eq 0`, echoing the actual count
        (e.g. `MarkdownPreview: 12 test(s) passed, 0 failed`);
      - replace the now-false comment above that gate with an accurate one.

- [ ] **Step 4 (Refactor):** Re-run the whole gate path affected by the change and
      confirm no previously-green gate flipped for an unrelated reason.

For each behavioral step, follow Red → Green → Refactor. Do not batch all tests at
the end.

## Required Tests

- [ ] Every sanitizer vector listed in Step 1 has a test, and each test fails for
      the right reason before its fix when a fix is needed.
- [ ] Legitimate markdown is not over-sanitized (positive controls present).
- [ ] A gate whose suite executed zero tests reports **Failed** — demonstrated by
      running the gate with the suite emptied, then restored.
- [ ] With the tests present, the `MarkdownPreview` gate reports `Passed` **with an
      executed-test count > 0**.

At minimum, the first behaviour must be demonstrated by a failing test before
implementation:

> A suite that executes no tests cannot make its release gate report `Passed`.

## Failure / Edge Cases

- `dotnet test` exits 0 while running no tests (the defect itself).
- Trx written with `total = 0`, or no trx written at all.
- A sanitizer that is *too* strict and silently destroys legitimate markdown.
- The `MarkdownPreview` gate double-running vs `BackendFull` (do not add the
  project to the solution).

Failures must produce typed/inspectable outcomes rather than silent fallback where
the subsystem spec requires determinism.

## Security & File-Safety Gate

Before completion, explicitly confirm:

- [ ] no user file was mutated through an unreviewed/bypass path
- [ ] no arbitrary path or secret is exposed
- [ ] dependency/security boundaries remain intact
- [ ] no destructive action silently broadened its scope
- [ ] relevant higher-level safety rules remain stronger than convenience

Notes: this sprint **strengthens** a security control (it removes a false claim of
sanitizer coverage and adds real coverage). The `-FailGate` hook and the self-test
behaviour must remain intact. Mark an item N/A in the handoff only with a
one-sentence justification.

## Acceptance Criteria

- [ ] `tests/DocumentIDE.MarkdownPreview.Tests` contains real tests that execute
      and pass, and at least one would fail if `RenderAndSanitize` stopped
      sanitizing.
- [ ] `scripts/verify-release.ps1` cannot record a `Passed` gate for a suite that
      executed zero tests.
- [ ] The false REL-02 / readiness-report mitigation claim is corrected in this
      sprint's handoff (and flagged for the REL-03 re-issue).

## Definition of Done

- [ ] all implementation steps completed
- [ ] required tests pass
- [ ] repository affected test suite passes
- [ ] no required test skipped
- [ ] no unresolved sprint-required placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] `docs/sprints/SPRINT-STATUS.md` updated
- [ ] sprint handoff written using `docs/templates/SPRINT-HANDOFF-TEMPLATE.md`
- [ ] coherent commit created

## Commit Guidance

Recommended final commit:

```text
fix(release): fail a release gate whose suite executed no tests
```

Multiple commits are allowed when each is independently reviewable.

## Ready-to-Run Agent Prompt

```text
Implement sprint REL-04 — Release Gate Integrity & Markdown Preview Sanitizer
Coverage only.

Read AGENTS.md, this sprint document, and every Required Reading document first.
Verify REL-02 is DONE before editing.

Context you can rely on (verify it yourself, do not assume):
- tests/DocumentIDE.MarkdownPreview.Tests holds only a .csproj and has never held a
  test file; `dotnet test` on it exits 0 with "No test is available".
- scripts/verify-release.ps1's MarkdownPreview gate only calls Assert-LastExit, so
  it reports Passed on that empty suite.
- src/DocumentIDE.Infrastructure/Markdown/MarkdownPreviewService.cs (Markdig +
  HtmlSanitizer) is instantiated by no test.

Do not add the test project to DocumentIDE.sln. Do not edit REL-02's handoff. Do
not change share behaviour.

Use TDD: the gate-integrity behaviour must be shown failing before it is fixed, and
the sanitizer tests are written before any sanitizer change. If no sanitizer change
is needed, say so explicitly with evidence.

At completion:
1. run all Required Tests and affected repository verification,
2. self-review git diff,
3. map Acceptance Criteria to evidence,
4. update SPRINT-STATUS.md,
5. write the sprint handoff,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the sprint commit guidance.
```
