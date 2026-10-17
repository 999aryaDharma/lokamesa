# V1 Readiness Report — REL-03

**Sprint:** REL-03 — V1 Readiness Gate
**Date:** 2026-10-16
**Audited revision:** `73d0790` (branch `chore/rel-03-v1-readiness-gate`) plus REL-03 artifacts only (this report, `tests/DocumentIDE.IntegrationTests/Scripts/V1ReadinessGateTests.cs`, SPRINT-STATUS/handoff). REL-03 changed no production code.
**Sources of truth used:** `docs/specs/00-master-product-technical-spec.md` (§3, §26–27, §31, §82–90, §97, §101), `docs/specs/11-testing-strategy.md` (§26–28), `docs/specs/12-implementation-plan.md` (Completion Gates), `docs/sprints/SPRINT-STATUS.md`, all sprint handoffs in `docs/sprints/handoffs/`, `docs/quality/performance-baseline.md`, and the implementation code/tests themselves.
**Verdict:** BLOCKED

> V1 is evidence-based, not feature-count based. The subsystem layers of this repository are extensively tested and the REL-02 release pipeline is fail-closed, but several locked product behaviors are unimplemented or unreachable in the shipped application (`DocumentIDE.Desktop.exe` + `DocumentIDE.ShareHost.exe`, the only executables published by `scripts/build-release.ps1`). Until the blockers in §5 are closed, V1 cannot be declared ready.

---

## 1. How this gate was performed

1. **Requirement inventory** — every locked requirement from master spec §82–90, §97, §101.1–101.16, spec 12 "Completion Gates" and "Updated Completion Gates", and testing strategy §26–28 was collected verbatim.
2. **Evidence mapping** — each requirement was mapped to (a) automated tests that exist and are exercised by REL-02's gate run, (b) code paths, or (c) an explicit gap with `file:line` citations.
3. **Scans** — placeholder scan, skipped-test scan, and spec-drift scan over `src/` and `tests/` (§6).
4. **Offline verification** — checked that the core app performs no outbound network calls and that network features are opt-in (§6.3).
5. **Risk review** — reviewed accepted residual risks/ADRs recorded in handoffs (§7).
6. **Honest verdict** — a `GO` verdict is only valid with zero `FAIL`/`UNVERIFIED` rows. That condition does not hold.

**Status vocabulary used in the mapping tables**

| Status | Meaning |
|---|---|
| `PASS` | Locked behavior implemented and verified by automated tests that pass in the REL-02 gate run (or equivalent deterministic test evidence). |
| `FAIL` | Locked behavior is unimplemented, or implemented but unreachable in the shipped product, or a required acceptance test is absent. |
| `UNVERIFIED` | Evidence is insufficient to confirm the locked behavior end-to-end; needs explicit follow-up verification. |
| `ACCEPTED-RISK` | Behavior is implemented; a documented residual risk was reviewed and accepted with a mitigation/limitation statement (see §7). |

---

## 2. Verdict and headline blockers

**BLOCKED** — 16 of 22 spec-12 completion gates are `PASS`; 6 are `FAIL` (search, DOCX open-in-Word, Markdown editing, batch apply/undo, PDF finalization, and graph rendering — §5 records exactly which layer is missing for each). The master-spec acceptance sections §83–§90 and §97 do not hold at product level.

The eight blockers that drive the verdict (details in §5):

