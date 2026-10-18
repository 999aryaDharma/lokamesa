# Sprint Handoff — SCM-14

**Status:** DONE  
**Branch:** `feat/git-conflict-state-resolution-shell`  
**Commits:** `4dbef98` (implementation + tests); this handoff commit follows.

## What changed

- **Real unmerged state, typed.** `GitConflictService` reads the SCM-02
  `GitStatusSnapshot` and reports the unmerged entries Git actually reported.
  Conflict kinds (`UU`, `AA`, `DD`, `AU`, `UA`, `DU`, `UD`) come from SCM-02's
  porcelain-v2 classification; no second status parser was added.
- **Typed observation outcomes.** `Available` (conflicts present),
  `NoConflicts`, `NoRepository`, `GitUnavailable`, `WorktreeMissing`,
  `IndexLocked`, `Unreadable` and `Failed` are all typed results, never thrown.
  Git diagnostics are passed through the SCM-13 credential redactor.
- **Unsafe actions refused typed.** Commit and Commit All now preflight the
  unmerged index and return `ConflictedWorktree` *before* Git runs (and before
  Commit All can stage anything). SCM-10's discard/restore refusal and SCM-12's
  pull preflight were verified as reachable and consistent rather than
  duplicated. `GitConflictService.CheckActionAsync` answers with a typed
  `GitConflictActionDecision` (action, blocked reason, affected paths).
- **Conflict summary API.** `GET /api/source-control/conflicts?path=…` returns
  conflict entries plus a `BlockedActions` list naming Commit, Commit All,
  Discard, Restore and Pull with their typed reason and affected paths. The
  route stays inside the existing append-only SCM region and inherits the
  privileged localhost guard.
- **Manual-open actions only through existing seams.** A conflict entry carries
  a registered `DocumentId` and `OpenAction` (`OpenInWord` / `OpenDocument` /
  `None`). It carries no absolute path. `ConflictPanel` calls the existing
  `onOpenDocument(documentId)` seam; it never constructs an opener or a URL.
- **No DOCX merge.** The shell does not parse, merge, pick a side, or write any
  conflicted file. A DOCX conflict is reported like any other and resolved by a
  human in Word through the existing guarded host.
- **Frontend.** `ConflictPanel.tsx` is self-contained, props-driven and
  default-exported with colocated tests, in the `GitHistoryPanel` /
  `DiscardRestoreDialog` / `PullDialog` style. `App.tsx` was not touched.

## Files changed

