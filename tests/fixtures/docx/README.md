# Golden DOCX Regression Corpus (QA-01)

Canonical DOCX fixture corpus and structural invariance tests for DOCX safety.
Specification sources: spec 06 §26 (golden fixture corpus) and §28 (resource
fixtures), spec 11 §4–§7 (DOCX golden fixtures / assertions / split-run test),
master spec §54 (Word fidelity testing) and §92 (golden DOCX regression).

## How the corpus is stored

Fixtures are **generated programmatically at test time** by
`tests/DocumentIDE.GoldenTests/GoldenDocxFixtureBuilder.cs` and verified to be
**byte-deterministic** (fixed ZIP entry order, fixed ZIP timestamps, fixed
compression level, fixed media bytes). No opaque `.docx` binaries are committed;
the generator source is the version-controlled corpus. Tests write fixtures only
to per-test temp directories and delete them afterwards.

Run the suite:

```pwsh
$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"
& "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe" `
    test tests\DocumentIDE.GoldenTests\DocumentIDE.GoldenTests.csproj --nologo
```

Expected runtime is a few seconds; the suite is CI-friendly.

## Valid fixtures

| Fixture | Purpose (spec) | Key expectations | Strict replacement case |
|---|---|---|---|
| `simple` | Minimal single-section baseline | preflight valid, 1 section, body text searchable | `Golden Corpus` → `Golden Archive` |
| `split-runs` | `NUSA-DUA-1` fragmented across runs (spec 11 §6) | one logical match spanning run boundaries; unrelated runs keep text + `rPr` | `NUSA-DUA-1` → `NUSA DUA 1` |
| `mixed-format` | Bold/italic/underline/color/font/size runs | replacement spans italic + underlined runs; unrelated paragraph formatting unchanged | `Report Summary` → `Report Overview` |
| `tables` | Table cell text (spec 06 §23) | `Table` origin matching; `tblPr`/`tcPr` survive | `GOLDEN-TARGET` → `GOLDEN-REPLACED` |
| `headers` | Default + even-page headers (spec 06 §22) | `Header` origin matching; only the matched header part changes | `ACME GOLDEN` → `ACME ARCHIVE` |
| `footers` | Default + even-page footers (spec 06 §22) | `Footer` origin matching; only the matched footer part changes | `Golden Draft` → `Archive Draft` |
| `sections` | Two sections, mixed margins, landscape break | 2 `sectPr`; page size/margins/orientation survive | `GOLDEN-TWO` → `GOLDEN-SECOND` |
| `images` | Inline drawing + real PNG media part | media part and drawing subtree byte/structure-identical | `GOLDEN` → `ARCHIVE` |
| `hyperlinks` | External hyperlink relationship + style | hyperlink element/relationship survive | `GOLDEN` → `ARCHIVE` |
| `numbering` | `numPr` list references `numbering.xml` | numbering part and `numPr` survive | `GOLDEN` → `ARCHIVE` |
| `custom-styles` | `pStyle`/`rStyle` custom styles | styles part and style references survive | `GOLDEN` → `ARCHIVE` |
| `fields` | `PAGE` field with `instrText` code + result | field code never searchable/replaceable (spec 06 §24), result text is | `GOLDEN` → `ARCHIVE` |
| `tracked-changes` | `w:ins` / `w:del` revisions | inserted text searchable, deleted (`w:delText`) not; revision subtrees survive (spec 06 §25) | `GOLDEN` → `ARCHIVE` |

Per-fixture expectations (table counts, media counts, section counts, header/footer
part counts, expected text + origin, absent text, structural replacement case) are
declared in `tests/DocumentIDE.GoldenTests/GoldenDocxFixtures.cs`.

## Hostile / resource fixtures (spec 06 §28, spec 11 §28)

| Fixture | Attack | Expected safe outcome |
|---|---|---|
| `hostile-entry-explosion` | 10,001 ZIP entries | preflight `TooManyEntries`, source unchanged, mutation blocked |
| `hostile-oversized-single-part` | part above per-part uncompressed budget* | preflight `PartSizeExceeded` |
| `hostile-oversized-total` | package above total uncompressed budget* | preflight `TotalUncompressedSizeExceeded` |
| `hostile-high-compression` | ~1 MiB repeated XML compressing at a high ratio | preflight valid; deep search bounded by occurrence budget (`OccurrenceBudgetExceeded`); source unchanged |
| `hostile-parser-budget` | 500 matches against a 50-match budget | typed `InvalidDataException("OccurrenceBudgetExceeded")` |
| `hostile-dtd-external-entity` | DTD / XXE declaration | preflight `DtdProhibited`, never resolved |
| `hostile-zip-slip` | `../escape.txt` ZIP entry | preflight `ZipSlipAttempt`, nothing extracted |
| `hostile-not-a-zip` | non-ZIP bytes with `.docx` name | preflight `InvalidZipArchive` (corrupted) |
| `hostile-malformed-xml` | truncated XML part | preflight `XmlMalformed` (corrupted) |

\* Physical attack sizes that would need >100 MiB allocations use tightened
preflight budgets to exercise the same rejection code paths; the deployed
default budgets themselves are exercised by `DocxResourceBudgetTests` in
`tests/DocumentIDE.IntegrationTests/Docx`.

## Assertion helpers

`tests/DocumentIDE.GoldenTests/GoldenDocxAssertions.cs` provides:

- **package validity** — preflight valid + zero warnings + every XML/relationship
  part parses with DTD processing prohibited;
- **unrelated-part invariance** — every package part except the matched part(s)
  is decompressed-byte-identical;
- **unrelated-content invariance** — paragraphs outside the match are canonically
  identical; formatting/structure elements (`sectPr`, `pPr`, `tblPr`, `tcPr`,
  `numPr`, `pStyle`, `rStyle`, drawings, fields, revisions, ...) unchanged;
- **run-level invariance** — uninvolved runs keep text + `rPr`; the replacement
  anchors on the first matched run's formatting; no run gains new formatting;
- **no-op fingerprints** — indexing (preflight + search) and no-op mutations
  (target absent, identity replacement) must not rewrite package bytes (spec 06 §19).
