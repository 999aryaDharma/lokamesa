# Sprint Handoff - SCM-12

**Status:** DONE
**Branch:** `feat/safe-fast-forward-pull` (worktree `C:\Users\User\.local\share\opencode\worktree\c18aac\scm-12-pull`, base `e0673f9`)
**Commits:** `5a11585` `feat(scm): safe-fast-forward-pull` (implementation + tests) plus the `docs(scm): SCM-12 sprint handoff` commit on top.

## What changed

- **New `GitPullService`** (Infrastructure) implements conservative Pull
  (spec 13 §14.3/§18/§19/§20/§21/§23) through the SCM-01 controlled process
  boundary (structured `ArgumentList`, never a shell string):
  - **Invocation is exactly `pull --ff-only --no-rebase`.** `--ff-only` refuses
    any non-fast-forward outcome; the explicit `--no-rebase` prevents a user's
    `pull.rebase`/`rebase.autoStash` configuration from changing behaviour. No
    `--rebase`/`--no-ff`/`--merge`/`--force`/`--all`/`--tags`/`--prune`, no
    branch/remote/refspec argument is ever passed.
  - **Preflight before any Git mutation.** A conflicted worktree (unmerged
    entries) is typed `ConflictedWorktree`; staged or unstaged tracked changes
    are typed `DirtyWorktree`. Both are blocking results that run **no** pull,
    so Git cannot autostash, overwrite or partially apply local work.
  - **Divergence is a typed blocking state**, never an error string:
    `GitPullOutcome.Diverged` with `ErrorCode "Diverged"` and the product
    statement "Local and remote history have diverged. No automatic
    merge/rebase was performed." No merge commit is created and no rebase runs.
  - **Runs at the resolved true worktree root** (`GitRepositoryService`) and
    therefore through the same SCM-01 per-worktree mutation queue that
    SCM-06/07/11/13 use (`pull` is a mutating command for the classifier), so
    concurrent pull/stage/commit/fetch on one worktree serialize.
  - **Refresh after success (step 4):** status is re-read from Git, the files
    changed by the fast-forward are read with a read-only
    `git diff --name-only -z <old> <new>` (commit ids only, no filename on any
    command line), each changed path is containment-checked, registered
    documents are handed to the existing `IBatchApplyReindexTrigger` (the
    index/search seam), and the refreshed tracking state is re-read.
  - **History:** a typed `GitPullHistoryRecord` is written through a new
    `IGitPullHistoryRecorder` implemented by the **existing `JsonlHistoryStore`**
    (`git-pull-{operationId}.jsonl`), not a parallel store. A failed history or
    reindex write is a warning on an otherwise successful pull, never a silent
    drop and never a fabricated failure.
  - **Typed non-success outcomes:** `NoUpstream`, `NoRepository`,
    `GitUnavailable`, `DirtyWorktree`, `ConflictedWorktree`, `Diverged`,
    `IndexLocked`, `AuthenticationRequired`, `PermissionDenied`,
    `HostUnreachable`, `WorktreeMissing`, `Failed`. Classification reuses the
    SCM-01 `GitFailureClassifier` (index lock, divergence, auth, permission,
    untracked/dirty conflicts) and SCM-11's `GitRemoteFailureClassifier`
    (remote-only auth/host markers) — no third marker list.
  - **No credential material** reaches an outcome: every result is built
    through one `Result(...)` helper that runs messages and the carried
    `GitProcessResult` through `GitCredentialRedactor` (SCM-13).
- **Privileged API (append-only):** `SourceControlController` gained one
  `_pulls` field/ctor parameter, a bounded SCM-12 region with
  `POST api/source-control/pull` and `ToPullResponseAsync`, plus the appended
  `SourceControlPullRequest`/`SourceControlPullResponse` records. Existing
  members were not reordered, reformatted or deleted. A missing path is HTTP 400
  `PathRequired`; every execution outcome is HTTP 200 with a typed body
  (`isBlocking` distinguishes divergence/dirty/conflict from failures).
  `WebAppHost.AllowedControllers` was **not** touched (W30 lane).
