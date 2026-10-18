# Sprint Handoff — SCM-13

**Status:** DONE
**Branch:** `feat/explicit-git-push` (worktree `C:\Users\User\.local\share\opencode\worktree\c18aac\sprint-scm-13`, base `ffbd735`)
**Commits:** `f4a96ef` `feat(scm): explicit-git-push` (implementation + tests) plus the `docs(scm-13): sprint handoff` commit on top (see `git log --oneline ffbd735..HEAD`).

## What changed

- **New `GitPushService`** (Infrastructure) implements the explicit push (spec 13
  §14.4/§18/§19/§20/§23) against real Git through the SCM-01 controlled process
  boundary (`IGitProcessRunner`, structured `ArgumentList`, never a shell string):
  - `PushAsync(path)` — discovers the repository, gates on the configured
    upstream, runs one `git push`, and re-reads the tracking state from Git.
- **The invocation is exactly `["push"]`.** No branch, remote or refspec
  argument and no `--force`/`-f`/`--force-with-lease`/`--all`/`--tags`/`--mirror`/
  `--delete`/`--set-upstream`/`--prune`, and no `pull`/`merge`/`rebase`/`commit` —
  pinned by `PushCarriesOnlyStructuredPushArguments`, which scans **every**
  recorded invocation for the forbidden tokens (`--no-verify` included).
- **Push is always explicit.** Nothing in this repository calls `PushAsync`;
  `CommitDoesNotAutoPush` drives the real SCM-07 commit flow with a recording
  runner and asserts (a) the local commit exists, (b) `commit` really ran,
  (c) no `push` invocation ever occurred, and (d) the remote branch is still at
  its pre-commit id. `PushButton.test.tsx` adds the frontend half: rendering the
  component issues **no** request at all.
- **Push is scoped to the current branch's configured upstream** by reusing the
  SCM-11 resolution (`GitRemoteService.GetRemoteStateAsync` — the same call the
  fetch gate uses), so push and fetch can never disagree about which branch is
  tracked. A branch with **no configured upstream** is the first-class typed
  `NoUpstream` state and runs **no** Git at all — never a push to a guessed
  default remote (`PushWithNoUpstreamIsTypedAndRunsNoGit` asserts no `push`
  invocation; `PushEndpointReportsNoUpstreamTypedly` for the API).
- **Runs against the resolved true worktree root** (`rev-parse --show-toplevel`
  through `GitRepositoryService.DiscoverAsync`), which is also the SCM-01
  per-worktree mutation queue key: `push` is a mutating command for
  `GitCommandClassifier`, so concurrent push/fetch/stage/commit on one worktree
  serialize. `PushUsesTheTrueWorktreeRootEvenFromASubdirectory` pins the working
  directory when a nested `docs/` path is passed, and
  `PushRunsThroughThePerWorktreeMutationQueue` pins `MaxObservedConcurrency == 1`
  for three concurrent pushes issued from the root, a nested subdirectory and the
  root.
- **Failures are typed and inspectable** (`GitPushOutcome`): `Succeeded`,
  `NoUpstream`, `NoRepository`, `GitUnavailable`, `AuthenticationRequired`,
  `Rejected`, `PermissionDenied`, `HostUnreachable`, `Failed`. Every non-success
  carries the originating `GitProcessResult` and an `ErrorCode`. Classification is
  **reused, not duplicated**: shared signatures come from the SCM-01
  `GitFailureClassifier` (re-derived from the `LC_ALL=C`-pinned diagnostics so it
  also works when `ErrorKind` is unset) and the remote-only signatures from
  SCM-11's `GitRemoteFailureClassifier`. No second marker list was added and
  SCM-11's classifier was not modified.
- **A rejected push preserves every local commit.**
  `RejectedPushIsTypedAndPreservesLocalCommits` diverges a second clone from the
  bare remote, pushes anyway, and asserts the outcome is `Rejected`/`ErrorCode
  "Rejected"` while HEAD, the local `log`, the porcelain status and a
  length+mtime+SHA-256 snapshot of the working tree are byte-identical, and the
  remote branch was not rewritten.
