# Sprint Handoff — QA-01

**Status:** DONE  
**Branch:** `test/qa-01-golden-docx-regression-corpus`  
**Commits:** `f625146` `test(quality): golden docx regression corpus` · `5ddce5b` `fix(docx): keep no-op text replacement byte-identical`

## What changed

- New `tests/DocumentIDE.GoldenTests` project (net10.0, xunit; refs Core + Infrastructure), added to `DocumentIDE.sln` with exactly one project entry + configurations + nesting.
- `GoldenDocxFixtureBuilder.cs` — deterministic programmatic DOCX builder (fixed ZIP entry order, fixed ZIP timestamps, fixed compression, fixed media bytes; no opaque binaries committed).
- `GoldenDocxFixtures.cs` — canonical corpus: 13 valid fixtures (simple, split-runs, mixed-format, tables, headers, footers, sections/mixed-margins/orientation, images, hyperlinks, numbering, custom-styles, fields, tracked-changes) and 9 hostile/resource fixtures (entry explosion, oversized single part, oversized total declaration, high compression, parser/occurrence budget, DTD/XXE, zip-slip, non-ZIP, malformed XML), each with declared expected outcomes.
- `GoldenDocxAssertions.cs` — helper assertions: package validity (preflight + all XML parts parse with DTD prohibited), unrelated-part/paragraph/formatting-element invariance, run-formatting preservation (anchor + uninvolved runs + no invented formatting), SHA-256 fingerprints, decompressed-part snapshots.
- `DocxGoldenTests.cs` — 39 tests: mandated first test `AllGoldenFixtures_OpenAndParseAsExpected` (open/parse + origins + structural counts + byte-determinism), `StrictReplacement_InvariantSuite_Passes` (all 13 valid fixtures), `HostileFixtures_FailSafely` (all 9 hostile fixtures), no-op indexing fingerprints (13), no-op mutation fingerprints (target absent, identity), high-compression characterization.
- `tests/fixtures/docx/README.md` — documents every fixture's purpose, expectations, hostile outcomes, helpers and run command (spec 06 §26/§28, spec 11 §4–§7/§28, master §54/§92).
- One minimal production fix (see Deviations): `StrictTextMutator.MutateText` now treats an ordinal identity replacement as a byte-preserving no-op (spec 06 §19), with RED-verified regression tests.

## Files changed

- `tests/DocumentIDE.GoldenTests/DocumentIDE.GoldenTests.csproj` (new)
- `tests/DocumentIDE.GoldenTests/DocxGoldenTests.cs` (new)
- `tests/DocumentIDE.GoldenTests/GoldenDocxFixtures.cs` (new)
- `tests/DocumentIDE.GoldenTests/GoldenDocxFixtureBuilder.cs` (new)
- `tests/DocumentIDE.GoldenTests/GoldenDocxAssertions.cs` (new)
- `tests/fixtures/docx/README.md` (new)
- `src/DocumentIDE.Infrastructure/Docx/StrictTextMutator.cs` (**production edit**, 12-line no-op guard)
- `DocumentIDE.sln` (**shared-file edit**, GoldenTests entry/configs/nesting only)
- `docs/sprints/SPRINT-STATUS.md` intentionally NOT edited (controller-owned per dispatch)

## Verification