- **DI:** `Program.cs` appends a scoped `GitPullService` registration (it uses
  the scoped document repository and reindex seam);
  `PrivilegedSurfaceRegistration` appends the `IGitPullHistoryRecorder` binding
  to `JsonlHistoryStore`.
- **Frontend `PullDialog.tsx` + colocated `PullDialog.test.tsx` (20 tests):**
  self-contained, props-driven, default-exported dialog. It runs no Git on
  mount/close, shows the fast-forward-only/no-merge/no-rebase policy, posts
  exactly `{ path }` (token in the header, never the URL) only after explicit
  confirmation, is disabled with no upstream or no Git, renders a divergence as
  a blocking alert (including "Nothing was changed."), renders every typed
  non-success outcome, and reports changed paths / reindex count / already-up-to-date
  on success. It is not wired into `App.tsx` (out of scope).

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Infrastructure/SourceControl/GitPullService.cs` — new.
- `src/DocumentIDE.Web/src/features/source-control/PullDialog.tsx` — new.
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitPullSafetyTests.cs` — new
  (two classes: `GitPullSafetyTests` 16 + `GitPullApiTests` 4 = **20**).

Additional files needed to satisfy the sprint (same precedent as SCM-10/11/13):

- `src/DocumentIDE.Core/SourceControl/GitPullState.cs` — **new** typed Core
  contracts (`GitPullOutcome`, `GitPullRefresh`, `GitPullResult`,
  `GitPullHistoryRecord`, `IGitPullHistoryRecorder`). The subsystem convention
  (SCM-02/06/07/10/11/13) places the typed public contract in Core.
- `src/DocumentIDE.Infrastructure/History/JsonlHistoryStore.cs` — implements
  the new recorder in the **same** store; `git-pull-*` record kind and read-back.
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — additive
  field/ctor parameter + bounded SCM-12 route/mapping + appended records.
