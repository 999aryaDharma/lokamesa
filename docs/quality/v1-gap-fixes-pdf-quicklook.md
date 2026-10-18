# V1 Gap Fixes — PDF finalization UX mount + QuickLook decision (G8 / G4)

**Scope:** close the final two items the REL-03 re-review addendum
(`docs/quality/v1-readiness-report.md` §10) left open:

1. **§9.8 / G8** — mount the PDF export UX against the now-live privileged route.
2. **§9.4 / G4 residual** — resolve QuickLook in the shipped host: mount it against a real
   page source, or implement + record the deliberate product path.

This lane is frontend-only. It changed **no** host allow-list, no controller and no security
middleware: `PdfController` and `WordController` were already registered in the shipped host
(`src/DocumentIDE.Desktop/WebView/WebAppHost.cs:157-172`), so the FND-02/QA-03 guard is
untouched and `PrivilegedSurfaceHostTests` / `BatchPreviewHostTests` / `AppShellSurfaceHostTests`
needed no edits.

**Branch:** `fix/v1-pdf-quicklook-mount` (base `integration/parallel-batches` @ `f2faa1b`)

**Verification (this branch):**

| Command | Result |
|---|---|
| `dotnet build DocumentIDE.sln` | **0 errors** (158 pre-existing warnings) |
| `dotnet test` — `DocumentIDE.IntegrationTests` `Desktop` namespace | **20 passed / 0 failed / 0 skipped** |
| `dotnet test` — full `DocumentIDE.IntegrationTests` | **786 passed / 3 failed / 0 skipped** (see note) |
| `npx vitest run` (in `src/DocumentIDE.Web`) | **385 passed / 55 files** (baseline `f2faa1b`: 374 passed / 53 files) |
| `npx tsc -b` | exit 0 |
| `npx oxlint` | exit 0 (warnings only — pre-existing `only-export-components` class) |

No existing test was weakened or removed. Both new production files were driven Red → Green →
Refactor (failing test first, confirmed the expected failure, then the minimum implementation).

---

## 1. G8 — PDF finalization UX is now mounted

**Unreachable before:** `src/DocumentIDE.Web/src/features/pdf/PdfActions.tsx` was imported only
by its own test. The privileged route existed and was exercised, but no product surface called
it — so §9 condition 8 ("wire the UX") was unmet even though the backend half (G8 "reduced") was
done.

### Routes / contracts wired (derived from `PdfController.cs`, not invented)

| Client call | Route | Request (verbatim `PdfController` DTO) | Response (controller projection) |
|---|---|---|---|
| `exportSinglePdf` | `POST /api/pdf/export` | `{ sourceDocxPath, outputDirectory?, outputFileName?, collisionBehavior? }` | `{ status, outputPath, sourceFingerprintBefore, sourceFingerprintAfter, errorMessage, isSuccess }` |
| `exportSeparatePdfs` | `POST /api/pdf/export-batch` | `{ sourceDocxPaths[], outputDirectory, collisionBehavior? }` | `{ totalCount, succeededCount, skippedCount, failedCount, cancelledCount, isSuccess, summary, files[{ sourceDocxPath, status, outputPath, errorMessage }] }` |
| `mergeDocumentsToPdf` | `POST /api/pdf/merge` | `{ orderedSourcePaths[], outputPath, collisionBehavior? }` | `{ status, outputPath, pageCount, orderedSources, errorMessage, isSuccess }` |

- `status` is the engine `PdfExportStatus` **name** (`Succeeded`, `OutputCollision`,
  `SourceNotFound`, `SourceInvalid`, `ExportFailed`, `DiskFull`, `Cancelled`), matching the
  `BatchPreviewController` convention — the UI never depends on numeric enum ordinals.
- `collisionBehavior` accepts only `"overwrite"` / `"rename"`; it is **omitted** for the default,
  which the controller maps to `PdfCollisionBehavior.Fail` (never silently overwrite).
- Gating is unchanged: `Origin` + `Host` + `X-DocumentIDE-Session-Token` through
  `PrivilegedRequestGuard`; the client sends the host-injected token.

### Files changed / added

