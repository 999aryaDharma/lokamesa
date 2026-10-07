# Sprint Handoff — DOCX-04

**Status:** DONE  
**Branch:** `feat/docx-04-document-lens-metadata` (base `e34fb32`)  
**Commits:** `af7877e` (`feat(docx): document lens metadata`), `76fcd60` (`feat(web): docx document lens inspector`)

## What changed

- Created `DocumentLensService` (Core) — read-only DOCX "document lens" that composes the
  existing IDX-01 package preflight and the IDX-02 `IDocxStructureParser` and returns a typed
  `DocumentLensResult` (counts, summaries, headings, header/footer presence) or a typed failure
  (`FileNotFound`, `PackageValidationFailed`, `UnexpectedStructure`, `MetadataUnreadable`).
  Derived values are named `*Summary` (`PaperSummary`, `OrientationSummary`, `MarginSummary`,
  `PrimaryFontSummary`, `PrimaryFontSizeSummary`).
- Extended the existing IDX-02 `DocxStructureParser` (no second parser) to populate the
  IDX-04 `DocumentMetadata` shape from the package, read-only (`FileAccess.Read`,
  `ZipArchiveMode.Read`, never Update):
  - counts: `w:sectPr` sections, `w:tbl` tables, `w:drawing`/`w:pict` images,
    page count from Word's last-saved `docProps/app.xml` when present;
  - paper/orientation/margins per section (twips → deterministic cm strings,
    `Mixed (...)` when sections differ; margin default 1440 twips per OOXML);
  - primary font/size = most-used effective run formatting with
    direct → paragraph style → run style → docDefaults resolution, empty runs ignored,
    ties broken by ordinal-smallest font name / smallest size;
  - headings from `HeadingN`/`Title` paragraph styles or `w:outlineLvl`;
  - header/footer presence from `word/header*.xml` / `word/footer*.xml` parts.
  - optional `IDocxPackagePreflight` constructor parameter (same pattern as
    `StrictTextMutator` / `DocxContextFormatter`); default `null` keeps all existing call sites
    and DI registration working unchanged.
  - Text-indexing payloads (`SearchableBody`, `HeadingsText`, `TablesText`, `HeadersText`,
    `FootersText`) intentionally unchanged — deep DOCX text indexing is not DOCX-04 scope.
- Created `DocxInspector.tsx` (Web) — right-side inspector rendering filename/path,
  size/modified time, counts, summary rows labeled `(summary)`, heading list,
  header/footer state, a typed error state (`Metadata unavailable` + error code + message),
  and a stale badge. Clicking supported metadata values (paper, margins, font, font-size,
  headings) invokes the typed `onApplyMetadataFilter({ field, value })` callback using the
  SRCH-03 filter field names; unsupported values (orientation, counts) stay plain text.

## Planned-file mapping (required by dispatch)

| Planned / expected | Actual |
|---|---|
| `src/DocumentIDE.Core/Documents/DocumentLensService.cs` | created as planned |
| `src/DocumentIDE.Web/src/features/inspector/DocxInspector.tsx` | created as planned |
| `tests/DocumentIDE.Core.Tests/Documents/DocumentLensTests.cs` | created as planned |
| Web test (`DocxInspector.test.tsx`) | `src/DocumentIDE.Web/src/features/inspector/DocxInspector.test.tsx` — active vitest convention; `tests/frontend/**` is legacy/unwired, per BATCH-B-DISPATCH test-location ruling |
| **Additional modified file (flagged)** | `src/DocumentIDE.Infrastructure/Docx/DocxStructureParser.cs` — the existing IDX-02 parser was extended instead of creating a duplicate abstraction, per the sprint rule "modify that existing file rather than creating a duplicate abstraction" and the dispatch rule "Reuse the existing DOCX parsing machinery (IDX-02) — no second parser". This is the only file outside the planned-file list that changed. |

## Verification

- RED (mandated first): `npx vitest run src/features/inspector/DocxInspector.test.tsx` →
  FAIL, `Failed to resolve import "./DocxInspector"` (component did not exist).
- GREEN: same command after implementation → PASS (1 test), later 6/6 after RED→GREEN for
  filter callback, error state, stale state, size/modified display, and the
  "Detected headings" derived label.
- RED (Core): `dotnet test tests/DocumentIDE.Core.Tests --filter FullyQualifiedName~DocumentLensTests`
  → build error CS0234/CS0246 (`DocumentIDE.Core.Documents` / `DocumentLensService` missing).
- GREEN: same command → PASS (9/9).
- `dotnet test tests/DocumentIDE.Core.Tests` → PASS (449 passed, 0 failed, 0 skipped).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~Docx|FullyQualifiedName~IncrementalIndex|FullyQualifiedName~DocumentMetadataRepository|FullyQualifiedName~Context"`
  → PASS (42 passed, 0 failed).
- `npx vitest run` (full web suite) → PASS (21 files, 102 tests).
- `npm run build` → PASS. `npm run lint` → warnings only (no errors; new helper export
  produces the same `react(only-exports-components)` fast-refresh warning as the existing
  `MetadataFilterChips.tsx`).
- Full `DocumentIDE.IntegrationTests` run: 2 failures are artifacts-dependent release-script
  tests (`InstallerTests`, `ScriptTests.Release_Build_Embeds_Production_Web_Assets`) that assert
  `artifacts/` produced by `scripts/verify.ps1`; expected in a fresh worktree. One filesystem
  watcher test flaked once and passed on focused re-run.