- **Interactive credential prompting is disabled and the push cannot hang on a
  terminal prompt.** No new environment is set by this sprint: the invocation
  carries no `EnvironmentOverrides`, so the SCM-01 `ProtectedEnvironment` applies
  (`GIT_TERMINAL_PROMPT=0`, `GIT_ASKPASS=""`, `SSH_ASKPASS=""`,
  `GCM_INTERACTIVE=never`), and the configured credential **helper stays enabled**
  so cached credentials still work (SCM-11 F4 decision, unchanged — no
  `GIT_CONFIG_*` / `credential.helper` override, the reserved namespace is still
  untouched). `PushProcessIsBoundedAndDisablesInteractiveCredentials` asserts the
  four effective values on the actual push request and that the invocation keeps a
  positive, non-infinite timeout (the 120 s `DefaultTimeout` backstop).
  `PushAuthenticationFailureIsTypedAndBoundedFromGitDiagnostics` drives a scripted
  runner with `ErrorKind` deliberately **unset** and asserts the call returns
  typed `AuthenticationRequired` in well under the bound (< 30 s) with no
  `Username`/`https://` text in the surfaced message.
- **Credential material never reaches an outcome, error or log line.** New
  `GitCredentialRedactor` replaces the userinfo of any `scheme://userinfo@host`
  URL with `***`; `GitPushService` builds **every** result through one private
  `Result(...)` helper, so the message, the failure `ErrorMessage` and the carried
  `StandardOutput`/`StandardError` are all sanitized.
  `PushFailureNeverSurfacesCredentialMaterialFromRemoteUrl` seeds Git
  diagnostics containing `https://alice:s3cr3t-pw@example.invalid/repo.git`, types
  the failure `AuthenticationRequired`, and asserts the password **and** the
  `alice:` userinfo are absent from the message, `ErrorMessage`, stdout and
  stderr.
- **Privileged API** (`SourceControlController`, same localhost session-token
  guard as SCM-03/06/07/09/11): `SourceControlPushRequest` (one appended record)
  plus `POST api/source-control/push` (body `{path}`) and the mapper
  `ToPushResponse`. A missing path is HTTP 400 `{errorCode:"PathRequired", error}`
  and Git never runs; every execution outcome is HTTP 200 with a typed body
  (`SourceControlPushResponse{outcome,isSuccess,errorCode,message,remote,failure}`)
  and success carries the tracking state re-read from Git.
  `PushRouteRejectsMissingInvalidTokenAndHostileOrigin` asserts 401 (no token),
  403 (hostile `Origin`) and 401 (wrong token) and that the remote branch is
  untouched, i.e. no unauthenticated request reached Git.
- **Frontend `PushButton.tsx` + colocated `PushButton.test.tsx` (15 tests).**
  Posts the explicit push through the privileged API only when the user clicks
  (rendering issues no request), sends exactly `{path}` in the body and the
  session token in the header (never in the URL), is gated on a configured
  upstream (`hasUpstream=false` ⇒ disabled and no Git), is disabled when Git is
  unavailable, renders the refreshed upstream on success, keeps a typed rejection
  between attempts and clears it on the next success, renders **every**
  non-success outcome in a `role="alert"` region (parameterised over all eight
  non-success outcomes — never blank, never `undefined`), surfaces the rejected
  case with the "no local commit was changed" statement, and exposes **only** the
  Push action (no pull/force/branch/fetch surface).

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Infrastructure/SourceControl/GitPushService.cs` — delivered as planned (new, 257 lines).
- `src/DocumentIDE.Web/src/features/source-control/PushButton.tsx` — delivered as planned (new).
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitPushTests.cs` — delivered as planned (new; two classes: `GitPushTests` 16 tests + `GitPushApiTests` 4 tests = **20**).

Additional files needed to satisfy the sprint (mapping recorded, matching the
SCM-11 handoff precedent):

- `src/DocumentIDE.Core/SourceControl/GitPushState.cs` — **new** typed Core
  contracts (`GitPushOutcome`, `GitPushResult`). The planned file list names only
  the Infrastructure service, but the subsystem convention (SCM-02 `GitStatusResult`,
  SCM-06 `GitStageResult`, SCM-07 `GitCommitResult`, SCM-11
  `GitRemoteStateResult`) places the typed public contract in Core. A **separate
  file** was chosen over appending to `GitRemoteState.cs` so this lane cannot
  conflict with a parallel SCM lane.
