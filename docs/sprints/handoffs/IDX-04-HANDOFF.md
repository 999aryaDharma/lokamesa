# Sprint Handoff — IDX-04

**Status:** DONE  
**Branch:** `feat/idx-04-metadata-persistence`  
**Commits:** `5ec2a91` (`feat(indexing): metadata persistence (IDX-04)`)

## What changed
- Created database migration `Migration_005_MetadataPersistence` (`005_MetadataPersistence.cs`) defining `document_metadata` and `markdown_metadata` tables with foreign keys referencing `documents(id)` (`ON DELETE CASCADE`).
- Registered `Migration_005_MetadataPersistence` in `MigrationRunner.cs`.
- Added persistent domain entities and interfaces in `src/DocumentIDE.Core/Domain/`:
  - `DocumentMetadata.cs`: page count, section count, table count, image count, paper summary, orientation summary, margin summary, primary font, primary font size, header present, footer present, headings JSON, extracted timestamp, parser version.
  - `MarkdownDocumentMetadata.cs`: frontmatter JSON, headings JSON, outbound links JSON, wikilinks JSON, code block count, updated timestamp, parser version.
  - `DocumentMetadataDto.cs`: metadata DTO for inspector/search queries without source binary data.
  - `MetadataNormalizer.cs`: deterministic normalization for paper, margin, and font summaries.
  - `IDocumentMetadataRepository.cs`: repository interface in `DocumentIDE.Core.Domain.Repositories`.
- Implemented `DocumentMetadataRepository` in `src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentMetadataRepository.cs`:
  - CRUD operations and state tracking (`ready`, `stale`, `error`) for derived document and markdown metadata.
  - Automatic detection of parser version mismatches marking index status as `stale` and `IsRebuildable = true`.
  - Proper mapping of SQL `NULL` values ensuring null means not applicable rather than fabricated defaults.
- Added comprehensive TDD integration tests in `tests/DocumentIDE.IntegrationTests/Persistence/DocumentMetadataRepositoryTests.cs`.

## Files changed
- `src/DocumentIDE.Core/Domain/DocumentMetadata.cs`
- `src/DocumentIDE.Core/Domain/MarkdownDocumentMetadata.cs`
- `src/DocumentIDE.Core/Domain/DocumentMetadataDto.cs`
- `src/DocumentIDE.Core/Domain/MetadataNormalizer.cs`
- `src/DocumentIDE.Core/Domain/Repositories/IDocumentMetadataRepository.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/005_MetadataPersistence.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/DocumentMetadataRepository.cs`
- `tests/DocumentIDE.IntegrationTests/Persistence/DocumentMetadataRepositoryTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/IDX-04-HANDOFF.md`

## Verification
- `C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe test DocumentIDE.sln` → PASS (419 passed, 0 failed)
- Integration test suite `DocumentMetadataRepositoryTests` → PASS (5 passed, 0 failed)

## Security & File-Safety Gate Confirmation
- [x] No user file was mutated through an unreviewed/bypass path (N/A - metadata persistence only writes to application SQLite database `app.db`).
- [x] No arbitrary path or secret is exposed.
- [x] Dependency/security boundaries remain intact.
- [x] No destructive action silently broadened its scope (Deleting metadata only clears derived database tables).
- [x] Relevant higher-level safety rules remain stronger than convenience.

## Acceptance criteria
- [x] Derived metadata can be deleted/rebuilt (`DeleteMetadataAsync` resets metadata and sets status to `stale`; re-saving rebuilds metadata).
- [x] Null means not applicable, not fabricated defaults (SQL `NULL` preserved for inapplicable fields).
- [x] Metadata survives restart (Tested with app restart simulation against database file).
- [x] Parser version mismatch marks derived data rebuildable (`GetMetadataDtoAsync` flags `IsRebuildable = true` and updates index status to `stale` on version mismatch).
- [x] Mixed margin summary is deterministic (`MetadataNormalizer` sorts and formats distinct margin entries deterministically).

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `CAN-03` (Document Paper Object & Thumbnail Shell) — unblocked
- `DOCX-04` (Document Lens Metadata) — unblocked
- `IDX-05` (FTS5 Search Index) — unblocked
