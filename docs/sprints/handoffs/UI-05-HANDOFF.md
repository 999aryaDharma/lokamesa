# Sprint Handoff — UI-05 (Context Pack & Missing-Resource Surfaces)

**Status:** DONE — both stopped items from UI-04 are now shipped
**Branch:** `feat/context-pack-missing-resource-surfaces`
**Worktree:** `ui-05-overlays` (started detached; branch created first)

## Outcome

| Step | Outcome |
| --- | --- |
| A1. Bounded, honest full-text source per document | **Shipped** |
| A2. `POST /api/context/pack` with typed outcomes | **Shipped** |
| A3. `ContextPackDialog` behind a real selection trigger | **Shipped, copy-only** |
| B1. `DELETE /api/documents/{id}/reference` | **Shipped** |
| B2. Authenticated `LocateMovedFileDialog` | **Shipped (RED proven against the old fetch)** |
| B3. `MissingResourceCard` mounted from `availabilityStatus` | **Shipped** |

No product decision was invented: there is no export destination, no save-dialog
plumbing and no file write. "Reference" means exactly the FS-06 database row;
the physical file is never touched.

---

## A. Context pack

### Full-text source (`ContextPackBuilder`)

`src/DocumentIDE.Core/Context/ContextPackBuilder.cs` builds the pack over an
explicit ordered `IReadOnlyList<string>` of document ids:

- `.md` / `.markdown` / `.txt` (and `DocumentType == markdown`) → the real source
  read from the document's `CurrentPath` — the same authority as
  `GET /api/documents/{id}/markdown`. Reads are streamed and capped.
- `.docx` → **only** the indexed text (`search_content.body`, the parser's
  `SearchableBody`) through the new read-only `IIndexedTextReader` implemented by
  `FtsIndex`. The emitted item is labelled `extracted-docx-text` /
  `Extracted DOCX text`, and its content begins with
  `> Extracted DOCX text (from the document index, not the original .docx bytes): …`
  so copied Markdown can never pass the index off as the file's bytes.
- No indexed text → typed `NoTextAvailable` (409) with `documentId`; never an
  empty item.
- Caps are real and typed: **128 KiB per document**, **512 KiB per pack**,
  **100 documents** per request. Over-cap is `DocumentTooLarge` / `PackTooLarge`
  (413); over-budget is `BudgetExceeded` (422); unknown id is `UnknownDocument`
  (404); empty selection is `EmptySelection` (400).
- The token estimate is the existing `ContextExportService.EstimateTokens`
  result. No estimate is computed or guessed in the controller.
- The combined Markdown is `ContextExportService.GenerateCombinedMarkdown`, so
  the route's `markdown` and the dialog's copy use the same join/order.

### Route

`POST /api/context/pack` lives in
`src/DocumentIDE.Api/Controllers/ContextPackController.cs`:

```json
{ "documentIds": ["md-1", "docx-1"], "tokenBudget": 8000 }
```

Success returns the ordered `documents[]` (id, title, `textSource`,
`contentLabel`, content, counts), `markdown`, and `tokenEstimate`. Every refusal
returns `{ status, errorCode, documentId?, detail?, tokenEstimate? }` with no
partial content. `tokenBudget` is optional; the UI sends `null`.

### Mount

- `Toolbar` gained one real control, **Context pack**, disabled while the
  document selection is empty.
- `App` builds the ordered document-id list from the canvas selection (folders
  excluded), calls the route, and opens `ContextPackDialog` with the returned
  sources. A typed refusal becomes the status banner and opens **no** dialog.
- **Copy-only.** `ContextPackDialog` previously rendered **Export context.md**
  unconditionally and showed "Exported!" without writing. The control is now
  conditional on `onExportToFile`; `App` passes no export callback, so the
  mounted dialog has no export control at all. This is a presentational guard,
  not new plumbing.

---

## B. Missing resources

### Reference-only removal

`DELETE /api/documents/{documentId}/reference`
(`DocumentReferenceController`) calls the already-shipped
`MissingResourceService.RemoveReferenceAsync`:

- deletes only the `documents` row (workspace/canvas rows cascade by FK),
- performs **zero** filesystem writes,
- unknown id → typed 404 `UnknownDocument`, nothing deleted.

The new integration test hashes the file before and after and asserts the file
still exists with the identical SHA-256.

