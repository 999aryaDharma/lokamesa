# BAT-03 parallel implementation

Spec: docs/sprints/batch/BAT-03-batch-preview-api-review-shell.md; master and subsystem specs remain authoritative.

## Global Constraints

- Implement BAT-03 only. BAT-01 and BAT-02 are DONE. Branch: feat/bat-03-batch-preview-api-review-shell.
- Local filesystem remains source of truth. Preview never mutates source files. No apply endpoint or destructive Replace All bypass.
- Exact ordinal matching only; no fuzzy, semantic, regex. DOCX logical matching reuses LogicalTextMatcher with mandatory package preflight.
- Resolve trusted document IDs through BatchScopeResolver; reject unsafe/reparse paths before reading. Typed per-file failures; no raw exceptions or arbitrary paths in responses.
- Strict Red → Green → Refactor for behavior. Reuse plan repository, scheduler and DOCX tooling. No new dependencies.
- Read AGENTS.md, docs/agent policies, required reading in sprint before editing. No downstream features.
- Parallel workers own disjoint files; do not edit another worker's files or spawn agents. No git commits until controller grants the commit turn; never git add -A.

## Shared wire contract

Use camelCase JSON, string values for status/origin/failure in preview response (explicit DTOs, not globally changed enum settings).

- GET /api/batch-operations/workspaces → [{id,name}]. Allows explicit workspace selection in the reachable review shell.
- GET /api/batch-operations/workspaces/{workspaceId}/scope-options → {documents:[{id,fileName}],collections:[{id,name}]}. Trusted workspace-only metadata; named file checkboxes and collection select keep internal IDs out of product forms. No CRUD.
- POST /api/batch-operations/preview body {scope:{workspaceId,type,documentIds,folderPath,includeSubfolders,excludedBranches,collectionId},find,replacement}. Scope type uses existing numeric enum Selection=0, Folder=1, Workspace=2, Collection=3. Return 202 {planId,jobId}.
- GET /api/batch-operations/{planId} returns {planId,jobId,status,progress,errorCode,scope,filesScanned,filesAffected,occurrenceCount,breakdown:{body,table,header,footer},files:[{documentId,fileName,selected,lockState,failureCode,occurrences:[{id,origin,context,selected}]}]}.
- scope response includes original explicit scope fields plus includedSubfolders (list), subfolderCount, and inspectable scope failures. Match-containing subfolders must be labeled accurately if resolver's list is not exhaustive.
- status is Pending/Running/Completed/Cancelled/Failed/Expired. progress is 0..100. origin is Body/Table/Header/Footer. errorCode and failureCode nullable, sanitized codes.
- PUT /api/batch-operations/{planId}/selection body {files:[{documentId,selected,selectedOccurrenceIds}]} → refreshed preview DTO. Only completed, unexpired plans, existing IDs accepted; freeze parameters and membership; persist in BatchOperationPlan files. Reject stale source with typed conflict requiring re-preview.
- POST /api/batch-operations/{planId}/cancel → 204. Cancellation ends preview safely, no source writes.
- UI Apply remains disabled: this sprint supplies preview/review only; BAT-04 supplies apply. UI must persist reviewed selection with an explicit Save selection action and show failures.
- API client uses existing injected API URL and session-token globals and privileged API guard. Poll until terminal status, cancel/cleanup on unmount. Backend uses existing local job scheduler without capturing disposed scoped services.

### Task 1: Backend preview and persisted review

Own src/DocumentIDE.Core/Batch new preview DTO/domain files, src/DocumentIDE.Infrastructure/Batch, necessary persistence/migrations, src/DocumentIDE.Api/Controllers/BatchPreviewController.cs, src/DocumentIDE.Api/Program.cs, backend Batch tests only. Do not edit Web, sprint docs, or the plan.

Read Global Constraints and Shared wire contract in this plan (these sections are included here by reference; controller also provides the plan path). Read required spec documents and current matching/resolver/repository/scheduler/security code. Implement the exact shared contract using smallest existing patterns. Determine trusted collection snapshots from existing persistence; if absent, report before adding minimum storage/read adapter rather than pretending collections work or silently widening scope.

The controller already added tests/DocumentIDE.IntegrationTests/Batch/BatchPreviewTests.cs and demonstrated compile RED in artifacts/bat-03-red-initial.log. Own this test now. Add minimal declarations, run runtime RED, then green preview. Add meaningful TDD tests for markdown ordinal counts, DOCX body/table/header/footer/cross-run corpus, byte-identical preview, valid file plus failed file, progress/cancel, selection persistence/unknown IDs, stale and expired plans, API privilege guard. Hash bytes actually scanned; bound input/package reads; no source mutation. Preserve no-match and failed files as inspectable outcomes. Lock state uses existing safe probing if available, else accurately report Unknown without inventing certainty.

Persist plan before enqueue; background job creates its own service scope or uses safely owned singleton dependencies. Ensure selection cannot race preview completion or broaden membership. Do not implement apply.

SDK: C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe. Focused test command: dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~BatchPreview --nologo -m:1 -v:q. Avoid full suite concurrently. Log RED/GREEN results in artifacts and report file.

Self-review and write report listing files, exact commands/results, TDD evidence, spec mapping, deviations and concerns. Return DONE/DONE_WITH_CONCERNS/NEEDS_CONTEXT/BLOCKED; no commits until controller grants turn.

### Task 2: Reachable frontend review shell

