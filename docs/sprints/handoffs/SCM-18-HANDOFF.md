# Sprint Handoff — SCM-18

**Status:** DONE
**Branch:** `feat/worktree-api-surface`
**Commits:** single commit — see `git log` for `feat(scm): expose worktree services over the privileged API`

## What changed

- Exposed the already-tested SCM-15/SCM-16 worktree services over the
  privileged Source Control API. **No new Git behaviour, no service change, no
  new dependency, and no branch management** — every route is a thin, typed
  mapping over an existing service call.
- Five append-only routes on `SourceControlController` (spec 13 §16/§18):
  - `GET api/source-control/worktrees?path=` → `ListWorktreesAsync`
  - `POST api/source-control/worktrees/open` → `OpenWorktreeAsync`
  - `POST api/source-control/worktrees` → `CreateWorktreeAsync`
  - `DELETE api/source-control/worktrees` → `RemoveWorktreeAsync`
  - `POST api/source-control/worktrees/prune` → `RefreshWorktreesAsync`
- Typed DTOs in the existing SCM style (`status`/`isSuccess`/`errorCode`/
  `message` + payload + optional `failure`):
  `SourceControlWorktreeInfoResponse`,
  `SourceControlWorktreeListResponse`, `...OpenRequest/Response`,
  `...CreateRequest/Response`, `...RemoveRequest/Response`,
  `...ChangeResponse`, `...RefreshRequest/Response`.
- Status convention matches every existing SCM route:
  - **400** only for input rejected before any Git ran — missing/blank path,
    unsafe/non-canonical target path, malformed ref token, blank workspace id.
    The service's pre-Git `InvalidRequest`/`InvalidTargetPath` statuses map to a
    typed `{ errorCode, error }` body.
  - **200** with a typed body for every inspectable outcome:
    `NotARepository`, `GitUnavailable`, `RefNotFound`, `TargetAlreadyExists`,
    `RequiresAttachmentFlow`, `NotRegistered`, `IsMainWorktree`,
    `WorktreeLocked`, `Unavailable`, `DirtyWorktree`, `PreflightFailed`,
    `CreateFailed`, `RemoveFailed`, parse/list failures.
  - `errorCode` mirrors the typed status on non-success and is `null` on
    success. `RequiresAttachmentFlow` keeps `errorCode: null` (it is a signal,
    not an error).
- `RequiresAttachmentFlow` attaches nothing: the route only relays the
  SCM-15 signal and the FS-01 flow stays the only way to attach a folder
  source. Covered by a real-registry test that asserts the workspace has zero
  folder sources afterwards.
- Dirty removal carries the typed change list (path, state, index/working-tree
  change, conflict kind, status characters) per spec 13 §16.4.
- DI appended to `Program.cs`: `IGitWorktreeService` and
  `IGitWorktreeMutationService` registered **scoped** (both consume the scoped
  FS-01 `IFolderSourceService`; neither was previously registered).
- Services are injected per action with `[FromServices]` rather than through
  the controller constructor, so the SCM-18 region is genuinely append-only and
  no existing member of `SourceControlController.cs`/`Program.cs` was edited,
  reordered, or deleted.
- `SourceControlController` was **not** added to `WebAppHost.AllowedControllers`
  — reachability stays with UI-03, which lands the mount and the allow-list
  together.

## Files changed

- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (+401 lines,
  append-only: 5 routes + mapping helpers + DTOs; 0 deletions)
- `src/DocumentIDE.Api/Program.cs` (+14 lines, append-only DI block)
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeApiTests.cs`
  (new, 12 tests)
- `docs/sprints/handoffs/SCM-18-HANDOFF.md` (this file)

## Verification

- Red stage:
  `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~GitWorktreeApi"`
  → FAIL (compile errors: all `SourceControlWorktree*` types missing), expected.
- `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~GitWorktreeApi"`
  → **PASS, 12/12** (real temp repositories and real Git worktrees).
- `dotnet build DocumentIDE.sln` → **PASS**, 0 errors, 104 warnings
  (all pre-existing package/analyzer warnings; no new warning from SCM-18).
- `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~GitWorktree"`
  → **PASS, 80/80**.
