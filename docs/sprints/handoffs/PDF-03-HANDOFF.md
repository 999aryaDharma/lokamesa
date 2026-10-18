# Sprint Handoff — PDF-03

**Status:** DONE
**Branch:** `perf/pdf-03-streaming-ordered-pdf-merge`
**Commits:** `ebcfaf5`

## What changed

Streaming ordered PDF merge over PDF-01/PDF-02 render services. New
`DocumentIDE.Core.Pdf` orchestration (`PdfMergeService` + `PdfMergeModels`)
plus a presentational `MergeOrderDialog` web component. DOCX inputs are
rendered strictly in user order via `PdfExportService.ExportSingleAsync`
into a fresh per-job temp dir (WORD-03 queue, concurrency stays 1), then
intermediates are appended sequentially with an 80 KB output buffer and
the job dir is deleted on success AND failure. The merge order is the
explicit request list verbatim — never sorted or inferred.

File mapping note (planned names reused 1:1, no renames):
- planned `src/DocumentIDE.Core/Pdf/PdfMergeService.cs` → created as planned
- planned `src/DocumentIDE.Web/src/features/pdf/MergeOrderDialog.tsx` → created as planned (+ colocated `MergeOrderDialog.test.tsx`)
- planned `tests/DocumentIDE.IntegrationTests/Pdf/PdfMergeMemoryTests.cs` → created as planned
- supporting (not in plan, required by acceptance): `PdfMergeModels.cs`
  (`OrderedMergeRequest`/`OrderedMergeResult`), 1 DI line in `Api/Program.cs`
  (`AddScoped<PdfMergeService>`, privileged Api only)

Merge library decision (spec 09 §11, license compatibility): BCL-only
hand-rolled sequential object-graph appender — zero new packages, zero new
license surface. Rejected iText (AGPL copyleft, incompatible) and adding
any external merge dependency (new license/supply-chain surface for a P1
a bounded streaming appender satisfies). Object bodies (incl. content
streams) are carried over verbatim with cross-file reference renumbering;
a combined page tree + catalog + xref table are rebuilt. Typed
`ExportFailed` (never silent) for encrypted PDFs, compressed object/xref
streams, missing page-tree objects, and malformed inputs.

Collision-behavior mapping (per PDF-01/02 guidance): `CollisionBehavior`
is applied to the final merged output with Fail/Overwrite/Rename semantics
identical to PDF-01; a pre-existing user PDF is never deleted (partial
merge artifacts this call created are removed on failure/cancel).

## Files changed

- `src/DocumentIDE.Core/Pdf/PdfMergeService.cs` (new: `MergeOrderedAsync`,
  `MergeDocumentsOrderedAsync`, `CreateJobDirectory`/`GetPdfJobsRoot`
  per spec 09 §10, `MeasurePeakAdditionalBytesAsync` for the §18 test)
- `src/DocumentIDE.Core/Pdf/PdfMergeModels.cs` (new: `OrderedMergeRequest`
  explicit-order contract, `OrderedMergeResult` with `PageCount`,
  echoed `OrderedSources`, `TempJobDirectory`)
- `src/DocumentIDE.Web/src/features/pdf/MergeOrderDialog.tsx` (new: drag
  reorder list, visible filenames, final count, Up/Down buttons, Confirm
  emits current explicit order)