### Locate transport fix

`LocateMovedFileDialog` used relative `fetch()` URLs with no session token. In
the shipped desktop host the page origin is `https://appassets.html2sav` and the
privileged API is `http://127.0.0.1:<port>`, so those requests could never reach
the API. It now uses `src/features/files/missingResourceApi.ts`, which follows
the shipped authenticated pattern (`OpenInWordAction` / `docxDiffApi`):
host-injected `__DOCUMENTIDE_API_URL__` plus
`X-DocumentIDE-Session-Token`. Candidates and the confirmed rebind both use it;
the props/rendering are unchanged.

The RED test was run before the fix: both new auth assertions failed with the
received URL `/api/documents/...` (relative, no base). They pass after.

### Card mount

`MissingResourcesPanel` (`features/files/MissingResourcesPanel.tsx`) renders one
`MissingResourceCard` per workspace document whose real `availabilityStatus` is
not `available`, over the workspace surface, using `lastKnownPath` (then
`currentPath`, then filename) and the typed status map
(`missing`, `permission_denied`, `locked`, everything else `unavailable`).
Locate opens the fixed dialog; Remove calls the reference route and reloads the
workspace, so the card disappears only after the backend confirms removal.

---

## Files changed

**New (backend)**

- `src/DocumentIDE.Core/Context/ContextPackBuilder.cs`
- `src/DocumentIDE.Core/Search/IIndexedTextReader.cs`
- `src/DocumentIDE.Api/Controllers/ContextPackController.cs`
- `src/DocumentIDE.Api/Controllers/DocumentReferenceController.cs`
- `tests/DocumentIDE.IntegrationTests/Context/ContextPackHostTests.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/DocumentReferenceHostTests.cs`

**New (frontend)**

- `src/DocumentIDE.Web/src/features/context/contextPackApi.ts` / `.test.ts`
- `src/DocumentIDE.Web/src/features/files/missingResourceApi.ts`
- `src/DocumentIDE.Web/src/features/files/missingResourceModel.ts`
- `src/DocumentIDE.Web/src/features/files/MissingResourcesPanel.tsx` / `.test.tsx`
- `src/DocumentIDE.Web/src/App.contextPack.test.tsx`
- `src/DocumentIDE.Web/src/App.missingResources.test.tsx`

**Modified**

- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` (register
  `ContextExportService`, `ContextPackBuilder`, `MissingResourceService`,
  `IIndexedTextReader`)
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` (allow-list the two new
  controllers; no guard change)
- `src/DocumentIDE.Infrastructure/Search/FtsIndex.cs` (implements
  `IIndexedTextReader`; additive read)
- `src/DocumentIDE.Web/src/App.tsx`
- `src/DocumentIDE.Web/src/features/context/ContextPackDialog.tsx`
- `src/DocumentIDE.Web/src/features/files/LocateMovedFileDialog.tsx`
- `src/DocumentIDE.Web/src/features/shell/Toolbar.tsx`
- `src/DocumentIDE.Web/src/features/workspace/workspaceApi.ts` (adds the
  already-serialized `lastKnownPath`)
- `docs/sprints/handoffs/UI-05-HANDOFF.md` (this file)

No `.pen`, `docs/quality/**`, `scripts/verify-release.ps1`,
`docs/sprints/SPRINT-STATUS.md`, manifest/roadmap, document-mutation, recovery,
SCM, batch or sharing file was changed. `SourceControlController` is untouched.
No runtime dependency was added.

## Existing tests changed (complete list)

1. `LocateMovedFileDialog.test.tsx` — **additive**: two tests pin the absolute
   loopback URL and the session-token header (candidates + rebind), plus global
   cleanup. No existing assertion was removed or weakened.
2. `ContextPackDialog.test.tsx` — **additive**: one test proves no export control
   renders without `onExportToFile`. The existing export test still supplies the
   callback and is unchanged.
3. `App.dialogs.test.tsx` — comment and test title updated only: the context pack
   and missing-resource surfaces now have real triggers/hosts, so the test says
   "closed without a real trigger". Every negative assertion is unchanged.

No other existing test was modified.

## Verification

Run from the worktree root unless noted. Frontend commands run in
`src/DocumentIDE.Web` after `npm ci` (fresh worktree).

