# Sprint Handoff — BAT-08 — Strict Font & Font-Size Operations

**Status:** DONE
**Branch:** `feat/bat-08-strict-font-font-size-operations`
**Commits:** `554c0e1` (implementation); completion metadata in the following docs commit.

## What changed
- New `SetFontOperation` (Core): explicit V1 target semantics — text-bearing runs in `word/document.xml`, `word/header*.xml`, `word/footer*.xml` that carry a **direct** font-family reference (`w:rPr/w:rFonts` `@w:ascii` or `@w:hAnsi`); optional `TargetFontFamily` narrows by current direct family (ordinal, case-insensitive); runs whose family comes from styles/defaults are deliberately out of scope so V1 never flattens style formatting (spec 06 §21, spec 07 §17). Deterministic preview of effective current→new family per run with run identity (`font_{part}_{origin}_p{n}_r{n}`), 30-char context (BAT-05 `TextPreviewContext`), and Body/Table/Header/Footer origins; runs already at the requested family and non-DOCX files preview as unaffected.
- New `SetFontSizeOperation` (Core): separate operation (spec 07 §18), same run target semantics for direct `w:sz`; previews current→new half-points; only `w:sz/@w:val` changes.
- New shared `DocxFontScan` (in `SetFontOperation.cs`): single-sourced run enumeration used by both previews and the apply-time mutator (field instructions excluded, canonical paragraph/run indexing mirrored from the BAT-05 logical map), plus the shared `FontChangeTarget` review record and fail-closed fingerprint/directory/ambiguity resolution (BAT-05 parity).
- New `StrictFontMutator` (Infrastructure): implements `IDocxFontMutator`. Two-phase fail-closed mutation — verify every reviewed target (part → paragraph → run identity + expected current value, typed `FontTargetNotFound`/`FontTargetMismatch`/`FontTargetInvalid`) on a read-only archive first; apply in memory; prove paragraph/section invariants unchanged (`w:pPr` + `w:sectPr` canonical signature); persist only changed parts; require the styles part byte-identical across persistence; XML-integrity check; optional `IDocxPackagePreflight`. Failed targets and no-ops leave package bytes untouched; only `@w:ascii`/`@w:hAnsi` (family) or `w:sz/@w:val` (size) change.
- Apply flows route exclusively through the untouched BAT-04 `BatchApplyEngine` mutate delegate (reviewed fingerprint + selected occurrence IDs + snapshot + atomic replace); deselected runs remain identical.
- TDD: 5 Red→Green cycles (family op; size op; mixed preview/target match; mutator fail-closed + invariants; parameter validation), then required edge-case tests. RED evidence: missing-type compile failure, `NotImplementedException`, wrong inclusion of already-at-target run, silent mismatched-target apply, `FileNotFoundException` instead of typed rejection.

