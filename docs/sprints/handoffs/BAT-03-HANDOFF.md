# Sprint Handoff — BAT-03 — Batch Preview API & Review Shell

**Status:** DONE
**Branch:** `feat/bat-03-batch-preview-api-review-shell`
**Commits:** `2d42b5e`..`4899761` (implementation, fixes, ADR, plan docs; completion metadata in the following docs commit.)

## What changed
- Added queued preview endpoint `POST /api/batch-operations/preview` with a progress job, `GET /api/batch-operations/{planId}` polling DTO, `PUT .../selection`, `POST .../cancel`, plus workspace and trusted scope-option listing endpoints (shared camelCase wire contract in `docs/plans/BAT-03-parallel-implementation.md`).
- Preview renders files scanned/affected/occurrence counts and the body/table/header/footer breakdown from exact ordinal matching (DOCX logical matching reuses `LogicalTextMatcher` with mandatory package preflight); markdown uses ordinal counts.
- Reachable `BatchPreviewPanel` review shell with explicit Selection/Folder/Workspace/Collection scopes, named file checkboxes and collection select (internal IDs kept out of product forms), per-file/per-occurrence selection persisted only through an explicit Save selection action, typed failure display, progress/error/cancel, and stale-prompt re-preview.
- Apply stays disabled: no apply endpoint or destructive Replace All path exists; BAT-04 owns source mutation.
- Desktop `WebAppHost` exposes only `BatchPreviewController` behind the existing `PrivilegedRequestGuard` with a temp-AppDb test seam; `PrivilegedRequestGuard` permits cross-site Fetch Metadata only for the exact native origin `https://appassets.html2sav` while retaining loopback Host validation, Origin validation and per-request startup-secret checks (`docs/decisions/ADR-001-native-app-fetch-metadata.md`).
- Persistence: additive migration `010_Collections` (workspace-indexed, cascade foreign keys) for trusted collection membership snapshots; plans/selections persisted in `BatchOperationPlan` files with fingerprints.
- Final-review fixes: non-Folder scopes no longer show/send exclusion or recursion fields and the API boundary normalizes them so a resolved DTO cannot claim enforcement that never happens; closing the panel during the initial preview POST now retains creation ownership and cancels the late-returned plan exactly once (no orphaned server scan).

## Files changed
- `docs/decisions/ADR-001-native-app-fetch-metadata.md`
- `docs/plans/BAT-03-parallel-implementation.md`
- `src/DocumentIDE.Api/BatchPreviewRegistration.cs`
- `src/DocumentIDE.Api/Controllers/BatchPreviewController.cs`
- `src/DocumentIDE.Api/Program.cs`
- `src/DocumentIDE.Api/Security/PrivilegedRequestGuard.cs`
- `src/DocumentIDE.Core/Batch/BatchPreview.cs`
- `src/DocumentIDE.Core/Docx/LogicalTextMatcher.cs`
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs`
- `src/DocumentIDE.Infrastructure/Batch/BatchCollectionReader.cs`
- `src/DocumentIDE.Infrastructure/Batch/BatchPreviewCoordinator.cs`
- `src/DocumentIDE.Infrastructure/Batch/BatchPreviewService.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/010_Collections.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `src/DocumentIDE.Web/src/App.tsx`
- `src/DocumentIDE.Web/src/features/batch/BatchApp.test.tsx`
- `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.test.tsx`
- `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.tsx`
- `src/DocumentIDE.Web/src/features/batch/batchApi.ts`
- `tests/DocumentIDE.Core.Tests/Docx/LogicalTextMatcherTests.cs`
- `tests/DocumentIDE.IntegrationTests/Batch/BatchPreviewTests.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/BatchPreviewHostTests.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/WebAppHostTests.cs`

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter FullyQualifiedName~BatchPreview` → 15 passed, 0 skipped (final fix; 14 before).
- `npm test` in `src/DocumentIDE.Web` → 18 files / 80 tests passed, 0 skipped (final fix; 78 before); `npm run build` → PASS.
- `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped (explicit invocation; solution configuration omits this project).
- Controller full verification on `14108e1`: `scripts/verify.ps1` PASS — build + 428 Core + 172 Integration + 7 Architecture + 78 Web tests + Web build; SecurityTests 52 PASS; none skipped; logs `artifacts/bat-03-final-verification.log`, `artifacts/bat-03-final-security.log`.
- Post-fix `scripts/verify.ps1` rerun over `4899761` → PASS: build, 428 Core + 173 Integration + 7 Architecture tests, Web build, 80 Web tests, 0 skipped; log `artifacts/bat-03-postfix-verify.log`.
- Post-fix explicit `dotnet test tests/DocumentIDE.SecurityTests/...` → 52 passed, 0 skipped; log `artifacts/bat-03-postfix-security.log`.
- TDD evidence: per-task RED/GREEN logs in `.superpowers/sdd/BAT-03-parallel-implementation/task-{1..4}-report.md` and `final-fix-report.md`; final whole-branch review (`final-review-report.md`) returned 0 Critical / 2 Important / 0 Minor; scoped re-review of `4899761` verdicted both ADDRESSED with no new breakage.