- `src/DocumentIDE.Core/SourceControl/GitConflictState.cs` (new typed contracts)
- `src/DocumentIDE.Infrastructure/SourceControl/GitConflictService.cs` (new)
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` (append-only SCM-14 region + payloads; commit response gains `ConflictedPaths`)
- `src/DocumentIDE.Api/Program.cs` (scoped `GitConflictService` registration)
- `src/DocumentIDE.Core/SourceControl/GitCommitResult.cs` (`ConflictedWorktree`, optional `ConflictedPaths`)
- `src/DocumentIDE.Infrastructure/SourceControl/GitCommitService.cs` (typed conflict preflight for Commit and Commit All)
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` (SCM-14 models; `ConflictedWorktree` commit outcome)
- `src/DocumentIDE.Web/src/features/source-control/ConflictPanel.tsx` (new)
- `src/DocumentIDE.Web/src/features/source-control/ConflictPanel.test.tsx` (new)
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitConflictTests.cs` (new, 20 tests)
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitCommitTests.cs` (existing unresolved-conflict test updated to the new typed `ConflictedWorktree` outcome)

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj` → **PASS 1001 / FAIL 2 / SKIP 0** (1003 total). The two failures are the expected fresh-worktree environmental failures:
  - `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`
  - `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  (`artifacts/` is absent in this new worktree.)
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~GitConflictTests"` → **PASS 20 / FAIL 0** (20 total).
- `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj` → **PASS 459 / FAIL 0** (459 total).
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj` → **PASS 12 / FAIL 0** (12 total).
- `npx tsc -b` → **PASS** (exit 0).
- `npx oxlint` → **PASS** (exit 0; only pre-existing fast-refresh warnings).
- `npx vitest run` → **PASS 77 files / 546 tests** (0 failed).
- TDD evidence:
  - first run of `ConflictFixtureIsDetected` before implementation → `error CS0246: GitConflictService could not be found` (red);
  - after the minimal service, a wrong HEAD expectation failed, then the test passed (green);
  - Step 2 test compile failed with `CS0117` for `GitCommitOutcome.ConflictedWorktree` (red) before the preflight existed;
  - frontend test failed to resolve `./ConflictPanel` (red) before the component existed.
- `git diff --check` is clean; `git status --short` is clean after the handoff commit.

## Acceptance criteria

- [x] **Conflict state is visible and actionable.** `GitConflictTests.ConflictFixtureIsDetected` (real two-branch merge), `ConflictRouteReportsSummaryBlockedActionsAndManualOpenActions`, `ConflictPanel.test.tsx` summary/blocked-action tests.
- [x] **DOCX conflicts are not auto-merged.** `DocxConflictIsReportedAndNeverAutoMerged` snapshots the whole worktree (including `.git`) before/after the shell and proves identical keys/hashes, unchanged DOCX bytes, and no `.orig`/`.rej` output. The service contains no write/merge path for conflicted content.
- [x] **Resolution does not bypass existing file-safety rules.** The shell is read-only; `CommitAndCommitAllAreRefusedWhileUnmergedAndRepositoryIsUnchanged` proves HEAD, commit count, status and every byte are unchanged; `DiscardOfConflictedPathIsRefusedTypedAndFileIsUnchanged` reaches SCM-10's `ConflictedPath` refusal; `PullIsRefusedWhileUnmergedAndNoPullRuns` proves no `pull` invocation; `ConflictRouteDoesNotOfferAnOpenActionForUnregisteredDocuments` proves no arbitrary-path opener.

## Deviations / spec conflicts

- **Added typed contracts in Core** (`GitConflictState.cs`) and extended
  `GitCommitResult`/`GitCommitService`: the sprint asks for blocking at the
  service/API boundary, and the existing commit path let Git report the failure
  after running. The new preflight returns the typed outcome without invoking
  Git. The existing SCM-07 test was updated to the new intended outcome.
- **`docs/sprints/SPRINT-STATUS.md` was not edited.** The sprint DoD asks for
  it, but the dispatching/controller instruction explicitly reserves the board
  (`SPRINT-STATUS.md`) for the controller. Recorded here as the controlling
  deviation.
- **No controller allow-list change and no `App.tsx` change**, per explicit
  instructions (SCM reachability remains the W30 gap lane).
- **No separate check-action HTTP route.** The typed decision is exposed as
  `GitConflictService.CheckActionAsync`; the API summary already lists every
  blocked action so the pane does not need an extra round trip.

## Security & file-safety gate

- [x] **No user file mutated through an unreviewed/bypass path.** The service
  performs only discovery + `status`; tests hash the whole worktree around it.
- [x] **No arbitrary shell command through a filename/path.** No shell is used;
  the service delegates to `GitRepositoryService` and never places a filename on
  a command line. No new probe/command was added.
- [x] **No repository escape through path handling.** Paths come from the status
  snapshot; document lookup uses `GitDocumentPath` normalization plus
  containment. The payload never carries the resolved absolute path.
- [x] **No secret/credential output.** Failure streams and messages pass through
  `GitCredentialRedactor`; `ConflictFailureDiagnosticsNeverExposeRemoteUserInfo`
  asserts a URL userinfo password never appears.
- [x] **No destructive action silently broadened.** Commit/Commit All/ Pull are
  refused before running; discard/restore is path-scoped; the shell itself
  mutates nothing.
- [x] **Higher-level safety rules remain stronger than convenience.** DOCX is
  never merged; opening is a document id through the existing guarded host; a
  state that cannot be read safely is refused rather than assumed clean.

## Risks discovered

- The `SourceControlController` is still not in `WebAppHost.AllowedControllers`
  (W30), and `ConflictPanel` is not mounted in `App.tsx` by instruction. The
  feature is tested at the service/API/component level but is not yet reachable
  in the shipped desktop shell until W30 is closed.
- `IndexLocked`/`Unreadable` are covered at the service boundary with a
  delegating runner; the HTTP route itself was not exercised through a fake
  runner. It maps the same typed result, but the route-level test is not
  duplicated.
- Real rename/rename conflict statuses (`DD`/`AU`/`UA`) are pinned against the
  Git version installed on this agent; a different Git version could classify a
  rename/rename fixture differently while still leaving SCM-02's parser
  authoritative.
- The two release-script tests fail only because `artifacts/` is absent in this
  fresh worktree; no product code was involved.

## Next eligible sprint(s)

- SCM-17 — Source Control Regression & Release Hardening, once the controller
  updates the board and its remaining dependencies (for example QA-02) are DONE.