## Files changed
- `src/DocumentIDE.Core/Batch/Operations/SetFontOperation.cs` (new)
- `src/DocumentIDE.Core/Batch/Operations/SetFontSizeOperation.cs` (new)
- `src/DocumentIDE.Infrastructure/Docx/StrictFontMutator.cs` (new; not in the sprint's planned list — see Deviations)
- `tests/DocumentIDE.IntegrationTests/Batch/FontOperationsTests.cs` (new, 9 tests)
- No existing/shared file was modified (zero diff outside these four new files).

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- Focused `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --nologo --filter "FullyQualifiedName~FontOperations"` → **9/9 passed, 0 failed, 0 skipped**.
- `DocumentIDE.Core.Tests` → **440/440 passed, 0 skipped**.
- `DocumentIDE.IntegrationTests` (full) → **230/232 passed, 0 skipped**; the 2 failures are `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` and `ScriptTests.Release_Build_Embeds_Production_Web_Assets`, which require `artifacts/installer/DocumentIDE-Setup.msi` and `artifacts/desktop/...` produced by the release script. Proven pre-existing: both fail identically at the base commit with BAT-08 changes stashed (missing release artifacts in a fresh worktree), and are expected to pass once the controller's verification pipeline builds artifacts.
- TDD evidence per behavior: `.superpowers/sdd/BAT-08-strict-font-font-size-operations/task-1-report.md`.
- `scripts/verify.ps1` → PASS on the sprint tree: build, 440 Core + 232 Integration (incl. 9 FontOperations tests) + 7 Architecture tests, Web build/tests, 0 skipped; log `.superpowers/sdd/BAT-08-strict-font-font-size-operations/verify-bat-08.log`.
- Explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped.

## Acceptance criteria
- [x] **No normalize-to-template behavior** — the mutator rewrites only the reviewed run's font reference; tests assert the styles part and every other package entry byte-identical (`AssertOnlyPartChanged`), paragraph/section properties canonically unchanged (`AssertFormattingInvariantsUnchanged`), and `w:sz`/`w:spacing`/`w:pgMar`/`w:pgSz` values intact (`Apply_FontChangeLeavesSizeAndMarginsUnchanged`).
- [x] Font change leaves size/margins unchanged (required test #1).
- [x] Size change leaves family/margins unchanged (required test #2).
- [x] Mixed formatting preview identifies affected text (required test #3) — affected/deselected/inherited/already-target identification, filter matching, origins, contexts.
- [x] Edge cases: file changed after preview → `Skipped/Stale` with bytes intact; one bad file → typed `MutationFailed` while another file applies; deselected occurrence untouched.
- [x] No placeholder markers; `git diff` self-reviewed; coherent commit `554c0e1`.

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — apply only via the BAT-04 engine's mutate delegate, target resolved by directory + preview fingerprint, fail-closed on mismatch/ambiguity.
- [x] No arbitrary path or secret exposed — no new endpoints/paths; typed error codes only; mutation scoped to reviewed parts inside the provided package.
- [x] Dependency/security boundaries intact — BCL only (`System.IO.Compression`, `System.Xml.Linq`), no new packages; Core→Infrastructure seam preserves layering.
- [x] No destructive action silently broadened its scope — direct-formatting-only semantics documented; preview lists every affected run; mutator verifies reviewed values before writing and never touches styles/sizes/spacing/sections outside the reviewed property.
- [x] Higher-level safety rules remain stronger than convenience — no-op and failure paths preserve package bytes (read-only verification, Update archive only opened when a real change persists); unknown targets block instead of guessing.

## Deviations / spec conflicts
- Planned Files listed only the two Core operations and the test file. The mutation capability required a new Infrastructure file `StrictFontMutator.cs` (mirroring the BAT-05 `IDocxOccurrenceMutator` + `StrictTextMutator` seam pattern); it is additive and touches no existing file. Recorded per the sprint's "modify that existing file rather than creating a duplicate abstraction" rule: no equivalent font-mutation abstraction existed, and DOCX-03's `StrictTextMutator` is text-only.
- V1 target scope is deliberately narrowed to **direct** run formatting (no style flattening) per spec 06 §21; the broader "set all body text" reading of spec 07 §17 is represented by the preview's explicit scope instead of silently rewriting style-inherited runs. No higher-level requirement weakened; no normalize-to-template workflow exists.

## Risks discovered
- `StrictFontMutator` is directly callable on an original path and performs no recovery snapshot by itself — same latent property BAT-05 recorded for `MutateOccurrences`; the sole production caller is the BAT-04 engine's temp copy. A second direct caller would need a guard.
- Font size parameters use OOXML half-points (`12 pt = 24`); the UI must convert for display. Deterministic by design.
- Style/default-inherited fonts and sizes are out of V1 scope; an "effective formatting" variant would require style-chain resolution and an explicit flattening decision (future sprint, not required here).
- No `SetFontSize` current-size target filter exists (spec §17 only defines the optional match for font family); per-occurrence deselection covers narrower review.
- The 2 release/installer IntegrationTests failures are environmental (missing `artifacts/` in a fresh worktree), proven identical at base; controller verify builds them.

## Next eligible sprint(s)
- `BAT-09` — Strict Page-Layout Operations (deps BAT-04, DOCX-03 both DONE) — batch longpole; can reuse the two-phase strict mutator + invariant pattern.
- `BAT-10` — Strict Batch Rename (deps BAT-04, FS-07; parallel batch A).
- Board update (`docs/sprints/SPRINT-STATUS.md`) is controller-owned.
