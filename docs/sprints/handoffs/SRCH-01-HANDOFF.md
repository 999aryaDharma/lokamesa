# Sprint Handoff — SRCH-01

**Status:** DONE  
**Branch:** `feat/srch-01-structured-query-parser`  
**Commits:** N/A (uncommitted)

## What changed
- Created `SearchQuery` domain record, `QueryFilter`, `QueryDiagnostic`, `SearchMode`, `SearchScope`, and `DiagnosticSeverity` enums in `src/DocumentIDE.Core/Search/SearchQuery.cs`.
- Implemented structured query parser in `src/DocumentIDE.Core/Search/SearchQueryParser.cs` satisfying all requirements:
  - Tokenizes raw input into positive terms, exact quoted phrases (`"..."`), excluded terms (`-term`, `-"phrase"`), field filters (`key:value`, `key:"value"`), mode override (`mode:fuzzy`, `mode:smart`, etc.), and scope override (`scope:folder`, etc.).
  - Normalizes initial field names (`folder`, `type`, `font`, `paper`, `margin`, `modified`, `name`, `heading`, `font-size`) and aliases (`path`/`dir` -> `folder`, `ext`/`extension`/`filetype` -> `type`, `date`/`updated` -> `modified`, `filename`/`title` -> `name`, `header`/`h1`/`h2`/`h3` -> `heading`, `fontsize`/`size` -> `font-size`).
  - Normalizes filter value aliases (`markdown` -> `md`, `doc` -> `docx`).
  - Emits controlled `QueryDiagnostic` instances for unknown filter fields (Warning), malformed filters with empty values (Error), and unclosed quotes (Warning).
  - Provided `ToFts5Query` escaping helper to guarantee protection against SQL and FTS syntax injection.
- Added comprehensive unit tests in `tests/DocumentIDE.Core.Tests/Search/SearchQueryParserTests.cs` following Red -> Green -> Refactor TDD.

## Files changed
- `src/DocumentIDE.Core/Search/SearchQuery.cs`
- `src/DocumentIDE.Core/Search/SearchQueryParser.cs`
- `tests/DocumentIDE.Core.Tests/Search/SearchQueryParserTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/SRCH-01-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter "FullyQualifiedName~SearchQueryParserTests"` → PASS (9/9 passed)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test` → PASS (517/517 passed: 7 architecture, 362 core, 148 integration)

## Acceptance criteria
- [x] Parser output is independent from storage query language (pure C# domain AST models in `SearchQuery`).
- [x] Quoted phrase parses (`ExactPhrases`).
- [x] Negative term parses (`ExcludedTerms`).
- [x] Unknown filter returns controlled diagnostic (`QueryDiagnostic` with Warning).
- [x] Raw query cannot inject SQL (safely tokenized into typed AST and escaped for FTS5).

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path: N/A - pure parser implementation.
- [x] no arbitrary path or secret is exposed: N/A - pure query parsing domain module.
- [x] dependency/security boundaries remain intact: intact, zero new dependencies introduced.
- [x] no destructive action silently broadened its scope: N/A - no filesystem mutation in this sprint.
- [x] relevant higher-level safety rules remain stronger than convenience: intact.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `SRCH-02` — Exact Ranked Search
