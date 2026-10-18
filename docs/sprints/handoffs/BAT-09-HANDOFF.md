# Sprint Handoff — BAT-09 — Strict Page-Layout Operations

**Status:** DONE
**Branch:** `feat/bat-09-strict-page-layout-operations`
**Commits:** `149df08` (implementation); `b607c0d` (initial handoff); preview-model fix commit on top (spec 07 §19 status model — see below; hash recorded in the task report).

## What changed
- New `SetMarginsOperation` / `SetPaperSizeOperation` / `SetOrientationOperation` / `SetAlignmentOperation` (Core, all in `SetPageLayoutOperations.cs`): four independent strict operations with separate parameter records, separate preview result/occurrence types, and separate occurrence identities (`margin_/papersize_/orientation_{part}_s{n}`, `align_{part}_{origin}_p{n}`), satisfying the acceptance criterion “every operation has separate type/preview semantics” (spec 07 §19–20, spec 06 §20).
  - **Preview model (spec 07 §19 binding example).** Each `BuildOccurrencesAsync` returns a typed preview result whose rows carry a `PageLayoutTargetStatus`: `Changed` (applicable, `Selected=true`), `Unchanged` (current == requested — the spec’s “Section 2: unchanged” is exactly this row, rendered from current==new), and `Unspecified` (no explicit `w:pgMar`/`w:pgSz`; never conflated with Unchanged). Unchanged/Unspecified rows are preview-only: `Selected=false`, never selectable, and excluded from the mutator target lists — a selection containing only such IDs is a no-op that preserves package bytes, while an entirely unknown selected ID still fails closed (`SelectedOccurrenceNotFound`). The three section operations also return `AllSectionsTargeted`, stating when the operation’s scope covers every section of the document (“If operation targets all sections, state so.”). Alignment uses the same status model; paragraphs without direct `w:jc` are documented as outside the target scope and are not listed (they are never shown as Unchanged).
  - `SetMargins`: per-section four `w:pgMar` values in twips (spec 07 §19); every section of the document is listed in document order — changed sections show `Section N: old→new`, already-at-target sections show their current==new values with status Unchanged.
  - `SetPaperSize`: per-section absolute `w:pgSz` `@w:w/@w:h` (canonical `PaperSizeTwips.A4*`/`Letter*`); never touches margins or `@w:orient`; already-A4 sections are listed as Unchanged.
  - `SetOrientation`: sets `w:orient` and preserves the physical page size by swapping `@w:w/@w:h` only when they are ordered the other way for the requested orientation; the preview shows the exact post-swap dimensions. Already-correct orientation+ordering is listed as Unchanged.
  - `SetAlignment`: per-paragraph direct `w:pPr/w:jc` justification in body, table, header and footer parts; canonical mapping `Left/Center/Right/Justify` → `left/center/right/both`; already-at-target paragraphs are listed as Unchanged, style-inherited paragraphs are outside the target scope (no flattening, spec 06 §21).
  - All four validate parameters with typed `InvalidParameters` before any file access, honor a 10 000-row budget, return empty previews for non-DOCX files, and never mutate during preview.
- New shared `DocxPageLayoutScan` (Core): single-sourced deterministic read model — sections are the `w:sectPr` elements of `word/document.xml` in document order (paragraph-level breaks then the body-level final section); alignment paragraphs use the same paragraph indexing as the BAT-05 logical map / BAT-08 run scan; sections without explicit `w:pgMar`/`w:pgSz` appear as `Unspecified` preview rows and are never targeted (V1 never invents page geometry).
- New `StrictPageLayoutMutator` (Infrastructure): implements the new Core seam `IDocxPageLayoutMutator`. Two-phase fail-closed mutation on a read-only archive: verify every reviewed section/paragraph identity and expected value first (typed `PageLayoutTargetNotFound` / `PageLayoutTargetMismatch`), apply only the reviewed attributes in memory, prove via a masked-scope signature that nothing outside the reviewed attributes changed (`PageLayoutInvariantViolated`), persist only parts with a real change, require the styles part byte-identical across persistence, verify XML integrity, optional `IDocxPackagePreflight`. No-op and blocked paths never open the package for update, so package bytes are preserved (spec 06 §19). Unchanged targets are byte-identical to before this fix.
- Apply flows exclusively through the untouched BAT-04 `BatchApplyEngine` mutate delegate with the shared `PageLayoutChangeTarget` fingerprint/directory resolution and selected occurrence IDs (BAT-05/BAT-08 parity); only preview rows with status `Changed` can enter the mutator target list; stale files are rejected by the engine; one failing file yields a typed per-file `MutationFailed` outcome while other files succeed.
- TDD: mandated first RED (A4 test written first → compile failure for absent production types), then 4 behavioral Red→Green cycles (paper size; margins; orientation; alignment — the alignment RED also caught a real `w:jc` lookup bug), plus edge/no-op/fail-closed/validation cycles. The spec 07 §19 preview-model fix followed the same strict TDD: new status tests were written first → compile RED for the absent preview API, then implemented → GREEN 18/18. Full evidence in `.superpowers/sdd/BAT-09-strict-page-layout-operations/task-1-report.md`.