1. **Graph view is a placeholder** — no node/edge rendering, no explicit-vs-inferred styling, no select/navigate/open document. GR-03 steps 1/3/4/5 are unmet (`src/DocumentIDE.Web/src/features/graph/DocumentGraph.tsx:8,13-17,22-31`).
2. **Batch Apply is unreachable** — the reviewed-mutation center of V1 is disabled in the UI ("Applying changes is not available yet", `BatchPreviewPanel.tsx:147`) and no apply/undo route exists in any host. Preview works; apply and undo cannot be performed by a user.
3. **"Open in Word" is unreachable in the shipped app** — the desktop host serves only `BatchPreviewController`; `/api/word/open` is 404 in the product (`WebAppHost.cs:56-63,138-141`; `BatchPreviewHostTests.cs:91-92`).
4. **Search is unreachable** — `/api/search` has no server implementation anywhere (only the fetch call in `searchStore.ts:73`), and `SearchPanel` is not mounted in the app shell.
5. **Share lifecycle is unreachable** — no share create/stop/revoke HTTP surface exists, and `ShareManager`/`ShareDialog` are callback-driven with no API wiring (admitted in `SHR-11-HANDOFF.md:45`).
6. **Workspace/canvas bootstrap is not wired** — the canvas store is never populated outside tests, so the shipped app shows an empty canvas and cannot attach folder sources.
7. **Markdown editing is a stub** — `MarkdownEditor.tsx` is a demo (`documentId = 'dummy'`, empty load, `console.log` save) and is not mounted.
8. **E2E administrative scenario test missing** — required by `docs/specs/11-testing-strategy.md` §20/§27; no test exercises the end-to-end flow.

---

## 3. Spec 12 completion gates — mapping

### 3.1 Initial completion gates (`docs/specs/12-implementation-plan.md` "Completion Gates")

| Locked gate (verbatim) | Status | Evidence / gap |
|---|---|---|
| Filesystem remains source of truth. | PASS | Physical operations go through reviewed APIs (`PhysicalFileOperationsController`), identity/move correlation (`FS-02/FS-05`) and containment (`FS-08`) are tested (`PhysicalFileOperationsIntegrationTests`, `MoveCorrelationIntegrationTests`, `ReparsePointContainmentTests`). No canvas drag moves files (`CAN` tests). REL-02 gate run 19/19. |
| Named workspaces persist. | PASS | SQLite repositories + migrations (`WorkspaceRepository`, `FolderSourceRepository`); persistence/reopen/reconciliation tests (`StartupReconciliationTests`, `FolderSourceTests`). |
| Canvas placement preserves spatial memory. | PASS | `CanvasPositionTests`, `CAN-06` persistence/pinning tests, `usePersistedPosition` tests. |
| Search is incremental/realtime. | FAIL | Backend verified (`IncrementalIndexCoordinatorIntegrationTests`, `FtsIndexTests`, `ExactSearchTests`), but there is **no `/api/search` server endpoint** and the search UI is not mounted (gap G3). |
| DOCX opens in Word. | FAIL | `WordController` + `WordDesktopLauncherTests` pass against the `DocumentIDE.Api` host, but the shipped desktop host excludes the route (404 asserted in `BatchPreviewHostTests.cs:91`) (gap G4). |
| Markdown edits internally. | FAIL | `MarkdownEditor.tsx:4-30` is a placeholder (`documentId = 'dummy'`, empty load, `console.log` save, "Simple textarea for now"); not mounted anywhere (gap G7). |
| Search & Replace is deterministic, recursive, previewable, undoable. | FAIL | Engine, occurrence review, strict mutation and undo are verified at service level (`SearchReplaceOperationTests`, `SafeApplyTests`, `BatchSafetyRegressionTests`, `UndoTests`), but Apply is disabled in the product UI (`BatchPreviewPanel.tsx:147`) and no apply/undo route exists (gap G2). |
| 30-day recovery works. | PASS | `RecoveryStorageTests` (budget 5 GiB, 80% warning, fail-closed capacity), `CrashRecoveryTests`, `UndoTests`, QA-07 startup repair in both hosts. Undo *reachability* is blocked via gap G2. |
| PDF single/batch/merge works. | FAIL | Engines verified (`SinglePdfExportTests`, `BatchPdfExportTests`, `PdfMergeMemoryTests`), but no PDF route exists in any host and `PdfActions` is explicitly presentational (`PDF-04-HANDOFF.md:98-101`); not mounted (gap G8). |
| Graph distinguishes explicit vs inferred. | FAIL | No edge rendering exists at all; §27.2 truthful semantics unimplemented in the view (gap G1). |
| Public share cannot mutate local files. | PASS | ShareHost exposes GET-only viewer routes; isolation and dependency tests (`ShareHostDependencyTests`, `PublicShareAttackSurfaceTests`, `ShareWebSecurityTests`); QA-05 audit DONE. |
| Security suite passes. | PASS | REL-02 "SecurityGate" 91 tests pass; this sprint adds an automated guard that no release-blocking suite contains a skipped test (`NoSkippedTests_InReleaseBlockingSuites`). |
| Golden DOCX regression suite passes. | PASS | `DocxGoldenTests` + golden corpus (QA-01) in the REL-02 gate run. |

