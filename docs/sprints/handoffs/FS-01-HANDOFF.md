# Sprint Handoff — FS-01: Folder Source Attachment

**Status:** REVIEW  
**Branch:** `feat/fs-01-folder-source-attachment`  
**Commit:** `feat(filesystem): folder source attachment` (pending)  
**Implementation Agent:** Codex  
**Reviewer Agent:** Antigravity  

---

## What Changed

### Core & Infrastructure
- Defined `FolderNotFoundException` typed exception for missing/inaccessible directory paths.
- Defined `AttachFolderResult` DTO for returning attached domain entity and supported document count.
- Extended `FolderSource` domain entity with `DisplayName`, `AddedAt`, and `LastReconciledAt` fields.
- Implemented `IFolderSourceRepository` and `FolderSourceRepository` for SQLite persistence.
- Implemented `IFolderSourceService` and `FolderSourceService` for path canonicalization, folder validation, recursive file enumeration (`.docx`, `.md`) while skipping hidden directories (`.git`, `$RECYCLE.BIN`, etc.), and non-mutating folder attachment.
- Created migration `002_FolderSourceFields` adding `display_name` and `last_reconciled_at` columns to SQLite `folder_sources` table.

### API Layer
- Added `FolderSourcesController` in `DocumentIDE.Api` exposing:
  - `POST /api/workspaces/{workspaceId}/folder-sources`: Attaches a folder path and returns `{ folderSource, supportedFileCount, displayName }`. Returns `400 Bad Request` with typed error if folder is missing/inaccessible.
  - `GET /api/workspaces/{workspaceId}/folder-sources`: Lists attached folder sources for a workspace.

### Frontend
- Created `AddFolderSource.tsx` component in `src/DocumentIDE.Web/src/features/workspace/` allowing users to enter a local folder path, submit attachment request, handle loading/error states, and view cards of attached folder sources showing canonical path, file count, and availability badge.

### Tests
- Created `FolderSourceTests.cs` integration test suite (5 test cases) covering attachment, path validation, reopening workspace restoration, source non-mutation, and hidden directory exclusion.
- Created `AddFolderSource.test.tsx` Vitest component tests (4 test cases) covering UI rendering, empty path validation, API call success, and error display.

---

## Files Changed

| File | Purpose |
|---|---|
| `src/DocumentIDE.Core/Filesystem/FolderNotFoundException.cs` | Typed exception for missing/inaccessible folder paths |
| `src/DocumentIDE.Core/Filesystem/AttachFolderResult.cs` | Attachment result DTO |
| `src/DocumentIDE.Core/Filesystem/IFolderSourceService.cs` | Core folder source service interface |
| `src/DocumentIDE.Core/Filesystem/FolderSourceService.cs` | Core folder source service implementation |
| `src/DocumentIDE.Core/Domain/FolderSource.cs` | Extended domain entity with DisplayName and tracking timestamps |
| `src/DocumentIDE.Core/Domain/Repositories/IFolderSourceRepository.cs` | Core folder source repository interface |
| `src/DocumentIDE.Infrastructure/Persistence/Repositories/FolderSourceRepository.cs` | SQLite repository implementation |
| `src/DocumentIDE.Infrastructure/Persistence/Migrations/002_FolderSourceFields.cs` | Schema migration for new folder source columns |
| `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs` | Registered Migration 002 |
| `src/DocumentIDE.Api/Controllers/FolderSourcesController.cs` | Authenticated REST controller for folder source attachment |
| `src/DocumentIDE.Api/Program.cs` | Registered services, repositories, and controllers in DI |
| `src/DocumentIDE.Web/src/features/workspace/AddFolderSource.tsx` | React UI component for folder attachment |
| `src/DocumentIDE.Web/src/features/workspace/AddFolderSource.test.tsx` | Vitest unit test suite for AddFolderSource UI |
| `tests/DocumentIDE.IntegrationTests/Filesystem/FolderSourceTests.cs` | Integration tests for FolderSource persistence & non-mutation |
| `docs/sprints/SPRINT-STATUS.md` | FS-01 → REVIEW |

---

## Verification

- `dotnet test` → PASS (22 passed: 5 Core.Tests + 17 IntegrationTests)
- `npm test` in `src/DocumentIDE.Web` → PASS (12 passed: 3 test files)
- `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` → PASS (ALL VERIFICATION STEPS PASSED SUCCESSFULLY!)

---

## Acceptance Criteria

- [x] **No `.documentide` metadata is written into source folder** — Verified by `Attaching_Folder_Does_Not_Mutate_Source_Directory` test. Zero files/lockfiles written.
- [x] **Attaching a folder does not mutate contents** — Verified by `Attaching_Folder_Does_Not_Mutate_Source_Directory` test. Directory entries before and after attachment are 100% byte-for-byte & path-for-path identical.
- [x] **Supported document enumeration** — Recursively counts `.docx` and `.md` files; ignores hidden folders like `.git`. Verified by `Hidden_Directories_Are_Excluded_From_File_Count` test.
- [x] **Typed error handling** — Nonexistent or inaccessible directory paths throw `FolderNotFoundException` and return `400 Bad Request`. Verified by `Reject_Nonexistent_Or_Inaccessible_Path_With_Typed_Error` test.

---

## Security & File-Safety Gate

- [x] **No user file was mutated through an unreviewed/bypass path** — Attachment operation is strictly read-only on the filesystem.
- [x] **No arbitrary path or secret is exposed** — Endpoints are guarded by `PrivilegedRequestGuard` and reject unauthorized cross-site requests.
- [x] **Dependency/security boundaries remain intact** — Follows clean architecture (Core interfaces, Infrastructure persistence, Api controllers).
- [x] **No destructive action silently broadened its scope** — No physical files are created, moved, or deleted.
- [x] **Relevant higher-level safety rules remain stronger than convenience** — Local filesystem remains 100% authoritative and untouched.

---

## Deviations / Spec Conflicts

None.

---

## Risks Discovered

- **`SQLitePCLRaw.lib.e_sqlite3` vulnerability warning (NU1903)**: Pre-existing since FND-03. Documented in FND-03/FND-07 handoffs for resolution before REL-01.

---

## Next Eligible Sprint(s)

Once FS-01 is marked `DONE`:
- `CAN-02` — Folder Canvas Object (now unblocked: FS-01 DONE, CAN-01 DONE)
- `FS-02` — Stable Document Identity (now unblocked: FND-03 DONE, FS-01 DONE)
- `BAT-02` — Batch Scope Resolver (partially unblocked)
- `FS-08` — Reparse Point & Source Containment (partially unblocked)
