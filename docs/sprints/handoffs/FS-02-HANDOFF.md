# Sprint Handoff — FS-02: Stable Document Identity

**Status:** DONE  
**Branch:** `feat/fs-02-stable-document-identity`  
**Commit:** `5d473d9`  
**Implementation Agent:** Antigravity  
**Reviewer Agent:** User / Codex  

---

## What changed

### Core & Infrastructure
- Added `size_bytes` and `modified_at` columns to `documents` table via database schema migration `003_DocumentIdentityFields` and registered it in `MigrationRunner`.
- Updated `Document` domain model to include `SizeBytes` and `ModifiedAt` properties.
- Added `GetByVolumeAndObjectIdAsync` and `GetDocumentsByFingerprintAsync` methods to `IDocumentRepository` and implemented them in `DocumentRepository`.
- Created `IFileIdentityProvider` interface and its Windows implementation `WindowsFileIdentityProvider` to fetch stable NTFS volume serial number and 64-bit file index.
- Created `DocumentIdentityService` which implements `ResolveDocumentAsync` to resolve local files to stable IDs via NTFS metadata or fall back to fingerprint/size/mtime correlation for non-NTFS cases.
- Registered all new repositories, providers, and services in `Program.cs`.

### Tests
- Created `DocumentIdentityTests.cs` containing integration tests for NTFS renames, case-insensitive mapping, distinct ID assignment, and non-NTFS fallback fingerprint correlation.

---

## Files changed

| File | Purpose |
|---|---|
| [`src/DocumentIDE.Core/Domain/Document.cs`](file:///D:/document-ide/src/DocumentIDE.Core/Domain/Document.cs) | Added `SizeBytes` and `ModifiedAt` tracking properties. |
| [`src/DocumentIDE.Core/Domain/Repositories/IDocumentRepository.cs`](file:///D:/document-ide/src/DocumentIDE.Core/Domain/Repositories/IDocumentRepository.cs) | Added database query methods for lookup by NTFS volume/object ID and content fingerprint. |
| [`src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentRepository.cs`](file:///D:/document-ide/src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentRepository.cs) | Implemented SQLite mapping for new fields and new database queries. |
| [`src/DocumentIDE.Infrastructure/Persistence/Migrations/003_DocumentIdentityFields.cs`](file:///D:/document-ide/src/DocumentIDE.Infrastructure/Persistence/Migrations/003_DocumentIdentityFields.cs) | Schema migration adding `size_bytes` and `modified_at` columns. |
| [`src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`](file:///D:/document-ide/src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs) | Registered migration 003. |
| [`src/DocumentIDE.Core/Filesystem/IFileIdentityProvider.cs`](file:///D:/document-ide/src/DocumentIDE.Core/Filesystem/IFileIdentityProvider.cs) | Defined interface for querying unique file identifiers from OS. |
| [`src/DocumentIDE.Infrastructure/Filesystem/WindowsFileIdentityProvider.cs`](file:///D:/document-ide/src/DocumentIDE.Infrastructure/Filesystem/WindowsFileIdentityProvider.cs) | NTFS Win32 implementation utilizing `GetFileInformationByHandle`. |
| [`src/DocumentIDE.Core/Filesystem/DocumentIdentityService.cs`](file:///D:/document-ide/src/DocumentIDE.Core/Filesystem/DocumentIdentityService.cs) | Resolution and correlation logic for stable document identity. |
| [`src/DocumentIDE.Api/Program.cs`](file:///D:/document-ide/src/DocumentIDE.Api/Program.cs) | Registered the new services and repositories in DI. |
| [`tests/DocumentIDE.IntegrationTests/Filesystem/DocumentIdentityTests.cs`](file:///D:/document-ide/tests/DocumentIDE.IntegrationTests/Filesystem/DocumentIdentityTests.cs) | Integration tests validating identity behaviors. |

---

## Verification

- `dotnet test` → PASS (26 passed: 5 Core.Tests + 21 IntegrationTests)
- `npm test` (src/DocumentIDE.Web) → PASS (12 passed: 3 test files)
- `scripts/verify.ps1` → ALL VERIFICATION STEPS PASSED SUCCESSFULLY!

---

## Acceptance criteria

- **[x] Path is an attribute, never identity** — Verified by `Rename_Keeps_DocumentId` test where file rename updates path while preserving identical `DocumentId`.
- **[x] Ambiguous duplicate content is not auto-merged** — Verified by `Two_Distinct_Duplicate_Files_Receive_Distinct_DocumentIds` test where identical duplicate files receive distinct `DocumentIds`.
- **[x] Casing changes do not duplicate records** — Verified by `Path_Casing_Change_Does_Not_Duplicate_Record` test.
- **[x] Fallback non-NTFS correlation works** — Verified by `Fallback_NonNTFS_Rename_Correlates_Correctly` test where renames on non-NTFS are correlated safely using content fingerprint.

---

## Deviations / spec conflicts

None.

---

## Risks discovered

- **SQLitePCLRaw Nuget warning (NU1903):** Pre-existing vulnerability warning remains tracked for resolution in `REL-01`.

---

## Next eligible sprint(s)

Once FS-02 is marked DONE:
- `FS-03` — Runtime File Watcher (unblocked: FND-04 DONE, FS-02 DONE)
- `FS-07` — Explicit Physical File Operations (unblocked: FS-02 DONE, FND-02 DONE)
- `HIST-01` — Recovery Snapshot Storage (unblocked: FND-03 DONE, FS-02 DONE)
- `IDX-01` — DOCX Package Resource Preflight (unblocked: FND-04 DONE, FS-02 DONE)
- `IDX-03` — Markdown Structural Parser (unblocked: FND-04 DONE, FS-02 DONE)
- `WORD-01` — Open DOCX in Microsoft Word (unblocked: FND-07 DONE, FS-02 DONE)