- `src/DocumentIDE.Infrastructure/SourceControl/GitCredentialRedactor.cs` — **new**
  (credential redaction, §14.4/§20/§23).
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — extended,
  **insertions only**: one `_pushes` field + ctor param, the new
  `SourceControlPushRequest`/`SourceControlPushResponse` records (appended after
  the class), a new bounded SCM-13 region (`POST push` + `ToPushResponse`), and
  the `using`-free mappings only. No existing member reformatted or reordered; the
  controller class summary line was deliberately left untouched to keep the diff
  purely additive.
- `src/DocumentIDE.Api/Program.cs` — **append-only** DI registration of
  `GitPushService` immediately after the `GitRemoteService` registration. No other
  line touched.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  appended `GitPushOutcome` + `SourceControlPushResult` (no existing type touched).
- `src/DocumentIDE.Web/src/features/source-control/PushButton.test.tsx` — new
  colocated component test (CAN-05 batch-B ruling: the repo's `tests/frontend/**`
  paths are stale; every prior feature component is colocated).

Not modified (deliberately): `TestGitRepository.cs` and `GitFetchTests.cs` — the
shared fixtures (`CreateRepositoryWithUpstream`/`WithoutUpstream`, `CommitFile`,
`CloneRemote`, `Head`, `SnapshotWorkingTree`, `WorkingTreeStatus`,
`DeleteDirectoryBestEffort`) and internal runners (`ScriptedGitProcessRunner`,
`RecordingGitProcessRunner`, `UnavailableGitProcessRunner`) already covered every
need; SCM-13 reuses them across classes rather than duplicating them.
`GitProcessRunnerOptions.cs`, `GitFailureClassifier.cs`,
`GitRemoteFailureClassifier.cs`, `GitRemoteService.cs`, `GitRepositoryService.cs`
were **not** touched (no new environment keys; no duplicated classification).

Lane safety (not touched, left to the controller/board): `docs/sprints/SPRINT-STATUS.md`,
`docs/quality/**`, `scripts/verify-release.ps1`, `src/DocumentIDE.Desktop/WebView/WebAppHost.cs`,
`sprint-manifest.json`, `01-dependency-graph.md`, `02-execution-waves.md`,
`00-sprint-index.md`. No downstream feature (pull/merge/rebase, branch management,
discard/restore, remote management) was implemented, and no new NuGet/npm
dependency was added (`package.json`/`*.csproj` unchanged).

## TDD evidence (Red → Green → Refactor)

Backend cycles were strict RED-first. The mandated first behavior was

> Local bare-remote push succeeds.

which failed first (`error CS0246: The type or namespace name 'GitPushService'
could not be found`), then went GREEN with `GitPushState.cs` + a minimal
`GitPushService`, then was refactored.

- **Cycle A (step 1, mandatory first test):** RED — `PushToLocalBareRemoteSucceeds`
  did not compile (`CS0246 GitPushService`) → GREEN with the Core contracts + the
  minimal service (`dotnet test … --filter "~GitPush"` → 1 passed).
- **Cycle B (steps 1–3, typed outcomes):** RED — added
  `RejectedPushIsTypedAndPreservesLocalCommits`, `PushWithNoUpstreamIsTypedAndRunsNoGit`,
  `PushAuthenticationFailureIsTypedAndBoundedFromGitDiagnostics`,
  `PushPermissionDeniedIsTypedFromGitDiagnostics`, `PushHostUnreachableIsTyped`,
  `PushWhenGitUnavailableIsTypedGitUnavailable`, `PushOutsideRepositoryIsTypedNoRepository`;
  4 failed with `Assert.Equal() Failure: Values differ` (got `Failed` where a typed
  outcome was required) → GREEN with the upstream gate + `ClassifyPushFailure`
  → 8 passed.
- **Cycle C (step 3, redaction):** RED — `PushFailureNeverSurfacesCredentialMaterialFromRemoteUrl`
  failed (userinfo present in the carried diagnostics) → GREEN with
  `GitCredentialRedactor` + routing every result through the single `Result(...)`
  helper → 9 passed.
