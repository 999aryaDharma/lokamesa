# V1 Integration Gaps — Backend / Host Fixes (LANE B)

- Branch: `fix/v1-gap-engine-wiring`
- Base: `integration/parallel-batches` @ `72c0678` (Lane C frontend `4aa9a08` is on the integration branch, not on this branch)
- Scope: close the **backend / host** half of the V1 integration gaps so the shipped
  app shell (Lane C) can actually reach the engines it renders. Frontend *features*
  (`features/{graph,markdown,search,share,workspace,canvas}/`) are Lane C and were not touched.
- Authority used for the contract: `docs/quality/v1-readiness-report.md` §5 (gaps) and §9 (GO
  conditions), plus the shipped frontend data-source modules read from
  `integration/parallel-batches` (`shareApi.ts`, `graphApi.ts`, `markdownApi.ts`,
  `workspaceApi.ts`, `batchApi.ts`, `searchStore.ts`).

## Status: DONE (with documented residual risk)

| Gap | Surface | Status |
|-----|---------|--------|
| G2 | Batch apply / undo / history routes + frontend Apply | DONE (engine wired, host-exposed) |
| G3 | Realtime search route | DONE |
| G4 | Shared privileged DI + shipped-host allow-list | DONE |
| G5 | Owner share lifecycle (create/list/get/stop/revoke/regenerate/members) | DONE (server-side; see limits) |
| G8 | PDF finalization routes | DONE |
| G9 | End-to-end administrative workflow test | DONE |
| — | Workspace bootstrap (`documents`, `folder-sources`), workspace graph, Markdown load/save, `window.__DOCUMENTIDE_*__` globals | DONE (contract completion for the shipped shell) |

---

## G2 — Batch apply / undo / history

**Unwired before:** `BatchPreviewController` only served
`preview`/`{planId}`/`selection`/`cancel`; there was no `apply`, no `undo`, and no
`history`. `batchApi.ts` called `POST {planId}/apply`, `POST {planId}/undo` and
`GET {planId}/history`, all of which 404'd. `BatchPreviewPanel` had no working Apply.

**Wired:**
- `src/DocumentIDE.Api/Controllers/BatchPreviewController.cs:57` `POST api/batch-operations/{planId}/apply`
- `src/DocumentIDE.Api/Controllers/BatchPreviewController.cs:85` `POST api/batch-operations/{planId}/undo`
- `src/DocumentIDE.Api/Controllers/BatchPreviewController.cs:104` `GET api/batch-operations/{planId}/history`
- Driven by `BatchApplyCoordinator` (reviewed plan → BAT-04 `BatchApplyEngine`) and the
  owner-side JSONL history store (`Infrastructure/History/JsonlHistoryStore.cs`).
- Status/outcome enums are projected to strings explicitly (no global `JsonStringEnumConverter`),
  so the wire vocabulary matches `batchApi.ts` (`Applied`/`Skipped`/`Failed`, `Stale`, …).
- Error mapping: `UnknownPlan`→404, `UnsupportedOperationType`/`InvalidPlanParameters`→400,
  other `InvalidOperationException`→409. Undo of an unknown snapshot→404, other→409.
- Frontend (owned by this lane): `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.tsx`
  — Apply enabled only when `editable && !dirty && applyState?.planId !== preview?.planId`; a
  409/410 whose message contains “Re-preview” flips the plan to `stale`.

**Tests:**
- `tests/DocumentIDE.IntegrationTests/Desktop/PrivilegedSurfaceHostTests.cs:86`
  `BatchApplySurface_AppliesReviewedMutation_ThenUndoRestores_AndHistoryRecords`
- `tests/DocumentIDE.IntegrationTests/Desktop/PrivilegedSurfaceHostTests.cs:133`
  `BatchApply_UnknownPlan_Returns404_AndExpiredPlan_Returns409`
- Frontend: `src/DocumentIDE.Web/src/features/batch/BatchPreviewPanel.test.tsx` (green).