| File | What it does |
|---|---|
| `src/features/pdf/pdfApi.ts` **(new)** | Typed client for the three routes above; `PdfRequestError` carries the controller `errorCode` on 4xx/5xx instead of leaking an opaque failure. |
| `src/features/pdf/PdfExportPanel.tsx` **(new)** | The container that mounts the presentational `PdfActions` on real engine calls (pattern: `SharePanel`/`BatchPreviewPanel`). Owns busy/error/results/progress state, resolves a document's real source path, and holds the output-location choices. |
| `src/App.tsx` | A `PDF` tool-bar toggle mounts `PdfExportPanel`; the panel is driven by the **current canvas selection** and a document-id → source-path map taken from the same workspace bootstrap that populates the canvas. |
| `src/features/pdf/pdfApi.test.ts` **(new)** | Pins the exact wire shape of all three routes + typed error surfacing. |
| `src/features/pdf/PdfExportPanel.test.tsx` **(new)** | Pins single / separate / merge flows, collision handling and the missing-source refusal. |
| `src/App.shell.test.tsx` | Adds a shell-level test that the panel mounts and reaches `/api/pdf/export` for a selected DOCX. |

### Honest behavior (no fake success)

- **Source resolution is real.** The export source is the document's actual `CurrentPath`, taken
  from `GET /api/workspaces/{id}/documents`. If it cannot be resolved, the panel reports a typed
  refusal (`No source path available for <name>.`) and does **not** call the engine. A separate
  export refuses the whole request if any selected document has no path — it never silently drops
  one.
- **Typed engine refusals are surfaced, not swallowed.** `OutputCollision` renders the
  `PdfActions` collision fieldset (Overwrite / Rename / Skip / Cancel); the first attempt is shown
  as `skipped`, never as success, and a retry only happens after an explicit decision. Batch
  collisions come back as per-file `skipped` rows with the engine message.
- **Output location is an explicit user choice.** Single export lands beside the source (engine
  default). Separate export requires a visible, editable **Output folder**; merge requires a
  visible, editable **Merged PDF file**. Both are pre-filled from the selection but must be
  confirmed/changed by the owner — the client never invents an output location silently.
- **Existing-file policy is explicit.** Separate/merge offer a visible
  *If a PDF already exists* choice (Stop and report / Rename / Overwrite); the default stays
  fail-closed.
- **Word COM concurrency is untouched.** The panel only calls the PDF controller; the controller
  routes every render through the WORD-03 `IWordAutomationQueue` (concurrency 1). No new COM path
  was added.
- **Local filesystem stays the source of truth.** Export reads the source DOCX and writes a new
  PDF; it never mutates the DOCX (the engine re-fingerprints the source and fails if it changed).

**Tests:** `pdfApi.test.ts` 4/4; `PdfExportPanel.test.tsx` 5/5 (single result, collision → explicit
decision, missing-source refusal, separate with explicit folder + per-file outcomes, merge with
explicit order + path); `App.shell.test.tsx` adds the mount + live-route test.

---

## 2. G4 residual — QuickLook: deliberate product-path decision (documented)

**Question §9.4 asks:** either mount the WORD-04 Quick Look overlay against a real page source,
or implement/record the deliberate product decision the shell already has.

**Decision:** the owner DOCX "preview" product path is **open-in-Word**, not the `QuickLook`
overlay. The overlay is **deliberately not mounted**, because the shipped hosts expose **no
page-render data source** for it. This is option (b); mounting `QuickLook` over a non-existent
source would be a UI over nothing.

### Evidence

1. **`QuickLookService` is not reachable from any host.** The service
   (`src/DocumentIDE.Core/Preview/QuickLookService.cs`) exists and is tested, but a full search
   for `QuickLookService` / `IQuickLookRenderer` / `WordQueueQuickLookRenderer` finds references
   only in their own definitions and in tests — no controller, no host composition.
2. **No page-render route exists.** The complete privileged route inventory
   (`Route(` / `Http{Get,Post}(` across `src/DocumentIDE.Api/Controllers`) contains **no**
   page/preview/QuickLook route. The only DOCX viewer endpoint is the public ShareHost path
   `GET /share/{shareId}/documents/{docId}/preview` (public viewer surface, share-token gated,
   `src/DocumentIDE.ShareHost/Endpoints/SnapshotEndpoints.cs:64`) — it is not an owner-app data
   source and is not reachable from the privileged shell.
3. **The renderer is unregistered.** `IQuickLookRenderer` has exactly one implementation
   (`WordQueueQuickLookRenderer`) and is not registered in `PrivilegedSurfaceRegistration` or any
   other composition root, so even a hypothetical route would have no renderer injected.
4. **The pipeline is a spec prerequisite.** Master spec 06 §12 defines Quick Look as
   `DOCX → Word export to temp PDF → PDF pages rendered to images → cache`. Until that pipeline
   is exposed, there is nothing to read.

