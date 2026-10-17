# Sprint Handoff — SCM-07

**Status:** DONE
**Branch:** `feat/git-commit-commit-all`
**Commits:** `ee89425`, `20123c8`, `c83c6b5`, `d34adaa`, `ddbc39d`, `2700dfa`, `b9474ed`, `4100042`, `3061b18`, `03b4c2b`, plus this handoff commit at the tip — see `git log --oneline 72c0678..HEAD` (base `72c0678`)

## What changed

- New `GitCommitService` (Infrastructure) implements the two commit flows against
  real Git (spec 13 §11/§19/§20/§23):
  - `CommitAsync` → `git commit -m <message>` against the **current staged state only**,
  - `CommitAllAsync` → SCM-06 `GitStageService.StageAllAsync` **then** the same commit.
  Both run through the SCM-01 `IGitProcessRunner` (structured `ArgumentList`, never a
  shell string) with the resolved true worktree root (`rev-parse --show-toplevel`) as
  the working directory — which is also the SCM-01 per-worktree mutation queue key, so
  concurrent commit requests serialize per worktree.
- **Commit and Commit All are visibly distinct flows.** `GitCommitFlow { Commit, CommitAll }`
  travels on every result and on the API/frontend payloads. The plain flow never stages
  anything itself: the commit invocation is **exactly** `["commit","-m",message]` — no
  `-a`, `--all`, `--no-verify`, `--amend` or `--allow-empty` (pinned by
  `CommitAllNeverUsesTheAllFlagOnTheCommitItself`,
  `CommitMessageIsPassedAsAStructuredArgumentAndNeverShellInterpreted`,
  `CommitNeverBypassesHooksWithNoVerify`).
- **Commit All is the only staging flow, and it reports what it staged.** It stages
  through the SCM-06 pipeline, then records the paths Git reported as staged into
  `StagedPaths`, and only then commits. A failed staging step is a typed commit failure
  and the commit never runs (`StageFailure`), so a partial staging can never be reported
  as a commit.
- **Typed, inspectable outcomes** (new Core contract `GitCommitResult`):
  `Succeeded` / `MessageRequired` / `MessageInvalid` / `NothingStaged` /
  `NoRepository` / `GitUnavailable` / `MissingIdentity` / `HookRejected` /
  `IndexLocked` / `Failed`, never a silent fallback. Failures carry the originating
  `GitProcessResult`; input rejections carry an `ErrorCode`.
- **Message validation before Git runs and before Commit All stages** (`ValidateMessage`):
  blank/whitespace-only ⇒ `MessageRequired`; a NUL character ⇒ `MessageInvalid`. Both are
  rejected with **zero Git invocations** and, critically, **before** Commit All touches
  the index — a rejected message can never leave the index mutated by a flow that then
  reports no commit. The message is always a structured `-m` argument.
- **No empty commits** (spec 13 §11.3): `CommitCoreAsync` pre-reads status through
  `GetStatusAsync`; when nothing is staged and there is no conflict, Git is **not
  invoked at all** and the result is typed `NothingStaged`. An unresolved conflict is
  deliberately left for Git to report (typed `Failed` with
  `ErrorKind = GitProcessErrorKind.GitConflict`), so the index stays the authority.
  A race where the index changes between the pre-check and the commit is caught by Git's
  own "nothing to commit" output and mapped back to `NothingStaged`.
- **Hooks stay enabled** (spec 13 §11.1/§23). A failing `pre-commit`/`commit-msg` is a
  typed `HookRejected` with no commit created; the hook's own output is surfaced on the
  typed failure; a successful hook runs and the commit succeeds.
- **Success returns the commit identifier plus summary metadata** (`GitCommitInfo`:
  full id, short id, subject, branch) read via one read-only metadata call
  `["log","-1","--format=%H%x00%h%x00%s"]`, and the status re-read from Git after the
  commit is embedded in the same result. A metadata read failure cannot turn a created
  commit into a reported failure.
- **No automatic push** (spec 13 §14.4/§23): the service contains no remote subcommand;
  `CommitNeverTouchesARemote` pins it.
- API (`SourceControlController`, same privileged localhost guard as SCM-03/SCM-06):
  `POST api/source-control/commit|commit-all` (body `{path, message}`).
  `MessageRequired`/`MessageInvalid` ⇒ HTTP 400 `{errorCode, error}` (nothing ran);
  every other outcome ⇒ HTTP 200 typed
  `SourceControlCommitResponse{outcome,flow,isSuccess,errorCode,message,commit,stagedPaths,status,failure}`;
  success ⇒ `status` with the re-read snapshot and `commit` with the new identity.
