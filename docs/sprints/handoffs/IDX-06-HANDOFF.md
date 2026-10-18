# Sprint Handoff — IDX-06

**Status:** DONE  
**Branch:** `feat/idx-06-incremental-index-scheduler`  
**Commits:** `feat(indexing): incremental index scheduler`

## What changed
- Created `IncrementalIndexCoordinator` in `src/DocumentIDE.Core/Indexing/IncrementalIndexCoordinator.cs` to schedule and coordinate incremental parse, metadata persistence, FTS search index updates, and cache invalidation based on filesystem changes.
- Added `IDocxStructureParser` interface (`src/DocumentIDE.Core/Indexing/IDocxStructureParser.cs`) and updated `DocxStructureParser` (`src/DocumentIDE.Infrastructure/Docx/DocxStructureParser.cs`) to implement structured parsing.
- Added `IndexStateChangedEventArgs` and `CacheInvalidationEventArgs` in `src/DocumentIDE.Core/Indexing/IndexStateEvents.cs`.
- Registered `IncrementalIndexCoordinator` and indexing dependencies in `src/DocumentIDE.Api/Program.cs`.
- Created comprehensive unit test suite in `tests/DocumentIDE.Core.Tests/Indexing/IncrementalIndexCoordinatorTests.cs` (8 tests) verifying TDD behaviors and acceptance criteria.
- Created end-to-end integration test suite in `tests/DocumentIDE.IntegrationTests/Indexing/IncrementalIndexCoordinatorIntegrationTests.cs` testing full SQLite `AppDb` + `FtsIndex` + `DocumentMetadataRepository` + `MarkdownStructureParser` document lifecycle (Created -> Modified -> Deleted).

## Files changed
- `src/DocumentIDE.Core/Indexing/IDocxStructureParser.cs`
- `src/DocumentIDE.Core/Indexing/IndexStateEvents.cs`
- `src/DocumentIDE.Core/Indexing/IncrementalIndexCoordinator.cs`
- `src/DocumentIDE.Infrastructure/Docx/DocxStructureParser.cs`
- `src/DocumentIDE.Api/Program.cs`
- `tests/DocumentIDE.Core.Tests/Indexing/IncrementalIndexCoordinatorTests.cs`
- `tests/DocumentIDE.IntegrationTests/Indexing/IncrementalIndexCoordinatorIntegrationTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/IDX-06-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter IncrementalIndexCoordinatorTests` → PASS (8/8 tests passed)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter IncrementalIndexCoordinatorIntegrationTests` → PASS (1/1 tests passed)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test` → PASS (All 518 tests passed across 3 test assemblies: 362 Core, 149 Integration, 7 Architecture)

## Acceptance criteria
- [x] No DB writer transaction wraps expensive parsing — `File.ReadAllTextAsync` and structural parsing (`IMarkdownStructureParser`/`IDocxStructureParser`) are executed prior to DB repository write operations.
- [x] Index failure does not break other search results — Exceptions during parsing individual documents publish an `error` state and do not interrupt other documents or break FTS search.
- [x] One modified file reindexes only itself — verified by `OneModifiedFile_ReindexesOnlyItself` unit test.
- [x] Repeated event for same fingerprint does not duplicate work — verified by `RepeatedEvent_SameFingerprint_DoesNotDuplicateWork` unit test.
- [x] Deleted file is removed from active index but retained as missing metadata — verified by `DeletedFile_IsRemovedFromActiveIndex_ButRetainedAsMissingMetadata` unit test and `EndToEnd_MarkdownFile_CreatedModifiedDeleted_Lifecycle` integration test.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path — N/A (read-only indexing & metadata persistence).
- [x] no arbitrary path or secret is exposed.
- [x] dependency/security boundaries remain intact.
- [x] no destructive action silently broadened its scope.
- [x] relevant higher-level safety rules remain stronger than convenience.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `AI-01` — Embedding Provider Abstraction (unblocked)
- `QA-02` — Filesystem & Indexing Soak (unblocked)
- `SRCH-02` — Exact Ranked Search (unblocked)
