# Sprint Handoff — AI-03

**Status:** DONE  
**Branch:** `feat/ai-03-semantic-search-mode`  
**Commits:** `feat(semantic): semantic search mode`

## What changed
- Created `SemanticSearchService.cs` in `DocumentIDE.Core/Search` implementing `ISemanticSearchService` for query embedding generation, nearest-neighbor vector similarity lookup (cosine similarity), metadata filtering, and stale/unavailable embedding handling.
- Integrated `ISemanticSearchService` into `DocumentSearchService.cs` to delegate `SearchMode.Smart` queries to semantic search while preserving deterministic exact search.
- Created `SearchModeSwitch.tsx` in `DocumentIDE.Web/src/features/search` rendering explicit mode toggle tabs ("Exact", "Fuzzy", "Smart") in the search panel.
- Integrated `SearchModeSwitch` into `SearchPanel.tsx` allowing users to switch search mode seamlessly.
- Registered `ISemanticSearchService` and `IDocumentSearchService` in `DocumentIDE.Api/Program.cs`.
- Added comprehensive unit tests in `SemanticSearchTests.cs` and `SearchModeSwitch.test.tsx` verifying:
  1. Semantic mode finds conceptually related fixtures.
  2. Exact mode results remain deterministic and unchanged.
  3. SearchReplace does not depend on semantic search service.
  4. Model/provider unavailability produces typed diagnostic outcomes.
  5. Stale embeddings (fingerprint mismatch) are gracefully skipped.
  6. Metadata filters are preserved in semantic mode.

## Files changed
- `src/DocumentIDE.Core/Search/SemanticSearchService.cs`
- `src/DocumentIDE.Core/Search/DocumentSearchService.cs`
- `src/DocumentIDE.Api/Program.cs`
- `src/DocumentIDE.Web/src/features/search/SearchModeSwitch.tsx`
- `src/DocumentIDE.Web/src/features/search/SearchModeSwitch.test.tsx`
- `src/DocumentIDE.Web/src/features/search/SearchPanel.tsx`
- `tests/DocumentIDE.Core.Tests/Search/SemanticSearchTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/AI-03-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test` → PASS (7 architecture, 407 core, 158 integration tests)
- `npm test -- --run` in `src/DocumentIDE.Web` → PASS (16 test files, 59 tests)

## Acceptance criteria
- [x] No invisible semantic contamination of destructive actions.
  - Evidence: `SearchReplace` operations do not invoke `ISemanticSearchService`. `DocumentSearchService` maintains strict FTS5 exact matching in `SearchMode.Exact`.

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
- `BAT-03` — Batch Preview API & Review Shell