### The deliberate product path (implemented + proven)

The shipped, working owner DOCX path is already wired: double-click a DOCX **canvas card** or
**graph node** → `openInWord(documentId)` (`src/features/files/OpenInWordAction.ts`) →
`POST /api/word/open` → `WordController` → `WordDesktopLauncher`. This route is in the shipped
host allow-list (`WebAppHost.cs:160`) and is now covered by a shell-level test:

- `src/App.shell.test.tsx` — *"G4: double-clicking a DOCX canvas card opens it in Word (the
  deliberate QuickLook product path)"* asserts a real `POST /api/word/open` with
  `{ documentId: 'docx-1' }` from the rendered shell (no test-only props).

The decision is also recorded in code next to where the overlay would otherwise mount:
`src/DocumentIDE.Web/src/App.tsx` (header comment, "G4 deliberate decision — DOCX Quick Look").

**Section 9.4 is satisfied by (b):** a deliberate product path exists, is mounted, is tested, and
the decision not to mount `QuickLook` is recorded with the evidence above.

---

## 3. Tests run (exact counts)

| Suite | Command | Result |
|---|---|---|
| Solution build | `dotnet build DocumentIDE.sln` | 0 errors / 158 warnings |
| Desktop host surfaces | `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~DocumentIDE.IntegrationTests.Desktop"` | **20 / 20 passed**, 0 skipped |
| Full integration project | `dotnet test tests/DocumentIDE.IntegrationTests` | see note below |
| Web unit/component | `npx vitest run` (baseline 374/53 → **385/55**) | all passed |
| Web types | `npx tsc -b` | exit 0 |
| Web lint | `npx oxlint` | exit 0 |

The `Desktop` subset includes `PrivilegedSurfaceHostTests` (its `PdfSurface_IsReachable_AndReportsTypedStatuses`
case is the backend half of G8) and `AppShellSurfaceHostTests`, `BatchPreviewHostTests`,
`ShareLifecycleHostTests` — all pass unchanged, confirming the host allow-list and guard are
intact.

**Full `DocumentIDE.IntegrationTests` note (3 failures, none caused by this lane):**

