# Sprint Handoff — BAT-07 — Regex Advanced Replace

**Status:** DONE
**Branch:** `fix/bat-07-regex-advanced-replace`
**Commits:** `f8e220f` (implementation); completion metadata in the following docs commit.

## What changed
- New opt-in `RegexReplaceOperation` mirroring the BAT-05 exact structure: `RegexEnabled` defaults false (OFF path delegates verbatim to exact semantics), `RegexOptions.NonBacktracking` opt-in with incompatible-pattern typed rejection, finite match timeout (2 s default, ≤0 rejected), eager compile before any file I/O with typed `InvalidRegexPattern`, zero-length-match and occurrence-budget guards, capture-group expansion via `match.Result` identical in preview and apply, reviewed-match-IDs-only apply through the BAT-04 engine (fail-closed `EnsureAllSelectedFound`, fingerprint gate, snapshot, atomic replace).
- `LogicalTextMatcher` additive `Pattern` support: empty-query early-return preserved for existing callers; ordinal loop (budget, cancellation, comparison, advance) unchanged; instance timeout bounds the DOCX regex scan (proven <3 s on pathological fixture).
- `SearchReplaceOperation` change is exactly 6× `private`→`internal` + 3 comment lines, zero logic — BAT-05 semantics byte-identical.
- No UI (spec 07 §6 regex-indicator UI belongs to a downstream UX sprint — disclosed), no API, no engine changes, BCL Regex only, no new dependencies.

## Files changed
- `src/DocumentIDE.Core/Batch/Operations/RegexReplaceOperation.cs` (new)
- `src/DocumentIDE.Core/Batch/Operations/SearchReplaceOperation.cs` (visibility-only)
- `src/DocumentIDE.Core/Docx/LogicalTextMatcher.cs` (additive Pattern)
- `tests/DocumentIDE.Core.Tests/Batch/RegexReplaceTests.cs` (new, 12 tests)

## Verification
- `scripts/verify.ps1` → PASS on final tree: build, 440 Core + 200 Integration + 7 Architecture tests, Web build, 19 files / 89 Web tests, 0 skipped; log `artifacts/bat-07-verify.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped; log `artifacts/bat-07-security.log`.
- Reviewer re-ran at HEAD: RegexReplace 12/12 (<1 s), SearchReplace 8/8, SafeApply 19/19, LogicalTextMatcher 11/11, Architecture 7/7.
- TDD: 5 RED→GREEN cycles (mandated "OFF by default" first, CS0246 RED re-executed for ledger); one green-at-birth stale test disclosed honestly; evidence in `.superpowers/sdd/BAT-07-regex-advanced-replace/final-report.md`.
- Task review: **Spec ✅, Quality Approved**; Low/Info only (see risks).

## Acceptance criteria
- [x] Regex never bypasses preview — only two public entry points (build occurrences + create mutate); apply exclusively through engine gates; structural test.
- [x] Regex is OFF by default — default-false + exact-delegate + source-unmutated test.
- [x] Catastrophic-pattern fixture times out safely — 100 ms budget MD (<1 s), DOCX cross-run (<3 s), NonBacktracking (<1 s), all typed.
- [x] Capture-group replacement preview matches apply — per-occurrence `ReplacementOutput` equals final content (MD + cross-run DOCX, selected-only).

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — engine gates reused untouched.
- [x] No arbitrary path or secret exposed — typed codes, no new I/O surface.
- [x] Dependency/security boundaries intact — BCL only, no csproj changes.
- [x] No destructive action silently broadened its scope — default stays exact (spec 07 §6 honored).
- [x] Higher-level safety rules stronger than convenience — invalid/pathological/timeout all fail closed before mutation.

## Deviations / spec conflicts
- None. No spec weakened.

## Risks discovered
- Deferred: DOCX zero-length vs budget check ordering (both typed fail-closed; text path rejects immediately); timeout is per-match-call (bounded input + budget + caller cancellation present; NonBacktracking documented as stronger option); report miscount (5 vs 6 visibility changes, docs nit).
- Pre-existing NuGet advisories (AngleSharp/HtmlSanitizer/SQLitePCLRaw) surfaced during runs — unrelated, no dependency changes here.

## Next eligible sprint(s)
- Batch chain closed (BAT-01→BAT-11 path: BAT-08/09/10 + HIST-02 remain READY; BAT-11 still blocked on BAT-10+HIST-02).
- Other READY unchanged: `DOCX-04`, `SHR-08`, `CAN-04`, `QA-01`, `QA-02`, `CAN-08`, `BAT-08`, `BAT-09`, `BAT-10`, `HIST-02`.
