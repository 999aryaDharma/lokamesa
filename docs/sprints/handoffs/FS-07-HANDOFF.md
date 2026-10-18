# Sprint Handoff — FS-07

**Status:** DONE  
**Branch:** `security/fs-07-explicit-physical-file-operations`  
**Commits:** `N/A (working branch)`

## What changed
- Implemented strict basename safety validator (`PathSafetyValidator.cs`) rejecting directory path separators (`/`, `\`), path traversal tokens (`.`, `..`), rooted paths (`C:\`, `/...`), Windows reserved device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9` with or without extensions), Alternate Data Streams (`:`), trailing dots/spaces, invalid filename characters, and control characters.
- Created core physical file operations service (`IPhysicalFileOperations`, `PhysicalFileOperations.cs`) supporting safe explicit rename, move, duplicate, create directory, create Markdown file, create DOCX template file, and Windows Recycle Bin delete.
- Implemented explicit destination collision protection failing closed with typed `PathCollisionException` to prevent silent overwriting.
- Integrated move intent journaling with `IMoveCorrelationService.RecordMoveIntent` before disk operations to correlate watcher events.
- Encapsulated Windows Recycle Bin shell deletion behind `IRecycleBin` interface using `WindowsRecycleBin` with Win32 `SHFileOperationW` P/Invoke (`FO_DELETE` + `FOF_ALLOWUNDO`).
- Created OpenXML package generator (`MinimalDocxBuilder.cs`) producing valid minimal `.docx` package bytes for template file creation.
- Enforced canvas drag isolation contract: canvas drag components update virtual position coordinates only and never invoke physical file move operations.
- Developed REST API controller (`PhysicalFileOperationsController.cs`) for physical file operations with proper HTTP status codes (200 OK, 400 Bad Request, 404 Not Found, 409 Conflict).
- Registered `IRecycleBin` and `IPhysicalFileOperations` in `Program.cs`.
- Added comprehensive unit test suite (`PhysicalFileOperationsTests.cs`) with 41 tests and integration test suite (`PhysicalFileOperationsIntegrationTests.cs`).

## Files changed
- `src/DocumentIDE.Core/Filesystem/PathSafetyValidator.cs`
- `src/DocumentIDE.Core/Filesystem/PhysicalFileOperationsModels.cs`
- `src/DocumentIDE.Core/Filesystem/IRecycleBin.cs`
- `src/DocumentIDE.Core/Filesystem/IPhysicalFileOperations.cs`
- `src/DocumentIDE.Core/Filesystem/MinimalDocxBuilder.cs`
- `src/DocumentIDE.Core/Filesystem/PhysicalFileOperations.cs`
- `src/DocumentIDE.Infrastructure/Filesystem/WindowsRecycleBin.cs`
- `src/DocumentIDE.Api/Controllers/PhysicalFileOperationsController.cs`
- `src/DocumentIDE.Api/Program.cs`
- `tests/DocumentIDE.Core.Tests/Filesystem/PhysicalFileOperationsTests.cs`
- `tests/DocumentIDE.IntegrationTests/Filesystem/PhysicalFileOperationsIntegrationTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/FS-07-HANDOFF.md`

## Verification
- `dotnet test --filter "FullyQualifiedName~PhysicalFileOperations"` → PASS (43/43 tests pass: 41 unit tests in Core.Tests, 2 integration tests in IntegrationTests)
- `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj` → PASS (202/202 tests pass)

## Acceptance criteria
- [x] Rename rejects separators, `..`, rooted path, reserved names, ADS syntax.
- [x] Delete enters Windows Recycle Bin via P/Invoke `SHFileOperationW`.
- [x] Canvas drag code path never calls `IPhysicalFileOperations`.
- [x] No arbitrary path write endpoint.
- [x] Destination collision is explicit and fails closed.

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
- `FS-08` — Reparse Point & Source Containment