### 3.2 Updated completion gates (`docs/specs/12-implementation-plan.md` "Updated Completion Gates")

| Locked gate (verbatim) | Status | Evidence |
|---|---|---|
| foreign browser origin cannot invoke privileged local API | PASS | `PrivilegedApiSecurityTests`, `LocalApiSecurityAuditTests`, `BatchPreviewHostTests` (hostile Origin/Host rejected); QA-03/QA-05. |
| ShareHost is separate and has no privileged mutation/Word dependencies | PASS | Separate `DocumentIDE.ShareHost.exe`; `ShareHostDependencyTests`, `PublicShareAttackSurfaceTests` ("does not contain DocumentIDE.Api"); REL-01 packaging. |
| Live Share never auto-expands membership | PASS | `LiveShareMembershipTests`; SHR-05 content-frozen semantics. |
| Word COM maximum concurrency is 1 | PASS | `WordAutomationQueueTests` (single STA worker queue); WORD-03. |
| SQLite WAL concurrency test passes | PASS | `SqliteConcurrencyTests` (WAL + busy_timeout; reads during writes). |
| destructive batch fails closed without recovery capacity | PASS | `RecoveryStorageTests` capacity fail-closed; `BatchSafetyRegressionTests`; QA-07. |
| hostile DOCX resource tests pass | PASS | `DocxResourceBudgetTests`, `DocxChallengerStressTests` (IDX-01; ZIP budgets, DTD prohibited, skip-for-safety). |
| share CSP/token/rate-limit tests pass | PASS | `ShareWebSecurityTests`, `ShareTokenTests`, `DownloadPermissionTests`; SHR-08 + QA-05. |
| canvas/graph/PDF performance regressions pass | PASS | QA-06 benchmark gate (`Qa06GraphPerformanceGate.test.ts`, `Qa06CanvasPerformanceGate.test.ts`, `PdfMergeMemoryTests`, `docs/quality/performance-baseline.md`). The graph helpers are benchmarked in isolation (not yet wired — see G1). |

---

## 4. Master-spec product acceptance — mapping

| Spec acceptance section | Status | Notes (gap reference) |
|---|---|---|
| §82 Acceptance Criteria — Core File Safety | PASS | No rewrite on open, drag never moves files, failed strict edit leaves original valid, preview before apply (engine), undo snapshot before mutation, Recycle Bin default, Word conflict handling — all covered by safety/Security suites. |
| §83 Acceptance Criteria — Workspace | FAIL | Subsystems pass; product cannot create/attach/load a workspace (gap G6). |
| §84 Acceptance Criteria — Canvas | FAIL | Canvas component/persistence tested; shipped shell never populates the canvas (`setObjects`/`upsertObject` called only from tests), so folder objects/picker/open-all/positions cannot be exercised (gap G6). |
| §85 Acceptance Criteria — DOCX | FAIL | Inspect/index/QuickLook/mutation tested at service level; open-in-Word 404 in the shipped host, QuickLook not mounted (gap G4). |
| §86 Acceptance Criteria — Markdown | FAIL | Parser/preview/sanitizer/wikilink/backlink tests pass; the editing surface is a stub and unmounted; "graph participation" blocked by G1 (gap G7). |
| §87 Acceptance Criteria — Search | FAIL | Backend query/ranking/filters/fuzzy tested; no endpoint, UI unmounted (gap G3). |
| §88 Acceptance Criteria — Search & Replace | FAIL | Scope/matching/runs/review/strict mutation/safe write/30-day record tested; product Apply disabled, no apply route, history UI unmounted (gap G2). |
| §89 Acceptance Criteria — Graph | FAIL | View switch works; the graph view renders no relations (gap G1). |
| §90 Acceptance Criteria — Sharing | FAIL | Serving side verified (token/CSP/rate limits/scoping); create Snapshot/Live, stop/revoke have no product path (gap G5). |
| §97 Definition of Done for Product Foundation | FAIL | "local filesystem authoritative" holds; "named workspaces work", "search is real-time", "DOCX opens in Word", "Markdown edits internally", "strict batch mutation safe and previewable" (apply), "PDF output works", "graph supports knowledge workflow", "sharing read-only" are not deliverable end-to-end in the shipped app. |