- Frontend: new `CommitBox.tsx` keeps Commit and Commit All as two explicit buttons,
  disables both on a blank/whitespace-only message (mirroring the API's typed rejection,
  so the API is never called), disables while busy and when Git is unavailable, surfaces
  typed failures in a `role="alert"` region instead of a silent fallback, and reports the
  created commit plus the re-read status via `onCommitSucceeded`/`onStatusRefreshed`.
  `SourceControlPanel.tsx` gained an optional `renderCommitBox` render prop;
  the pane still owns no commit logic and exposes no branch-management surface.

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Infrastructure/SourceControl/GitCommitService.cs` — delivered as planned (new).
- `src/DocumentIDE.Web/src/features/source-control/CommitBox.tsx` — delivered as planned (new).
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — delivered as planned (extended: 3 DTOs + 2 routes + `RunCommitActionAsync`/`ToCommitResponseAsync`/`ToCommitInfo` helpers, same file, no duplicate controller).
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitCommitTests.cs` — delivered as planned (new; two classes: `GitCommitTests` 33 tests + `GitCommitApiTests` 8 test cases = 41).

Additional files needed to satisfy the sprint (mapping recorded):

- `src/DocumentIDE.Core/SourceControl/GitCommitResult.cs` — **new** typed outcome
  contract (`GitCommitFlow`, `GitCommitOutcome`, `GitCommitInfo`, `GitCommitResult`),
  consumed by Infrastructure + API. Mirrors the SCM-02 `GitStatusResult` / SCM-06
  `GitStageResult` Core placement.
- `src/DocumentIDE.Api/Program.cs` — **append-only** DI singleton registration of
  `GitCommitService` at the end of the existing SCM block (after `GitStageService`).
  No other line in the file was touched.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  `GitCommitFlow` + `GitCommitOutcome` unions, `SourceControlCommitInfo`,
  `SourceControlCommitResult` model types.
- `src/DocumentIDE.Web/src/features/source-control/CommitBox.test.tsx` — 7 colocated
  component tests (CAN-05 batch-B ruling).
- `src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.tsx` +
  `SourceControlPanel.test.tsx` — optional `renderCommitBox` render-prop seam so the
  changes pane can host the commit box (panel still owns no mutation logic).

`TestGitRepository.cs` was **not** modified: the SCM-06 `RunGitCaptureUtf8` helper
already covered the non-ASCII path need.

Lane safety (verified with `git diff --name-only 72c0678..HEAD -- <lane files>` → empty):
Lane B (`BatchPreviewController.cs`, the new Search/Share/Pdf controllers,
`WebView/WebAppHost.cs`) and Lane C (every `src/features/**` file except the four listed
above; `App.tsx`) were **not touched**. `docs/sprints/SPRINT-STATUS.md`,
`docs/quality/**`, `scripts/verify-release.ps1` and the sprint manifests/roadmaps were
**not touched** (left to the controller).

## TDD evidence

Backend cycles were strict RED-first; the mandated first behavior was

> Commit contains staged changes only.

which failed first (`CommitContainsStagedChangesOnly`), then went GREEN with the minimal
`CommitAsync` (message validation + `git commit -m` + commit-identity read + status
re-read), then refactored.

- Cycle 1 (staged-only commit): RED `CommitContainsStagedChangesOnly` → GREEN.
- Cycle 2 (message validation): RED `BlankOrWhitespaceOnlyCommitMessageIsRejectedTypedWithoutRunningGit`
  (5-case theory) → GREEN with 7 tests; a follow-up RED
  (`CommitMessageContainingNulIsRejectedTypedWithoutRunningGit`,
  `CommitMessageIsPassedAsAStructuredArgumentAndNeverShellInterpreted`) widened the fix.
- Cycle 3 (never create an empty commit): RED
  `UnstagedOnlyChangesAreNeverCommittedAndReportNothingStaged`,
  `CleanWorktreeReportsNothingStagedWithoutCreatingAnEmptyCommit`,
  `DocumentSavedAgainAfterStagingIsNotPartOfTheCommit` → GREEN with the status pre-check
  (10 tests).