- **Cycle D (steps 1/3 hardening, §18/§19/§23 edge cases):** RED-first for the new
  assertions, then GREEN — `PushCarriesOnlyStructuredPushArguments`,
  `PushUsesTheTrueWorktreeRootEvenFromASubdirectory`,
  `PushRunsThroughThePerWorktreeMutationQueue`,
  `PushProcessIsBoundedAndDisablesInteractiveCredentials`,
  `PushHandlesPathsWithSpacesUnicodeAndSpecialCharacters`,
  `PushOnDisappearedWorktreeIsTyped`, and the required step-4 regression
  `CommitDoesNotAutoPush` (16 passed).
- **Cycle E (API):** RED confirmed by `dotnet build` (`CS0246
  SourceControlPushRequest` / `SourceControlPushResponse`) → GREEN with the
  appended records + `POST push` + `ToPushResponse` + the `Program.cs` DI
  registration → `PushEndpointPublishesLocalCommit`,
  `PushEndpointReportsNoUpstreamTypedly`, `PushEndpointRequiresPath`,
  `PushRouteRejectsMissingInvalidTokenAndHostileOrigin` → **20/20**.
- **Cycle F (frontend):** RED — `npx vitest run src/features/source-control/PushButton.test.tsx`
  failed with `Error: Failed to resolve import "./PushButton" from
  "src/features/source-control/PushButton.test.tsx". Does the file exist?` →
  GREEN with the appended model types + `PushButton.tsx` → **15/15**.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitPush"` → **Passed! Failed: 0, Passed: 20, Skipped: 0, Total: 20** (16 service + 4 API).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **Failed! Failed: 2, Passed: 847, Skipped: 0, Total: 849.** Both failures are the two pre-existing release-artifact checks expected in a fresh worktree, and no other test failed:
  - `DocumentIDE.IntegrationTests.Scripts.ScriptTests.Release_Build_Embeds_Production_Web_Assets` — `Error Message: Executable should exist at …\artifacts\desktop\DocumentIDE.Desktop.exe` (`Scripts\ScriptTests.cs:line 69`).
  - `DocumentIDE.IntegrationTests.Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — `Error Message: Installer MSI artifact should exist at …\artifacts\installer\DocumentIDE-Setup.msi` (`Release\InstallerTests.cs:line 96`).

  `artifacts/` is gitignored and absent in a fresh worktree, these tests run the
  release packaging scripts, and this diff touches neither `scripts/` nor
  packaging — the same two failures are recorded in the SCM-01…SCM-11 handoffs.
  The known unrelated flake
  `Batch.BatchRenameTests.BuildPreview_ShowsOldAndNewBaseNamesDeterministicallyAndTouchesNothing`
  **passed** in this run, so no re-run was needed.
- `dotnet build DocumentIDE.sln --nologo -v minimal` → **Build succeeded, 0 Error(s), 105 Warning(s)** (all pre-existing NuGet advisories NU1603/NU1902/NU1903; no new compiler or analyzer warning from this change).
- `npx vitest run` (`src/DocumentIDE.Web`) → **Test Files 58 passed (58), Tests 425 passed (425)**, 0 failed (adding `PushButton.test.tsx`).
- `npx tsc -b` (`src/DocumentIDE.Web`) → **exit 0**.
- `npx oxlint` (`src/DocumentIDE.Web`) → **exit 0**, warnings only. The one new
  `react(only-export-components)` warning on `PushButton.tsx:26` is the established
  convention for co-locating the exported API helper (`postPush`) with the
  component — `RemoteStatus.tsx`, `CommitBox.tsx` and `ChangeActions.tsx` carry the
  same warning class. No new warning class, no errors.

## Acceptance criteria

- [x] **Push is always user initiated.** `CommitDoesNotAutoPush` (real commit flow:
  no `push` invocation, remote branch unchanged) + `PushButton.test.tsx` "never
  pushes automatically and only pushes when the user clicks" (no request on
  render; exactly one `POST /api/source-control/push` after the click). No
  production caller of `PushAsync` exists outside the API route.
- [x] **Remote rejection preserves local commits.**
  `RejectedPushIsTypedAndPreservesLocalCommits` — typed `Rejected`/`"Rejected"`
  while HEAD, `log`, porcelain status and the working-tree snapshot are identical
  before/after and the remote branch was not rewritten; the API/UI report the same
  outcome with the "no local commit was changed" statement.
- [x] **No secret or credential prompt is shown in normal UI logs.**
  `PushFailureNeverSurfacesCredentialMaterialFromRemoteUrl` (password and userinfo
  absent from message/`ErrorMessage`/stdout/stderr),
  `PushAuthenticationFailureIsTypedAndBoundedFromGitDiagnostics` (no
  `Username`/`https://` in the surfaced message), and
  `PushProcessIsBoundedAndDisablesInteractiveCredentials` (interactive prompting
  disabled, so no prompt is ever raised). The UI renders only the typed outcome,
  the curated message and the redacted diagnostic.

