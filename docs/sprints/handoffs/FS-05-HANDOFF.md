# Sprint Handoff — FS-05

**Status:** DONE  
**Branch:** `feat/fs-05-rename-move-correlation`  
**Commits:** `N/A (working branch)`

## What changed
- Implemented same-volume NTFS stable identity rename/move correlation (`MoveCorrelationService.CorrelateSameVolumeMoveAsync`) preserving `DocumentId` and virtual workspace metadata.
- Implemented short-lived operation journal (`PhysicalMoveIntent` dictionary in `MoveCorrelationService`) for app-originated physical file moves with automatic watcher event correlation.
- Integrated `FileChangeCoordinator` with `MoveCorrelationService` to swallow transient watcher `Deleted` events during active move intents and emit correlated `FileChangeType.Renamed` events.
- Developed multi-factor cross-volume candidate scoring algorithm (`MoveCorrelationScorer`) evaluating content fingerprint (SHA256), filename, size in bytes, and mtime timestamp.
- Enforced strict ambiguity refusal rules: multiple plausible candidates, duplicate content, candidate score < 75, or source file still existing on disk force `IsAmbiguous = true` and reject auto-binding unless explicit `forceRebind: true` is supplied.
- Built REST API endpoints (`GET /api/documents/{documentId}/candidates`, `GET /api/documents/{documentId}/rebind-candidates`, `POST /api/documents/{documentId}/rebind`, `POST /api/documents/{documentId}/move`) in `MoveCorrelationController.cs`.
- Implemented React user confirmation dialog `LocateMovedFileDialog.tsx` displaying candidate match scores, ambiguity warning banners, custom path selection, and trigger callbacks.

## Files changed
- `src/DocumentIDE.Core/Filesystem/IMoveCorrelationService.cs`
- `src/DocumentIDE.Core/Filesystem/MoveCorrelationModels.cs`
- `src/DocumentIDE.Core/Filesystem/MoveCorrelationScorer.cs`
- `src/DocumentIDE.Core/Filesystem/MoveCorrelationService.cs`
- `src/DocumentIDE.Core/Filesystem/FileChangeCoordinator.cs`
- `src/DocumentIDE.Api/Controllers/MoveCorrelationController.cs`
- `src/DocumentIDE.Web/src/features/files/LocateMovedFileDialog.tsx`
- `tests/DocumentIDE.Core.Tests/Filesystem/MoveCorrelationTests.cs`
- `tests/DocumentIDE.IntegrationTests/Filesystem/MoveCorrelationIntegrationTests.cs`
- `src/DocumentIDE.Web/src/features/files/LocateMovedFileDialog.test.tsx`
- `docs/sprints/SPRINT-STATUS.md`

## Verification
- `dotnet test --filter "FullyQualifiedName~MoveCorrelation"` → PASS (11/11 tests pass: 6 unit, 5 integration)
- `npm test` in `src/DocumentIDE.Web` → PASS (7/7 test files, 27/27 tests pass, including 6 LocateMovedFileDialog tests)

## Acceptance criteria
- [x] Same-volume move preserves DocumentId.
- [x] Two identical candidate copies require confirmation (`IsAmbiguous = true`).
- [x] App-originated move correlates watcher delete/create sequence via operation journal.
- [x] Identical content does not automatically mean same identity.
- [x] No silent merge of ambiguous records.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path
- [x] no arbitrary path or secret is exposed
- [x] dependency/security boundaries remain intact
- [x] no destructive action silently broadened its scope
- [x] relevant higher-level safety rules remain stronger than convenience

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `FS-06` — Missing File & Folder Recovery
- `FS-07` — Explicit Physical File Operations
