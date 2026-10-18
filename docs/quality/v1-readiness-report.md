# V1 Readiness Report — REL-03

**Verdict:** GO

**Gate run:** `pwsh -File scripts/verify-release.ps1` · commit `aba47ecaf45bfb55c77cfd9ff68543ac0d33c3f7` · branch `integration/parallel-batches` · `workingTreeDirty=false` · **19/19 gates passed, 0 failed, 0 environment-gated** · packaged artifacts verified · machine report at `artifacts/release-verify/verification-report.json` (`verdict: PASSED`).

## 1. How this gate was performed

One release run on the final tree, not a collection of per-suite claims. The pipeline builds the solution,
runs the full backend suite, projects the named release-blocking gates from that single run's trx evidence,
runs the MarkdownPreview suite (which is deliberately outside the solution), typechecks/lints/tests/builds
the web app, executes the Word/COM suite on this Word-capable machine, then packages and verifies the
artifacts. Packaging is only invoked when every gate passes. Numbers below are from that run.

**Backend:** 1,782 tests across the run — IntegrationTests 1,063 · Core 473 · Security 162 · Golden 39 ·
Safety 24 · Architecture 12 · Performance 3 · Soak 6. Two further packaging tests run after packaging
(post-package gate). MarkdownPreview 21. Word/COM filter 77 across projects. **0 failed, 0 skipped.**
**Frontend:** 91 files / 637 tests, TypeScript clean, oxlint clean, production bundle 511 modules
(572.89 kB, 164.61 kB gzip) with `motion` bundled.

## 2. Spec 12 completion gates — mapping

Every gate below is `PASS` with evidence from the run above or from the named test.

### 2.1 Initial completion gates

| Locked gate (verbatim) | Status | Evidence |
|---|---|---|
| Filesystem remains source of truth. | PASS | Reviewed physical-operation API + containment (`PhysicalFileOperationsIntegrationTests`, `ReparsePointContainmentTests`); canvas drag never moves files; source mutation uses snapshot → temp → validate → atomic replace and fails closed when recovery cannot be created (`RecoveryStorageTests`, `SafeApplyTests`). |
| Named workspaces persist. | PASS | `WorkspaceRepository`/`FolderSourceRepository` + migrations, reopen and reconciliation tests (`StartupReconciliationTests`, `FolderSourceTests`); workspace bootstrap populates the shipped shell. |
| Canvas placement preserves spatial memory. | PASS | `CanvasPositionTests`, CAN-06 persistence/pinning, `usePersistedPosition`; positions are keyed by stable document id, not path. |
| Search is incremental/realtime. | PASS | `SearchController` (`GET /api/search`) is reachable from the desktop host (allow-list) and `SearchPanel` is mounted; `IncrementalIndexCoordinatorIntegrationTests`, `FtsIndexTests`, `ExactSearchTests`, plus search UI tests. |
| DOCX opens in Word. | PASS | `WordController` is on the desktop host allow-list; the Word/COM suite executed on this machine (77 Word-filtered tests, 0 failed); `openInWord` is the inspector's primary action on screens 05/06. |
| Markdown edits internally. | PASS | The editor is no longer a stub (no placeholder markers) and is mounted; `MarkdownEditor` + `MarkdownPreview` (21 tests, including the sanitizer suite) and the markdown round-trip tests. |
| Search & Replace is deterministic, recursive, previewable, undoable. | PASS | `POST {planId}/apply` and `POST {planId}/undo` exist and the apply control is enabled; engine/review/strict-mutation/undo verified by `SearchReplaceOperationTests`, `SafeApplyTests`, `BatchSafetyRegressionTests`, `UndoTests`; operation history is listed in screen 10 (`GET api/history/operations`). |
| 30-day recovery works. | PASS | `RecoveryStorageTests` (budget, 80% warning, fail-closed), `CrashRecoveryTests`, QA-07 startup repair in both hosts; undo reachable through the batch plan flow. |
| PDF single/batch/merge works. | PASS | `PdfController` exposes `export`, `export-batch`, `merge`, `merge-pdfs`; `PdfExportPanel` is mounted; `SinglePdfExportTests`, `BatchPdfExportTests`, `PdfMergeMemoryTests`. |
| Graph distinguishes explicit vs inferred. | PASS | Real rendering: `<svg>` edges, solid lines for explicit and `strokeDasharray="6 4"` for inferred, orthogonal `<path>`; scope controls and legend tested (`DocumentGraph` tests, `Qa06GraphPerformanceGate`). |
| Public share cannot mutate local files. | PASS | ShareHost serves GET-only viewer routes; `ShareHostDependencyTests`, `PublicShareAttackSurfaceTests`, `ShareWebSecurityTests`; QA-05 audit DONE. |
| Security suite passes. | PASS | SecurityGate 162/162 in this run; the gate additionally proves no release-blocking suite contains a skipped test. |
| Golden DOCX regression suite passes. | PASS | GoldenGate 39/39 (QA-01 corpus). |

