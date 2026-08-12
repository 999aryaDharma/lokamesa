# Sprint Handoff — MD-03 — Frontmatter & Explicit Link Index

**Status:** DONE  
**Branch:** `feat/md-03-frontmatter-explicit-link-index`  
**Commits:** `1398e2c` (`feat(markdown): frontmatter & explicit link index`)

## What changed
- Enhanced `MarkdownLinkIndexer` in `src/DocumentIDE.Core/Markdown/MarkdownLinkIndexer.cs`:
  - `GetFrontmatterIndex`: Flattens frontmatter structured data (including YAML array/list collections) into searchable key-value string representations.
  - `NormalizeRelativeLink`: Normalizes relative link targets against physical document source paths without modifying source files.
  - `GetExplicitRelationCandidates`: Emits explicit outbound link candidates (normal relative links, wikilinks, and frontmatter dependencies).
  - `ResolveLinkTarget`: Resolves relative link targets and wikilinks against workspace files, handling exact path matching, unique filename stem resolution, and returning typed ambiguity when multiple candidates match.
- Added comprehensive unit test suite in `tests/DocumentIDE.Core.Tests/Markdown/MarkdownLinkIndexerTests.cs`:
  - Searchable frontmatter key/value extraction with collection flattening.
  - Path normalization for relative targets against physical source path.
  - Unique relative link and wikilink resolution.
  - Ambiguous wikilink detection returning multiple candidates.
  - Candidate emission and verification that source Markdown text is never mutated.

## Files changed
- `src/DocumentIDE.Core/Markdown/MarkdownLinkIndexer.cs`
- `tests/DocumentIDE.Core.Tests/Markdown/MarkdownLinkIndexerTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/MD-03-HANDOFF.md`

## Verification
- `& "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe" test` → PASS (424 passed, 0 failed across all projects)
  - `DocumentIDE.ArchitectureTests`: 7 passed
  - `DocumentIDE.Core.Tests`: 283 passed (including 5 MarkdownLinkIndexerTests)
  - `DocumentIDE.IntegrationTests`: 134 passed

## Security & File-Safety Gate Confirmation
- [x] No user file was mutated through an unreviewed/bypass path (Indexing operations are pure read-only operations; source Markdown files remain untouched).
- [x] No arbitrary path or secret is exposed.
- [x] Dependency/security boundaries remain intact.
- [x] No destructive action silently broadened its scope.
- [x] Relevant higher-level safety rules remain stronger than convenience.

## Acceptance criteria
- [x] Derived relations never rewrite Markdown automatically (`GetExplicitRelationCandidates` and index building do not modify source string or physical file).
- [x] Frontmatter fields searchable (`GetFrontmatterIndex` flattens structured data into searchable strings).
- [x] Relative link target resolves when unique (`ResolveLinkTarget` resolves single relative or stem matches).
- [x] Save updates link candidates (`GetExplicitRelationCandidates` extracts all relation candidates on save/reindex).

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `MD-04` (Wikilink Resolution & Backlink Inputs) — unblocked
- `GR-01` (Relation Store & Extraction) — unblocked (with FND-03)
- `CTX-01` (Context Pack Core) — unblocked (with GR-02)