## Acceptance criteria
- [x] No direct destructive 'Replace All' path bypasses plan — no apply/mutation endpoint exists in the branch; preview paths are read-only and byte-fingerprinted; scoped re-review confirmed the fix introduced none.
- [x] Preview performs no source mutation — `BatchPreviewTests.Preview_PerformsNoSourceMutation_AndPersistsFingerprintAndExactOccurrences` (RED first, then GREEN) plus byte-identical corpus assertions.
- [x] Counts match fixture corpus — `Preview_DocxCorpus_CrossRunsAndAllOrigins_AndInvalidPackageRemainInspectable`, markdown ordinal count tests, breakdown assertions.
- [x] Apply button unavailable until preview completes — `BatchPreviewPanel.test.tsx` Apply-disabled cases (always disabled this sprint: BAT-04 supplies apply).

## Security & file-safety gate
- [x] No user file mutated through an unreviewed/bypass path — all preview/selection flows are read-only; no apply route exists.
- [x] No arbitrary path or secret exposed — responses carry document IDs and sanitized typed codes only; workspace-scoped membership enforced by resolver and collection reads.
- [x] Dependency/security boundaries intact — privileged guard retains Host/Origin/startup-secret checks; the exact native-origin exception is recorded in ADR-001 with denial tests; no ShareHost or Word-concurrency change.
- [x] No destructive action silently broadened its scope — exclusion/recursion fields are normalized at the API boundary for non-Folder scopes; selection revalidates fingerprints and membership.
- [x] Higher-level safety rules stay stronger than convenience — stale/expired plans and unknown IDs are rejected with typed conflicts requiring re-preview.

## Deviations / spec conflicts
- TDD history is not pristine: the API/storage implementation followed incidental compile RED rather than an expected runtime failure, and the interrupted initial UI fix lost its original RED artifact. Later RED/GREEN evidence (including both final-review fixes) is real and logged; no historical evidence was invented.
- An earlier unexplained Origin-denial security test failure remains without a root cause; subsequent combined and full runs passed, and no cause was fabricated.
- No spec was weakened; collection membership is read-only (no CRUD) per this sprint's approved adapter.

## Risks discovered
- Cancellation is cooperative: preflight/XML load/IndexOf run as bounded synchronous operations, so cancellation is not instantaneous; no hard parser-memory ceiling is claimed.
- Aggregate worst-case capacity (per-file × package × candidate bounds) and eager DOM rendering are not benchmarked; do not claim validated performance at every allowed maximum.
- Native-host evidence is HTTP-level with simulated browser headers; WebView2 browser-native execution remains a release gate, not a certified result.
- Scheduler state is in-memory: interrupted jobs fail conservatively after restart; completed plans/selections persist. No resumability promise.
- Apply (BAT-04) must re-check fingerprints and lock state at its mutation boundary; preview lock state can be Unknown.
- Deferred minors from final review: cross-type scope fields (`folderPath`/`documentIds`/`collectionId`) still persist unconditionally at the API boundary (UI sends neutral values, resolver ignores them); duplicate cancel POST possible when unmount races an in-flight cancel (server tolerates).
- Preexisting NuGet advisories and timing-sensitive watcher tests (QA-02 risk) predate this sprint.

## Next eligible sprint(s)
- `BAT-04` — Transactional Safe Apply Engine (READY after this sprint; deps BAT-03, HIST-01, FS-07 all DONE).
- Other READY: `DOCX-04`, `CAN-04`, `CAN-08`, `QA-01`, `QA-02`, `SHR-08`.