---

## 5. Integration gaps (blocker detail)

Each gap lists the locked requirement, the evidence of absence, and the sprint layer that owns the fix. REL-03 does not schedule or implement these.

**G1 — Graph view is a placeholder (blocks §89, §27.2, spec-12 gate "Graph distinguishes explicit vs inferred", testing-strategy §18).**
`DocumentGraph.tsx:8` ("Simple Graph view component placeholder") renders only `positions` nodes as text (`:22-31`) with a dummy position-shift click (`:13-17`); no edges, no explicit/inferred distinction, no select/navigate/open, no relation/filter surface. GR-03's own required tests ("Explicit edge is solid", "Double-click Markdown node opens editor") do not exist; the GR-03 handoff claims graph implementation but the component is unchanged apart from canvas decoupling. GR-05 bounded rendering (`graphLayoutWorker.ts`, `GraphScopeControls.tsx`) and AI-04 semantic suggestions are imported only by tests (`GraphPerformance.test.ts:2-3`, `Qa06GraphPerformanceGate.test.ts:2-4`), not by any shipped component.

**G2 — Batch apply/undo unreachable (blocks §88 and the "undoable" part of the S&R gate).**
`BatchPreviewPanel.tsx:147`: `<button disabled title="Applying changes is not available yet">Apply</button>` plus "Preview and review only." No `/apply` (or undo) route exists — `BatchPreviewController` exposes only workspaces/scope-options/preview/get/selection/cancel; `BatchApplyEngine` and history are exercised only by tests. `HistoryPanel` (BAT-11) is presentational and unmounted ("No API/DI wiring (downstream owns it)", `BAT-11-HANDOFF.md:18`).

**G3 — Search unreachable (blocks §87 and the search gate).**
`searchStore.ts:73` fetches `/api/search`, but no server route implements it anywhere in `src/` (grep: only this fetch + `docs/specs/01-system-architecture.md:324`). `SearchPanel.tsx` is imported only by its own test; `App.tsx:34-38` mounts only canvas/list/graph + batch panel. SRCH-05 handoff records "Risks discovered: None."

**G4 — Open in Word / QuickLook unreachable in shipped host (blocks §85 and the DOCX gate).**
The desktop `WebAppHost` removes all default controller providers and registers only `BatchPreviewController` (`WebAppHost.cs:56-63,138-141`); `BatchPreviewHostTests.cs:91-92` asserts 404 for `/api/word/open` and `/api/batch-operations/{planId}/apply` in the shipped host. The frontend calls the route (`OpenInWordAction.ts:29`) and `DocumentCanvas.tsx:485` double-click routes every document there. `QuickLook` (WORD-04) is imported only by its own test. The `DocumentIDE.Api` host serves the routes but is never published by `scripts/build-release.ps1` (only Desktop + ShareHost).

**G5 — Share lifecycle unreachable (blocks §90 create/stop/revoke).**
No create/revoke/member-management endpoint exists: ShareHost maps only GET viewer routes (`Program.cs:39-270`); `DocumentIDE.Api` has no share controller. `ShareManager`/`ShareDialog` are imported only by their own tests; `SHR-11-HANDOFF.md:45` states the callbacks "must" be wired to the privileged API — "until then the UI proves intent but performs no real share lifecycle."

