# Sprint Handoff — MD-04 — Wikilink Resolution & Backlink Inputs

**Status:** DONE  
**Branch:** `feat/md-04-wikilink-resolution-backlink-inputs`  
**Commits:** `feat(markdown): wikilink resolution & backlink inputs` (`b6ace97`)

## What changed
- Created `WikilinkResolver` in `src/DocumentIDE.Core/Markdown/WikilinkResolver.cs` implementing deterministic wikilink resolution:
  1. Exact relative-path resolution evaluated first against target paths.
  2. Unique filename-stem matching fallback.
  3. Disambiguation listing returning all candidates when multiple workspace files match a stem without picking any arbitrarily.
  4. Alias syntax support (`[[target|Alias]]`, `[[target#anchor|Alias]]`) preserving target identity.
  5. Diagnostic reporting for unresolved and ambiguous links.
  6. Extraction of deterministic `BacklinkInput` relations for resolved wikilinks.
- Refactored `MarkdownLinkIndexer.ResolveLinkTarget` to delegate wikilink resolution to `WikilinkResolver`.
- Added unit test suite in `tests/DocumentIDE.Core.Tests/Markdown/WikilinkResolverTests.cs` verifying all required behaviors and step requirements.

## Files changed
- `src/DocumentIDE.Core/Markdown/WikilinkResolver.cs`
- `src/DocumentIDE.Core/Markdown/MarkdownLinkIndexer.cs`
- `tests/DocumentIDE.Core.Tests/Markdown/WikilinkResolverTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/MD-04-HANDOFF.md`

## Verification
- `& "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe" test`
  - Result: **PASS** (300 Core tests, 134 Integration tests, 7 Architecture tests)
- `WikilinkResolverTests`:
  - `UniqueStemResolves` → PASS
  - `TwoSameStemDocsAreAmbiguous` → PASS
  - `AliasPreservesTargetIdentity` → PASS
  - `RelativeExactPathResolvesFirst` → PASS
  - `UnresolvedLinkExposesDiagnostic` → PASS
  - `ResolveMarkdownLinkInfo_ResolvesCorrectly` → PASS
  - `ResolveDocumentWikilinks_ExtractsResolutionsAndDiagnostics` → PASS
  - `ExtractBacklinkInputs_ReturnsResolvedInboundRelations` → PASS

## Acceptance criteria
- [x] No silent arbitrary candidate choice — When multiple files share a filename stem, `WikilinkResolver` marks the result ambiguous (`IsResolved = false`, `IsAmbiguous = true`), leaves `ResolvedPath = null`, returns all candidate paths, and emits an `Ambiguous` diagnostic.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path — Resolution is purely functional and read-only.
- [x] no arbitrary path or secret is exposed — Relative links are normalized within workspace paths using safe path normalization.
- [x] dependency/security boundaries remain intact — Core layer remains self-contained with no external infrastructure leak.
- [x] no destructive action silently broadened its scope — N/A: Resolution performs no filesystem mutations.
- [x] relevant higher-level safety rules remain stronger than convenience — Strict disambiguation enforced rather than guessing.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `GR-01` — Relation Store & Extraction (Ready)
