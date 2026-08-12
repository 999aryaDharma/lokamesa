# Sprint Handoff — SRCH-02

**Status:** DONE  
**Branch:** `feat/srch-02-exact-ranked-search`  
**Commits:** N/A (uncommitted / ready for parent commit)

## What changed
- Created `DocumentSearchService` in `src/DocumentIDE.Core/Search/DocumentSearchService.cs` along with domain models `SearchResult`, `DocumentSearchOptions`, `SearchExecutionResult`, and `IDocumentSearchService`:
  - Implemented real-time workspace, folder, and document-scoped exact lexical search using `IFtsIndex` and `IDocumentRepository`.
  - Implemented deterministic tier-based ranking priority: exact filename match > filename partial match > heading > body > table > header/footer > frontmatter.
  - Implemented origin-aware match-source labels and snippet extractions.
  - Implemented backend scope enforcement filtering results strictly by workspace, folder path, type/extension, or allowed document ID sets.
  - Added warm-query execution time measurement (`ExecutionTimeMs`).
- Created integration tests in `tests/DocumentIDE.IntegrationTests/Search/ExactSearchTests.cs` verifying required test cases following strict Red -> Green -> Refactor TDD workflow.
- Created unit tests in `tests/DocumentIDE.Core.Tests/Search/DocumentSearchServiceTests.cs` using custom fake repositories and indexes.

## Files changed
- `src/DocumentIDE.Core/Search/DocumentSearchService.cs`
- `tests/DocumentIDE.Core.Tests/Search/DocumentSearchServiceTests.cs`
- `tests/DocumentIDE.IntegrationTests/Search/ExactSearchTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/SRCH-02-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter "FullyQualifiedName~Search"` → PASS (32/32 search tests passed: 23 core, 9 integration)

## Acceptance criteria
- [x] No semantic matching in exact mode (`SearchMode.Exact` uses pure FTS5 and deterministic ranking).
- [x] No reparse on keystroke (search operates directly on SQLite FTS index without disk reparsing).
- [x] Filename exact outranks body match (verified by `FilenameExact_OutranksBodyMatch`).
- [x] Table/header source is identified (verified by `TableAndHeaderSource_IsIdentified`).
- [x] Search only returns documents in requested scope (verified by `Search_OnlyReturnsDocumentsInRequestedScope`).

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path: N/A - read-only search querying.
- [x] no arbitrary path or secret is exposed: verified - paths and document metadata remain contained within workspace scope.
- [x] dependency/security boundaries remain intact: intact.
- [x] no destructive action silently broadened its scope: N/A - search is non-destructive.
- [x] relevant higher-level safety rules remain stronger than convenience: intact.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `SRCH-03` — Metadata Filters
- `SRCH-04` — Fuzzy Exploratory Search
