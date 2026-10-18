# Sprint Handoff — IDX-05

**Status:** DONE  
**Branch:** `feat/idx-05-fts5-search-index`  
**Commits:** `feat(indexing): fts5 search index`

## What changed
- Created SQLite FTS5 migration `008_FtsIndex` to manage the `search_content` virtual table with unindexed `document_id` and indexed fields: `filename`, `body`, `headings`, `tables`, `headers`, `footers`, `frontmatter`.
- Implemented `IFtsIndex` and `FtsIndex` supporting single document indexing, batch indexing, document deletion, clearing, rebuilding, FTS5 term matching with snippet generation, match source detection, and index diagnostics.
- Registered `IFtsIndex` in API dependency injection.
- Added comprehensive integration test suite `FtsIndexTests.cs` verifying exact token lookups, source file preservation on delete/rebuild, responsive concurrent search & update operations, graceful malformed input handling, and index diagnostics.

## Files changed
- `src/DocumentIDE.Core/Search/FtsModels.cs`
- `src/DocumentIDE.Core/Search/IFtsIndex.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/008_FtsIndex.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `src/DocumentIDE.Infrastructure/Search/FtsIndex.cs`
- `src/DocumentIDE.Api/Program.cs`
- `tests/DocumentIDE.IntegrationTests/Search/FtsIndexTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/IDX-05-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter FtsIndexTests` → PASS (5/5 tests passed)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test` → PASS (All affected test suites passed)

## Acceptance criteria
- [x] No external search server — local SQLite FTS5 engine used.
- [x] Index is rebuildable from source — `RebuildIndexAsync` clears FTS table and re-indexes without modifying physical source files on disk.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path
- [x] no arbitrary path or secret is exposed
- [x] dependency/security boundaries remain intact
- [x] no destructive action silently broadened its scope
- [x] relevant higher-level safety rules remain stronger than convenience

## Deviations / spec conflicts
None. Note on migration naming: migration 008 was created (`008_FtsIndex.cs`) to follow the existing migration sequence (`001` through `007`).

## Risks discovered
None.

## Next eligible sprint(s)
- `IDX-06` — Incremental Index Scheduler
- `SRCH-01` — Structured Query Parser