- `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --no-build`
  → **993 passed, 2 failed, 0 skipped, 995 total**. Both failures are the known
  fresh-worktree environmental ones (no `artifacts/`):
  - `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
  - `ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  `PdfMergeMemoryTests` and host tests did **not** flake in this run.
- `dotnet test tests\DocumentIDE.Core.Tests\DocumentIDE.Core.Tests.csproj`
  → **PASS, 459/459**.
- `git diff --unified=0` self-review → only additions; no unintended deletion
  in either modified file.

## Acceptance criteria

- [x] Every worktree capability the UI needs is reachable over the privileged
      API with typed outcomes — list/open/create/remove/prune routes, covered by
      `GitWorktreeApiTests` (`ListReturnsMainAndCreatedWorktree`,
      `OpenRegisteredUnattachedWorktreeRequiresAttachmentFlowAndAttachesNothing`,
      `CreateFromExistingBranchCreatesRealWorktree`,
      `CreateFromTagAndExplicitCommitCreatesDetachedWorktrees`,
      `RemoveCleanRegisteredWorktreeSucceeds`,
      `PruneReturnsRereadRegistryAndDeletesNoValidWorktree`).
- [x] No new Git behaviour — routes call only the SCM-15/16 services; no service,
      runner, argument vector, or queue change is in the diff.
- [x] Append-only and non-disturbing — `git diff` reports 415 insertions and
      0 deletions; the 80 existing `GitWorktree*` tests still pass.
- [x] Typed refusals before Git: unsafe target paths (relative, `..`, drive
      root, `.git`), blank id/path, and malformed refs return 400 with **zero**
      `IGitProcessRunner` invocations
      (`UnsafePathsAndMalformedRefsAreRejectedBeforeGitRuns`).
- [x] Dirty worktree refused and still present; main worktree refused; a
      non-existent ref creates nothing; prune deletes no valid worktree.
- [x] Missing/invalid session token `401`, hostile Origin/Host `403`
      (`WorktreeRoutesRejectMissingInvalidTokenAndHostileOriginOrHost`).
- [x] Temp repositories and worktree directories cleaned up in test disposal.

## Deviations / spec conflicts

- **`DELETE api/source-control/worktrees` takes a JSON body**
  (`workingDirectory`, `worktreePath`). Spec 13 §18 sketches
  `DELETE .../worktrees/{id}`; the SCM-18 sprint table is authoritative and
  specifies the id-less route, so the body carries the canonical path.
- **`errorCode` is the typed status string** on non-success. The SCM-15/16
  services expose `ErrorMessage` but no error-code field; this matches the
  existing history-route convention (`result.ErrorCode ?? result.Status`).
- **`[FromServices]` injection** instead of adding constructor parameters,
  solely to honour the append-only controller constraint. No behaviour
  difference; both services are scoped and resolved per request.
- **`Worktrees` on open/create/remove/refresh is a plain list** (nullable when
  the post-mutation registry could not be re-read), not a nested
  `SourceControlWorktreeListResponse`; a failed re-read is surfaced through the
  top-level `failure` so a successful mutation is never hidden.

## Risks discovered

- `GitWorktreeService.ActiveWorktree` is request-scoped state in this host
  (the service must be scoped because it consumes scoped FS-01). No SCM-18
  route exposes `ActiveWorktree`, so this is not observable today; if UI-03
  wants session-wide "active worktree" state it needs a separate, deliberate
  decision (e.g. persisting the opened path), not a silent singleton.
- `worktrees/open` requires a real `workspaceId`. A caller passing an unknown
  id gets `RequiresAttachmentFlow` (nothing attached), never an error, which is
  the intended FS-01 signal but means UI-03 must supply the active workspace id.
- SCM-18 routes remain unreachable from the WebView until UI-03 adds the
  controller to `WebAppHost.AllowedControllers`; this is intentional and
  explicitly out of scope.
- The two environmental integration failures above will stay red in any fresh
  worktree until production web assets are built/`artifacts/` exists; they are
  unrelated to SCM-18.

## Next eligible sprint(s)

- `UI-03` — mount screen 07 on these routes and land the
  `WebAppHost.AllowedControllers` entry together.