## Files changed
- `src/DocumentIDE.Core/Batch/Operations/SetPageLayoutOperations.cs` (new; preview status model added by the fix commit)
- `src/DocumentIDE.Infrastructure/Docx/StrictPageLayoutMutator.cs` (new; not in the sprint's planned list — see Deviations; unchanged by the fix commit)
- `tests/DocumentIDE.IntegrationTests/Batch/PageLayoutOperationsTests.cs` (new, 18 tests)
- No existing/shared file was modified (zero diff outside these three new files). `docs/sprints/SPRINT-STATUS.md` intentionally not touched (controller-owned).

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- Focused `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --nologo --filter "FullyQualifiedName~PageLayoutOperations"` → **18/18 passed, 0 failed, 0 skipped** (re-run after the spec 07 §19 preview-model fix).
- `DocumentIDE.Core.Tests` → **440/440 passed, 0 skipped** (re-run after the fix).
- `DocumentIDE.IntegrationTests` (full, final tree with `artifacts/` present) → **284/284 passed, 0 skipped**.
- TDD evidence per behavior (original cycles + spec-gap fix cycles): `.superpowers/sdd/BAT-09-strict-page-layout-operations/task-1-report.md`.
- `scripts/verify.ps1` - PASS on the sprint tree: build + 440 Core + 12 Safety + 7 Architecture + 284 Integration + Web build/tests, 0 skipped (`.superpowers/sdd/BAT-09-strict-page-layout-operations/verify-bat-09.log`).

## Acceptance criteria
- [x] Every operation has separate type/preview semantics — distinct parameter records (`SetMarginsParameters`, `SetPaperSizeParameters`, `SetOrientationParameters`, `SetAlignmentParameters`), distinct preview result/occurrence types with distinct fields/IDs, four separate operation classes, four separate preview tests, and `BatchOperationType.SetMargins/SetPaperSize/SetOrientation/SetAlignment` are distinct values.
- [x] Spec 07 §19 preview shape (fix) — the preview model lists Unchanged rows with current==new plus a typed status, distinguishes Unspecified (no explicit geometry) from Unchanged, and states `AllSectionsTargeted` when the operation targets all sections; `Build_MarginsPreviewListsChangedUnchangedAndUnspecifiedSections`, `Build_PreviewStatesWhenOperationTargetsAllSections`, and the paper/orientation/alignment status tests lock it.
- [x] Unchanged/Unspecified rows are never applicable — `Selected=false` and excluded from mutator target lists; `Apply_UnchangedEntriesAreNotApplicableAndPreservePackageBytes` proves a selection containing only an Unchanged ID leaves package bytes untouched; unknown selected IDs still fail closed (`SelectedOccurrenceNotFound`).
- [x] Required test “A4 operation does not alter margins” — `Apply_A4PaperSizeDoesNotAlterMargins` (mandated first RED; asserts margins, orientation, font, size, spacing intact; only `word/document.xml` changed; styles byte-identical).
- [x] Required test “Margin operation does not alter font” — `Apply_MarginsChangeDoesNotAlterFont` (margins changed; font family/size, spacing, alignment, page size intact; only targeted part changed; styles byte-identical).
- [x] Required test “Alignment operation does not alter section properties” — `Apply_AlignmentChangeDoesNotAlterSectionProperties` (sections signature canonically unchanged; font/spacing intact; only targeted part changed; styles byte-identical).
- [x] Steps 1–5 implemented: margins by section; A4 without altering margins; orientation; alignment only on targeted (reviewed, selected `Changed`) paragraphs; per-section/per-target old→new previews with deselection.
- [x] Edge cases: file changed after preview → `Skipped/Stale` with bytes intact; one bad file → typed `MutationFailed` while the valid file applies.
- [x] No-op discipline: previews list already-at-target targets as Unchanged (visible, not applicable); direct mutator calls with already-satisfied values and empty target lists leave package bytes untouched.
- [x] Typed fail-closed outcomes (`PageLayoutTargetNotFound`, `PageLayoutTargetMismatch`, `PageLayoutInvariantViolated`, `InvalidParameters`, `OccurrenceBudgetExceeded`, `SelectedOccurrenceNotFound`); no silent fallback.
- [x] No placeholder markers; `git diff` self-reviewed; coherent commits `149df08`, `b607c0d`, and the preview-model fix commit (hash in the task report).

## Security & file-safety gate
- [x] No user file was mutated through an unreviewed/bypass path — apply only via the BAT-04 engine’s mutate delegate; target resolved by directory + preview SHA-256 fingerprint, failing closed on mismatch/ambiguity; only reviewed, selected occurrence IDs mutate.
- [x] No arbitrary path or secret exposed — no new endpoints/paths or user-controlled part names; part names come from the scan of the provided package; typed error codes only.
- [x] Dependency/security boundaries remain intact — BCL only (`System.IO.Compression`, `System.Xml.Linq`); Core→Infrastructure seam keeps raw zip/XML mutation out of Core; no new packages.
- [x] No destructive action silently broadened its scope — each operation changes exactly one property family; masked-scope signature + styles byte-identity guard prove nothing else changed; previews list every section/paragraph in the operation’s scope with an explicit status, and V1 never invents page geometry.
- [x] Relevant higher-level safety rules remain stronger than convenience — recovery snapshot and atomic replace stay owned by BAT-04; unknown/mismatched targets block instead of guessing; no-op/failed paths preserve package bytes.

## Deviations / spec conflicts
- Planned Files listed only the Core operations file and the test file. The mutation capability required a new Infrastructure file `StrictPageLayoutMutator.cs` (mirroring the BAT-05 `IDocxOccurrenceMutator` + `StrictTextMutator` and BAT-08 `IDocxFontMutator` + `StrictFontMutator` seam pattern); it is additive and touches no existing file. No equivalent page-layout mutation abstraction existed.
- V1 target scope is deliberately explicit-only: sections without `w:pgMar`/`w:pgSz` and paragraphs without direct `w:jc` are not mutated (no fabricating geometry, no style flattening) per spec 06 §21 and the master “strict operations” rule; the preview surfaces this precisely — sections without explicit geometry appear as `Unspecified` rows (distinct from `Unchanged`), and paragraphs without direct justification are documented as outside the target scope and are not listed. No higher-level requirement weakened.
- `SetOrientation` is defined as setting `w:orient` while preserving the physical page size (dims swapped only when needed for the requested orientation). This is the Word-consistent reading of “orientation” as its own operation; `SetPaperSize` conversely sets exact dimensions and never touches `@w:orient`.

## Risks discovered
- `StrictPageLayoutMutator` is directly callable on an original path and performs no recovery snapshot by itself — same latent property BAT-05/BAT-08 recorded; the sole production caller is the BAT-04 engine’s temp copy. A second direct caller would need a guard.
- Geometry parameters use OOXML twips (1/20 pt); the UI must convert for display. Deterministic by design; canonical A4/Letter constants provided.
- Orientation requests on a section with a malformed `w:orient` value (not portrait/landscape) are treated as a change that normalizes the attribute; preview shows the raw current value.
- The 2 release/installer IntegrationTests failures are environmental (missing `artifacts/` in a fresh worktree); controller verify builds them.

## Next eligible sprint(s)
- Check `docs/sprints/SPRINT-STATUS.md` after the controller updates the board (not touched here per dispatch rules). Batch B siblings (BAT-11, etc.) may depend on their own DONE prerequisites.