1. `ScriptTests.Release_Build_Embeds_Production_Web_Assets` — environmental: `artifacts/desktop/DocumentIDE.Desktop.exe` is absent in a fresh worktree (no release build was run). **Pre-existing / named.**
2. `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — environmental: `artifacts/installer/DocumentIDE-Setup.msi` is absent for the same reason. **Pre-existing / named.**
3. `SourceControl.GitWorktreeMutationQueueTests.EnqueueAsync_CancelledWhileQueued_OperationNeverRuns` — **unrelated flake**, not one of the two known failures and not caused by this frontend lane (it exercises the git worktree mutation queue). It passes deterministically in isolation
   (`--filter "FullyQualifiedName~GitWorktreeMutationQueueTests"` → **7/7 passed**) and produced no
   change in this lane's scope. Reported here rather than hidden.

New tests added by this lane: **11** (4 `pdfApi` + 5 `PdfExportPanel` + 2 `App.shell`).

---

## 4. Residual risks / honest limits

1. **WORD-04 `QuickLook` is still unimplemented in the product.** This lane records the deliberate
   decision and proves the alternative path; it does **not** implement the high-fidelity overlay.
   Closing WORD-04 requires a page-render route (Word → temp PDF → images) on the privileged host
   and a DI registration for `IQuickLookRenderer`. Until then, "Quick Look" as a feature is not
   claimable.
2. **No incremental batch/merge progress.** The PDF routes are synchronous, so per-file progress
   cannot be streamed. The mounted progress surface shows final counts and an `aria-live`
   "Generating PDFs…" status while the request is in flight — it does not fabricate a percentage.
3. **Output location is a text field, not an OS folder picker.** No folder-picker/dialog route
   exists for the privileged shell, so separate/merge output is a visible, editable, pre-filled
   path. This is honest but not the richest UX; a native picker is a future improvement.
4. **Single export defaults to the source folder** (PDF-01 engine behavior when no
   `outputDirectory` is supplied). This matches spec 09 and is surfaced in the UI hint.
5. **Share `allowPdfDownload` still returns `PdfArtifactUnavailable`** (`ShareLifecycleService`).
   The share UI is out of this lane's scope and already mounted; this lane does not claim PDF share
   artifacts work. Left as recorded in `docs/quality/v1-gap-fixes-backend.md`.

---

## 5. Review follow-up

Independent re-review returned **APPROVED WITH NOTES** (no blockers) with eight findings. All eight
were resolved; the fixes are confined to the same files this lane already touched and add no
features. Items 1–7 were driven Red → Green (the new assertion was run failing first).

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | minor | `PdfActions` passes `(decision, applyToRemaining)` but the container handler accepted only `decision`, so the visible *Apply choice to remaining collisions* checkbox had no effect. | **Honoured the flag.** `handleCollisionResolve` now takes `applyToRemaining`; when it is set, the decision is retained as the run's standing policy and `runSingleExport` reuses it to resolve the remaining collision of the run without re-prompting (a retained `Overwrite`/`Rename` survives into the follow-up engine call). New test: *"honours \"apply choice to remaining collisions\"…"*. |
| 2 | minor | The merge missing-source refusal interpolated the raw document id (`No source path available for ${id}.`) while single/separate used the display name. | `runMerge` resolves `id → selection.name` exactly like the other two paths. New test: *"names the offending document, not its raw id…"*. |
| 3 | minor | `progress`/`summary` were never reset when a single export started, so a stale 100% bar/summary from a previous separate/merge run lingered. | Added `beginRun()`, called at the start of **every** run (single/separate/merge button handlers); it clears error, summary, results, collisions, pending collision and progress. New test: *"clears the previous run's progress when a new export starts"*. |
| 4 | nit | A merge `OutputCollision` rendered `skipped` in the results but `failed = 1` in the progress bar. | Both now derive from one `outcomeForStatus(...)` value via `countsForOutcome(outcome)`. New test: *"reports a merge collision as skipped in both the results and progress"*. |
| 5 | nit | HTTP rejections surfaced the raw controller code (e.g. `SourceRequired`) with no human-readable text. | `pdfApi.PdfRequestError` now builds its message from `ownerMessageFor(code)`, which maps the known controller codes (`SourceRequired`, `OutputDirectoryAndSourcesRequired`, `OutputPathAndOrderedSourcesRequired`) to owner-readable text **and keeps the raw code** on `errorCode` and as a `(Code)` suffix. `PdfExportPanel.describeError` surfaces that message. New tests in `pdfApi.test.ts` (mapped + unmapped fallback) and `PdfExportPanel.test.tsx` (owner-readable alert). |
| 6 | nit | The `App.shell.test.tsx` mock matched `/api/pdf/export` by prefix, so it also shadowed `/api/pdf/export-batch`. | The mock now matches exact paths (`endsWith`) and the batch route has its own response branch. New shell test: *"G8: separate export reaches the batch route (the mock does not shadow it)"*, proven failing against the old prefix before the fix. |
| 7 | nit | The G8 selection was driven through `useCanvasStore.setSelection`, proving the flow only at store level. | The G8 test now performs a **real click** on the `Document: Report.docx` canvas card (the same card the G4 double-click test uses) and asserts the store reflects it before exporting. |
| 8 | nit | The shipped allow-list was cited as `WebAppHost.cs:160-171`; it is actually `157-172`. | Citation corrected to `WebAppHost.cs:157-172` in §Scope above. |

**Deliberate limitation (documented, not a hidden regression):** the new batch shell test still seeds
a two-document selection at store level, because `DocumentNode` exposes only plain-click selection
(`applyClick(..., additive = false)`) — there is no additive click/keyboard multi-select gesture to
drive two cards from the shell. The export gesture itself is a real button click; the mock-fidelity
assertion is what the test pins. A future canvas additive-selection gesture would let this become a
pure-gesture test.

**Review-follow-up verification (this branch):**

| Command | Result |
|---|---|
| `npx vitest run` (in `src/DocumentIDE.Web`) | **392 passed / 55 files** (pre-fix baseline on this branch: 385 / 55) |
| `npx tsc -b` | exit 0 |
| `npx oxlint` | exit 0 |
| `dotnet build DocumentIDE.sln` | 0 errors / 158 warnings |

New tests added by the review follow-up: **7 net** (1 remaining-collisions + 1 merge-name +
1 progress-reset + 1 merge-collision-consistency + 1 panel error-message in
`PdfExportPanel.test.tsx`; 1 net in `pdfApi.test.ts` — the raw-code assertion became a
human-message + raw-detail pair; 1 batch-route shell test in `App.shell.test.tsx`). The G8 shell test
was also strengthened in place (real card click replacing the store-seeded selection).