| Command | Result |
| --- | --- |
| `npx tsc -b` | exit 0 |
| `npx oxlint` | exit 0, **35 warnings** — all pre-existing `react(only-export-components)`; none in a new/changed file |
| `npx vitest run` | **91 files / 637 tests passed, 0 failed, 0 skipped** |
| `dotnet build DocumentIDE.sln --nologo` | **0 errors**, 110 warnings (pre-existing package/analyzer warnings; none names a new/changed file) |
| `dotnet test tests/DocumentIDE.IntegrationTests` (run 1) | **1065 total: 1063 passed, 2 failed, 0 skipped** |
| `dotnet test tests/DocumentIDE.IntegrationTests` (run 2) | **1065 total: 1063 passed, 2 failed, 0 skipped** |
| `dotnet test tests/DocumentIDE.Core.Tests` | **473/473 passed, 0 skipped** |
| `dotnet test tests/DocumentIDE.SecurityTests` | **162/162 passed, 0 skipped** |
| `dotnet test tests/DocumentIDE.ArchitectureTests` | **12/12 passed, 0 skipped** |
| `git diff --check` | clean |

The two integration failures are the known environmental packaging tests that
need a prepared release tree (`artifacts/` is absent in a fresh worktree):

- `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets`
- `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`

No required test was skipped. No flake was observed in either integration run.

### TDD evidence

1. **Locate transport RED:** the two new assertions failed against the pre-fix
   component with `Received: "/api/documents/doc-123/rebind-candidates"` and
   `Received: "/api/documents/doc-123/rebind"`; both pass after the client fix.
2. **Backend RED:** before the routes existed, 11 of 13 new integration tests
   failed with `404 NotFound` (the two guard tests passed, because the guard
   rejects before routing). After the routes: **13/13 pass**.
3. **Component RED:** the no-export test failed before the export control was
   made conditional; it passes after.
4. Frontend focused run after implementation: **39/39** across the 8 touched
   suites; full suite **637/637**.

## Deviations

1. **Caps are product-tunable constants, not spec numbers.** The UI-05 contract
   requires per-document and total caps but names no size. Chosen:
   128 KiB/document, 512 KiB/pack, 100 documents/request. They live as public
   constants on `ContextPackBuilder` so a later product decision is a one-line
   change; the refusal types do not change.
2. **The dialog's visible estimate remains its pre-existing client-side
   approximation.** The route returns the real `ContextExportService` estimate
   and the UI's generic model uses the identical 4-chars/token formula, but the
   component was not redesigned to accept a server estimate prop. Mounting it
   does not fabricate a number: the label says "Estimated".
3. **The missing-resource host is a strip above the workspace.** It renders only
   when the workspace payload really contains non-available documents, and it is
   present in every workspace view (canvas/list/graph) rather than pushing a
   card into each renderer. This keeps `DocumentList`/`DocumentCanvas` unchanged.
4. **No export destination was chosen**, per the contract. Export stays
   unavailable rather than inventing a `Packs/` path.

## Residual risks

- A `.docx` whose index row is stale relative to the file will contribute the
  stale indexed text. That is exactly what the contract asks for ("the indexed
  text"), and the item is labelled as extracted; reindexing remains the index
  subsystem's job.
- Removing a reference leaves any `search_content` row for that id behind
  (no FK). Search reads are document-id based and the missing-row state does not
  expose it through the workspace payload, but a later cleanup could delete the
  orphaned index row. This sprint did not change index semantics.
- The pack route reads Markdown from `CurrentPath` directly (same file authority
  as the Markdown route) rather than calling HTTP; both paths share the
  filesystem source of truth, so this is consistent, not a new authority.
- Context pack is copy-only until a real export destination/model exists.

## Acceptance criteria

- [x] Design screen 12 reaches 5 of 6 overlays (QuickLook excepted by design):
      merge order, PDF progress, PDF collision, undo review, context pack, plus
      the missing-resource surface.
- [x] Every new route is typed, bounded and privileged-guarded (401 without a
      token, 403 hostile Origin, 403 hostile Host, asserted on both routes).
- [x] No file is mutated by the reference route (SHA-256 asserted identical).
- [x] No fabricated content: no rows/text counted anything absent; the copy-only
      dialog has no export control; a typed refusal opens no dialog.
- [x] QuickLook remains absent, pinned by negative tests.