Own src/DocumentIDE.Web/src/features/batch/* and src/DocumentIDE.Web/src/App.tsx plus their frontend tests only. Do not edit backend, sprint docs, or the plan. Read Global Constraints and Shared wire contract in this plan, required spec documents, existing fetch/session globals, frontend test conventions, and App/canvas integration first.

Implement accessible BatchPreviewPanel and smallest API client/types. Add a reachable Batch operations toggle to App, leaving canvas usable. Load explicit workspace choices from shared endpoint; do not hardcode a workspace. Allow explicit Selection/Folder/Workspace/Collection scope with appropriate IDs/root/exclusions/subfolders (default ON), exact find/replacement fields. No fuzzy/semantic inputs. Show resolved scope/counts/tree/exclusions accurately, all four origins, file failures and lock state, progress/error/cancel. File and occurrence checkboxes persist only via Save selection. Apply is disabled until completed and remains unavailable with a clear next-sprint explanation because no apply implementation exists. Handle stale responses by prompting re-preview.

Follow strict TDD with frontend tests before each behavior; mock API contract. Cover counts/origins, unavailable Apply while pending, per-file/occurrence edits and persistence, errors/stale/cancellation. Use current dependencies/native controls; no packages. Poll without leaked timers or late requests changing another plan. Run npm test and npm run build in src/DocumentIDE.Web, report exact outputs and RED/GREEN evidence. Avoid editing existing tests except where integration requires it.

Self-review and write report listing files, commands/results, TDD evidence, acceptance mapping, deviations and concerns. Return DONE/DONE_WITH_CONCERNS/NEEDS_CONTEXT/BLOCKED; no commits until controller grants turn.

### Task 3: Desktop host reachability

Own src/DocumentIDE.Desktop/WebView/WebAppHost.cs and focused tests/DocumentIDE.IntegrationTests/Desktop/BatchPreviewHostTests.cs only. No backend/Web/docs changes or subagents. Read AGENTS.md, BAT-03 sprint required reading and host/security/test patterns.

Global constraints: preview never mutates source, privileged guard retains Host/Origin/startup-secret enforcement, no public ShareHost dependency or changed Word concurrency, no unrelated routes broadened, strict TDD. API contract is frozen in Shared wire contract of docs/plans/BAT-03-parallel-implementation.md; read that section only alongside this brief.

Backend worker will expose DocumentIDE.Api.BatchPreviewRegistration.AddBatchPreviewServices(this IServiceCollection services) in src/DocumentIDE.Api/BatchPreviewRegistration.cs. It registers AppDb and batch dependencies with TryAdd. Integrate that helper into desktop host, register only BatchPreviewController from Api assembly (small controller feature provider/filter if necessary), initialize AppDb and MapControllers behind existing PrivilegedRequestGuard. Preserve health/status routes and existing startup lifecycle. Never start tests against user's app.db: add smallest optional injected AppDb seam or equivalent test-only host configuration that avoids real user storage.

Write failing integration proof first: actual WebAppHost starts on dynamic loopback port, temp AppDb is seeded, missing session denied, authorized workspaces/preview/poll succeeds and source bytes unchanged. Unknown unrelated controller route stays absent. Existing host/security tests must pass. Production helper can be absent initially (compile RED), then runtime RED before reachability implementation. Coordinate helper dependency with controller; do not duplicate backend DI registrations. Use SDK C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe; focused tests only, no concurrent full suite. If backend is building, coordinate sequential .NET builds through controller to avoid generated-file locks.

Write detailed report files/tests/exact commands/results, RED/GREEN evidence, acceptance mapping and concerns. No commits until controller grants serialized turn. Return DONE/DONE_WITH_CONCERNS/NEEDS_CONTEXT/BLOCKED with concise test summary.

### Task 4: Native WebView browser request authorization

Own src/DocumentIDE.Api/Security/PrivilegedRequestGuard.cs and focused relevant tests (desktop BatchPreviewHostTests plus existing API guard tests if needed). No other production files/docs/subagents. Read AGENTS.md, required BAT-03/master security/canvas/batch specs, existing guard and actual MainWindow/LocalSessionBridge flow, prior task-3 report and current host test.

Concrete integration defect: production WebView origin is https://appassets.html2sav while API is dynamic http://127.0.0.1. Browser Fetch Metadata marks that request cross-site. Guard currently rejects all cross-site before checking the already permitted application Origin, so actual UI batch calls fail despite direct HttpClient tests passing.

Follow strict TDD: actual native-host HTTP regression sends Origin exactly https://appassets.html2sav and Sec-Fetch-Site cross-site plus valid startup secret; preview/workspaces must succeed with exact CORS origin. Observe current 403 first. Minimal guard change: allow cross-site Fetch Metadata only for EXACT https://appassets.html2sav (ordinal case-insensitive comparison consistent with existing origin handling), preserving loopback Host validation and subsequent origin validation + session authorization. Do not admit subdomains, arbitrary localhost origins, null/foreign origins or schemes through this exception. Every privileged actual request still requires valid startup secret. Standard OPTIONS preflight exposes no privileged state; retain existing CORS handling for recognized app origin. Test valid native preflight and authorized call, missing/invalid secret denied, foreign/null/subdomain/wrong-scheme/host denial. Existing cross-site foreign/local-origin denials must remain. Never rewrite/forge browser metadata or weaken secret checks. No ShareHost changes.

SDK C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe. Backend matcher fixer currently owns .NET turn; request turn through controller before build/tests to avoid WPF generated-file locks. No commits until serialized grant. Detailed report .superpowers/sdd/BAT-03-parallel-implementation/task-4-report.md with exact RED/GREEN commands/output, files/spec mapping, security implications and concerns; short status on return. Controller will author ADR and broad verification. No full suites while implementation ongoing.