### 2.2 Updated completion gates

| Locked gate (verbatim) | Status | Evidence |
|---|---|---|
| foreign browser origin cannot invoke privileged local API | PASS | `PrivilegedApiSecurityTests`, `LocalApiSecurityAuditTests`, host tests rejecting hostile Origin/Host on every route family, including the new SCM, history, context-pack and document-reference routes. |
| ShareHost is separate and has no privileged mutation/Word dependencies | PASS | Separate `DocumentIDE.ShareHost.exe`; `ShareHostDependencyTests`, `PublicShareAttackSurfaceTests`; packaged as its own executable. |
| Live Share never auto-expands membership | PASS | `LiveShareMembershipTests`; membership changes require an explicit owner action. |
| Word COM maximum concurrency is 1 | PASS | `WordAutomationQueueTests` (single STA worker). |
| SQLite WAL concurrency test passes | PASS | `SqliteConcurrencyTests` (WAL + busy_timeout, reads during writes). |
| destructive batch fails closed without recovery capacity | PASS | `RecoveryStorageTests` capacity fail-closed; `BatchSafetyRegressionTests`; QA-07. |
| hostile DOCX resource tests pass | PASS | `DocxResourceBudgetTests`, `DocxChallengerStressTests`, `DocxPreflightUnitTests` (ZIP budgets, DTD prohibited). |
| share CSP/token/rate-limit tests pass | PASS | `ShareWebSecurityTests`, `ShareTokenTests`, `DownloadPermissionTests`; SHR-08 + QA-05. |
| canvas/graph/PDF performance regressions pass | PASS | PerformanceGate 3/3; `Qa06GraphPerformanceGate`, `Qa06CanvasPerformanceGate`, `PdfMergeMemoryTests` (now in a non-parallel collection so its process-wide measurement is meaningful). |

## 3. Master-spec product acceptance

| Spec acceptance section | Status | Notes |
|---|---|---|
| §82 Core File Safety | PASS | No rewrite on open, drag never moves files, failed strict edit leaves the original valid, preview before apply, undo snapshot before mutation, Recycle Bin default. |
| §83 Workspace | PASS | Workspaces can be created, attached and loaded in the shipped shell; canvas bootstrap populates real documents. |
| §84 Canvas | PASS | Folder/file objects, picker, open-folder fan-out with real contained documents, positions persisted. |
| §85 DOCX | PASS | Inspect/index/open-in-Word/document-aware diff all reachable; QuickLook page rendering remains out of V1 by design (see residual risks). |
| §86 Markdown | PASS | Editing surface real and mounted; preview/sanitizer/wikilink/backlink tests pass; graph participation renders. |
| §87 Search | PASS | `/api/search` reachable, UI mounted, ranking/filters/fuzzy verified. |
| §88 Search & Replace | PASS | Apply/undo reachable, reviewed plan required, strict mutation and history listing. |
| §89 Graph | PASS | Explicit vs inferred rendering with legend; scope controls. |
| §90 Sharing | PASS | Create/stop/revoke reachable through the owner share surface; serving side token/CSP/rate-limit verified. |
| §97 Definition of Done | PASS | Every listed capability is deliverable end-to-end in the shipped desktop app. |

## 4. Former integration gaps — closed

- **G1 graph placeholder** — closed: real node/edge rendering with explicit vs inferred styling.
- **G2 batch apply/undo unreachable** — closed: apply and undo routes exist and the UI control is enabled.
- **G3 search unreachable** — closed: `SearchController` allowed in the desktop host; panel mounted.
- **G4 open-in-Word 404** — closed: `WordController` on the allow-list; Word suite executed.
- **G5 share lifecycle unwired** — closed: create/stop/revoke surface wired.
- **G6 workspace/canvas not populated** — closed: bootstrap populates the canvas from real workspace data.
- **G7 markdown editor stub** — closed: real editor, mounted.
- **G8 PDF unreachable** — closed: four PDF routes and a mounted panel.
- **G9 (bundle of the above)** — closed with the individual items above.