Required tests from the sprint doc:

- [x] Local bare-remote push succeeds — `PushToLocalBareRemoteSucceeds` (the mandatory RED-first test) + `PushEndpointPublishesLocalCommit`.
- [x] Rejected push is surfaced — `RejectedPushIsTypedAndPreservesLocalCommits`.
- [x] Authentication-required state does not hang waiting for terminal input — `PushAuthenticationFailureIsTypedAndBoundedFromGitDiagnostics` (+ the scripted, network-free diagnostic classification).
- [x] Commit does not auto-push — `CommitDoesNotAutoPush`.

Implementation steps:

- [x] Step 1 Implement push operation — `GitPushService.PushAsync` + `GitPushOutcome`/`GitPushResult`.
- [x] Step 2 Integrate configured credential helpers — invocations carry no `EnvironmentOverrides`, so the SCM-01 `ProtectedEnvironment` applies (`GIT_TERMINAL_PROMPT=0`, `GIT_ASKPASS=""`, `SSH_ASKPASS=""`, `GCM_INTERACTIVE=never`) while the configured helper stays enabled; pinned by `PushProcessIsBoundedAndDisablesInteractiveCredentials`.
- [x] Step 3 Capture remote diagnostics safely — typed failures carry the originating `GitProcessResult` with all streams redacted by `GitCredentialRedactor` through the single `Result(...)` helper.
- [x] Step 4 Add no-auto-push regression test — `CommitDoesNotAutoPush` (+ the frontend render-inertness test).

Failure / edge cases from the sprint doc:

- [x] Git is not installed / not discoverable — `PushWhenGitUnavailableIsTypedGitUnavailable`.
- [x] Repository/worktree disappears during an operation — `PushOnDisappearedWorktreeIsTyped`.
- [x] Path contains spaces, Unicode or special characters — `PushHandlesPathsWithSpacesUnicodeAndSpecialCharacters` (fixture dir and remote dir both `scm13 repo ü 東 & (# …`; the pushed SHA verifies on the bare remote).
- [x] Hook / permission / authentication / conflict / remote rejection failure — `Rejected`, `PermissionDenied`, `AuthenticationRequired`, `HostUnreachable`, generic `Failed` are all typed; a conflicted *local* index is a stage/commit concern (SCM-06/07) and does not change push classification.
- [x] Index lock unavailable / Word changed a DOCX mid-operation — N/A in one sentence: push neither reads nor writes the index and never touches document contents, so those races cannot affect it.

Definition of Done:

- [x] all implementation steps completed
- [x] required tests pass
- [x] repository affected test suite passes (2 pre-existing environmental failures, quoted above)
- [x] no required test skipped (`Skipped: 0` in every run)
- [x] no unresolved sprint-required placeholder markers (no `TODO`/`FIXME`/`NotImplementedException` added; "N/A" items carry a one-sentence justification)
- [x] `git diff` self-reviewed (additive only: 1 644 insertions, 0 deletions)
- [x] acceptance criteria mapped to evidence
- [ ] `docs/sprints/SPRINT-STATUS.md` updated — **delegated to the board/controller** (see Deviations): this lane is instructed not to touch the shared status board; the sprint was handed off READY → DONE by `ffbd735`/batch integration.
- [x] sprint handoff written using `docs/templates/SPRINT-HANDOFF-TEMPLATE.md`
- [x] coherent commit created (`f4a96ef`)

## Deviations / spec conflicts

- **`docs/sprints/SPRINT-STATUS.md` was not edited** and the sprint doc's own
  checkboxes/`**Status:** BLOCKED` line were left as-is. This is the repository
  convention (SCM-11 is DONE with the identical state in its own doc) and the
  batch instruction for this lane; marking the board is the controller's action.
  No higher-level requirement is weakened.