- `dotnet test tests\DocumentIDE.GoldenTests\DocumentIDE.GoldenTests.csproj` → **39 passed, 0 failed, 0 skipped** (1 s)
- `dotnet test tests\DocumentIDE.Core.Tests\DocumentIDE.Core.Tests.csproj` → **440 passed, 0 failed, 0 skipped**
- `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj` → 264 passed / 2 failed — both are the pre-existing release-artifact checks (`artifacts/desktop/DocumentIDE.Desktop.exe`, `artifacts/installer/DocumentIDE-Setup.msi`; require `scripts/build-release.ps1`, absent in a fresh worktree); excluding exactly those two → **264/264 passed, 0 skipped**
- `dotnet test tests\DocumentIDE.ArchitectureTests` → **7 passed, 0 failed**; `dotnet test tests\DocumentIDE.SafetyTests` → **12 passed, 0 failed**
- `dotnet sln DocumentIDE.sln list` includes `tests\DocumentIDE.GoldenTests\DocumentIDE.GoldenTests.csproj`
- Assertion-helper mutation sanity: intentionally wrong anchor + comparing changed parts both fail as expected (reverted; evidence in report)
- `scripts/verify.ps1` - PASS on the sprint tree: build + 39 GoldenTests + 440 Core + 12 Safety + 7 Architecture + 266 Integration + Web build/tests, 0 skipped (`.superpowers/sdd/QA-01-golden-docx-regression-corpus/verify-qa-01.log`).

## Acceptance criteria

- [x] **Corpus is version controlled and deterministic** — generator source committed; fixtures are built byte-deterministically at test time (fixed entry order/timestamps/compression/media bytes); the corpus test asserts build-twice byte equality for every fixture.

## Required tests

- [x] All fixtures open/parse as expected → `AllGoldenFixtures_OpenAndParseAsExpected`
- [x] Strict replacement invariant suite passes → `StrictReplacement_InvariantSuite_Passes` (13 fixture cases)
- [x] Hostile fixtures fail safely → `HostileFixtures_FailSafely` (9 cases) + `HighlyCompressedFixture_IsActuallyHighlyCompressed_AndStillBounded`

## Security & File-Safety Gate

- [x] no user file was mutated through an unreviewed/bypass path — tests mutate only temp copies they wrote; hostile fixtures are asserted byte-identical after all attempts
- [x] no arbitrary path or secret is exposed — fixtures are self-contained temp files; no real user paths
- [x] dependency/security boundaries remain intact — new project references only Core + Infrastructure; no new package dependencies
- [x] no destructive action silently broadened its scope — the only production change narrows behavior (identity no-op performs no write)
- [x] relevant higher-level safety rules remain stronger than convenience — spec 06 §19 no-op byte preservation is now enforced by regression tests

## Deviations / spec conflicts

1. **Production edit in `StrictTextMutator.cs` (DOCX-03 code):** the new regression test exposed that an ordinal identity replacement re-serialized the part and atomically replaced the original, violating spec 06 §19 (“A no-op operation must not rewrite package bytes”). Fixed with a 12-line early-return guard; RED verified before the guard. Batch apply (`MutateOccurrences`, BAT-04/05 seam) was not changed — its callers resolve occurrences explicitly; flagged as risk.
2. **Local fixture builder instead of `DocxTestFixtureBuilder`:** per dispatch, GoldenTests carries its own small deterministic builder rather than a test-project-to-test-project reference to `DocumentIDE.IntegrationTests`.
3. **Corpus superset of the sprint's minimum list:** hyperlinks, numbering, custom-styles and mixed margins/orientation were added because spec 06 §26 requires them for the canonical corpus.
4. **Shared-file edit:** `DocumentIDE.sln` touched only to add the GoldenTests project entry + configurations + nesting (15 lines).

## Risks discovered

- `DocxPackagePreflight` has no compression-ratio budget (only size/count budgets). The high-compression hostile fixture therefore asserts bounded deep parsing (typed `OccurrenceBudgetExceeded`) and an untouched source, not a ratio-based rejection. A future hardening sprint may add an explicit ratio check.
- `StrictTextMutator.MutateOccurrences` (batch apply seam) still rewrites a package if a selected occurrence is an identity replacement; no current batch caller produces identity selections, but the same spec 06 §19 rule could be applied there later.
- Byte-determinism is asserted within one process/platform run; ZIP deflate determinism across platforms is assumed, not cross-verified.

## Next eligible sprint(s)

- None directly depend on QA-01 alone. `REL-02` (release verification pipeline) remains blocked until QA-02/QA-04/QA-05/QA-06 are also DONE.