**Residual risk:** none material. Stale-file protection is per-file: a file changed after
preview is reported `outcome=Skipped, reasonCode=Stale` and left untouched (release-gate
behaviour, asserted in G9).

---

## G3 — Realtime search

**Unwired before:** `searchStore.ts` fetched `GET /api/search?q=…`, but no controller served
it on either host, so search always failed.

**Wired:**
- `src/DocumentIDE.Api/Controllers/SearchController.cs:20` `GET api/search`
- Backed by `IDocumentSearchService` (`DocumentSearchService` → FTS index), registered in the
  shared DI superset.

**Tests:** `tests/DocumentIDE.IntegrationTests/Desktop/PrivilegedSurfaceHostTests.cs:32`
`SearchSurface_IsReachable_AndReturnsIndexedMatch`.

**Residual risk:** the route is bounded by the privileged guard and returns only indexed
documents; no arbitrary-path exposure.

---

## G4 — Shared privileged DI + shipped-host allow-list

**Unwired before:** the API host (`DocumentIDE.Api/Program.cs`) and the shipped desktop host
(`WebAppHost`) each registered a different, partial set of services; several controllers were
registered in one host but not the other, and the desktop allow-list omitted surfaces the app
shell mounts.

**Wired:**
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs:38` `AddPrivilegedSurfaces()` — one
  **superset** of service *implementations* (repositories, engines, history, sharing) using
  `TryAdd*` so each host's own registrations stay authoritative.
- Called from `src/DocumentIDE.Api/Program.cs` (appended immediately after
  `AddBatchPreviewServices()`, end of the DI block — the SCM-01 block was not touched) and from
  `src/DocumentIDE.Desktop/WebView/WebAppHost.cs:66`.
- `WebAppHost.AllowedControllers` (`WebAppHost.cs:157`) now allow-lists exactly the mounted
  surface: `BatchPreview`, `Word`, `FolderSources`, `MoveCorrelation`, `Search`, `Pdf`, `Share`,
  plus `Reconciliation`, `Graph`, `MarkdownDocument` (see contract completion below).
- `WebAppHost.StartAsync`/`InitializeAndStartAsync` gained an optional
  `Action<IServiceCollection>? configureServices` seam (invoked before the shared registrations)
  so tests can override e.g. `ShareTokenService`.

**Security posture unchanged:** this only adds service implementations. Every request is still
authorised per-request by `PrivilegedRequestGuard` (Origin + `X-DocumentIDE-Session-Token`), and
the desktop host still restricts which controllers exist via the allow-list.

**Tests:** exercised by all of `PrivilegedSurfaceHostTests`, `ShareLifecycleHostTests`,
`AppShellSurfaceHostTests`.

---

## G5 — Owner share lifecycle

**Unwired before:** `ShareDialog`/`ShareManager` had intent-only callbacks; nothing persisted a
`Share`, rotated/cleared its token, or mutated membership. `shareApi.ts` called
`/api/shares` (list/create/get) and `…/stop|revoke|regenerate|members`, all 404.

**Wired:**
- `src/DocumentIDE.Infrastructure/Sharing/ShareLifecycleService.cs`
  (`CreateAsync:60`, `GetAsync:119`, `ListAsync:121`, `StopAsync:123`, `RevokeAsync:133`,
  `RegenerateAsync:146`, `AddMemberAsync:158`, `RemoveMemberAsync:182`).
- `src/DocumentIDE.Api/Controllers/ShareController.cs` (`api/shares`):
  `POST:29`, `GET:52` (**bare array** as `shareApi.listShares` requires), `GET {id}:61`,
  `POST {id}/stop:68`, `POST {id}/revoke:75`, `POST {id}/regenerate:82`,
  `POST {id}/members:89`, `DELETE {id}/members/{documentId}:103`.
- Response shape mirrors `ShareManagementState` exactly: `shareId, mode, status, shareToken,
  shareUrl, revoked, allowOriginalDownload, allowPdfDownload, members[{documentId, displayName}],
  tunnelError`.
- Lifecycle model uses the existing `Share` columns (no schema change): **stop** = past
  `ExpiresAt`; **revoke** = cleared `TokenHash` + past `ExpiresAt`; **regenerate** = rotated
  token + cleared `ExpiresAt`. `status` is derived (`stopped` when revoked/expired else
  `active`), `revoked` = empty `TokenHash`. The raw token is returned **only** at create /
  regenerate; reads return an empty `shareToken` (only the hash is persisted).
- Membership mutation is Live-only; a frozen Snapshot rejects with `SnapshotMembershipFrozen`
  (409). Controller mapping: `NoMembers`/`UnknownDocument`/`UnsupportedMemberType`/
  `DocumentUnavailable`→400; `SnapshotMembershipFrozen`/`DocxPreviewUnavailable`/
  `PdfArtifactUnavailable`/`NotAMember`→409; `InvalidMode`→400.
- New seam `src/DocumentIDE.Infrastructure/Sharing/SnapshotMarkdownRendererAdapter.cs` wraps
  `MarkdownPreviewService` as `ISnapshotMarkdownRenderer` (Snapshot publishing).

**Tests:** `tests/DocumentIDE.IntegrationTests/Desktop/ShareLifecycleHostTests.cs` — 6/6:
`CreateSnapshot_ReturnsActiveState_ThenStopAndRevokeAreObservable:39`,
`Regenerate_RotatesToken_AndReactivatesAStoppedShare:94`, `List_ReturnsCreatedShares:119`,
`LiveShare_SupportsExplicitMembershipChanges:136`,
`Create_WithNoMembers_Returns400_AndUnknownShareReturns404:180`,
`Mutation_RequiresThePrivilegedGuard:209`. (Red first: 5 failed / 1 passed before the service
and controller existed.) `ShareTokenService` is overridden via `configureServices` to a temp
`_sharesRoot`, so tests never write to the real `%LOCALAPPDATA%\DocumentIDE\shares`.

**Residual risk (honest limits — do NOT claim these are closed):**
1. **Viewer-side enforcement is not end-to-end.** The public `ShareHost` verifies tokens from its
   own in-memory `ShareTokenService`; a persisted `TokenHash` is not consulted cross-process.
   Stopping/revoking here makes the *owner state* correct and rejects the old token *within the
   privileged host*, but the shared-link revoke is not yet enforced by the running ShareHost
   process. `ShareHost` was deliberately left unmodified to satisfy
   `ShareHostDependencyTests` / `PublicShareAttackSurfaceTests`.
2. **DOCX Snapshot members and `allowPdfDownload=true` cannot publish artifacts yet** (they need
   the WORD-04 preview cache / PDF artifacts). These fail as typed 409
   (`DocxPreviewUnavailable` / `PdfArtifactUnavailable`) rather than publishing a broken share.
3. `shareUrl` is assembled as `/share/{id}[?token=…]` by the owner UI; actual public/tunnel URL
   assembly remains SHR-09's responsibility.
4. The mounted share UI is Lane C; this lane only serves and the desktop host mounts the routes.

---

## G8 — PDF finalization

**Unwired before:** PDF engines (`PdfExportService`, `BatchPdfExportService`, `PdfMergeService`)
existed but no host exposed them.

**Wired:** `src/DocumentIDE.Api/Controllers/PdfController.cs` (`api/pdf`) —
`POST export:40`, `POST export-batch:52`, `POST merge:65`, `POST merge-pdfs:77`. Engines
registered in the shared DI superset; statuses projected to typed strings with explicit error
codes rather than raw exception leakage.

**Tests:** `tests/DocumentIDE.IntegrationTests/Desktop/PrivilegedSurfaceHostTests.cs:67`
`PdfSurface_IsReachable_AndReportsTypedStatuses`.

**Residual risk:** none material; heavy merge is memory-bounded (see `PdfMergeMemoryTests`).

---

## G9 — End-to-end administrative workflow

**Unwired before:** no test proved the whole administrative path worked across the privileged
host: bootstrap → preview → reviewed apply → history → PDF finalization → undo, plus the
release-gate rule that a plan changed after preview cannot mutate the file.

**Wired:** `tests/DocumentIDE.IntegrationTests/EndToEnd/AdministrativeWorkflowE2ETests.cs`
— 2/2:
- `AdministrativeScenario_FromWorkspaceThroughApplyHistoryFinalizationAndUndo:41`
- `ReleaseGate_PlanChangedAfterPreview_CannotMutateTheFile:162`

**Residual risk:** this is a host-level E2E test (real Kestrel, real SQLite in temp). It does
not drive the WPF WebView2 UI or Word COM; those remain covered by their own sprint suites.

---

## Contract completion for the shipped app shell (Lane C) — previously unreachable routes

While wiring the contract in `shareApi.ts` / `graphApi.ts` / `markdownApi.ts` / `workspaceApi.ts`
it was found that several routes the shipped shell calls existed on **neither** host. They are
part of the same “mounted UI reaches a real engine” gap and were closed here:

| Contract | Route | Implementation |
|----------|-------|----------------|
| `workspaceApi.getCachedDocuments` | `GET api/workspaces/{id}/documents` | `ReconciliationController.GetCachedDocuments` — now allow-listed on the shipped host (`WebAppHost.cs:169`) |
| `workspaceApi.getFolderSources` | `GET api/workspaces/{id}/folder-sources` | `FolderSourcesController` (already allow-listed) |
| `graphApi.fetchWorkspaceGraph` | `GET api/workspaces/{id}/graph` | **new** `src/DocumentIDE.Api/Controllers/GraphController.cs:28` → `{ nodes:[{documentId,displayName,documentType}], edges:[{sourceDocumentId,targetDocumentId,relationType,relationOrigin,confidence}] }` |
| `markdownApi.loadMarkdown` / `saveMarkdown` | `GET`/`PUT api/documents/{id}/markdown` | **new** `src/DocumentIDE.Api/Controllers/MarkdownDocumentController.cs:35,50` |
| app-shell globals | `window.__DOCUMENTIDE_API_URL__`, `__DOCUMENTIDE_SESSION_TOKEN__`, `__DOCUMENTIDE_WORKSPACE_ID__` | `src/DocumentIDE.Desktop/WebView/LocalSessionBridge.cs:30` `BuildInjectionScript()` (adds the previously missing workspace id) |

**Graph truthfulness (master §27.2, spec 08 §14, AI-04):** relation origins are passed through
unchanged (`Explicit`/`Manual` → solid, `Inferred`/`semantic` → dashed); the client downgrades any
unknown origin to `inferred`, so a relationship is never promoted to a factual claim. Only ids,
display names and relation labels are served — never a filesystem path.

**Markdown safe-write (non-negotiable):** `PUT` runs the mandated pipeline — **recovery snapshot
first** (`RecoverySnapshotService`), then temp mutation, validation (round-trip check) and an
atomic replace (`IAtomicFileReplacer`); on any failure the temp is rolled back. If the snapshot
cannot be created the mutation is **blocked** (`409 RecoveryUnavailable`, fail closed). Non-markdown
documents are rejected with 409, unknown documents with 404. Document identity is the FS-02 id —
no path is read from or written to the client.

**Tests:** `tests/DocumentIDE.IntegrationTests/Desktop/AppShellSurfaceHostTests.cs` — 5/5:
`WorkspaceBootstrapSurfaces_AreReachable:33`, `GraphSurface_ReturnsNodesAndTruthfulEdgeOrigins:56`,
`MarkdownSurface_LoadsSource_AndSavesThroughSafeWrite:103`,
`MarkdownSurface_RejectsNonMarkdownDocument:135`,
`SessionBridge_InjectsWorkspaceIdApiUrlAndToken:156`.

**Red → Green:** the test project first failed to compile
(`'LocalSessionBridge' does not contain a definition for 'WorkspaceId'`) — the missing seam —
and the new routes were unregistered/404. After adding the bridge seam, allow-listing the
controllers and implementing the two controllers, all 5 pass.

---

## Verification (this lane, all green except the 2 known environmental failures)

| Command | Result |
|---------|--------|
| `dotnet build DocumentIDE.sln` | **0 errors**, 119 warnings |
| `dotnet test tests/DocumentIDE.IntegrationTests` | **745 passed / 3 failed** (748 total) |
| `dotnet test tests/DocumentIDE.SecurityTests` | **91 / 91 passed** |
| `dotnet test tests/DocumentIDE.ArchitectureTests` | **12 / 12 passed** |
| `dotnet test tests/DocumentIDE.Core.Tests` | **459 / 459 passed** |
| `npx vitest run` (in `src/DocumentIDE.Web`) | **47 files / 319 tests passed** |

Focused runs while iterating: `AppShellSurfaceHostTests` 5/5, `ShareLifecycleHostTests` 6/6,
`PdfMergeMemoryTests` 6/6.

**The 3 IntegrationTests failures:**
1. `ScriptTests.Release_Build_Embeds_Production_Web_Assets` — **expected environmental**
   (release build artifact absent in a fresh worktree).
2. `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — **expected
   environmental** (needs `dotnet publish` release output).