- `scripts/verify.ps1` - PASS on the sprint tree: build + 7 Architecture + 12 Safety + 449 Core + 266 Integration + Web build/tests, 0 skipped (`.superpowers/sdd/DOCX-04-document-lens-metadata/verify-docx-04.log`).

## Required tests → evidence

- [x] **Inspector displays fixture metadata** — `DocxInspector.test.tsx` (web), exact test name;
  asserts counts, `(summary)` labels, headings, header/footer presence.
- [x] **Mixed sections show mixed summaries** — `DocumentLensTests.cs` (Core), exact test name;
  two-section fixture → `Mixed (A4, Letter)`, `Mixed (Landscape, Portrait)`, `Mixed (...)` margins,
  stable across repeated inspections.
- [x] **Metadata click can invoke search filter where supported** — `DocxInspector.test.tsx`,
  exact test name; typed callbacks `{field:'paper'|'margin'|'font'|'font-size'|'heading', value}`
  with `font-size` normalized to numeric; orientation/counts not clickable.

## Implementation steps → evidence

1. Counts where reliable — section/table/image counts + Word-saved page count
   (`Inspect_ComputesCountsSummariesHeadingsAndPresence`).
2. Paper/orientation/margin summaries — per-section extraction + `Mixed (...)` determinism
   (`Mixed_sections_show_mixed_summaries`).
3. Deterministic primary font/font-size summaries — style-resolving counter with documented
   tie-breaks (`Inspect_PrimaryFontAndSizeSummariesAreDeterministic`).
4. Headings + header/footer presence — `HeadingsJson` → typed heading list; part detection.
5. Right-side Inspector — `DocxInspector.tsx` `aside` with all fields.

## Failure / edge cases → evidence

- Package validation failure → typed `PackageValidationFailed` (preflight error code surfaced):
  `Inspect_InvalidPackage_ReturnsTypedFailure`.
- Unexpected OOXML with no preflight → typed `UnexpectedStructure`:
  `Inspect_MalformedXmlWithoutPreflight_ReturnsTypedUnexpectedStructure`.
- Well-formed but unusual OOXML (no `w:body`) → deterministic empty result, not a crash:
  `Inspect_UnusualButWellFormedOoxml_ReturnsDeterministicEmptyResult`.
- Unreadable derived metadata → typed `MetadataUnreadable`:
  `Inspect_MalformedHeadingMetadata_ReturnsTypedFailure`.
- Source package never opened for update: `Inspect_DoesNotModifySourceFile` (bytes + write
  time unchanged) plus read-only open mode in code.

## Acceptance criteria

- [x] Derived heuristic is labeled as a summary, not a source fact — naming
  (`PaperSummary`, `OrientationSummary`, `MarginSummary`, `PrimaryFontSummary`,
  `PrimaryFontSizeSummary`) and UI copy (`Paper (summary)`, `Margins (summary)`,
  `Main font (summary)`, `Main size (summary)`); asserted in `DocxInspector.test.tsx`.
- [x] Any derived heuristic value labeled in both naming and UI copy (web test asserts
  `(summary)` labels; Core tests bind the `*Summary` property names).

## Security & File-Safety Gate Confirmation

- [x] No user file was mutated through an unreviewed/bypass path — the lens pipeline is
  strictly read-only (`FileMode.Open/FileAccess.Read/FileShare.Read`, `ZipArchiveMode.Read`);
  regression test asserts source bytes and last-write time are unchanged.
- [x] No arbitrary path or secret is exposed — the result echoes only the caller-supplied
  path; no path traversal or content of other files is read beyond the package itself.
- [x] Dependency/security boundaries remain intact — no new dependencies; DTD processing
  prohibited and `XmlResolver = null` in the parser's XML readers; preflight gate runs before
  deep parsing when provided.
- [x] No destructive action silently broadened its scope — N/A (read-only feature, no
  mutation path added).
- [x] Relevant higher-level safety rules remain stronger than convenience — invalid packages
  and unexpected XML never degrade to silently empty metadata; they return typed results.

## Deviations / spec conflicts

- `docs/sprints/SPRINT-STATUS.md` was intentionally NOT edited, per BATCH-B-DISPATCH
  ("the controller updates the board after integration"). The sprint doc's DoD line is
  satisfied by the controller.
- `DocxStructureParser` previously returned constant stub metadata; DOCX-04 makes it actually
  read the package. Preflight remains optional to keep the existing DI registration and all
  existing call sites unchanged; the lens service passes a preflight instance in tests and
  callers can do the same.
- No API/DI wiring was added (`Program.cs` untouched) because this sprint has no controller/
  endpoint in scope and the inspector is prop-driven like `RelationsInspector`.

## Risks discovered

- Page count is only available when Word previously saved `docProps/app.xml`; otherwise it is
  `null` and the UI omits the count (spec allows absence of exact page count).
- Image count definition is deterministic but scoped: picture elements (`w:drawing`/`w:pict`)
  in the main document part only, not headers/footers or media-part reuse counting.
- Heading detection is style-name/outline-level based; paragraph-level `w:outlineLvl` inside
  custom styles is not resolved through the style table (documented in parser docs).

## Next eligible sprint(s)

- `WORD-04` (High-Fidelity Quick Look) once `WORD-03` is DONE — its `DOCX-04` dependency is now
  satisfied.
- No other sprint lists `DOCX-04` as a dependency.
