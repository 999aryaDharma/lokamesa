# Sprint Handoff — REL-04

**Status:** DONE
**Branch:** `fix/rel-04-release-gate-integrity`
**Commits:** `fix/rel-04-release-gate-integrity` (exact hash in the sprint report thread)

## What changed

### 1. Real sanitizer coverage (`tests/DocumentIDE.MarkdownPreview.Tests`)

The project held only a `.csproj` and had never contained a test file (confirmed:
`git log --all --diff-filter=A --name-only -- tests/DocumentIDE.MarkdownPreview.Tests/*`
shows only commit `bce9f11` adding the csproj). It now contains 21 executing tests
that exercise the REAL `MarkdownPreviewService.RenderAndSanitize` (Markdig +
Ganss.Xss), not a fake:

- `MarkdownPreviewServiceSanitizationTests.cs` (16 tests): `<script>` removal,
  event-handler attributes (`onerror`/`onload`/`onclick`), `javascript:` URLs in
  `href`/`src` (including mixed case and entity-encoded), `<iframe>`,
  `<meta http-equiv="refresh">`, `<form>`, raw HTML embedded in Markdown,
  HTML inside a fenced code block stays escaped text, and `null`/empty input.
- `MarkdownPreviewServiceRenderingTests.cs` (5 tests): positive controls —
  headings, emphasis, strong, lists, Markdown links, `http`/`https` URLs and an
  `https` image are preserved (over-sanitization guard).

The test project was aligned with the known-good
`tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj` package versions
(Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4,
`<Using Include="Xunit" />`, ImplicitUsings + Nullable). It was **not** added to
`DocumentIDE.sln` (REL-02 keeps it out so the explicit gate is its only runner).

**A real vector leaked.** `<form action="https://evil.example"><input name="x"></form>`
survived sanitization: HtmlSanitizer's default allowed-tag set includes `form`.
On the public-share render path that is a phishing / credential-exfiltration
vector. Fixed minimally in `MarkdownPreviewService.RenderAndSanitize` by
`sanitizer.AllowedTags.Remove("form")` (the element and its subtree are dropped);
no other sanitization policy was changed. Every other required vector was already
neutralized — no production change was invented for them.

### 2. Honest release gate (`scripts/verify-release.ps1`)

- New shared helper `Assert-TrxEvidence` is the single implementation of the rule:
  a gate passes only on trx evidence that (a) exists, (b) has zero failures, and
  (c) executed **more than zero** tests.
- The `$trxGateMap` loop now rejects a suite whose summed `total` is 0.
- The `MarkdownPreview` gate reads the trx the run wrote (dedicated
  `test-results/markdownpreview/` dir) and fails unless `total -gt 0` and
  `failed -eq 0`, echoing the real count
  (`MarkdownPreview: 21 test(s) passed, 0 failed`).
- The false comment above the gate (which claimed the gate prevented
  green-by-skipping while itself only calling `Assert-LastExit`) was replaced with
  an accurate explanation of the `total=0` trap.
- `-FailGate` / `-SelfTest` behaviour is unchanged and re-verified.

### 3. Correction of the REL-02 / readiness-report claim

REL-02 handoff item 4 and the accepted-risk row in
`docs/quality/v1-readiness-report.md` presented "the gate runs its csproj
explicitly" as the mitigation for `MarkdownPreview.Tests` not being in the
solution. That mitigation provided **no evidence**: the project contained no
tests, `dotnet test` printed "No test is available", exited 0, and the gate
recorded `[Passed] MarkdownPreview — MarkdownPreview suite passed`.

This handoff supersedes that claim: the project now executes 21 real tests and
the gate refuses a zero-test suite. Per the sprint contract, REL-02's handoff was
**not** edited; the readiness report's row must be corrected when REL-03
re-issues it.

## Files changed