3. `PdfMergeMemoryTests.Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes` —
   **flaky under parallel load**; passes 6/6 isolated (re-run confirms). Not caused by this
   lane.

## Non-negotiables — compliance notes

- Privileged localhost API still requires Origin/Host/session authorization
  (`PrivilegedRequestGuard`); `ShareLifecycleHostTests.Mutation_RequiresThePrivilegedGuard`
  asserts it.
- No arbitrary local path is exposed through public sharing; only ids/display names.
- Markdown source mutation requires recovery snapshot + temp mutation + validation + atomic
  replace, and is blocked if recovery cannot be created.
- `ShareHost` was **not** modified (keeps its dependency/isolation tests green); therefore no
  claim is made that it is an OS sandbox, and link revoke across processes is listed as residual
  risk above.
- Canvas/drag/DOCX-deep-edit product rules were not touched.

## Left for the frontend lane (Lane C)

- Mount/verify the owner share panel against the now-real `/api/shares` routes (server side is
  ready; response shape verified against `ShareManagementState`).
- Consume the graph envelope from `GET api/workspaces/{id}/graph` (shape verified against
  `GraphPayload`).
- Confirm the app-shell bootstrap reads the injected `window.__DOCUMENTIDE_WORKSPACE_ID__`
  (injected by `LocalSessionBridge`; default `"default"`).
