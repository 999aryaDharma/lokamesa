# UI-02 — Document Preview Content API (DOCX)

**Status:** READY
**Priority:** P1
**Kind:** feature (backend, read-only API)
**Recommended branch:** `feat/ui-02-document-preview-content-api`

## Goal

Add a **read-only** privileged route that returns the real extracted text of a `.docx` document so
the canvas file object can render a **faint page-1 preview from actual content** instead of the
current simulated grey lines (`LowDetailPaperLayout`).

This closes the honest half of the owner directive of 2026-10-18: a *content-derived* preview is
achievable now; a *rendered page image* is not (no Word → PDF → raster pipeline exists in any
shipped host — the same gap that blocks `QuickLook`/WORD-04).

## Design of record

- `docs/design/UI-DESIGN-BRIEF-MACOS.md` → § "Faint page-1 preview on a file object".
- `docs/design/screens-mac/01-canvas.png` — reference render.

## Depends On

— (none; backend-only, no sprint dependency).

**Consumed by:** `UI-01` (`docs/sprints/ui/UI-01-macos-shell-redesign.md`) as a *soft* dependency —
UI-01 ships the preview seam and the `.md` path first and must fall back to `LowDetailPaperLayout`
for `.docx` until this route lands.

**Hard rule:** Do not start downstream work on top of this branch until it is reviewed and merged.

## Required Reading

- `docs/specs/00-master-product-technical-spec.md` (privileged-surface + no-arbitrary-path rules)
- `docs/specs/03-workspace-canvas-spec.md` (CAN-03 paper object / thumbnail shell)
- `docs/design/UI-DESIGN-BRIEF-MACOS.md` (the preview requirement)
- `AGENTS.md` and the policies under `docs/agent/`

Also inspect, before writing any code (these are the patterns to copy, not invent):

- `src/DocumentIDE.Api/Controllers/MarkdownDocumentController.cs` — the read-only content-route
  pattern: identity is the FS-02 `documentId` (never a path), `404 UnknownDocument`,
  `409 DocumentUnavailable`. This sprint reuses that exact shape.
- `src/DocumentIDE.Core/Indexing/IDocxStructureParser.cs` — `DocxParseResult(Metadata,
  SearchableBody, HeadingsText, TablesText, HeadersText, FootersText)`.
- `src/DocumentIDE.Infrastructure/Docx/DocxStructureParser.cs` — the real extractor.
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` — DI; `IDocxStructureParser` is **already**
  registered at line 71 (verify, do not duplicate).
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` — `AllowedControllers` (line ~157); a controller
  absent from this list is **unreachable in the shipped product**, which is the exact class of bug
  tracked as SCM W30.
- `tests/DocumentIDE.IntegrationTests/Desktop/AppShellSurfaceHostTests.cs` and
  `PrivilegedSurfaceHostTests.cs` — the host-reachability test pattern.

## In Scope

1. **New controller** `src/DocumentIDE.Api/Controllers/DocumentPreviewController.cs`:

   ```
   GET /api/documents/{documentId}/preview
     -> 200 { documentId, kind: "docx", title, lines: ["…", …] }
     -> 404 { errorCode: "UnknownDocument" }
     -> 409 { errorCode: "NotPreviewableDocument" }   // not a .docx
     -> 409 { errorCode: "DocumentUnavailable" }      // file gone on disk
   ```

   - `.docx` **only**. `.md` keeps the existing `GET /api/documents/{id}/markdown` as its single
     source of truth — do **not** duplicate markdown authority here.
   - `lines` is derived from the real parse: build a bounded preview from
     `DocxParseResult.SearchableBody` (real body text), optionally prefixed by `HeadingsText` when
     it adds structure. Lines are the document's own words, never synthesized filler.
   - **Bounded payload** (mandatory): at most 60 lines, each clipped to at most 160 characters,
     total JSON payload ≤ 8 KB. A huge document must not become an unbounded dump.
   - **Identity**: resolve through `IDocumentRepository.GetByIdAsync(documentId)`; return the
     document's own id, never a filesystem path, and never accept a path as input.
   - **Read-only**: no snapshot, no temp file, no mutation pipeline. This route cannot change a byte
     on disk.
2. **Reachability**: add `DocumentPreviewController` to `WebAppHost.AllowedControllers` so the
   shipped desktop host serves it. Confirm `IDocxStructureParser` is resolvable in that host (it is
   registered in `PrivilegedSurfaceRegistration`); add it only if genuinely missing.
3. **Tests** (Red → Green → Refactor) — see "Required Tests".

## Out of Scope

- Any rendered page image (Word → temp PDF → page raster). That pipeline is a separate later sprint;
  it will feed the existing `DocumentNode` `thumbnailUrl` seam.
- Serving `.md` content through this route (already served by `/markdown`).
- Any frontend change (that is `UI-01`).
- Changing, widening, or relaxing any existing route, contract, or guard.
- Unrelated backend refactors.

## Security & File-Safety Gate

Before completion, explicitly confirm:

- [ ] the route is **read-only** — no snapshot, no temp file, no mutation code path
- [ ] no filesystem path is accepted as input and none is returned (identity is `documentId` only,
      resolved server-side) — no arbitrary local path is exposed
- [ ] the FND-02/QA-03 `PrivilegedRequestGuard` is untouched and still gates the route (proved by a
      host test that an unauthorized request is rejected)
- [ ] response payload is bounded (no unbounded content dump, no zip-bomb amplification)
- [ ] no higher-level safety rule was weakened for convenience

## Coordination

Both this sprint and the SCM **W30** surface-reachability lane edit `WebAppHost.cs`
(`AllowedControllers`). Whichever lands second must add its own entry to the **merged** list; the
controller (not a lane) performs that reconciliation. Never remove another lane's entry.

## Required Tests

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `GET /api/documents/{id}/preview` for a real seeded `.docx` returns `200` with non-empty `lines`
> whose text comes from the document's real content — against the shipped `WebAppHost`.

- [ ] unknown `documentId` → `404 UnknownDocument`
- [ ] a `.md` document → `409 NotPreviewableDocument`
- [ ] a `.docx` whose file is missing on disk → `409 DocumentUnavailable`
- [ ] real `.docx` → `200`, `kind == "docx"`, `lines` non-empty and derived from the real parse
- [ ] response body contains **no filesystem path** (assert the seeded path string is absent)
- [ ] a long document → `lines` length ≤ 60, no line longer than 160 chars, payload ≤ 8 KB
- [ ] **host reachability**: the shipped `WebAppHost` serves the route behind the privileged guard
      (authorized → `200`; unauthorized → rejected), proving it is not only on the dev API host
- [ ] existing `tests/DocumentIDE.IntegrationTests` suite stays green

## Planned Files / Artifacts

- `src/DocumentIDE.Api/Controllers/DocumentPreviewController.cs` (new)
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` — add to `AllowedControllers`
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` — only if the parser really is missing
- Controller tests + host test (colocated with the existing Desktop host tests)
- `docs/sprints/handoffs/UI-02-HANDOFF.md`

## Acceptance Criteria

- [ ] `GET /api/documents/{documentId}/preview` exists, is read-only, and is reachable in the
      shipped desktop host behind the privileged guard.
- [ ] It returns real `.docx`-derived preview lines, bounded, and never a filesystem path.
- [ ] `.md` remains served by `/markdown`; no duplicate authority introduced.
- [ ] `dotnet test tests/DocumentIDE.IntegrationTests` passes (no skipped/failing required test).
- [ ] No existing route or contract changed.