- `tests/DocumentIDE.MarkdownPreview.Tests/MarkdownPreviewServiceSanitizationTests.cs` (new)
- `tests/DocumentIDE.MarkdownPreview.Tests/MarkdownPreviewServiceRenderingTests.cs` (new)
- `tests/DocumentIDE.MarkdownPreview.Tests/DocumentIDE.MarkdownPreview.Tests.csproj` (aligned test packages/usings)
- `src/DocumentIDE.Infrastructure/Markdown/MarkdownPreviewService.cs` (remove `form` from allowed tags — proven RED first)
- `scripts/verify-release.ps1` (shared evidence guard; honest MarkdownPreview gate)
- `docs/sprints/handoffs/REL-04-HANDOFF.md` (this file)

## Verification

### Sanitizer tests

- `dotnet test tests/DocumentIDE.MarkdownPreview.Tests/DocumentIDE.MarkdownPreview.Tests.csproj`
  → initially **RED**: `Failed: 1, Passed: 20` — the failing test was
  `Form_AndItsSubmissionTarget_AreRemoved` (`Assert.DoesNotContain` found `<form`).
  All other vectors already passed.
- After the minimal `AllowedTags.Remove("form")` fix → **GREEN**:
  `Passed! - Failed: 0, Passed: 21, Skipped: 0, Total: 21`.
  trx counters: `total=21 passed=21 failed=0 notExecuted=0`.
- Mutation check (temporarily bypassed `sanitizer.Sanitize(html)` by returning raw
  HTML, then restored): `Failed: 13, Passed: 8` — the suite genuinely pins the
  sanitizer. No `[Fact(Skip=...)]` anywhere.

### Gate integrity — RED / GREEN through `scripts/verify-release.ps1` itself

Zero-test case, both directions (suite files temporarily moved aside, then restored):

| Script | Suite | MarkdownPreview gate result |
|---|---|---|
| original (`ab269c8`) | emptied | `[Passed] MarkdownPreview - MarkdownPreview suite passed` (with `No test is available`, exit 0) — **the defect** |
| hardened | emptied | `[Failed] MarkdownPreview - MarkdownPreview: trx evidence executed 0 tests; a suite that runs no tests cannot pass its release gate.` |
| hardened | 21 tests present | `[Passed] MarkdownPreview - MarkdownPreview: 21 test(s) passed, 0 failed` |

An empty suite writes a trx with `total=0` and exits 0 (verified directly), which
is why the pre-existing `$rows.Count -eq 0` check did not catch it and the new
`total > 0` rule does.

Self-test hooks unchanged:

- `verify-release.ps1 -SelfTest -FailGate SyntheticBackend` → exit 1, `BLOCKED`,
  `Forced failure via -FailGate (gate-block proof)`, packaging not invoked.
- `verify-release.ps1 -SelfTest` → exit 0, `Self-test PASSED`.

### Full pipeline — `-SkipPackage` (end-to-end)

Command:
`pwsh -NoProfile -NonInteractive -File scripts/verify-release.ps1 -SkipPackage -OutputDir artifacts/rel04-gate-final`
(the identical `-OutputDir artifacts/rel04-gate-full` invocation was the earlier
pre-commit working-tree run; see the note below).

Result: **exit 0, verdict `PASSED`** (all gates green; `PackagingVerification`
correctly `EnvironmentGated` because `-SkipPackage` was passed).

- `BackendBuild` → Passed.
- `BackendFull` → Passed: Core 459, Integration 848, Security 91, Safety 24,
  Golden 39, Architecture 12, Performance 3, Soak 6 (all 0 failed).
- Mapped gates → Passed with real counts: `SecurityGate: 91`, `SafetyGate: 24`,
  `GoldenGate: 39`, `ArchitectureGate: 12`, `PerformanceGate: 3`, `SoakGate: 6`,
  `BackendUnit: 459`, `BackendIntegration: 848`.
- `MarkdownPreview` → `[Passed] MarkdownPreview - MarkdownPreview: 21 test(s) passed, 0 failed`
  (trx: `artifacts/rel04-gate-full/test-results/markdownpreview/...trx`).
- `FrontendDeps`, `FrontendTypecheck`, `FrontendLint`, `FrontendTests` (425),
  `FrontendBuild` → Passed.
