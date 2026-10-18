# Sprint Handoff — PDF-02

**Status:** DONE
**Branch:** `feat/pdf-02-batch-separate-pdf-export`
**Commits:** `a978e51` (implementation) + docs commit (this handoff + status)

## What changed

Ordered batch separate-PDF export over PDF-01's single service, with
FND-04 job-runtime integration. New `DocumentIDE.Core.Pdf` orchestration
(`BatchPdfExportService` + `BatchPdfExportModels`) plus a presentational
`BatchExportProgress` web component. Each document is exported strictly in
input order via `PdfExportService.ExportSingleAsync`, so every Word render
still goes through the shared WORD-03 queue exactly once (concurrency
stays 1); no COM threading reimplemented. No Preview/QuickLook, Canvas,
or Share code touched. No PDF-03 merge work.

File mapping note (planned names reused 1:1, no renames):
- planned `src/DocumentIDE.Core/Pdf/BatchPdfExportService.cs` → created as planned
- planned `src/DocumentIDE.Web/src/features/pdf/BatchExportProgress.tsx` → created as planned (+ colocated `BatchExportProgress.test.tsx`)
- planned `tests/DocumentIDE.IntegrationTests/Pdf/BatchPdfExportTests.cs` → created as planned
- supporting (not in plan, required by acceptance): `BatchPdfExportModels.cs`
  (`BatchPdfExportItem`/`BatchPdfExportRequest`/`BatchPdfExportProgress`/
  `BatchPdfExportFileResult`/`BatchPdfExportResult`), 1 DI line in `Api/Program.cs`

Collision-behavior mapping (per PDF-01 handoff guidance): the batch
`CollisionBehavior` is forwarded unchanged to each `ExportSingleAsync`
call (Fail/Overwrite/Rename semantics identical to PDF-01); batch-level
`OutputCollision` outcomes aggregate as *skipped* (spec 09 §8 Skip).
Source/Word/disk failures aggregate as *failed*; never-started files
after cancellation aggregate as *Cancelled*. Summary line follows
spec 09 §12 (`N PDFs generated / M skipped / K failed`).

## Files changed

- `src/DocumentIDE.Core/Pdf/BatchPdfExportService.cs` (new: ordered loop, per-file progress, between-document cancellation, `CreateBatchJob` FND-04 adapter)
- `src/DocumentIDE.Core/Pdf/BatchPdfExportModels.cs` (new: request/progress/per-file/aggregate records with `Succeeded/Skipped/Failed/Cancelled` counts + `Summary`)
- `src/DocumentIDE.Web/src/features/pdf/BatchExportProgress.tsx` (new: progressbar, current filename, summary, Cancel button)
- `src/DocumentIDE.Web/src/features/pdf/BatchExportProgress.test.tsx` (new: 2 tests)
- `src/DocumentIDE.Api/Program.cs` (+1 line DI: `AddScoped<BatchPdfExportService>`, privileged Api only)
- `tests/DocumentIDE.IntegrationTests/Pdf/BatchPdfExportTests.cs` (new: 7 tests)
- `docs/sprints/SPRINT-STATUS.md` (PDF-02 row READY → DONE)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter "FullyQualifiedName~BatchPdfExportTests"` → PASS (7/7, 0 failed)
- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter "FullyQualifiedName~Pdf"` → PASS (19/19: 12 PDF-01 + 7 PDF-02, 0 failed)
- `dotnet test tests/DocumentIDE.Core.Tests/...` → PASS (459/459, 0 failed)
- `dotnet build src/DocumentIDE.Core` → 0 warnings, 0 errors
- `dotnet build src/DocumentIDE.Api` → 0 errors, 24 warnings (all pre-existing NU1603/NU1902/NU1903 package warnings, identical count with change stashed)
- `npx tsc -b` (DocumentIDE.Web) → clean
- `npx vitest run` (DocumentIDE.Web) → PASS (192/192 across 32 files, incl. 2 new)
- TDD Red proven: pre-implementation build failed with `CS0246: BatchPdfExportService could not be found`

## Acceptance criteria

- [x] Completed outputs are not deleted on unrelated later failure — `PartialFailures_AreVisible_AndCompletedOutputsSurvive` (good1/good2 PDFs exist after bad.docx fails) + cancellation test keeps first output; service performs no output deletion at all.
- [x] Multiple exports remain Word-concurrency=1 — `MultipleExports_RemainWordConcurrencyOne` (5 docs, 5 enqueues, renderer always inside queue dispatch, max concurrent renderer executions == 1).
- [x] Cancellation stops before next file — `Cancellation_StopsBeforeNextFile_AndKeepsCompletedOutputs` (1 succeeded, rest Cancelled, no further renderer calls, first PDF kept).
- [x] Partial failures are visible — typed per-file `ExportResult` (incl. error message) aggregated into succeeded/skipped/failed/cancelled counts + §12 summary line.

## Security & file-safety gate

- [x] no user file mutated through unreviewed path — DOCX only read via PDF-01 fingerprint path; outputs written only to the explicitly chosen output folder
- [x] no arbitrary path or secret exposed — paths stay local, error messages contain only paths/status, no secrets
- [x] dependency/security boundaries intact — Core has no Interop/Word/COM reference (0 Core warnings); ShareHost graph untouched; DI added to privileged Api only
- [x] no destructive action silently broadened — default collision policy is Fail (→ skipped); Overwrite/Rename only on explicit batch choice; no batch-level output cleanup exists
- [x] higher-level safety rules stronger than convenience — sequential await per file (no parallelism to "speed up" Word renders); cancellation never interrupts mid-COM, only between documents

## Deviations / spec conflicts

None. PDF-01 and FND-04 verified DONE in SPRINT-STATUS.md before editing.

## Risks discovered

- Real-Word risk inherited from PDF-01: no live-Word test (fake renderer only); recommend a manual multi-document Word-present smoke test before release, including a mid-batch cancel.
- `Progress<T>`-style async observers may observe reports out of order under threadpool dispatch; the service reports synchronously in order, but UI consumers should treat `CompletedCount` as monotonic-hint and render max-seen.

## Next eligible sprint(s)

- PDF-03 (Streaming Ordered PDF Merge) — now unblocked (depends PDF-02).
- PDF-04 remains BLOCKED on CAN-08 + PDF-03.

## Note for PDF-03 (merge) consumer

Reuse `BatchPdfExportService` (or `PdfExportService` per document) for the
render-each-to-temp phase with `CollisionBehavior: Overwrite` scoped to a fresh
per-job temp dir `%LOCALAPPDATA%\DocumentIDE\temp\pdf-jobs\<job-id>\`
(spec 09 §10), then merge in `BatchPdfExportResult.Files` order. Do not
reuse the batch `Summary` for merge output; merge needs its own ordered-result
contract. Temp intermediates (not user outputs) are the merge job's cleanup
responsibility — this sprint deletes nothing.
