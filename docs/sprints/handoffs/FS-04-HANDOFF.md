# Sprint Handoff — FS-04 (Hybrid Startup Reconciliation)

**Status:** DONE  
**Branch:** `feat/fs-04-hybrid-startup-reconciliation`  

## What changed
- Implemented `ReconciliationModels.cs` for file difference types (`Unchanged`, `Modified`, `New`, `Renamed`, `Missing`, `Locked`, `PermissionDenied`), reconciliation progress, and workspace reconciliation result contracts.
- Implemented `IStartupReconciler` and `StartupReconciler.cs` providing fast, non-blocking hybrid workspace reconciliation:
  - Instant retrieval of cached DB document presentation.
  - Recursive disk scanning of attached `FolderSource` roots collecting lightweight metadata (`size`, `modifiedAt`, `NTFS identity`).
  - Difference classification bypassing SHA256 fingerprinting and re-parsing for unchanged files.
  - Safe exception handling for locked/inaccessible files and missing folder roots.
  - Safe symlink/reparse point boundary enforcement.
- Extended `IDocumentRepository` & `DocumentRepository.cs` with `GetByFolderSourcePathAsync` and `GetByWorkspaceAsync`.
- Extended `IFolderSourceRepository` & `FolderSourceRepository.cs` with `UpdateLastReconciledAtAsync`.
- Implemented `ReconciliationController.cs` for REST endpoints (`POST /api/workspaces/{id}/reconcile`, `GET /status`, `GET /documents`) registered in `Program.cs`.
- Created React UI component `ReconciliationStatus.tsx` rendering non-blocking status badge.
- Added comprehensive integration tests in `StartupReconciliationTests.cs` and React component tests in `ReconciliationStatus.test.tsx`.

## Files changed
- `src/DocumentIDE.Core/Filesystem/ReconciliationModels.cs` (Created)
- `src/DocumentIDE.Core/Filesystem/IStartupReconciler.cs` (Created)
- `src/DocumentIDE.Core/Filesystem/StartupReconciler.cs` (Created)
- `src/DocumentIDE.Core/Domain/Repositories/IDocumentRepository.cs` (Modified)
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentRepository.cs` (Modified)
- `src/DocumentIDE.Core/Domain/Repositories/IFolderSourceRepository.cs` (Modified)
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/FolderSourceRepository.cs` (Modified)
- `src/DocumentIDE.Api/Controllers/ReconciliationController.cs` (Created)
- `src/DocumentIDE.Api/Program.cs` (Modified)
- `src/DocumentIDE.Web/src/features/workspace/ReconciliationStatus.tsx` (Created)
- `src/DocumentIDE.Web/src/features/workspace/ReconciliationStatus.test.tsx` (Created)
- `tests/DocumentIDE.IntegrationTests/Filesystem/StartupReconciliationTests.cs` (Created)
- `tests/DocumentIDE.Core.Tests/Word/WordDesktopLauncherTests.cs` (Modified)
- `tests/DocumentIDE.IntegrationTests/Word/WordDesktopLauncherTests.cs` (Modified)
- `docs/sprints/SPRINT-STATUS.md` (Modified)

## Verification
- `dotnet build DocumentIDE.sln --nologo` → PASS
- `dotnet test --nologo` → PASS (17 Core.Tests passed, 15 IntegrationTests passed)
- `npm test --prefix src/DocumentIDE.Web` → PASS (4 test files passed, 15 tests passed)
- `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` → PASS (ALL VERIFICATION STEPS PASSED SUCCESSFULLY!)

## Acceptance criteria
- [x] Changes made while app was closed are detected.
- [x] Unchanged files are not reparsed (bypasses SHA256/deep parse).
- [x] Cached canvas appears before reconciliation completes.
- [x] Filesystem wins over stale cache.
- [x] Reconciliation never rewrites source files (100% byte-for-byte identical verification).

## Safety & Non-negotiable Rules
- **Source-of-truth**: Local physical filesystem remains authoritative over cache.
- **Physical file mutation**: Zero physical file mutations performed during reconciliation.
- **Path containment**: Junctions/symlinks escaping source root are skipped.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `FS-06` — Missing File & Folder Recovery (Unblocked)
- `FS-05` — Rename & Move Correlation (Ready)