- `WordSuite` → Passed (48 Word tests in IntegrationTests + supporting suites).
- Report: `artifacts/rel04-gate-full/verification-report.json`.

No previously-green gate flipped (Part 4). The first full run was executed on the
working tree atop `ab269c8` (`workingTreeDirty: true`, report pinned `ab269c8`);
the **definitive run was re-executed at the sprint commit `e0e5b4d`
(`workingTreeDirty: false`)** and again reported `PASSED` with the same
`MarkdownPreview: 21 test(s) passed, 0 failed`. The verification report pins the
commit under test; this handoff paragraph is a docs-only edit made after that
run and changes no verified code.

### Pre-existing failure observed (flake, not a regression)

`DocumentIDE.IntegrationTests.Desktop.ShareLifecycleHostTests.LiveShare_SupportsExplicitMembershipChanges`
failed **once** during the targeted RED run's `BackendFull` (1 of 848). It
exercises live-share membership over the ShareHost HTTP surface and is unrelated
to this sprint's changes (sanitizer `form` removal, test project, verification
script). It did **not** recur in the definitive full `-SkipPackage` run
(IntegrationTests 848/848 passed), so it is a parallel/timing flake of the same
class REL-02 already recorded. Reported, not modified or hidden.

## Acceptance criteria

- [x] `tests/DocumentIDE.MarkdownPreview.Tests` contains real tests that execute
      and pass, and at least one fails if `RenderAndSanitize` stops sanitizing
      (mutation check: 13 failures).
- [x] `scripts/verify-release.ps1` cannot record `Passed` for a suite that
      executed zero tests (demonstrated with the suite emptied: gate `Failed`;
      mapped-gate loop also rejects `total = 0`).
- [x] The false REL-02 / readiness-report mitigation claim is corrected here and
      flagged for the REL-03 re-issue.

## Deviations / spec conflicts

1. **Test project package alignment.** The project pinned old packages and had no
   `<Using Include="Xunit" />`; it was aligned with `Core.Tests` (known-good)
   rather than guessing. No product effect.
2. **`mailto:` links are not preserved** by the default `HtmlSanitizer` policy.
   An early positive-control assumption was removed; the sprint only requires
   `http(s)` links to survive, so no policy change was made.
3. **Dedicated `markdownpreview` results dir.** A zero-test trx has no
   `UnitTest/@storage`, so assembly-name attribution cannot distinguish it from a
   missing trx. Reading the trx written by this run is the deterministic way to
   detect `total = 0`.
4. **Targeted gate runs used `-FailGate BackendFull`** to skip the unrelated slow
   suite while the real `MarkdownPreview` gate executed. The full `-SkipPackage`
   run is reported separately. `-FailGate`/`-SelfTest` were not modified.
5. **`docs/sprints/SPRINT-STATUS.md` not edited** — the controller owns the board
   and the row is already `READY`.

## Risks discovered

- HtmlSanitizer's default allowed set also includes other interactive elements
  (`input`, `button`, `select`, `option`, `object`, …). This sprint only removed
  `form`, the submission vector a RED test proved necessary. A follow-up should
  audit the remaining default-allowed surface for the share renderer.
- `scripts/verify.ps1` (FND-08) still never runs `MarkdownPreview.Tests` (not in
  the solution). `verify-release.ps1` is the release command; this is unchanged
  from REL-02 and remains a known gap for the non-release script.
- The `WordSuite` gate still relies on exit code only (it does not read a trx).
  It executed real Word tests in this run (48 IntegrationTests + supporting
  suites), but it has the same vacuous-pass shape if a future runner matches no
  `FullyQualifiedName~Word` tests. Out of this sprint's scope (Part 2 names the
  `$trxGateMap` loop and the `MarkdownPreview` gate only); flagged for a
  follow-up.

## Next eligible sprint(s)

- REL-03 — re-issue the V1 readiness report, correcting the accepted-risk row for
  `MarkdownPreview.Tests`.