**G6 — Workspace attach and canvas bootstrap unreachable (blocks §83/§84).**
`canvasStore.setObjects`/`upsertObject` are called only from tests; nothing loads workspace documents into the shell. `DocumentCanvas` is mounted without `folderFiles`/`onReleaseDocuments` (`App.tsx:34-36`), so folder picker/open-all cannot work; `AddFolderSource` is unmounted and its endpoint is not hosted in the shipped desktop app. `usePersistedPosition`/`SelectionController` (CAN-06/07) are likewise referenced only by tests.

**G7 — Markdown editing is a stub (blocks §86 "source editing" and the Markdown gate).**
`MarkdownEditor.tsx:4-5` ("in the real implementation CodeMirror 6 will be used"), `:9` (`documentId = 'dummy'`), `:17` (empty source), `:30` (`console.log` save). MD-01's handoff claiming implemented editing does not match the artifact. No other editor surface exists.

**G8 — PDF finalization unreachable (blocks the PDF gate).**
PDF engines are verified and PDF-04 built presentational UX, but no PDF route exists in any host and the PDF-04 handoff states the backend wiring "is left to the app shell / REL-03 consumer" (`PDF-04-HANDOFF.md:98-101`); `PdfActions` is not mounted.

**G9 — E2E administrative workflow test missing (blocks testing-strategy §27 "E2E admin workflow passes").**
Required scenario in `docs/specs/11-testing-strategy.md` §20 has no automated equivalent; the only `EndToEnd_*` tests are unrelated (indexing lifecycle, backlinks). Without it, §26 release-gate behaviors (stale plan can't apply, undo snapshot presence, etc.) are verified only per-subsystem.

---

## 6. Scan results (sprint step 2) and offline verification (step 4)

### 6.1 Placeholder scan

Command: `git grep -n -i -E "TODO|FIXME|NotImplementedException|placeholder" -- src ":(exclude)*/node_modules/*" ":(exclude)*/dist/*"`

Live placeholders that matter (unresolved sprint-required markers):

| Location | Finding | Impact |
|---|---|---|
| `src/DocumentIDE.Web/src/features/graph/DocumentGraph.tsx:8` | "Simple Graph view component placeholder" | Blocking (G1). |
| `src/DocumentIDE.Web/src/features/markdown/MarkdownEditor.tsx:4,17` | "Placeholder imports – … CodeMirror 6"; "Placeholder: empty source." | Blocking (G7). |
| `src/DocumentIDE.ShareHost/ShareTokenMiddleware.cs:9-11` | Stale doc comment: "Minimal middleware placeholder … simply forwards the request" — the class now validates tokens and fails closed without a service. | Documentation drift, non-blocking by itself; must not be quoted as the security design (that is `Program.cs` + QA-05). |

Benign hits reviewed and dismissed: HTML `placeholder=` attributes (`FolderPicker.tsx:140`, `DocumentList.tsx:203`, `SearchPanel.tsx:139`, `AddFolderSource.tsx:111`, `ContextPackDialog.tsx:139`, `LocateMovedFileDialog.tsx:207`), the `DocumentNode.tsx:53` "Title bar placeholder" comment, and test file text ("renders missing file placeholder", "routes Graph to the existing graph placeholder").

No `TODO`/`FIXME`/`NotImplementedException` hits in `src/`.

### 6.2 Skipped-test scan

Command: `git grep -n -E "Skip\s*=|Ignore\(|\.skip\(|it\.skip|xit\(|xdescribe\(|describe\.skip|\.todo\(" -- src tests` (frontend + backend).

Result: **zero** skipped tests. All matches in the raw output were false positives (`WaitForExit(`). No `[Fact(Skip=`, `[Theory(Skip=`, `it.skip`, `xit`, `describe.skip` or `.todo` exists in `src/` or `tests/`. This is now enforced continuously by `V1ReadinessGateTests.NoSkippedTests_InReleaseBlockingSuites`, which scans `SecurityTests`, `SafetyTests`, `GoldenTests`, `ArchitectureTests`, `PerformanceTests`, `SoakTests` for skip attributes. **No skipped safety/security gate.**

### 6.3 Offline verification

- No outbound network calls in core product code: `git grep` over `src/` shows only loopback (`127.0.0.1`), OOXML namespaces, and the local virtual host `https://appassets.html2sav` (WebView2 asset mapping). `index.html` loads no remote assets.
- Sharing transport is strictly opt-in: ngrok runs only through `NgrokTransport` after an explicit user action (SHR-09, tests `NgrokTransportTests`).
- External embedding provider is off by default (`AppSettings.EnableExternalEmbeddingProvider = false`, `AppSettings.cs:11`) and blocked by `EmbeddingCoordinator.cs:65,136`; local provider is the default (AI-02).
- Share viewer surface ships `default-src 'none'`-based CSP and sanitization (SHR-08; `ShareWebSecurityTests`).
- `scripts/verify-release.ps1` runs the full verification chain locally (REL-02, 19/19 gates, commit `ebdb248`).

### 6.4 Spec-drift scan (claims vs artifacts)

| Claim | Reality |
|---|---|
| GR-03 handoff: "graph view drag and node selection" implementation; status DONE | `DocumentGraph.tsx` is a placeholder; no edges/navigation (G1). |
| MD-01 handoff: "source-first markdown editing with autosave, manual save, change detection" | Demo stub, `console.log` save, not mounted (G7). |
| SHR-11 handoff: owner share management DONE | Callbacks unwired; no API endpoints; not mounted (G5). |
| BAT-11 handoff: batch results/history UI | Presentational only; explicitly "No API/DI wiring" (`:18`). |
| SRCH-05 / AI-04 handoffs: "Risks discovered: None." | Search endpoint missing; graph helpers unwired (G1/G3). |
| PDF-04 handoff: PDF UX DONE | Presentational; wiring left to app shell (`:98-101`) (G8). |

These are not spec conflicts by themselves (each handoff disclaims wiring somewhere), but they prove that "sprint DONE" in this repository currently does **not** imply product reachability, which is exactly why this gate exists.

---

## 7. Residual risks reviewed (sprint step 3)

Accepted / documented risks (must remain visible):

| Risk | Source | Handling |
|---|---|---|
| ShareHost process separation is risk reduction, not an OS sandbox (same-user full-code-execution) | `101.2`, SHR-10 handoff, QA-05 | Documented wording verified; not claimed as sandbox. `ACCEPTED-RISK`. |
| Failed-rename move-intent TTL window | QA-04 handoff | Documented, bounded; retest in QA-04 suite. `ACCEPTED-RISK`. |
| No compression-ratio budget in DOCX preflight (declared limits only) | QA-01/IDX-01 handoffs | Declared budgets + budgeted parse; ratio follow-up noted. `ACCEPTED-RISK`. |
| Orphan recovery-snapshot directories after crashes | QA-07 handoff | Startup repair handles plans/temps; snapshot-dir leak reported, no silent eviction. `ACCEPTED-RISK`. |
| `scripts/verify.ps1` (FND-08) blind spots (MarkdownPreview suite not in sln) | REL-02 handoff | `verify-release.ps1` is the release command and runs the suite explicitly. `ACCEPTED-RISK`. |
| IntegrationTests parallel-run SQLitePCL flake (`ObjectDisposedException: SQLitePCL.sqlite3`) | REL-02-era runs | Known flake, unrelated to this sprint; rerun on occurrence. `ACCEPTED-RISK`. |
| API-host integration tests depend on local `%LOCALAPPDATA%` DB state | observed in test infra | Tests seed temp DBs where it matters; noted for CI hygiene. `UNVERIFIED` at infra level only (not a product behavior). |
| Word COM tests require a licensed Word-capable runner | REL-02 handoff | Release workflow pinned to capable self-hosted runner; explicit justified skip only. `ACCEPTED-RISK`. |

---

## 8. Verified subsystem evidence catalog (what is genuinely done)

For balance, the following locked areas are implemented and carry automated test evidence (independent of the reachability gaps above):

- **File safety / strict mutation:** safe-write + atomic replace, snapshot-before-mutation, fail-closed capacity, recovery/undo/conflict policy (`HIST-01/02`, `BAT-04`, QA-04/QA-07, SafetyTests).
- **Batch engines:** S&R exact + regex, fonts, page layout, rename, occurrence review, results/history view-models (`BAT-05..11`, `DOCX-02/03`).
- **Identity & reconciliation:** fingerprint identity, rename/move correlation, missing-file recovery, watcher, startup reconciliation (`FS-02..FS-08`).
- **Containment:** reparse-point/junction containment, basename-only rename, cross-volume ambiguity defaults (`FS-07/08`, QA-04 challengers).
- **Security:** privileged API guard (Origin/Host/session secret), ShareHost separation, share token 256-bit + fixed-time verify, CSP/sanitizer, rate limits, download permissions (`QA-03/05`, SecurityTests).
- **Indexing/search/semantic:** FTS5, incremental scheduler, query parser, ranked/fuzzy/metadata search, embeddings abstraction, local provider, semantic mode, context packs (`IDX-05/06`, `SRCH-01..05`, `AI-01..04`, `CTX-01..04`).
- **DOCX/Word:** OOXML budget/pipeline, logical run map, Word STA queue (concurrency 1), lock detection, golden corpus (`IDX-01/02`, `DOCX-01..04`, `WORD-01..03`, QA-01).
- **Markdown:** structural parser, source-first save semantics (backend), preview/sanitization, frontmatter/wikilinks/backlinks (`IDX-03`, `MD-01..04`).
- **PDF:** single/batch/streaming merge with bounded memory (`PDF-01..03`).
- **Graph data layer:** relation store, explicit links, backlinks, manual relations, semantic relations, truthful data classification (`GR-01..04`, `SHR-06`).
- **Release engineering:** fail-closed `verify-release.ps1`, MSI packaging, checksums, commit-pinned verification report (REL-01/REL-02).

---

## 9. Conditions to flip this verdict to GO

1. Implement graph view rendering per GR-03 §Steps 1/3/4/5 + §27.2 semantics (solid explicit/dashed semantic), including its required tests.
2. Wire batch Apply + history to the privileged API (with the reviewed apply engine), enable the UI affordance, and prove stale-plan rejection + undo end-to-end.
3. Implement `/api/search` (privileged host) and mount the search UI in the app shell.
4. Expand the shipped desktop host surface to the required privileged routes (Word open, folder sources, rebind, etc.) with the FND-02/QA-03 guard intact, or provide an equivalent authorized product path; keep `BatchPreviewHostTests`-style 404 expectations updated deliberately.
5. Wire the share lifecycle (create Snapshot/Live, stop/revoke, membership) through authorized endpoints + mounted UI.
6. Wire workspace/canvas bootstrap (initial workspace load, folder attach, document placement) so the canvas is a live part of the product.
7. Replace the Markdown editor stub with the real editing surface and mount it.
8. Wire PDF finalization to its engines and mount the UX.
9. Add the E2E administrative scenario test (spec 11 §20) and the E2E programmer scenario where feasible (spec 11 §21, context packs staged).
10. Re-run `scripts/verify-release.ps1` and re-issue this report with a `GO`/`BLOCKED` verdict based purely on the new evidence.

Until then, V1 is **BLOCKED**. The tested subsystem core is substantial, and this report does not dispute it — but locked product behavior that a user cannot reach is not evidence of V1 readiness.

---

*Artifacts: `tests/DocumentIDE.IntegrationTests/Scripts/V1ReadinessGateTests.cs` enforces this report's existence, single-verdict consistency (GO ⇒ no FAIL/UNVERIFIED rows; BLOCKED ⇒ itemized blockers), verbatim coverage of all 22 spec-12 completion gates, scan/offline/risk evidence sections, and the no-skipped-tests rule for release-blocking suites.*

---

## 10. Re-review 2026-10-17 (post Lane B + Lane C + SCM-07 merges)

Verdict unchanged: **BLOCKED**. Blocker set is now much smaller than §5, but two §9 conditions are
not yet satisfied. Evidence was re-derived from the artifacts on `integration/parallel-batches`
(HEAD `0c4ff6a`, release gate REL-02 PASSED at `0c4ff6a676946b2714a75f25ccfcdaf5f3fcecfd`).

| Gap | Status | Evidence |
| --- | --- | --- |
| G1 graph placeholder | **CLOSED** | `features/graph/DocumentGraph.tsx` is a real view (explicit edges SOLID, inferred/semantic DASHED, top-K capped via `filterSemanticEdgesTopK`), mounted at `App.tsx:221`; `DocumentGraph.test.tsx` `TruthfulEdgeSemantics` present. |
| G2 batch apply/undo unreachable | **CLOSED** | `BatchPreviewController` exposes `POST {planId}/apply`, `POST {planId}/undo`, `GET {planId}/history`; `BatchPreviewPanel.tsx` has a real `apply()` path with `canApply` (the "not available yet" disabled button is gone). |
| G3 search unreachable | **CLOSED** | `SearchController` served by the privileged API and registered in the shipped host (`WebAppHost.cs:163`); `SearchPanel` mounted at `App.tsx:200`. |
| G4 Word/QuickLook unreachable in shipped host | **PARTIALLY CLOSED** | `WebAppHost.cs` now registers the privileged surfaces deliberately (`:160-171`: Word, Search, Pdf, Graph, Markdown + folder sources via `PrivilegedSurfaceRegistration.cs`). The QuickLook overlay is still imported only by its own test. |
| G5 share lifecycle unreachable | **CLOSED** | `ShareController` + `ShareLifecycleService` (create/list/get/stop/revoke/regenerate/members); `SharePanel` mounted at `App.tsx:35`. **Caveat carried from the lane report:** ShareHost viewer-side stop/revoke is not enforced cross-process (in-memory `ShareTokenService` vs persisted `TokenHash`) — link revocation must not be claimed end-to-end before SHR-09. |
| G6 workspace/canvas bootstrap unreachable | **CLOSED** | `workspaceBootstrap` + `fetchWorkspaceDocuments`/`fetchFolderSources` (`App.tsx:39-40`) populate the canvas store on startup; `GET /api/workspaces/{id}/documents` added; `AddFolderSource` imported. |
| G7 markdown editor stub | **CLOSED** | `MarkdownEditor` mounted at `App.tsx:253`; `MarkdownDocumentController` (`GET/PUT /api/documents/{id}/markdown`) served by the host. |
| G8 PDF finalization unreachable | **OPEN (reduced)** | The privileged route now exists (`PdfController`, registered `WebAppHost.cs:164`) and is exercised by tests, so the backend half of §9 condition 8 is done. But `features/pdf/PdfActions.tsx` is still imported only by its own test — the UX is **not mounted**, and §9 condition 8 explicitly requires the mount. |
| G9 E2E administrative workflow test missing | **CLOSED** | `tests/DocumentIDE.IntegrationTests/EndToEnd/AdministrativeWorkflowE2ETests.cs` (2/2 passing in the lane run). |

### Remaining conditions before this verdict can become GO

1. §9.8 — mount the PDF UX (`PdfActions`, plus a progress surface) in the app shell against the now-live route.
2. §9.4 — finish the shipped-host expansion decision for QuickLook (either mount it or record the deliberate
   product decision that double-click opens Word instead).
3. Re-run `scripts/verify-release.ps1` after those land and re-issue this report.

This addendum is evidence, not a schedule. It does not itself change the verdict.