- **Push is gated on a configured upstream.** With no configured upstream the
  service returns typed `NoUpstream`, runs **no** `git push`, and the component
  disables the action. This mirrors SCM-11's conservative reading of spec 13
  §14.2/§14.4 (there is no upstream to push to, and silently guessing a default
  remote is exactly the "silently broadened scope" the safety rules forbid). See
  Risks for the usability consequence.
- **One extra Core file beyond the planned list** (`GitPushState.cs`) and one
  extra Infrastructure helper (`GitCredentialRedactor.cs`); both follow the
  SCM-02/06/07/11 placement precedent and are consumed by the service and API. No
  lane-owned file was modified and no duplicate abstraction was introduced.
- **Frontend test path** follows the CAN-05 batch-B ruling (colocated
  `PushButton.test.tsx`), consistent with every prior feature component.
- **`PushButton.tsx` is intentionally not wired into `SourceControlPanel.tsx`**
  (consistent with `RemoteStatus.tsx`, which is also unwired) and takes
  `hasUpstream`/`gitUnavailable` from its host rather than reading remote state
  itself: that keeps the "no Git until the user clicks" property provable (any
  mount-time read would already be a Git invocation) and avoids duplicating
  SCM-11's read. The API route is fully exercised by `GitPushApiTests`; wiring the
  pane is a later UI task.
- **No new environment key was introduced** — `GIT_CONFIG_*` remains untouched
  (SCM-11 deliberately avoided the reserved namespace), and the credential helper
  is left enabled.
- **No second failure-classification list** — SCM-01 `GitFailureClassifier` and
  SCM-11 `GitRemoteFailureClassifier` are consumed read-only.

## Risks discovered

- **Push serializes with fetch/stage/commit on the same worktree.** `push` is a
  mutating command for `GitCommandClassifier`, so it always takes the per-worktree
  queue slot (desired, proven by `PushRunsThroughThePerWorktreeMutationQueue`) —
  a push therefore waits behind an in-flight stage/commit/fetch on that worktree.
  Acceptable for correctness; a read-only fast path is not possible for push.
- **The "does not hang" claim is bounded, not absolute.** The push invocation
  carries no interactive prompting and the invocation is bounded by the runner's
  120 s default timeout as a backstop; a misbehaving credential helper that ignores
  `GCM_INTERACTIVE`/askpass is still only cut off by that timeout. The scripted
  diagnostic test proves the typed, prompt-free path, not the misbehaving-helper
  path.
- **Classification is text-based** and therefore pinned to `LC_ALL=C` (SCM-01
  runner policy). An unmapped wording degrades to the generic `Failed` outcome —
  never to a silent success — and the raw (redacted) diagnostic stays on the
  typed failure.
- **`HostUnreachable` also covers "the remote is not a usable repository"**
  (inherited SCM-11 F9 product decision, no split). Both conditions require the
  same corrective action and both retain Git's exact redacted diagnostic.
- **A no-upstream branch cannot be pushed from Document IDE at all.** Since branch
  management is explicitly out of scope, the user must configure the upstream
  outside the app; the pane reports the explicit `NoUpstream` state instead of a
  silently guessed remote. Recorded as the product trade-off of the §14.2 reading.
- **Redaction is URL-userinfo-shaped.** `GitCredentialRedactor` covers
  `scheme://userinfo@host` forms (including `http`, `https`, `git`, `ssh`,
  `file`) and deliberately leaves scp-style `user@host:path` locations untouched
  (they carry no password). A credential embedded in a *different* diagnostic
  shape (e.g. a literal token in a hook message) is not detected — it is also not
  something Git prints from the configured remote URL.
- **No retry affordance** for transient remote failures: surfaced typed and left
  to the pane, matching SCM-11.

## Next eligible sprint(s)

- No downstream SCM sprint was opened or anticipated by this change; the SCM plan
  items that remain (branch management, pull/merge/rebase, discard/restore) are
  out of scope for SCM-13 and unblocked solely by their own `Depends On` rows.
- The unwired `PushButton`/`RemoteStatus` pane integration is the natural
  follow-up for whichever sprint owns the Source Control pane composition.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — push
  performs no in-place mutation of user content at all: it runs only `git push`
  through the SCM-01 controlled runner (which updates the remote branch), never
  touches the index or the working tree, and `RejectedPushIsTypedAndPreservesLocalCommits`
  proves the working-tree snapshot (length + mtime + SHA-256) and porcelain status
  are byte-identical before/after. Every route inherits the privileged localhost
  session guard (`PushRouteRejectsMissingInvalidTokenAndHostileOrigin`).
