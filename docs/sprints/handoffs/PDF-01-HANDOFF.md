# Sprint Handoff — PDF-01

**Status:** DONE
**Branch:** `feat/pdf-01-single-docx-pdf-export`
**Commits:** `43a1d3d` (+ this handoff-hash amendment)

## What changed

Single DOCX → PDF export through the WORD-03 serialized queue, without
mutating the source. New `DocumentIDE.Core.Pdf` orchestration
(`PdfExportService`) plus queue/renderer seams; Infrastructure provides a
thin `WordAutomationQueueAdapter` over WORD-03 and a late-bound COM
`WordPdfRenderer` (ExportAsFixedFormat). No COM threading reimplemented.
DI wired in privileged Api only. No Preview/QuickLook, Canvas, or Share
code touched. No PDF-02/03 work.

File mapping note (planned names reused 1:1, no renames):
- planned `src/DocumentIDE.Core/Pdf/PdfExportService.cs` → created as planned
- planned `tests/DocumentIDE.IntegrationTests/Pdf/SinglePdfExportTests.cs` → created as planned
- supporting (not in plan, required by acceptance): `PdfCollisionBehavior.cs`,
  `PdfExportStatus.cs`, `PdfExportModels.cs`, `IWordAutomationQueue.cs`,
  `IWordPdfRenderer.cs` (Core), `WordAutomationQueueAdapter.cs`,
  `WordPdfRenderer.cs` (Infrastructure), DI lines in `Api/Program.cs`

## Files changed

- `src/DocumentIDE.Core/Pdf/PdfExportService.cs` (new)
- `src/DocumentIDE.Core/Pdf/PdfExportModels.cs` (new: `PdfExportRequest`/`PdfExportResult`)
- `src/DocumentIDE.Core/Pdf/PdfExportStatus.cs` (new: Succeeded/OutputCollision/SourceNotFound/SourceInvalid/ExportFailed/DiskFull/Cancelled)
- `src/DocumentIDE.Core/Pdf/PdfCollisionBehavior.cs` (new: Fail/Overwrite/Rename)
- `src/DocumentIDE.Core/Pdf/IWordAutomationQueue.cs` (new: WORD-03 queue abstraction)
- `src/DocumentIDE.Core/Pdf/IWordPdfRenderer.cs` (new: ExportAsFixedFormat seam)
- `src/DocumentIDE.Infrastructure/Word/WordAutomationQueueAdapter.cs` (new: delegates to WORD-03 queue)
- `src/DocumentIDE.Infrastructure/Word/WordPdfRenderer.cs` (new: late-bound Word COM, STA-queue-only)
- `src/DocumentIDE.Api/Program.cs` (+4 lines DI)
- `tests/DocumentIDE.IntegrationTests/Pdf/SinglePdfExportTests.cs` (new: 12 tests)
- `docs/sprints/SPRINT-STATUS.md` (PDF-01 row READY → DONE)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter "FullyQualifiedName~SinglePdfExportTests"` → PASS (12/12, 0 failed)
- `dotnet test tests/DocumentIDE.Core.Tests/...` → PASS (459/459, 0 failed)
- `dotnet build src/DocumentIDE.Core` → 0 warnings, 0 errors
- `dotnet build src/DocumentIDE.Infrastructure` → 0 errors, no new warnings
- TDD Red proven: pre-implementation build failed with 4 errors
  (`DocumentIDE.Core.Pdf` / `PdfExportService` / `IWordAutomationQueue` /
  `IWordPdfRenderer` not found)

## Acceptance criteria

- [x] Word COM only via queue — `PdfExportService` holds no COM/Interop
  reference; the renderer seam is invoked strictly inside
  `IWordAutomationQueue.EnqueueAsync` (exactly 1 enqueue per export).
  Evidence: `WordCom_GoesOnlyThroughQueue` (renderer asserts it runs
  inside queue dispatch) + `ExistingPdf_IsNotSilentlyOverwritten`
  (collision path uses 0 enqueues, 0 renderer calls).
- [x] Generated PDF exists — `GeneratedPdfExists`.
- [x] DOCX fingerprint unchanged — `DocxFingerprintUnchanged` (SHA-256
  before/after + result fields).
- [x] Existing PDF never silently overwritten — default `Fail` returns
  typed `OutputCollision`, bytes untouched; `Overwrite`/`Rename` require
  explicit opt-in (separate tests).
- [x] Typed failures — `ExportFailed` (incl. partial-output cleanup),
  `DiskFull` (HRESULT 0x80070070), `SourceNotFound`, `SourceInvalid`,
  `Cancelled` paths in code; first four covered by tests.

## Security & file-safety gate

- [x] no user file mutated through unreviewed path — DOCX only read
  (fingerprint); outputs written only to explicitly resolved target
- [x] no arbitrary path or secret exposed — paths stay local, error
  messages contain only paths/status, no secrets
- [x] dependency/security boundaries intact — Core has no
  Interop/Word/COM reference; ShareHost graph untouched (0 warnings in Core)
- [x] no destructive action silently broadened — Overwrite only on
  explicit request; failures clean only artifacts this call created
- [x] higher-level safety rules stronger than convenience — default
  collision policy is Fail, not Overwrite

## Deviations / spec conflicts

None. WORD-03 verified DONE in SPRINT-STATUS.md before editing.

## Risks discovered

- `WordPdfRenderer` (real Word) has no live-Word test: CI/test machines
  without Word cannot exercise real `ExportAsFixedFormat`; covered by
  fake-renderer integration tests. Recommend a manual Word-present
  smoke test before release.
- `Rename` suffix scan caps at 9999 siblings (throws typed ExportFailed
  beyond that); fine for P0, PDF-02 batch should reuse the same helper.

## Next eligible sprint(s)

- PDF-02 (Batch Separate PDF Export) — now unblocked (depends PDF-01 + FND-04, both DONE).
- SHR-07 (Share Download Permissions) — depends SHR-04 (still BLOCKED) + PDF-01 (now DONE).

## Note for PDF-02 (batch) consumer

Reuse `PdfExportService.ExportSingleAsync` per document with
`CollisionBehavior` mapped from the batch collision choice (incl. "apply
to remaining"), aggregate the typed `PdfExportResult`s into the batch
summary (`12 PDFs generated / 1 skipped / 1 failed`, spec 09 §12).
Each call enqueues separately on the shared WORD-03 queue (concurrency
stays 1); cancellation between documents maps to `PdfExportStatus.Cancelled`.
Temp-merge artifacts for PDF-03 belong under
`%LOCALAPPDATA%\DocumentIDE\temp\pdf-jobs\<job-id>\` (spec 09 §10) —
not created in this sprint.