- `src/DocumentIDE.Api/Program.cs` — append-only DI registration.
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` — append-only recorder
  binding.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  appended `GitPullOutcome` + `SourceControlPullResult` (no existing type touched).
- `src/DocumentIDE.Web/src/features/source-control/PullDialog.test.tsx` — new
  colocated component test (CAN-05 batch-B ruling).

Not modified (deliberately): `TestGitRepository.cs`, `GitFetchTests.cs`
(fixtures reused), `GitProcessRunnerOptions.cs`, `GitFailureClassifier.cs`,
`GitRemoteFailureClassifier.cs`, `GitRemoteService.cs`, `GitRepositoryService.cs`,
`WebAppHost.cs`, `App.tsx`, `docs/sprints/SPRINT-STATUS.md`, `scripts/**`,
`docs/quality/**`, manifests/roadmaps, `package.json`/lock and `*.csproj`.

## TDD evidence (Red → Green → Refactor)

Backend cycles were strict RED-first. The mandated first behaviour was

> Clean fast-forward pull succeeds.

which was written first and failed for the expected reason
(`error CS0246: The type or namespace name 'GitPullService' could not be found`,
plus `IGitPullHistoryRecorder`), then went GREEN with the Core contracts and a
minimal success-path service, then was refactored (cycle 5).

- **Cycle 1 (step 2, mandatory first test):** RED `CleanFastForwardPullSucceeds`
  did not compile (CS0246) → GREEN with `GitPullState.cs` + minimal
  `GitPullService` → **1/1** passed.
- **Cycle 2 (steps 1/3/4):** added 14 service tests (divergence, dirty,
  conflicted, refresh, arguments/root/queue, no-upstream, index lock, auth
  redaction, host unreachable, Git missing, vanished worktree, no repository,
  Unicode paths). 3 failed for real reasons: discovery refusals returned a null
  `ErrorCode` (2) and a mid-operation worktree disappearance classified as
  generic `Failed` (1). Fixed typed codes and added the missing-worktree check
  in the failure path → **15/15**.
- **Cycle 3 (API):** RED confirmed by `dotnet build`
  (`CS0246 SourceControlPullRequest`/`SourceControlPullResponse`) → GREEN with
  the appended records + `POST pull` + mapping + DI + history-recorder binding →
  `GitPull` filter **19/19** (15 service + 4 API).
- **Cycle 4 (frontend):** RED `npx vitest run …/PullDialog.test.tsx` failed with
  `Failed to resolve import "./PullDialog"` → GREEN with the appended model
  types + `PullDialog.tsx` → **20/20**.
- **Cycle 5 (refactor/hardening):** added the JSONL round-trip test for the new
  record kind and a status-refresh warning; `GitPull` filter **20/20**. No
  production behaviour was weakened.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitPull"` → **Passed! Failed: 0, Passed: 20, Skipped: 0, Total: 20** (16 service + 4 API).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **Failed: 3, Passed: 980, Skipped: 0, Total: 983.** Two failures are the documented release-artifact checks that cannot pass in a fresh worktree (`artifacts/` is gitignored and absent); this diff touches neither `scripts/` nor packaging:
  - `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets` — missing `artifacts/desktop/DocumentIDE.Desktop.exe`.
  - `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — missing `artifacts/installer/DocumentIDE-Setup.msi`.

  The third is the sprint-prompt's known parallel-load flake
  `DocumentIDE.IntegrationTests.Pdf.PdfMergeMemoryTests.Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes`.
  It was re-run in isolation:
  `dotnet test … --filter "FullyQualifiedName~PdfMergeMemoryTests"` →
  **Passed: 6, Failed: 0, Skipped: 0**. It is unrelated to SCM (PDF memory
  measurement) and failed in both full-load runs while passing in isolation.
  The `GitPull` filter was re-run after the final `--no-renames` tweak →
  **20/20 passed**.
- `dotnet test tests/DocumentIDE.Core.Tests` → **Passed: 459, Failed: 0, Skipped: 0**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12 passed**.
- `dotnet test tests/DocumentIDE.SafetyTests` → **24 passed**.
- `dotnet test tests/DocumentIDE.SecurityTests` → **91 passed**.
- `dotnet test tests/DocumentIDE.GoldenTests` → **39 passed**.
- `dotnet build DocumentIDE.sln --nologo -v minimal` → **Build succeeded, 0 Error(s), 104 Warning(s)** (all pre-existing NuGet advisories/analyzer warnings; no new compiler warning from this change).
- `npx tsc -b` (`src/DocumentIDE.Web`) → **exit 0**.
- `npx oxlint` (`src/DocumentIDE.Web`) → **exit 0**, warnings only. The one new
  `react(only-export-components)` warning on `PullDialog.tsx:24` is the same
  established convention as `RemoteStatus.tsx`, `PushButton.tsx`,
  `CommitBox.tsx` and `ChangeActions.tsx` (co-located exported API helper).
- `npx vitest run` (`src/DocumentIDE.Web`) → **76 files / 540 tests passed, 0 failed**.

## Acceptance criteria

- [x] **Pull never silently creates a merge commit.** `CleanFastForwardPullSucceeds`
  asserts the new HEAD has exactly one parent and `rev-list --merges --count` is
  `0`; `DivergedHistoryIsRefusedWithoutMergeOrRebase` asserts HEAD, `log`,
  working tree and porcelain status are unchanged with `0` merge commits;
  `PullEndpointReportsDivergedAsBlocking` repeats the merge-count assertion
  through the API. The invocation is exactly `pull --ff-only --no-rebase`
  (`PullCarriesOnlyFastForwardNoRebaseArguments`).
- [x] **Pull never silently rebases.** The explicit `--no-rebase` is pinned in
  the argument test (which also scans every invocation for `--rebase`,
  `--no-ff`, `--merge`, `--force`, `-f`, `--all`, `--tags`, `--prune`, `merge`,
  `rebase`); the divergence test additionally asserts `git reflog` contains no
  `rebase` entry and the history/HEAD are byte-identical.
- [x] **Divergence is shown as a blocking state.** Service: `Diverged` +
  `IsBlocking` + `ErrorCode "Diverged"` + the exact "No automatic merge/rebase
  was performed" statement (`DivergedHistoryIsRefusedWithoutMergeOrRebase`).
  API: `isBlocking: true`, HTTP 200 (`PullEndpointReportsDivergedAsBlocking`).
  UI: blocking alert with "Nothing was changed." and no success result
  (`PullDialog.test.tsx`).

Required tests from the sprint doc:

- [x] Clean fast-forward pull succeeds — `CleanFastForwardPullSucceeds` (mandatory RED-first test) + `PullEndpointFastForwardsAndRefreshes`.
- [x] Diverged history is rejected without merge/rebase — `DivergedHistoryIsRefusedWithoutMergeOrRebase` + `PullEndpointReportsDivergedAsBlocking`.
- [x] Dirty/conflicted worktree is not silently overwritten — `DirtyWorktreeRefusalLeavesEveryFileByteIdentical` (path/length/mtime/SHA-256 snapshot + porcelain status + index staged list identical; no `pull` invocation) and `ConflictedWorktreeIsRefusedAndConflictStateIsPreserved` (real conflicting merge; `UU` state and every file preserved; no `pull` invocation).
- [x] Status/search/index refresh after successful pull — `SuccessfulPullRefreshesStatusDocumentsAndIndex` (post-pull `behind == 0`, changed path reported, registered document id handed to the reindex trigger, history record written) + `PullEndpointFastForwardsAndRefreshes` (`statusRefreshed`, refreshed status/remote) + `PullHistoryRecordRoundTripsThroughTheSharedJsonlStore`.

Implementation steps:

- [x] Step 1 dirty/conflict preflight — `DirtyWorktreeRefusalLeavesEveryFileByteIdentical`, `ConflictedWorktreeIsRefusedAndConflictStateIsPreserved`.
- [x] Step 2 ff-only pull — `CleanFastForwardPullSucceeds`, `PullCarriesOnlyFastForwardNoRebaseArguments`.
- [x] Step 3 divergence/conflict output — `DivergedHistoryIsRefusedWithoutMergeOrRebase`, `PullEndpointReportsDivergedAsBlocking`, `ConflictedWorktreeIsRefusedAndConflictStateIsPreserved`.
- [x] Step 4 refresh — `SuccessfulPullRefreshesStatusDocumentsAndIndex`.

Failure/edge cases:

- [x] Git not installed/discoverable — `PullWhenGitUnavailableIsTypedGitUnavailable`.
- [x] Repository/worktree disappears during an operation — `PullOnDisappearedWorktreeIsTyped` (the worktree is moved away exactly when Git is asked to pull).
- [x] Paths with spaces/Unicode/special characters — `PullHandlesPathsWithSpacesUnicodeAndSpecialCharacters`.
- [x] Index lock unavailable — `PullWhenIndexIsLockedIsTypedAndChangesNothing`.
- [x] Authentication/network failure — `PullAuthenticationFailureIsTypedAndRedacted`, `PullHostUnreachableIsTyped`.
- [x] Untracked-overwrite refusal — classified as `DirtyWorktree` (`UntrackedConflict` code) by the SCM-01 classifier; preflight does not treat unrelated untracked files as dirty.
- [x] Word changed a DOCX between status/pull — covered by the same backstop: Git refuses to overwrite a changed tracked file, and the failure is classified `DirtyWorktree` rather than silently applied.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the only
  working-tree write is Git's own fast-forward through the SCM-01 controlled
  runner; a dirty/conflicted worktree is refused before Git runs, and the dirty
  test proves every file (length + mtime + SHA-256) and the index are identical.
- [x] **no arbitrary shell command can be injected through a filename/path** —
  no shell exists (structured `ArgumentList`); the invocation is a constant
  array, the caller path is only a discovery input/working directory, and the
  changed-path read uses commit ids only (`PullCarriesOnlyFastForwardNoRebaseArguments`,
  `PullHandlesPathsWithSpacesUnicodeAndSpecialCharacters`).
- [x] **no repository escape is possible through path handling** — the pull runs
  at the true worktree root discovered by Git (also from a nested directory),
  and every Git-reported changed path is resolved and prefix-checked against
  that root before it is used for a document lookup.
- [x] **no secret/credential output is exposed in UI logs** — interactive
  credential prompting stays disabled by the SCM-01 protected environment and
  every result is built through the redacting `Result(...)` helper
  (`PullAuthenticationFailureIsTypedAndRedacted` asserts the password and
  `alice:` userinfo are absent from message/`ErrorMessage`/stdout/stderr).
- [x] **no destructive action silently broadened its scope** — the invocation
  is pinned to `pull --ff-only --no-rebase` (no merge/rebase/force/all/tags/
  prune/refspec); with no configured upstream nothing runs; preflight refuses
  any tracked change instead of pulling a subset.
- [x] **relevant higher-level safety rules remain stronger than convenience** —
  local filesystem/Git refs remain the source of truth, divergence is a blocking
  state rather than an automatic reconciliation, and the recovery-snapshot/
  temp-mutation/atomic-replace rule is **N/A** in one sentence: a fast-forward
  pull applies Git's own committed tree, it does not rewrite a user file through
  an in-place Document IDE mutation path.

## Deviations / spec conflicts

- **`docs/sprints/SPRINT-STATUS.md` was not edited** and the sprint doc's own
  `**Status:** BLOCKED` line/checkboxes were left as-is. The lane instruction for
  this sprint explicitly reserves the status board for the controller (the same
  convention recorded in SCM-11/SCM-13). This does not weaken a product rule.
- **One extra Core file** (`GitPullState.cs`) and the appended record kind in
  the existing `JsonlHistoryStore`; both follow the established placement
  precedent and the sprint prompt's explicit allowance ("if you add a record
  kind, keep it in the same `JsonlHistoryStore`").
- **Frontend test path** follows the CAN-05 batch-B ruling (colocated
  `PullDialog.test.tsx`), consistent with every prior feature component.
- **Preflight defines "dirty" as tracked staged/unstaged changes plus unmerged
  entries, not unrelated untracked files.** Rationale: an untracked scratch
  file in the worktree does not block a fast-forward, while a fast-forward that
  would overwrite an untracked file is refused by Git and typed
  `DirtyWorktree`/`UntrackedConflict`; treating all untracked files as blocking
  would make pull unusable for ordinary repositories without adding safety.
- **`PullDialog` is not wired into `App.tsx` or `SourceControlPanel.tsx`**;
  exposing the SCM surface in the shipped app shell is the separately tracked
  W30 lane.

## Risks discovered

- **Preflight→pull TOCTOU window.** A file can change between the status
  preflight and `git pull`. Git's own fast-forward refusal is the backstop
  (nothing is autostashed because `--no-rebase`/`--ff-only` are explicit), and
  such a failure is classified `DirtyWorktree`/`Failed`; the preflight is not a
  lock. Closing the window fully would require holding a worktree-wide edit lock
  across Word/other writers, which is outside this sprint.
- **Classification is text-based** and pinned to `LC_ALL=C` by the runner. An
  unmapped wording degrades to the generic `Failed` outcome — never to a silent
  success — and the redacted diagnostic stays on the failure.
- **Refresh failure does not fail the pull.** If the post-pull status read or
  `git diff` fails, the result stays `Succeeded` with `statusRefreshed: false`
  and/or a warning; search/index may serve the previous content until the next
  explicit refresh. The UI shows the warning text.
- **History is recorded only when a pull actually ran** (success and Git-run
  failures, including divergence). Preflight refusals (dirty/conflict/no
  upstream) record nothing because nothing happened.
- **No retry/cancel affordance** for transient remote failures; they are typed
  and left to the pane, matching SCM-11/SCM-13.
- **Detached HEAD reports `NoUpstream`** (no configured upstream), so pull is
  unavailable there; branch management remains out of scope.

## Next eligible sprint(s)

- SCM-14 (Git Conflict State & Resolution Shell) depends on SCM-12 and SCM-05,
  both now DONE — eligible per its own `Depends On` row.
- The `PullDialog` pane integration remains part of the W30 Source Control
  surface-reachability lane.
