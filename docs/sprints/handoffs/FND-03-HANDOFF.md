# Sprint Handoff — FND-03

**Status:** REVIEW  
**Branch:** `feat/fnd-03-sqlite-persistence-foundation`  
**Commits:** `feat(foundation): sqlite persistence foundation` (pending)

## What changed
- Created domain entities in `DocumentIDE.Core/Domain/`: `Workspace`, `FolderSource`, `Document`, `WorkspaceDocument`.
- Created repository interfaces in `DocumentIDE.Core/Domain/Repositories/`: `IWorkspaceRepository`, `IDocumentRepository`.
- Implemented `AppDb` in `DocumentIDE.Infrastructure/Persistence/` with SQLite connection factory and mandatory PRAGMA setup:
  - `PRAGMA journal_mode = WAL;`
  - `PRAGMA foreign_keys = ON;`
  - `PRAGMA busy_timeout = 5000;`
  - `PRAGMA synchronous = NORMAL;`
- Implemented migration infrastructure (`IMigration`, `MigrationRunner`) and initial migration `001_Initial.cs` creating tables for `workspaces`, `folder_sources`, `documents`, `workspace_documents`, and `schema_migrations` with appropriate foreign key constraints and indexes.
- Implemented `WorkspaceRepository` and `DocumentRepository` in `DocumentIDE.Infrastructure/Persistence/Repositories/`.
- Created integration test suite `SqliteConcurrencyTests.cs` verifying initial migrations, foreign key constraints, and concurrent reader/writer WAL behavior.

## Files changed
- `src/DocumentIDE.Core/Domain/Workspace.cs`
- `src/DocumentIDE.Core/Domain/FolderSource.cs`
- `src/DocumentIDE.Core/Domain/Document.cs`
- `src/DocumentIDE.Core/Domain/WorkspaceDocument.cs`
- `src/DocumentIDE.Core/Domain/Repositories/IWorkspaceRepository.cs`
- `src/DocumentIDE.Core/Domain/Repositories/IDocumentRepository.cs`
- `src/DocumentIDE.Infrastructure/DocumentIDE.Infrastructure.csproj`
- `src/DocumentIDE.Infrastructure/Persistence/AppDb.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/IMigration.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/001_Initial.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/WorkspaceRepository.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentRepository.cs`
- `tests/DocumentIDE.IntegrationTests/Persistence/SqliteConcurrencyTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/FND-03-HANDOFF.md`

## Verification
- `dotnet test` → PASS (9 tests passed across solution: 1 core unit test + 5 security integration tests + 3 SQLite concurrency/migration tests)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed)

## Acceptance criteria
- [x] Database lives under application-controlled local state → Default `%LOCALAPPDATA%\DocumentIDE\app.db` path in `AppDb`
- [x] No document contents become source-of-truth database blobs → Only metadata stored; physical files remain authoritative
- [x] WAL is verified at runtime → Verified via `PRAGMA journal_mode;` assertion returning `wal` in `SqliteConcurrencyTests.cs`

## Security & File-Safety Gate
- no user file was mutated through an unreviewed/bypass path: N/A (Metadata persistence foundation only, no filesystem mutations)
- no arbitrary path or secret is exposed: Verified (Database restricted to application local app data)
- dependency/security boundaries remain intact: Verified (Foreign keys enabled, transactions kept short)
- no destructive action silently broadened its scope: N/A (No file mutations)
- relevant higher-level safety rules remain stronger than convenience: Verified (Local filesystem remains authoritative source of truth)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- FND-04 (Background Job Runtime)
- FND-05 (Settings & Secret Storage)
- FND-06 (Structured Logging & Diagnostics)
- SHR-01 (Share Domain & Sanitized Manifest)
