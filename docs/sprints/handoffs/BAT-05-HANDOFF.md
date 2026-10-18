# Sprint Handoff — BAT-05 — Exact Search & Replace Operation

**Status:** DONE
**Branch:** `feat/bat-05-exact-search-replace-operation`
**Commits:** `9363e7b` (implementation); completion metadata in the following docs commit.

## What changed
- New operation plug-in `SearchReplaceOperation` for the BAT-04 boundary: exact query/replacement parameters (`Find` + `Replacement`, no mode knob), DOCX logical matches across runs via `LogicalTextMatcher` (body/table/header/footer with origins), Markdown exact ordinal `IndexOf` scan, occurrence IDs/context/origin generation (DOCX matcher IDs, MD `body:{index}` + 30-char window), and apply of only selected reviewed occurrences (selected set + fail-closed `EnsureAllSelectedFound`; DOCX chosen-only mutation right-to-left MD replacement).
- New `IDocxOccurrenceMutator.MutateOccurrences` capability in `StrictTextMutator` (+175 additive lines): occurrence-precise mutation keyed by PartName/ParagraphIndex/LogicalStartIndex. The pre-existing `MutateText` overloads, preflight flow, `File.Replace`→`File.Copy` fallback, and `VerifyPackageIntegrity` are byte-identical; existing callers verified (StrictTextMutator 8/8, SafeApply 19/19). The old `MutateText(matches)` overload degenerates to replace-all and cannot honor deselection — the addition is required by Step 5, not avoidable scope.
- Fingerprint + directory target resolution fails closed (`ApplyFingerprintMismatch`/`AmbiguousApplyTarget`); reparse-point/bounded reads; snapshot-before-replace stays owned by the BAT-04 engine.
- No fuzzy/semantic/regex path anywhere (source-verified zero references; behavioral near-miss test); no API/UI; no new dependencies; no engine/preview/selection changes.

## Files changed
- `src/DocumentIDE.Core/Batch/Operations/SearchReplaceOperation.cs` (new)
- `src/DocumentIDE.Infrastructure/Docx/StrictTextMutator.cs` (additive only)
- `tests/DocumentIDE.IntegrationTests/Batch/SearchReplaceOperationTests.cs` (new, 8 tests)

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- `scripts/verify.ps1` → PASS on final tree: build, 428 Core + 200 Integration + 7 Architecture tests, Web build, 18 files / 80 Web tests, 0 skipped; log `artifacts/bat-05-verify.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped; log `artifacts/bat-05-security.log`.
- Focused `... --filter FullyQualifiedName~SearchReplace` → 8/8 (reviewer reproduced).
- TDD evidence per behavior in `.superpowers/sdd/BAT-05-exact-search-replace-operation/final-report.md` (report-only; single squashed commit, no separate RED artifacts — recorded honestly).
- Task review: **Spec ✅, Quality Approved**; 4 Low findings deferred (see risks).

## Acceptance criteria
- [x] Default matching is exact/deterministic — no matching-mode knob exists; enforced by tests.
- [x] Split-run DOCX replacement works — `Apply_SplitRunDocxReplacementWorks` ("Hello tar"|"get world" → "Hello Replaced world").
- [x] Fuzzy/semantic provider is never called — behavioral near-miss test + `IFuzzySearchService`/`ISemanticSearchService` unreachability.
- [x] Deselected occurrence remains unchanged — DOCX remainder + exact MD full-text assertions.
- [x] Failure/edge cases — stale file `Skipped/Stale` with bytes intact; single bad file yields typed `MutationFailed` while others apply.

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — engine gating (Previewed+unexpired, fingerprint equality) reused untouched.
- [x] No arbitrary path or secret exposed — fail-closed target resolution, typed codes only.
- [x] Dependency/security boundaries intact — BCL only, no new packages.
- [x] No destructive action silently broadened its scope — deselected occurrences byte-identical; BOM-bearing `.md` edge noted below (narrow, latent).
- [x] Higher-level safety rules stronger than convenience — unknown/ambiguous targets block rather than guess.

## Deviations / spec conflicts
- None. No spec weakened.

## Risks discovered
- Deferred Lows from review: (1) implementer report cites a stale test name (`SplitRun_Docx_Replacement_Targets_Selected_Occurrence`, real name `Apply_SplitRunDocxReplacementWorks`) and a wrong reason code (`FingerprintMismatch` vs actual `Stale`) — content maps to real tests; (2) BOM-bearing `.md` would lose its BOM on apply (offsets stay consistent; no BOM test file); (3) `MutateOccurrences` never snapshots by itself — invariant holds only because the sole caller passes the engine's temp copy; a future direct caller on an original would bypass recovery-snapshot rule (needs guard when a second caller appears); (4) `SqliteConnection.ClearAllPools()` in test `Dispose` is process-global under parallel xUnit — only theoretical interaction with flaky unrelated suites.
- Pre-existing intermittent flake in unrelated suites (`BatchPreviewHostTests`, `SourceWatcherTests`, `ShareRepositoryTests`, `MoveCorrelationChallengerTests`) reproduced at base without this diff — QA-02 risk, not caused here.

## Next eligible sprint(s)
- `BAT-06` — Occurrence Review UX (dep BAT-05) — batch longpole continues.
- Other READY unchanged: `DOCX-04`, `SHR-08`, `CAN-04`, `QA-01`, `QA-02`, `CAN-08`, `BAT-08`, `BAT-09`, `BAT-10`, `HIST-02`.
