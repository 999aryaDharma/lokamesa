# Sprint Handoff — BAT-09 — Strict Page-Layout Operations

**Status:** DONE
**Branch:** `feat/bat-09-strict-page-layout-operations`
**Commits:** `149df08` (implementation); completion metadata in the following docs commit.

## What changed
- New `SetMarginsOperation` / `SetPaperSizeOperation` / `SetOrientationOperation` / `SetAlignmentOperation` (Core, all in `SetPageLayoutOperations.cs`): four independent strict operations with separate parameter records, separate preview occurrence types, and separate occurrence identities (`margin_/papersize_/orientation_{part}_s{n}`, `align_{part}_{origin}_p{n}`), satisfying the acceptance criterion “every operation has separate type/preview semantics” (spec 07 §19–20, spec 06 §20).
  - `SetMargins`: per-section four `w:pgMar` values in twips (spec 07 §19); preview shows `Section N: old→new` only for sections whose margins differ; already-at-target sections are excluded (spec 07 §19 “Section 2: unchanged”).
  - `SetPaperSize`: per-section absolute `w:pgSz` `@w:w/@w:h` (canonical `PaperSizeTwips.A4*`/`Letter*`); never touches margins or `@w:orient`.
  - `SetOrientation`: sets `w:orient` and preserves the physical page size by swapping `@w:w/@w:h` only when they are ordered the other way for the requested orientation; the preview shows the exact post-swap dimensions. Already-correct orientation+ordering is excluded.
  - `SetAlignment`: per-paragraph direct `w:pPr/w:jc` justification in body, table, header and footer parts; canonical mapping `Left/Center/Right/Justify` → `left/center/right/both`; style-inherited paragraphs are deliberately out of scope (no flattening, spec 06 §21) and already-at-target paragraphs are excluded.
  - All four validate parameters with typed `InvalidParameters` before any file access, honor a 10 000-occurrence budget, return zero occurrences for non-DOCX files, and never mutate during preview.
- New shared `DocxPageLayoutScan` (Core): single-sourced deterministic read model — sections are the `w:sectPr` elements of `word/document.xml` in document order (paragraph-level breaks then the body-level final section); alignment paragraphs use the same paragraph indexing as the BAT-05 logical map / BAT-08 run scan; sections without explicit `w:pgMar`/`w:pgSz` are reported as unspecified and never targeted (V1 never invents page geometry).
- New `StrictPageLayoutMutator` (Infrastructure): implements the new Core seam `IDocxPageLayoutMutator`. Two-phase fail-closed mutation on a read-only archive: verify every reviewed section/paragraph identity and expected value first (typed `PageLayoutTargetNotFound` / `PageLayoutTargetMismatch`), apply only the reviewed attributes in memory, prove via a masked-scope signature that nothing outside the reviewed attributes changed (`PageLayoutInvariantViolated`), persist only parts with a real change, require the styles part byte-identical across persistence, verify XML integrity, optional `IDocxPackagePreflight`. No-op and blocked paths never open the package for update, so package bytes are preserved (spec 06 §19).
- Apply flows exclusively through the untouched BAT-04 `BatchApplyEngine` mutate delegate with the shared `PageLayoutChangeTarget` fingerprint/directory resolution and selected occurrence IDs (BAT-05/BAT-08 parity); stale files are rejected by the engine; one failing file yields a typed per-file `MutationFailed` outcome while other files succeed.
- TDD: mandated first RED (A4 test written first → compile failure for absent production types), then 4 behavioral Red→Green cycles (paper size; margins; orientation; alignment — the alignment RED also caught a real `w:jc` lookup bug), plus edge/no-op/fail-closed/validation cycles. Full evidence in `.superpowers/sdd/BAT-09-strict-page-layout-operations/task-1-report.md`.