- Tunnel/public URL assembly remains SHR-09.

## Files changed (this lane)

New:
- `src/DocumentIDE.Api/Controllers/ShareController.cs`
- `src/DocumentIDE.Api/Controllers/GraphController.cs`
- `src/DocumentIDE.Api/Controllers/MarkdownDocumentController.cs`
- `src/DocumentIDE.Infrastructure/Sharing/ShareLifecycleService.cs`
- `src/DocumentIDE.Infrastructure/Sharing/SnapshotMarkdownRendererAdapter.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/ShareLifecycleHostTests.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/AppShellSurfaceHostTests.cs`
- `tests/DocumentIDE.IntegrationTests/EndToEnd/AdministrativeWorkflowE2ETests.cs`

Modified:
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` (share + relation registrations)
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` (allow-list)
- `src/DocumentIDE.Desktop/WebView/LocalSessionBridge.cs` (workspace-id global)

(Committed earlier as part of G2/G3/G4/G8: `BatchPreviewController.cs`, `SearchController.cs`,
`PdfController.cs`, `Program.cs`, `features/batch/BatchPreviewPanel.tsx` + test,
`BatchApplyCoordinator.cs`, `IndexCoordinatorReindexTrigger.cs`, `JsonlHistoryStore.cs`,
`Desktop/BatchPreviewHostTests.cs`, `Batch/BatchPreviewTests.cs`.)
