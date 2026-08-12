# Sprint Handoff — CTX-03 — DOCX Context Representation

**Status:** DONE  
**Branch:** `feat/ctx-03-docx-context-representation`  
**Commits:** `0cd43a4`

## What changed
- Created `DocxContextFormatter` in `DocumentIDE.Core.Context` to convert DOCX document structures (headings, paragraphs, tables, headers, footers) into stable, derived Markdown context representations.
- Supports origin labeling for headers (`[Header]`), footers (`[Footer]`), and tables.
- Supports context bounding via `DocxContextFormatterOptions.MaxCharacters` with clean truncation markers when limits are exceeded.
- Excludes field instructions (`w:instrText` and `w:fldChar`) from extracted readable context text.
- Integrates optional `IDocxPackagePreflight` package validation to reject invalid or unsafe packages before parsing.
- Added comprehensive unit tests in `DocxContextFormatterTests.cs` covering headings, table structure preservation, SHA-256 fingerprint immutability, context bounding, field instruction exclusion, and preflight rejection.

## Files changed
- `src/DocumentIDE.Core/Context/DocxContextFormatter.cs`
- `tests/DocumentIDE.Core.Tests/Context/DocxContextFormatterTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/CTX-03-HANDOFF.md`

## Verification
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test --filter "FullyQualifiedName~DocxContextFormatterTests"` → PASS (7/7 tests passed)
- `& 'C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe' test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj` → PASS (143/143 tests passed)

## Acceptance criteria
- [x] Representation is derived, not round-tripped back to DOCX.
- [x] Headings, paragraphs, and tables converted to stable Markdown-like text.
- [x] Origins and sections (Header, Footer, Table, Body) clearly labeled.
- [x] Extremely large extracted content bounded cleanly via MaxCharacters.
- [x] Zero destructive mutation; DOCX source fingerprint (SHA-256) verified unchanged.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path (Read-only stream access enforced, verified by SHA-256 fingerprint test).
- [x] no arbitrary path or secret is exposed.
- [x] dependency/security boundaries remain intact.
- [x] no destructive action silently broadened its scope.
- [x] relevant higher-level safety rules remain stronger than convenience.

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `CTX-04` — Token Estimate & Context Export (requires CTX-02 and CTX-03)