## Files changed
- `src/DocumentIDE.Core/Batch/Operations/SetPageLayoutOperations.cs` (new)
- `src/DocumentIDE.Infrastructure/Docx/StrictPageLayoutMutator.cs` (new; not in the sprint's planned list — see Deviations)
- `tests/DocumentIDE.IntegrationTests/Batch/PageLayoutOperationsTests.cs` (new, 16 tests)
- No existing/shared file was modified (zero diff outside these three new files).

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- Focused `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --nologo --filter "FullyQualifiedName~PageLayoutOperations"` → **16/16 passed, 0 failed, 0 skipped**.
- `DocumentIDE.Core.Tests` → **440/440 passed, 0 skipped**.
- `DocumentIDE.IntegrationTests` (full, re-run on the final tree) → **280/282 passed, 0 skipped**; the 2 failures are `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` and `ScriptTests.Release_Build_Embeds_Production_Web_Assets`, which require the release-pipeline artifacts `artifacts/installer/DocumentIDE-Setup.msi` and `artifacts/desktop/DocumentIDE.Desktop.exe`. Proven environmental (missing artifacts in a fresh worktree; identical failures documented at the same base for BAT-08), unrelated to this diff.
- TDD evidence per behavior: `.superpowers/sdd/BAT-09-strict-page-layout-operations/task-1-report.md`.
- `scripts/verify.ps1` — controller runs full repository verification on this worktree.

## Acceptance criteria
- [x] Every operation has separate type/preview semantics — distinct parameter records (`SetMarginsParameters`, `SetPaperSizeParameters`, `SetOrientationParameters`, `SetAlignmentParameters`), distinct preview occurrence types with distinct fields/IDs, four separate operation classes, four separate preview tests, and `BatchOperationType.SetMargins/SetPaperSize/SetOrientation/SetAlignment` are distinct values.
- [x] Required test “A4 operation does not alter margins” — `Apply_A4PaperSizeDoesNotAlterMargins` (mandated first RED; asserts margins, orientation, font, size, spacing intact; only `word/document.xml` changed; styles byte-identical).
- [x] Required test “Margin operation does not alter font” — `Apply_MarginsChangeDoesNotAlterFont` (margins changed; font family/size, spacing, alignment, page size intact; only targeted part changed; styles byte-identical).
- [x] Required test “Alignment operation does not alter section properties” — `Apply_AlignmentChangeDoesNotAlterSectionProperties` (sections signature canonically unchanged; font/spacing intact; only targeted part changed; styles byte-identical).
- [x] Steps 1–5 implemented: margins by section; A4 without altering margins; orientation; alignment only on targeted (reviewed, selected) paragraphs; per-section/per-target old→new previews with deselection.
- [x] Edge cases: file changed after preview → `Skipped/Stale` with bytes intact; one bad file → typed `MutationFailed` while the valid file applies.
- [x] No-op discipline: already-at-target sections/paragraphs are excluded from preview; direct mutator calls with already-satisfied values and empty target lists leave package bytes untouched.
- [x] Typed fail-closed outcomes (`PageLayoutTargetNotFound`, `PageLayoutTargetMismatch`, `PageLayoutInvariantViolated`, `InvalidParameters`, `OccurrenceBudgetExceeded`); no silent fallback.
- [x] No placeholder markers; `git diff` self-reviewed; coherent commit `149df08`.

## Security & file-safety gate
- [x] No user file was mutated through an unreviewed/bypass path — apply only via the BAT-04 engine’s mutate delegate; target resolved by directory + preview SHA-256 fingerprint, failing closed on mismatch/ambiguity; only reviewed, selected occurrence IDs mutate.
- [x] No arbitrary path or secret exposed — no new endpoints/paths or user-controlled part names; part names come from the scan of the provided package; typed error codes only.
- [x] Dependency/security boundaries remain intact — BCL only (`System.IO.Compression`, `System.Xml.Linq`); Core→Infrastructure seam keeps raw zip/XML mutation out of Core; no new packages.
- [x] No destructive action silently broadened its scope — each operation changes exactly one property family; masked-scope signature + styles byte-identity guard prove nothing else changed; previews list every affected section/paragraph and V1 never invents page geometry.
- [x] Relevant higher-level safety rules remain stronger than convenience — recovery snapshot and atomic replace stay owned by BAT-04; unknown/mismatched targets block instead of guessing; no-op/failed paths preserve package bytes.

## Deviations / spec conflicts
- Planned Files listed only the Core operations file and the test file. The mutation capability required a new Infrastructure file `StrictPageLayoutMutator.cs` (mirroring the BAT-05 `IDocxOccurrenceMutator` + `StrictTextMutator` and BAT-08 `IDocxFontMutator` + `StrictFontMutator` seam pattern); it is additive and touches no existing file. No equivalent page-layout mutation abstraction existed.
- V1 target scope is deliberately explicit-only: sections without `w:pgMar`/`w:pgSz` and paragraphs without direct `w:jc` are out of scope (no fabricating geometry, no style flattening) per spec 06 §21 and the master “strict operations” rule; previews make the scope inspectable. No higher-level requirement weakened.
- `SetOrientation` is defined as setting `w:orient` while preserving the physical page size (dims swapped only when needed for the requested orientation). This is the Word-consistent reading of “orientation” as its own operation; `SetPaperSize` conversely sets exact dimensions and never touches `@w:orient`.

## Risks discovered
- `StrictPageLayoutMutator` is directly callable on an original path and performs no recovery snapshot by itself — same latent property BAT-05/BAT-08 recorded; the sole production caller is the BAT-04 engine’s temp copy. A second direct caller would need a guard.
- Geometry parameters use OOXML twips (1/20 pt); the UI must convert for display. Deterministic by design; canonical A4/Letter constants provided.
- Orientation requests on a section with a malformed `w:orient` value (not portrait/landscape) are treated as a change that normalizes the attribute; preview shows the raw current value.
- The 2 release/installer IntegrationTests failures are environmental (missing `artifacts/` in a fresh worktree); controller verify builds them.

## Next eligible sprint(s)
- Check `docs/sprints/SPRINT-STATUS.md` after the controller updates the board (not touched here per dispatch rules). Batch B siblings (BAT-11, etc.) may depend on their own DONE prerequisites.