## 5. Scan results

### 5.1 Placeholder scan

No sprint-required placeholder markers remain in production code. `App.css` (dead) was removed; the
Markdown editor contains no `dummy` id, empty load, `console.log` save or "Simple textarea" marker. The one
intentional absence is Quick Look (WORD-04), which is reported as absent rather than stubbed, and a negative
test pins that absence.

### 5.2 Skipped-test scan

0 skipped tests in the release run, including the six release-blocking suites
(Security, Safety, Golden, Architecture, Performance, Soak). The gate enforces this itself: a suite with a
static skip, or a gate whose suite executes zero tests, fails the release.

### 5.3 Offline verification

The product is local-first: the app, index, search, PDF, share serving and source control all work against
local files and a local database. No feature requires a cloud account or network access; the only network
paths are explicit Git remote operations and public-share tunnel creation, both user-initiated. The release
run itself performs no network dependency for the core gates.

### 5.4 Spec-drift scan

Every claim above points at a route, a mounted surface or a named test. Two claims were deliberately
removed rather than softened: the design's unified `.docx` text diff (not implementable honestly — a
structural document-aware diff is shipped instead) and Quick Look (needs the Word → PDF → page-image
pipeline that no shipped host has).

## 6. Residual risks

These are implemented behaviours with a documented limitation — none blocks V1, and none is hidden.

| Risk | Handling |
|---|---|
| Quick Look (WORD-04) is not implemented | Reported as absent, not stubbed; negative test prevents a placeholder. Needs the Word → temp PDF → page-image pipeline. |
| Share viewer-side stop/revoke is not enforced cross-process | Token hash is persisted, but the in-memory token service means an already-open viewer may keep reading until its token expires; documented since SHR-08. |
| Operation-history listing is at-most-once | If the database write fails after a successful mutation, the failure surfaces as `409` and the operation is absent from the listing; the per-file JSONL audit still exists. Making it transactional needs an outbox. |
| SCM service registration exists in two places | `Program.cs` and `SourceControlServiceRegistration.AddSourceControlServices` both wire the SCM services (the latter is used by the desktop host). Consolidation is a recorded cleanup, not a behaviour risk. |
| History records predating HIST-04 are not backfilled | The listing shows operations recorded from now on; legacy undo still inserts a row from its real result. |
| Context-pack caps are tunable constants | 128 KiB/document, 512 KiB/pack, 100 ids/request; exported packs are copy-only because no file destination is defined. |
| Missing-document status is as fresh as the workspace bootstrap | A file removed after startup appears on the next reload. |
| Removing a document reference leaves an orphaned `search_content` row | Reference removal is DB-only by design and never touches the file; index cleanup is a follow-up. |
| Deleted-file discard bypasses the recovery snapshot | Deliberate: there is no current content to lose, and restore recreates the file; the outcome reports `recoveryAvailable=false` with the reason. |
| SCM read routes accept a caller-named repository path | Matches pre-existing SCM-03 behaviour on a privileged, session-guarded API; the *document* path is containment-checked. |
| Staging a `.git/…` path reports success with no index change | `.git` paths are now rejected on every document surface (diff/history/DOCX diff/restore); Git ignores its own directory for `add`, so nothing is staged. |
| Known package advisories | `HtmlSanitizer 8.0.601`, `AngleSharp 0.17.1` and `SQLitePCLRaw.lib.e_sqlite3 2.1.11` raise NuGet advisory warnings; upgrades need a dependency sprint with the sanitizer and SQLite suites re-run. |
| Public ShareHost is not an OS sandbox | Stated as risk reduction (same-user process isolation), never claimed as a sandbox; verified by the isolation ADR/tests. |

## 7. Conditions

All previously listed conditions to flip this verdict are satisfied: the shipped host serves every
controller the product needs (15 on the allow-list), every former gap has a route and a mounted surface, the
release pipeline passes end-to-end at the recorded commit, and the packaged artifacts are verified with
checksums in `artifacts/release-verify/verification-report.json`.