- Cycle 4 (Commit All): RED `CommitAllStagesEligibleChangesThenCommitsAndReportsItsFlow`,
  `CommitAndCommitAllRunDistinctFlows`, `CommitOnUnbornHeadCreatesTheFirstCommit`,
  `CommitAllOnUnbornHeadCreatesTheFirstCommit` → GREEN with `CommitAllAsync`, staged-path
  reporting, and unborn-HEAD support (14 tests).
- Cycle 5 (hooks): RED `FailingPreCommitHookIsATypedFailureAndCreatesNoCommit`,
  `FailingHookOutputIsSurfacedOnTheTypedFailure`,
  `FailingCommitMsgHookIsATypedFailureAndCreatesNoCommit`,
  `SuccessfulPreCommitHookRunsAndTheCommitSucceeds`,
  `CommitNeverBypassesHooksWithNoVerify` → GREEN (19 tests).
- Cycle 6 (failure vocabulary): RED `LockedIndexIsATypedFailureAndCreatesNoCommit`,
  `LockedIndexDuringCommitAllIsATypedFailureAndCreatesNoCommit`,
  `MissingGitIdentityIsATypedFailureAndCreatesNoCommit` (4-case theory),
  `IndexChangedBetweenPreCheckAndCommitIsReportedAsNothingStaged`,
  `UnresolvedConflictIsATypedCommitFailure` → GREEN with `ClassifyCommitFailureAsync`
  (27 tests).
- Cycle 7 (discovery failures — coverage only, no production change):
  `CommitOutsideAnyRepositoryIsTypedNoRepositoryWithoutRunningGit`,
  `CommitWhenGitIsUnavailableIsTypedGitUnavailable`,
  `WorktreeThatDisappearedIsATypedFailureAndCreatesNoCommit` (30 tests). These pin the
  existing Cycle-1 discovery mapping.
- Cycle 8 (argument discipline — coverage only, no production change):
  `CommitAllNeverUsesTheAllFlagOnTheCommitItself`, `CommitNeverTouchesARemote`,
  `CommitInALinkedWorktreeAdvancesOnlyThatWorktree` (33 tests).
- Cycle 9 (API): RED confirmed by `dotnet build` (`CS0246 SourceControlCommitRequest` /
  `SourceControlCommitResponse` not found) → GREEN controller routes + mapping + DI;
  `CommitRoutesRejectMissingInvalidTokenAndHostileOrigin` added on the same pass
  (41 tests total).
- Frontend: RED `CommitBox.test.tsx` failed on the missing `./CommitBox` import → GREEN
  `sourceControlModels.ts` types + `CommitBox.tsx` (7 tests); RED
  `SourceControlPanel.test.tsx` failed on the missing `renderCommitBox` seam → GREEN
  (23 tests). Two test-authoring bugs were fixed during Cycles 8–9 and the frontend cycle
  (an over-broad `--all` assertion, an unmodified `docs/b.md` expectation, and an
  in-flight test that assumed the message survives a *successful* commit — the component
  intentionally clears it); no production change resulted from either.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitCommit"` → **41 passed / 0 failed / 0 skipped** (33 service + 8 API cases; the sprint-filter evidence).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **769 passed / 2 failed / 0 skipped** (771 total). Both failures are the documented REL-03-sanctioned release-artifact checks (`ScriptTests.Release_Build_Embeds_Production_Web_Assets` — missing `artifacts/desktop/DocumentIDE.Desktop.exe`; `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — missing `artifacts/installer/DocumentIDE-Setup.msi`): `artifacts/` is gitignored, absent in a fresh worktree, and this diff touches neither `scripts/` nor packaging. The same two failures are recorded in the SCM-01…SCM-06, SCM-15 and SCM-16 handoffs.
