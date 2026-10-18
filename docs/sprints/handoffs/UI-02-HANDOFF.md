# Sprint Handoff — UI-02 (Document Preview Content API, DOCX)

**Status:** DONE (branch ready for controller review)
**Branch:** `feat/ui-02-document-preview-content-api`
**Base:** `b622314` (`integration/parallel-batches`)
**Worktree:** `C:\Users\User\.local\share\opencode\worktree\c18aac\ui-02-preview`

## What changed

A new **read-only** privileged route returns the real, bounded text of a `.docx`
document so a canvas file object can show a *content-derived* page-1 preview
instead of simulated grey lines.

- **New** `src/DocumentIDE.Api/Controllers/DocumentPreviewController.cs` —
  `GET /api/documents/{documentId}/preview`:
  - `200 { documentId, kind: "docx", title, lines: [ … ] }` for a real `.docx`
  - `404 { errorCode: "UnknownDocument" }`
  - `409 { errorCode: "NotPreviewableDocument" }` for a non-`.docx` document
    (`.md` keeps its single authority at `GET /api/documents/{id}/markdown`)
  - `409 { errorCode: "DocumentUnavailable" }` when the file is gone on disk
  - Identity is the FS-02 `documentId` resolved via `IDocumentRepository.GetByIdAsync`.
    No filesystem path is accepted as input and none is returned (`title` is the
    document's filename, never a path).
  - Lines come from the real DOCX parse (`IDocxStructureParser.Parse` →
    `DocxParseResult.SearchableBody`, with `HeadingsText` used only when the body
    is empty), bounded to **≤ 60 lines**, each **≤ 160 chars**, whole JSON payload
    **≤ 8 KB**.
  - Strictly read-only: no snapshot, no temp file, no mutation pipeline.
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` — added
  `DocumentPreviewController` to `AllowedControllers`. A controller absent from
  that list is unreachable in the shipped product (the SCM W30 bug class), so the
  route would have existed but never been served without this entry. No other
  lane's entry was removed or changed.
- `src/DocumentIDE.Infrastructure/Docx/DocxStructureParser.cs` — **deviation, see
  below.** The parser now populates `DocxParseResult.SearchableBody` with the
  document's real body paragraph text and `HeadingsText` with the heading text
  (read-only; extracted from the already-loaded `word/document.xml`). Previously
  `SearchableBody` returned `Path.GetFileName(filePath)` (a placeholder), which
  made a "real content" preview impossible.
- `tests/DocumentIDE.IntegrationTests/Desktop/DocumentPreviewHostTests.cs` — new
  host-level tests (7).
- `tests/DocumentIDE.IntegrationTests/Persistence/DocumentMetadataRepositoryTests.cs`
  — comment-only update (line ~237): the assertion is unchanged and still passes;
  the stale comment claimed the DOCX searchable body is "the file name (placeholder
  until deep DOCX parsing lands)", which is no longer true after this sprint.

`IDocxStructureParser` was already registered in
`src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` (line 71), verified — **no
DI change** was needed.

## Deviation from the contract (important)

The contract (and its Required Reading) assumes
`DocxParseResult.SearchableBody` already carries **real body text**; the design of
record says the route is "built on the existing `DocxStructureParser`".
In reality the parser returned `Path.GetFileName(filePath)` as `SearchableBody`
(documented in `DOCX-04-HANDOFF.md` as an intentional placeholder and in a test
comment as "placeholder until deep DOCX parsing lands"). The existing
`FakeDocxParser` test double models `SearchableBody` as `"DOCX body text"`, and the
master spec §17 / spec 05 §5 require body text indexing — so the intended shape was
clear and the real parser was unimplemented.

To satisfy both the contract instruction ("lines come from
`DocxParseResult.SearchableBody`") **and** the sprint goal ("real extracted text …
never synthesized filler") without inventing a second extractor, the existing
parser was extended minimally to extract real text. It reuses the same
`w:t`-concatenation the parser already used for heading detection (now factored into
one `ExtractParagraphText` helper). Table / header / footer text payloads remain
unset, exactly as documented for DOCX-04.

**Blast radius checked:** changing `SearchableBody` also feeds the FTS `body`
column via `IncrementalIndexCoordinator` (a spec improvement: DOCX body text is now
searchable). No test pins the old stub value; the one test that reached it searched
by filename (a durable FTS column) and still passes. `DocumentLensService` /
`DocxGitDiffProvider` consume only `Metadata`, unaffected.

## Required tests → evidence

Placed with the existing Desktop host tests, mirroring
`AppShellSurfaceHostTests` / `PrivilegedSurfaceHostTests` (all run against the
shipped `WebAppHost`).

| # | Behaviour | Test | Red evidence | Green |
|---|---|---|---|---|
| 1 | real `.docx` → `200`, `kind == "docx"`, `lines` non-empty & from real content | `RealDocxDocument_Returns200_WithLinesFromRealContent` | `Expected: OK, Actual: NotFound` (route absent) | PASS |
| 2 | unknown `documentId` → `404 UnknownDocument` | `UnknownDocumentId_Returns404_UnknownDocument` | `JsonReaderException` (404 body was not JSON) | PASS |
| 3 | `.md` → `409 NotPreviewableDocument` | `MarkdownDocument_Returns409_NotPreviewableDocument` | `Expected: Conflict, Actual: NotFound` | PASS |
| 4 | `.docx` file missing on disk → `409 DocumentUnavailable` | `DocxMissingOnDisk_Returns409_DocumentUnavailable` | `Expected: Conflict, Actual: NotFound` | PASS |
| 5 | response contains no filesystem path | `Response_NeverContainsFilesystemPath` | `Expected: OK, Actual: NotFound` | PASS |
| 6 | long document bounded (≤60 lines, ≤160 chars, ≤8 KB) | `LongDocument_IsBounded_To60Lines160CharsAnd8KB` | `Expected: OK, Actual: NotFound` | PASS |
| 7 | host reachability behind the privileged guard (authorized 200 / unauthorized rejected) | `PrivilegedGuard_GatesTheRoute_AuthorizedServed_UnauthorizedRejected` | `Expected: OK, Actual: NotFound` (regex-style: the guard only proves reachability once the route exists) | PASS |

Red run (before implementation): `Failed: 7, Passed: 0` — every case failed for the
expected reason: the route returned `404` because neither the controller nor the
`AllowedControllers` entry existed yet.

## Commands run and results

```
dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~DocumentPreviewHostTests"
  Red  (pre-implementation): Failed: 7, Passed: 0, Skipped: 0
  Green (post-implementation): Passed: 7, Failed: 0, Skipped: 0

dotnet test tests/DocumentIDE.IntegrationTests
  Failed: 2, Passed: 807, Skipped: 0, Total: 809
  → the only 2 failures are the known-environmental artifacts tests (below)

dotnet test tests/DocumentIDE.Core.Tests            → Passed: 459, Failed: 0
dotnet test tests/DocumentIDE.ArchitectureTests     → Passed: 12,  Failed: 0
dotnet test tests/DocumentIDE.SafetyTests           → Passed: 24,  Failed: 0
dotnet test tests/DocumentIDE.SecurityTests         → Passed: 91,  Failed: 0
dotnet test tests/DocumentIDE.GoldenTests           → Passed: 39,  Failed: 0
```

### Known-environmental failures (fresh worktree, no gitignored `artifacts/`)

Both failures are caused by missing build artifacts that `scripts/verify.ps1`
produces and that are gitignored — not by this change:

- `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
  → `Installer MSI artifact should exist at …\artifacts\installer\DocumentIDE-Setup.msi`
- `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  → `Executable should exist at …\artifacts\desktop\DocumentIDE.Desktop.exe`

No other failure was observed in any project.

## Security & File-Safety Gate

- [x] **Read-only** — the route has no snapshot/temp/mutation code path. It only
      calls `IDocumentRepository.GetByIdAsync` and `IDocxStructureParser.Parse`,
      which opens the package `FileMode.Open, FileAccess.Read, FileShare.Read` +
      `ZipArchiveMode.Read`. No `RecoverySnapshotService` / `IAtomicFileReplacer`
      is injected. (Evidence: the controller's dependencies are exactly
      `IDocumentRepository` + `IDocxStructureParser`.)
- [x] **No path exposure** — no filesystem path is accepted as input (the route
      parameter is `documentId`) and none is returned (`documentId`, `kind`,
      `title` = filename, `lines` = body text). Proved by
      `Response_NeverContainsFilesystemPath`, which asserts the seeded absolute
      path and its directory are absent from the response body.
- [x] **Guard intact** — zero edits to `PrivilegedRequestGuard` and the security
      middleware. `PrivilegedGuard_GatesTheRoute_AuthorizedServed_UnauthorizedRejected`
      proves the same `WebAppHost` serves `200` with a valid session token + allowed
      origin and `401` without a token.
- [x] **Bounded payload** — `LongDocument_IsBounded_To60Lines160CharsAnd8KB` seeds a
      100 × ~300-char document and asserts `lines.Count ∈ [1, 60]`, every line
      ≤ 160 chars, and the whole JSON body ≤ 8192 bytes. The 60 × 160 caps alone can
      exceed 8 KB, so the controller additionally measures the serialized payload
      and drops trailing lines until it fits (no unbounded dump; no zip-bomb
      amplification — only `word/document.xml` text is read).
- [x] **No higher-level rule weakened** — `.md` authority stays on
      `/markdown` (this route returns `409` for it), no existing route/guard was
      widened, and the new controller was added to the allow-list without removing
      another lane's entry.

## Remaining risk / notes

- A *rendered* page-1 image (Word → temp PDF → page raster) remains **out of
  scope** for this sprint. This route is a *content-derived* text preview only.
  The rendered image pipeline is the same missing capability that blocks
  `QuickLook` / **WORD-04**, which owns the future renderer; it will feed the
  existing `DocumentNode.thumbnailUrl` seam (per the design brief) and needs no
  redesign here.
- Contract deviation (parser text payloads) documented above; this is the one
  file changed beyond the sprint's "Planned Files" list, plus a comment-only fix in
  an existing test.
- `DocumentPreviewController` is reachable on **both** hosts: the dev API host
  (`Program.cs`, all controllers mapped) and the shipped desktop host (allow-list).

## Acceptance criteria

- [x] `GET /api/documents/{documentId}/preview` exists, is read-only, reachable in
      the shipped desktop host behind the privileged guard.
- [x] Returns real `.docx`-derived preview lines, bounded, never a filesystem path.
- [x] `.md` remains served by `/markdown`; no duplicate authority.
- [x] `dotnet test tests/DocumentIDE.IntegrationTests` passes except the two
      documented environmental artifacts tests (no skipped/failing *required* test).
- [x] No existing route or contract changed.