- [x] **no arbitrary shell command can be injected through a filename/path** — no
  shell exists (structured `ArgumentList`); the invocation is exactly `["push"]`
  and the caller path is used only as the discovery input, never concatenated into
  a command line (`PushCarriesOnlyStructuredPushArguments`,
  `PushHandlesPathsWithSpacesUnicodeAndSpecialCharacters`). The API body carries
  only `{path}` and the frontend test pins `JSON.parse(init.body)` to exactly
  `{path}`.
- [x] **no repository escape is possible through path handling** — the push runs at
  the true worktree root resolved by Git itself (`rev-parse --show-toplevel`
  through `GitRepositoryService.DiscoverAsync`), not at the caller-supplied path
  (`PushUsesTheTrueWorktreeRootEvenFromASubdirectory`); a path outside Git is typed
  `NoRepository` without Git running; no local path is exposed by any push
  response.
- [x] **no secret/credential output is exposed in UI logs** — the runner's
  protected environment disables interactive prompting (no credential is ever
  collected) while leaving the configured helper enabled, and every result is
  built through the single redacting `Result(...)` helper, so URL userinfo can
  never appear in an outcome message, error or carried stream
  (`PushFailureNeverSurfacesCredentialMaterialFromRemoteUrl`,
  `PushAuthenticationFailureIsTypedAndBoundedFromGitDiagnostics`,
  `PushProcessIsBoundedAndDisablesInteractiveCredentials`). The UI renders only
  the typed outcome/message.
- [x] **no destructive action silently broadened its scope** — push is pinned to
  exactly `["push"]` (no `--force`/`-f`/`--force-with-lease`/`--all`/`--tags`/
  `--mirror`/`--delete`/`--set-upstream`/`--prune`, no branch/remote/refspec, no
  `pull`/`merge`/`rebase`/`commit`, `--no-verify` rejected too), and with no
  configured upstream it runs nothing at all.
- [x] **relevant higher-level safety rules remain stronger than convenience** — the
  local filesystem and Git's own refs remain the source of truth (no parallel
  push state is persisted); all classification is literal and `LC_ALL=C`-pinned
  (no semantic/fuzzy matching); failures are typed rather than silently downgraded;
  no force-push, no default-remote guessing, no branch management, and no
  weakening of the SCM-01/SCM-11 guards. The file-safety
  recovery-snapshot/temp-mutation/atomic-replace rule is **N/A** in one sentence:
  this sprint mutates no user file content — it publishes existing commits through
  Git's own `push` and never rewrites local history.

## Controller review round 1 (fix-up applied)

The controller independently reviewed this branch and **falsified one claim**, then fixed it here
(TDD: RED first, then GREEN).

**Finding (credential exposure, narrow but real):** `GitCredentialRedactor`'s userinfo character
class excluded `@` (`[^/@\s]*`). Git echoes the remote URL verbatim, so a password that itself
contains the userinfo separator was only redacted up to the **first** `@`, leaving the tail behind:

    https://alice:p@s3cr3t@example.invalid/repo.git
      before: https://***@s3cr3t@example.invalid/repo.git   <- password tail leaked

That is a direct counterexample to the "no credential material can reach an outcome message, error
or log line" claim. The class doc even documented the exclusion as intentional, so the gap was not
noticed by the original test (which used a password without `@`).

**Fix:** the userinfo class is now `[^/\s]*` and greedy - it still cannot cross `/` or whitespace
(so it can never swallow the path or a following token) but consumes the **whole** userinfo up to the
last `@` of the authority.

**Evidence:** new test `PushFailureRedactsPasswordContainingAnAtSign` written first and observed
failing (the tail `s3cr3t` surfaced in `StandardOutput`/`StandardError`); after the fix the whole
`GitPush` suite is **21 passed / 0 failed / 0 skipped** (was 20).

**Residual limit (unchanged, stated honestly):** redaction covers explicit-scheme
`scheme://userinfo@host` URLs only. scp-style `user@host:path` locations carry no password and are
deliberately left untouched; a credential embedded in some other textual shape is not covered.