- `dotnet test tests/DocumentIDE.SecurityTests` → **91 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.Core.Tests` → **459 passed / 0 failed / 0 skipped** (Core touched via `GitCommitResult.cs`).
- `npx vitest run` (`src/DocumentIDE.Web`) → **48 files / 326 tests passed, 0 failed**.
- `npx tsc -b` (`src/DocumentIDE.Web`) → exit 0.
- `npx oxlint` (`src/DocumentIDE.Web`) → exit 0, 21 warnings / 0 errors. One of those is
  the same `react(only-export-components)` note the SCM-06 `ChangeActions.tsx` already
  carries, for co-locating the exported `postCommit` helper with the component (the
  established convention for the feature's API helpers, shared by `PdfActions.tsx`,
  `ShareManager.tsx`, `MetadataFilterChips.tsx`). No new warning class was introduced.

## Acceptance criteria

- [x] **Commit never silently includes unstaged-only changes** —
  `CommitContainsStagedChangesOnly` and `UnstagedOnlyChangesAreNeverCommittedAndReportNothingStaged`
  prove an unstaged-only change survives the commit with an empty commit list, and
  `CommitAllNeverUsesTheAllFlagOnTheCommitItself` pins the plain-flow invocation to
  exactly `["commit","-m",message]` (no `-a`/`--all`). `DocumentSavedAgainAfterStagingIsNotPartOfTheCommit`
  pins that content changed *after* staging is not swept in.
- [x] **Commit All is visibly distinct from Commit** —
  `CommitAndCommitAllRunDistinctFlows` and `CommitAllStagesEligibleChangesThenCommitsAndReportsItsFlow`
  (service) plus `CommitAllEndpointStagesCommitsAndReportsFlowAndStagedPaths` (API): the flow
  name travels on every result, only Commit All stages, and only Commit All reports
  `StagedPaths`. The frontend exposes them as two separate buttons and sends distinct
  routes (`/commit` vs `/commit-all`).
- [x] **Successful commit returns the created commit identifier** —
  `GitCommitInfo{CommitId, ShortId, Summary, BranchName}` is read back with one read-only
  `git log -1` call and surfaced on the result and the API payload
  (`CommitEndpointCreatesCommitAndReturnsIdentifierAndRefreshedStatus`);
  `CommitBox` renders `commit-last-commit` with the short id and subject.
- [x] **No automatic push occurs** — `CommitNeverTouchesARemote` records every invocation
  and asserts no remote subcommand is ever run; the service has no remote code path.

Required tests from the sprint doc:

- [x] Commit contains staged changes only — `CommitContainsStagedChangesOnly` (the mandatory RED-first test).
- [x] Unstaged changes remain untouched after Commit — `UnstagedOnlyChangesAreNeverCommittedAndReportNothingStaged` + `DocumentSavedAgainAfterStagingIsNotPartOfTheCommit`.
- [x] Commit All intentionally stages and commits eligible changes — `CommitAllStagesEligibleChangesThenCommitsAndReportsItsFlow` + `CommitAndCommitAllRunDistinctFlows`.
- [x] Hook failure is surfaced as a typed commit failure — `FailingPreCommitHookIsATypedFailureAndCreatesNoCommit`, `FailingCommitMsgHookIsATypedFailureAndCreatesNoCommit`, `FailingHookOutputIsSurfacedOnTheTypedFailure`.

Implementation steps:

- [x] Step 1 commit message validation — `ValidateMessage` (blank/whitespace, NUL) + 6 tests.
- [x] Step 2 staged-only commit — `CommitAsync` + pre-check + metadata read + status re-read.
- [x] Step 3 Commit All flow with status re-read — `CommitAllAsync` (SCM-06 stage-all, `StagedPaths`, refreshed status).
- [x] Step 4 surface hook/authentication failures — `HookRejected` / `MissingIdentity` / `IndexLocked` classification + hook-output surfacing.
- [x] Step 5 return commit metadata — `GitCommitInfo` via the read-only `log -1` call, in result and API payload.

Additional failure/edge cases from the sprint doc:

- [x] Nothing staged / empty commit — `CleanWorktreeReportsNothingStagedWithoutCreatingAnEmptyCommit`, `IndexChangedBetweenPreCheckAndCommitIsReportedAsNothingStaged`.
- [x] No repository / Git unavailable — `CommitOutsideAnyRepositoryIsTypedNoRepositoryWithoutRunningGit`, `CommitWhenGitIsUnavailableIsTypedGitUnavailable`.
- [x] Index lock — `LockedIndexIsATypedFailureAndCreatesNoCommit` + the Commit All variant.
- [x] Unresolved conflicts — `UnresolvedConflictIsATypedCommitFailure`.
- [x] Unborn HEAD (first commit) — `CommitOnUnbornHeadCreatesTheFirstCommit`, `CommitAllOnUnbornHeadCreatesTheFirstCommit`.
- [x] Linked worktree scoping — `CommitInALinkedWorktreeAdvancesOnlyThatWorktree`.
- [x] Worktree disappears mid-flight — `WorktreeThatDisappearedIsATypedFailureAndCreatesNoCommit`.

## Deviations / spec conflicts

- One extra Core file beyond the planned list (`GitCommitResult.cs`); it is the typed
  public contract consumed by Infrastructure/API and follows the SCM-02/SCM-06 Core
  placement precedent. No lane file touched; no higher-level requirement weakened.
- Frontend test path follows the CAN-05 batch-B ruling (colocated
  `CommitBox.test.tsx`), consistent with all prior feature components.
- **Hook classification is local to `GitCommitService`.** `GitFailureClassifier.cs`
  (SCM-01) was out of this sprint's ownership, so `HookRejected` is decided as
  `ErrorKind == GitCommitHookFailed` **or** (`ExitCode == 1` **and** a commit hook file
  exists, resolved through `git rev-parse --git-path hooks` so `core.hooksPath` and
  linked worktrees resolve correctly). This is a heuristic rather than a classifier
  change; see Risks.
- **Unmerged/conflict detection uses sprint-local markers.** Git's
  "Committing is not possible because you have unmerged files" text does **not** match
  `GitFailureClassifier`'s existing `unmerged paths` marker, so the service matches its
  own marker list and rewrites `ErrorKind` to `GitProcessErrorKind.GitConflict` on the
  reported failure. Surfaced as `Failed`, not a new outcome, because spec 13 §11 assigns
  conflict-resolution semantics to SCM-14.
- **`MissingGitIdentityIsATypedFailureAndCreatesNoCommit` uses a scripted/delegating
  runner** rather than a real Git process: the host machine may carry a global
  `user.name`/`user.email`, which would make a real reproduction flaky. The real Git
  signatures were probed and are pinned as the scripted inputs
  (`fatal: empty ident name (for <>) not allowed`, `Author identity unknown`,
  `unable to auto-detect email address`, `Please tell me who you are`).
- **Cycles 7 and 8 added no production code** — they are RED/GREEN coverage for the
  discovery mapping and argument discipline delivered in Cycles 1 and 4. Recorded here
  rather than claimed as new behavior.
- No production code change resulted from either test-authoring bug fixed in Cycles 8/9
  and the frontend cycle (details in TDD evidence).

## Risks discovered

- **Hook false-positive risk (consumer-relevant):** because the hook check is
  `ExitCode == 1 && a commit hook file exists`, a *non-hook* Git failure that exits 1
  while any `pre-commit`/`prepare-commit-msg`/`commit-msg` file is present could be
  reported as `HookRejected`. In practice Git exits 1 from `git commit` for
  "nothing to commit" (matched earlier by `NothingToCommitMarkers`) and hook rejection;
  the ordering in `ClassifyCommitFailureAsync` keeps the more specific cases first. A
  future sprint that is allowed to touch `GitFailureClassifier.cs` should promote this
  into a first-class `GitProcessErrorKind` and delete the heuristic.
- **Identity detection is text-based** (`IdentityFailureMarkers`) and therefore pinned to
  `LC_ALL=C` (the SCM-01 runner policy). A locale override would degrade it to the
  generic `Failed` outcome — never to a silent success.
- **Conflicts are reported as `Failed` with `ErrorKind = GitConflict`,** not a dedicated
  outcome; a consumer must inspect `failure.errorKind`. SCM-14 owns conflict semantics.
- **`CommitAll` stages tracked deletions and untracked-but-not-ignored files**
  (`git add --all -- .`, inherited SCM-06 semantics) and does not include ignored files.
  This matches spec 13 §9.4/§11.2 and is what "eligible changes" means here.
- **`StagedPaths` is a snapshot taken from the staging step's status re-read**, i.e. the
  set of paths with a staged change *before* the commit. For a plain `Commit` it is
  `null` by contract — clients must not treat `null` as "nothing was committed".
- **A NUL in the message is the only invalid-message case** besides blank; Git's own
  encoding/config rules (e.g. `commit.cleanup`) are untouched and can still make a
  message that passed validation fail inside Git, which surfaces as the generic `Failed`.
- **No retry affordance for `IndexLocked`** — a transient `index.lock` is surfaced typed
  and left to the pane; no automatic retry exists.
- **Unborn-HEAD note from SCM-06 no longer applies to commits:** both flows create the
  first commit successfully; it is only `git restore --staged` (unstage) that still fails
  before the first commit.

Consumer notes for SCM-08/09/11/13:

- **SCM-08 (History/Log):** reuse the SCM-07 metadata read pattern
  (`git log -1 --format=%H%x00%h%x00%s` — NUL-separated fields, then split) and the
  `GitCommitInfo` shape; `ReadCommitInfoAsync` already tolerates a metadata-read failure
  without corrupting the success verdict, which is the behavior a history reader needs.
  Note the SCM-06 unborn-HEAD limitation for *history* reads (no first commit exists yet).
- **SCM-09 (Discard):** the SCM-07 discipline to copy is "validate and pre-check before
  invoking Git, and keep destructive scope explicit": plain Commit never widens to
  `--all`, a rejected message runs nothing, and nothing-staged runs nothing. Discard must
  mirror the SCM-06 `ResolveContainedPath` containment rules and surface typed outcomes
  instead of silent fallbacks.
- **SCM-11 (Remote/sync, if it follows the roadmap):** SCM-07 deliberately contains **no**
  remote code path (`CommitNeverTouchesARemote`). Push/pull must be added as an explicit
  separate flow with its own typed outcomes; do not attach push to commit as a
  convenience. `GIT_TERMINAL_PROMPT=0` is already pinned by the SCM-01 runner.
- **SCM-13:** the `GitCommitOutcome` vocabulary and its HTTP mapping are the contract to
  preserve. The split "HTTP 400 for input rejection (nothing ran) / HTTP 200 typed body
  for every execution outcome" is now consistent across SCM-06 and SCM-07 — clients must
  branch on the body, not the status alone. Reuse `renderCommitBox`/`renderChangeActions`
  in `SourceControlPanel` for any further action surface, keeping the pane free of
  mutation logic.
- Any consumer mutating the repository must pass the workspace/active-document path and
  let the service resolve the true worktree root: that root is the SCM-01 queue key, so
  passing a subdirectory defeats per-worktree serialization.

## Next eligible sprint(s)

- SCM-08 (Git History & Log) and SCM-09 (Discard) become eligible once their own
  dependencies are DONE (SCM-07 satisfied the commit dependency for both). SCM-11 and
  SCM-13 remain governed by their own `Depends On` rows — this sprint deliberately did
  not open any remote or history surface.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the only writes
  are Git's own index/object updates through `git add --all` (Commit All only) and
  `git commit`, invoked via the SCM-01 controlled runner; the service writes no file
  itself, the plain Commit stages nothing, and every route carries the privileged
  localhost session guard (`CommitRoutesRejectMissingInvalidTokenAndHostileOrigin`).
- [x] **no arbitrary shell command can be injected through the message/path** — no shell
  exists (structured `ArgumentList`); the message is validated (blank, NUL) before Git
  runs and always passed as a discrete `-m` argument, never concatenated
  (`CommitMessageIsPassedAsAStructuredArgumentAndNeverShellInterpreted` — a message
  containing spaces, quotes, `;`, backticks, `$(...)`, `*` and leading `-` is provably
  one argument); blank/NUL messages never reach Git at all.
- [x] **no repository escape is possible through path handling** — discovery validates
  containment through the SCM-01 `GitRepositoryService`
  (`rev-parse --show-toplevel`) and every Git invocation uses the resolved true worktree
  root; `CommitInALinkedWorktreeAdvancesOnlyThatWorktree` proves a sibling linked worktree
  is untouched; non-Git folders are typed `NoRepository` without Git running.
- [x] **no secret/credential output is exposed in UI logs** — responses carry typed
  outcomes, Git's structured stdout/stderr for failures and paths only (no environment
  dumps); the UI renders the typed `message`/`errorCode`. Commits involve no credentials
  and the path never contacts a remote; the SCM-01 prompt-disabled environment policy
  applies unchanged.
- [x] **no destructive action silently broadened its scope** — the plain Commit is pinned
  to exactly `["commit","-m",message]` (no `-a`/`--all`/`--amend`/`--allow-empty`, no
  `--no-verify`); Commit All is an explicitly named, separately-routed flow that reports
  the paths it staged; a failed staging step never proceeds to commit; a blank/whitespace
  message is rejected **before** Commit All stages anything, so a rejected request cannot
  leave the index mutated; nothing-staged never becomes an empty commit; hooks are never
  bypassed.
- [x] **relevant higher-level safety rules remain stronger than convenience** — the local
  filesystem and the real Git index remain the source of truth (no proprietary staging
  area, no in-memory commit state); no semantic/fuzzy matching anywhere (all
  classification is literal, `LC_ALL=C`-pinned markers); typed failures instead of silent
  fallbacks (hook rejection, missing identity, index lock, conflicts); no automatic push.
  The file-safety snapshot/temp/atomic-replace rule is N/A in one sentence: this sprint
  mutates only the Git index and object database through Git's own transactional
  commands and performs no in-place mutation of user file content — that rule governs the
  source-mutation / Search & Replace pipelines.
