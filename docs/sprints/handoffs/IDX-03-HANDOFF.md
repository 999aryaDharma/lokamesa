# Sprint Handoff — IDX-03

**Status:** DONE  
**Branch:** `feat/idx-03-markdown-structural-parser`  
**Commits:** `feat(indexing): markdown structural parser implementation and tests`

## What changed
- Created `IMarkdownStructureParser` interface and concrete `MarkdownStructureParser` in `src/DocumentIDE.Core/Markdown/`.
- Created structured data models in `src/DocumentIDE.Core/Markdown/MarkdownModels.cs`: `MarkdownParseResult`, `MarkdownFrontmatterResult`, `MarkdownHeadingInfo`, `MarkdownLinkInfo`, `MarkdownOutlineNode`, `MarkdownMetadata`, `MarkdownLinkType`.
- Implemented YAML Frontmatter parsing: extracts structured key-value metadata, tags, and explicit `depends_on` dependencies at line 1 (`---`). Handles malformed YAML frontmatter gracefully without crashing or interrupting body parsing.
- Implemented ATX (`# H1` .. `###### H6`) and Setext (`===` / `---`) heading extraction with 1-based line numbers, character offsets (`StartOffset`, `EndOffset`), slug anchors, and hierarchical outline tree generation (`MarkdownOutlineNode`).
- Implemented link and wikilink extraction: standard inline links `[text](target#anchor)` and wikilinks `[[target#anchor|Alias]]`. Ambiguous wikilink resolution is explicitly deferred to downstream relation services.
- Implemented code block isolation: fenced code blocks (` ``` ` / `~~~`) are isolated so headings/links inside code blocks are ignored and `CodeBlockCount` is tracked.
- Implemented `SearchableBody` clean text generation (stripping frontmatter block) while preserving exact unaltered `RawSource`.
- Attached `ParserVersion = "1.0.0"` to parse results.
- Added comprehensive unit tests in `tests/DocumentIDE.Core.Tests/Markdown/MarkdownStructureParserTests.cs`.

## Files changed
- `src/DocumentIDE.Core/Markdown/IMarkdownStructureParser.cs`
- `src/DocumentIDE.Core/Markdown/MarkdownModels.cs`
- `src/DocumentIDE.Core/Markdown/MarkdownStructureParser.cs`
- `tests/DocumentIDE.Core.Tests/Markdown/MarkdownStructureParserTests.cs`
- `docs/sprints/SPRINT-STATUS.md`

## Verification
- `dotnet build DocumentIDE.sln` → PASS (0 errors)
- `dotnet test --filter "FullyQualifiedName~MarkdownStructureParserTests"` → PASS (10 passed, 0 failed)

## Acceptance criteria
- [x] Non-mutation rule: Source Markdown files remain plain `.md` on disk and are strictly read-only.
- [x] Frontmatter extraction: Extracts structured key-values, tags, explicit `depends_on` at line 1.
- [x] Fault-tolerant frontmatter: Malformed YAML populates `ErrorMessage` without throwing unhandled exceptions.
- [x] Headings & Outline: ATX and Setext headings extracted with 1-based line numbers, start/end offsets, slug anchors, and hierarchical outline tree.
- [x] Links & Wikilinks: Standard links, wikilinks, target/anchor/alias extracted cleanly; ambiguous resolution deferred.
- [x] Code block isolation: Ignored headings/links inside fenced code blocks; code block count tracked.
- [x] Searchable body: Clean text stripped of frontmatter provided for search indexers; raw source preserved.
- [x] Parser versioning: `ParserVersion = "1.0.0"`.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `MD-01` (Source-First Markdown Editor) — unblocked (`READY`)
