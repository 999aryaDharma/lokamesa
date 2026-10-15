# Sprint Handoff — WORD-04

**Status:** DONE
**Branch:** `feat/word-04-high-fidelity-quick-look`
**Commits:** `548ad8f234234ccd112e65343968f4215207287b`

## What changed

- Read-only multi-page Quick Look: `QuickLookService` (Core, zero Word/COM
  references) serves preview pages from a cache keyed by
  `DocumentId|content_fingerprint|renderer_version` (`word-pdf-v1`) and renders
  lazily — exactly one page per cache miss — through an `IQuickLookRenderer`.
- `WordQueueQuickLookRenderer` (Infrastructure) routes each page render through
  the WORD-03 dedicated STA `WordAutomationQueue` (concurrency exactly 1) and
  maps Word unavailable/busy/modal/COM states to typed `WordRenderException`
  kinds, which the service surfaces as inspectable `QuickLookErrorCode`
  outcomes (never throws, never silent fallback).
- WORD-02 editing state is surfaced on every result (`EditingInWord` +
  summary) without blocking the read-only preview.
- `PublicQuickLookReader` is cache-only by construction (holds no renderer);
  Core does not reference Infrastructure and ShareHost does not reference
  Infrastructure — proven by test.
- `QuickLook.tsx` + `useQuickLookKeyboard`: Space opens (ignored in editable
  elements), Escape closes, per-page lazy `loadPage`, `<mark>` search-term
  highlighting where page text exists, editing badge, typed error display.

## Files changed

- `src/DocumentIDE.Core/Preview/QuickLookService.cs` (new — planned file, exact name; reuses `ThumbnailContracts.cs` record/enum patterns from CAN-03, no name mapping needed)
- `src/DocumentIDE.Infrastructure/Word/WordQueueQuickLookRenderer.cs` (new — extra adapter required to actually route renders through the WORD-03 queue; export body stays injectable, default honestly reports Word-required)
- `src/DocumentIDE.Web/src/features/quick-look/QuickLook.tsx` (new — planned file, exact name)
- `src/DocumentIDE.Web/src/features/quick-look/QuickLook.test.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/Preview/QuickLookTests.cs` (new — planned file, exact path)
- `tests/DocumentIDE.IntegrationTests/Word/WordQueueQuickLookRendererTests.cs` (new — adapter coverage)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~QuickLook"` → PASS (24/24; Red stage confirmed via CS0246 before implementation, then 5 adapter failures fixed by passing availability in tests)
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~Preview|FullyQualifiedName~Word"` → PASS (81/81)
- `dotnet test tests/DocumentIDE.Core.Tests` → PASS (459/459)
- `dotnet test tests/DocumentIDE.ArchitectureTests` → PASS (7/7)
- `dotnet test tests/DocumentIDE.SafetyTests` → PASS (24/24)
- `npx vitest run` (DocumentIDE.Web) → PASS (183/183, 30 files); `tsc -b` clean; `oxlint` only fast-refresh co-location warnings
- `npm install` was required in the worktree (node_modules absent); no package.json changes

## Acceptance criteria

- [x] No eager Word render for all files at startup — service construction enqueues nothing; renderer construction enqueues nothing; closed overlay loads nothing; proven by `ConstructingService_PerformsNoEagerRender`, `ConstructingRenderer_EnqueuesNothing`, frontend closed test.
- [x] Preview never edits source (byte-identical temp source after render).
- [x] Cache invalidates on fingerprint change (+ explicit `InvalidateAsync`).
- [x] Public viewer does not directly call Word queue (cache-only reader; assembly-boundary assertions).
- [x] Word unavailable/busy/modal/COM → typed outcomes; editing-in-Word surfaced.

## Deviations / spec conflicts

None. One scope note, not a conflict: the real Word COM export body is not
implemented here — the adapter's default export reports `Unavailable`
("Microsoft Word is required for this operation") until PDF-01 lands a COM
export; the queue routing, laziness, and typed mapping are fully implemented
and tested behind the injectable export operation. Editing-in-Word surfaces
state without blocking (spec: "at least surface state").

## Risks discovered

- `HighlightSegments` is a client-side substring highlighter over renderer page text; if a future renderer omits the text layer, matches silently degrade to zero (by design, best-effort).
- Artifact eviction/TTL policy for temp PDFs is not defined; cache currently grows until `InvalidateAsync`. Downstream SHR-04 snapshot work should define retention.

## Next eligible sprint(s)

- SHR-04 Snapshot Share (BLOCKED on WORD-04 — now unblocked on this side; still needs MD-02 which is DONE, and SHR-03 DONE)