- `src/DocumentIDE.Web/src/features/pdf/MergeOrderDialog.test.tsx` (new: 2 tests)
- `src/DocumentIDE.Api/Program.cs` (+1 line DI: `AddScoped<PdfMergeService>`)
- `tests/DocumentIDE.IntegrationTests/Pdf/PdfMergeMemoryTests.cs` (new: 6 tests)
- `docs/sprints/SPRINT-STATUS.md` (PDF-03 row READY → DONE)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter "FullyQualifiedName~PdfMergeMemoryTests"` → PASS (6/6, 0 failed)
- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter "FullyQualifiedName~Pdf"` → PASS (25/25: 12 PDF-01 + 7 PDF-02 + 6 PDF-03, 0 failed)
- `dotnet test tests/DocumentIDE.IntegrationTests/...` (full) → 385/387; the 2 failures (`InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`, `ScriptTests.Release_Build_Embeds_Production_Web_Assets`) are PRE-EXISTING on clean base (verified via `git stash -u` re-run: 2/2 fail without my changes) — release-packaging tests, unrelated to PDF
- `dotnet test tests/DocumentIDE.Core.Tests/...` → PASS (459/459, 0 failed)
- `dotnet build src/DocumentIDE.Core` → 0 warnings, 0 errors
- `dotnet build src/DocumentIDE.Api` → 0 errors (only pre-existing NU1603/NU1902/NU1903 warnings)
- `npx tsc -b` (DocumentIDE.Web) → clean
- `npx vitest run` (DocumentIDE.Web) → PASS (206/206 across 35 files, incl. 2 new); `npx vitest run src/features/pdf` → 4/4
- TDD Red proven: pre-implementation build failed with `CS0246: PdfMergeService/OrderedMergeRequest could not be found`
- Peak-memory regression: additional=4,528,992 bytes vs totalSources=25,193,994 bytes (6 × ~4.2 MB inputs), ratio 18.0% — well under the 50% bound; peak ≈ one input file + 80 KB buffer, not the corpus

## Acceptance criteria

- [x] No silent inferred merge order — `Merge_OutputPageOrder_MatchesExplicitUserOrder` (user order gamma→alpha→beta verified by marker positions) + `Merge_RequiresExplicitOrder_EmptyRequestRejected` (empty list throws) + dialog test proving the component renders the explicit order verbatim and never sorts; service has no sort/infer path
- [x] Pages are not rasterized — `Merge_PreservesVectorContent_WithoutRasterization` (`Tj` + markers survive, no `/Subtype /Image` or `/DCTDecode`, per-page MediaBox preserved)
- [x] Peak memory does not scale linearly with total source bytes — `Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes` (18.0% < 50% bound)
- [x] Temp job dir cleaned on success AND failure — `MergeDocuments_CleansTempJobDir_OnSuccess` / `..._OnRenderFailure` (failure also leaves no merged output)

## Security & file-safety gate

- [x] no user file mutated through unreviewed path — DOCX only read via PDF-01 fingerprint path; intermediates go to a fresh per-job temp dir with `Overwrite` scoped to that dir; final output honors explicit collision choice
- [x] no arbitrary path or secret exposed — paths stay local, error messages contain only paths/status, no secrets
- [x] dependency/security boundaries intact — Core gains zero package references; ShareHost graph untouched; DI added to privileged Api only; no COM threading reimplemented (renders go through the WORD-03 queue seam)
- [x] no destructive action silently broadened — default collision policy is Fail; pre-existing outputs are never deleted (partial artifacts only); merge drops non-object bytes (e.g. comments) by construction, pages/resources carried verbatim
- [x] higher-level safety rules stronger than convenience — sequential per-file processing (no parallelism); encrypted/object-stream PDFs fail loudly instead of degrading; cancellation only between files

## Deviations / spec conflicts

None. PDF-02 verified DONE in SPRINT-STATUS.md before editing. One scope note: the sprint's "Step 3: Select license-compatible streaming merge library" is satisfied by the recorded BCL-only decision above (no third-party library), not by adopting a package.

## Risks discovered

- Real-Word risk inherited from PDF-01/02: no live-Word merge test (fake renderer only); recommend a manual multi-document Word-present smoke test before release, verifying merged page order and fonts against Word output.
- The appender supports plain-xref PDFs only (what Word writes). Encrypted or object-stream-compressed PDFs are rejected with typed errors — acceptable per spec, but surface the message in PDF-04 UX so users are not confused.
- Document-level constructs (outlines/bookmarks, AcroForm, named destinations) are not carried into the merged file; page content and resources are preserved losslessly. If PDF-04 needs bookmarks, that is a follow-up, not this sprint.

## Next eligible sprint(s)

- PDF-04 (PDF Finalization UX) — needs CAN-08 too; check status (CAN-08 DONE per board, so PDF-04 now unblocked on PDF-03).
- QA-06 (Performance Benchmark Gate) — blocked on CAN-07, SRCH-05, GR-05, PDF-03; PDF-03 leg now DONE with the peak-memory evidence above.
