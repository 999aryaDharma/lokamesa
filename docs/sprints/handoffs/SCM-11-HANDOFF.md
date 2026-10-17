# Sprint Handoff — SCM-11

**Status:** DONE
**Branch:** `feat/git-remote-state-fetch`
**Commits:** `feat(scm): git-remote-state-fetch` at the tip — see `git log --oneline b9b3e01..HEAD` (base `b9b3e01`)

## What changed

- New `GitRemoteService` (Infrastructure) implements the two sprint behaviors
  against real Git (spec 13 §14.1/§14.2/§18/§19/§20/§23), through the SCM-01
  `IGitProcessRunner` (structured `ArgumentList`, never a shell string):
  - `GetRemoteStateAsync` — reads the configured upstream reference, remote name
    and ahead/behind counters, and
  - `FetchAsync` — runs one explicit `git fetch` and re-reads the tracking state.
- **Upstream/tracking info is read, not re-parsed.** The state is projected from
  the same machine-readable status the Source Control pane already uses
  (`git status --porcelain=v2 -z --branch`, SCM-02): `branch.upstream` and
  `branch.ab`. No second porcelain parser was added.
- **No-upstream is a first-class typed state** (`GitRemoteOutcome.NoUpstream`,
  resolved and non-error): the state is present with `HasUpstream=false` and
  `Ahead`/`Behind` **`null`** — the pane renders an explicit "no upstream"
  message and never a bogus `0/0` (spec 13 §14.2).
- **Ahead/behind are reported** from Git's `branch.ab` counts and refresh after a
  fetch (behind moves 0 → 1 against the fixture remote).
- **Fetch never changes document or working-tree contents and never touches the
  index** (spec 13 §14.1). The service runs exactly `["fetch"]` — no
  `pull`/`push`/`merge`/`rebase`, no `--prune`/`--all`/`--tags`/`--force`
  (pinned by `FetchCarriesOnlyStructuredFetchArguments`). The fetch test asserts
  path + length + last-write-time + SHA-256 of every non-`.git` file, the
  porcelain status and the staged-index name list are byte-identical before and
  after the fetch.
- **Fetch runs against the resolved true worktree root** (`rev-parse
  --show-toplevel`), which is also the SCM-01 per-worktree mutation queue key:
  `fetch` is treated as a mutation by `GitCommandClassifier`, so concurrent
  fetch/stage/commit on one worktree serialize.
  `FetchUsesTheTrueWorktreeRootEvenFromASubdirectory` pins the working directory
  even when a nested subdirectory is passed, and
  `FetchRunsThroughThePerWorktreeMutationQueue` pins `MaxObservedConcurrency == 1`.
- **Remote errors are typed** (`GitFetchOutcome`): `Succeeded`, `NoUpstream`,
  `NoRepository`, `GitUnavailable`, `AuthenticationRequired`, `HostUnreachable`,
  `Rejected`, `PermissionDenied`, `Failed` — never a silent fallback. Failures
  carry the originating `GitProcessResult`; non-successes carry an `ErrorCode`.
- **Authentication does not hang** (spec 13 §14.4/§18): the SCM-01 runner pins
  `GIT_TERMINAL_PROMPT=0` and closes child stdin; the service maps
  `GitProcessErrorKind.GitAuthRequired` (or Git's prompt-disabled wording) to the
  typed `AuthenticationRequired`. The test asserts it returns well under 10 s and
  that no credential/host text is copied into the surfaced message.
- API (`SourceControlController`, same privileged localhost guard as
  SCM-03/06/07): `GET api/source-control/remote?path=…` and
  `POST api/source-control/fetch` (body `{path}`). A missing path is HTTP 400
  `{errorCode:"PathRequired", error}`; every resolved/failed state is HTTP 200
  with a typed body (`SourceControlRemoteResponse` /
  `SourceControlFetchResponse{outcome,isSuccess,errorCode,message,remote,failure}`).
  Success carries the tracking state re-read from Git.
- Frontend: new `RemoteStatus.tsx` renders the upstream reference and the
  ahead/behind counters, shows an explicit "No upstream configured for this
  branch." state (never `0/0`), posts the explicit Fetch through the privileged
  API, refreshes the counters from the response, surfaces typed failures in a
  `role="alert"` region, disables Fetch when there is no upstream or Git is
  unavailable, and exposes **only** Fetch — no push/pull/branch surface.

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Core/SourceControl/GitRemoteState.cs` — **new** typed Core
  contracts (`GitRemoteOutcome`, `GitRemoteState`, `GitRemoteStateResult`,
  `GitFetchOutcome`, `GitFetchResult`). Mirrors the SCM-02 `GitStatusResult` /
  SCM-06 `GitStageResult` / SCM-07 `GitCommitResult` placement (see Additional).
- `src/DocumentIDE.Infrastructure/SourceControl/GitRemoteService.cs` —
  delivered as planned (new).
- `src/DocumentIDE.Web/src/features/source-control/RemoteStatus.tsx` —
  delivered as planned (new).
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitFetchTests.cs` —
  delivered as planned (new; two classes: `GitFetchTests` 16 tests +
  `GitFetchApiTests` 6 tests = 22).

Additional files needed to satisfy the sprint (mapping recorded):

- `src/DocumentIDE.Core/SourceControl/GitRemoteState.cs` — the planned file list
  names only the Infrastructure service, but the subsystem convention (SCM-02/06/07)
  places the typed public contract in Core; the service and the API both consume it.
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — extended:
  3 DTOs (`SourceControlRemoteResponse`, `SourceControlFetchRequest`,
  `SourceControlFetchResponse`) + 2 routes (`remote`, `fetch`) + mapping helpers,
  same file, no duplicate controller. Edits are bounded to a new `_remotes`
  field/ctor param, the class summary line and a new SCM-11 region; no existing
  member was reformatted.
- `src/DocumentIDE.Api/Program.cs` — **append-only** DI singleton registration of
  `GitRemoteService` at the very end of the existing SCM block (after
  `GitCommitService`). No other line touched.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  `GitRemoteStatus` + `GitFetchOutcome` unions, `SourceControlRemoteModel`,
  `SourceControlFetchResult`.
- `src/DocumentIDE.Web/src/features/source-control/RemoteStatus.test.tsx` —
  6 colocated component tests (CAN-05 batch-B ruling; the repo's
  `tests/frontend/**` paths are stale).

`TestGitRepository.cs` was **not** modified: the shared fixture (`RunGit`,
`RunGitCaptureUtf8`, `Create`) already covered every need, and fetch fixtures
were kept self-contained in `GitFetchTests.cs`.

Lane safety: `docs/sprints/SPRINT-STATUS.md`, `docs/quality/**`,
`scripts/verify-release.ps1`, `docs/sprints/sprint-manifest.json`,
`docs/sprints/01-dependency-graph.md`, `docs/sprints/02-execution-waves.md` and
`docs/sprints/00-sprint-index.md` were **not touched** (left to the controller).
No next-sprint feature (push/pull/merge/branch management/discard/restore) was
implemented.

## TDD evidence

Backend cycles were strict RED-first; the mandated first behavior was

> Configured upstream is parsed correctly.

which failed first (`CS0246: GitRemoteService` not found), then went GREEN with
the minimal `GetRemoteStateAsync`, then refactored.

- Cycle 1 (read upstream): RED `ConfiguredUpstreamIsParsedCorrectly` (build error,
  missing type) → GREEN with `GitRemoteState.cs` + `GetRemoteStateAsync` (1 test).
- Cycle 2 (no-upstream / ahead-behind / no-repo / git-unavailable): added
  `NoUpstreamIsATypedFirstClassState`, `AheadBehindCountsMatchFixtureRemote`,
  `RemoteStateOutsideRepositoryIsTypedNoRepository`,
  `RemoteStateWhenGitUnavailableIsTypedGitUnavailable` → GREEN 5/5 (steps 1–2
  covered by the cycle-1 implementation).
- Cycle 3 (fetch): RED `CS1061 FetchAsync` → GREEN with `FetchAsync` +
  `ClassifyFetchFailure` + marker arrays (10 tests).
- Cycle 4 (typed failures & edge cases): `FetchHostUnreachableIsTyped`,
  `FetchAuthenticationRequiredIsTypedWithoutHanging` (scripted runner, no network),
  `FetchHandlesPathsWithSpacesUnicodeAndSpecialCharacters`, `FetchOutsideRepositoryIsTypedNoRepository`,
  `FetchWhenGitUnavailableIsTypedGitUnavailable`, `FetchOnDisappearedWorktreeIsTyped`
  + `ScriptedGitProcessRunner`/`RecordingGitProcessRunner` → GREEN **16/16**.
- Cycle 5 (API): RED confirmed by `dotnet build` (`CS0246
  SourceControlRemoteResponse` etc.) → GREEN controller routes + mapping + DI;
  `RemoteRoutesRejectMissingInvalidTokenAndHostileOrigin` added on the same pass
  → **22/22** across both classes.
- Cycle 6 (frontend): RED `RemoteStatus.test.tsx` failed on the missing
  `./RemoteStatus` import → GREEN `sourceControlModels.ts` types + `RemoteStatus.tsx`
  → **6/6**.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitFetch"` → **22 passed / 0 failed / 0 skipped** (16 service + 6 API).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **809 passed / 2 failed / 0 skipped** (811 total). Both failures are the documented REL-03-sanctioned release-artifact checks: `ScriptTests.Release_Build_Embeds_Production_Web_Assets` (missing `artifacts/desktop/DocumentIDE.Desktop.exe`) and `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` (missing installer artifacts). `artifacts/` is gitignored and absent in a fresh worktree; this diff touches neither `scripts/` nor packaging. The same two failures are recorded in the SCM-01…SCM-07 handoffs.
- Sibling-lane regression check: `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitCommit|FullyQualifiedName~GitStage|FullyQualifiedName~GitStatus|FullyQualifiedName~GitRepository"` → **102 passed / 0 failed / 0 skipped** (SCM-07 `GitCommitOutcome` vocabulary and `CommitNeverTouchesARemote` intact).
- `dotnet build DocumentIDE.sln --nologo` → **Build succeeded, 0 errors** (105 pre-existing warnings).
- `dotnet test tests/DocumentIDE.ArchitectureTests` → 12 passed.
- `dotnet test tests/DocumentIDE.Core.Tests` → 459 passed (Core touched via `GitRemoteState.cs`).
- `dotnet test tests/DocumentIDE.SafetyTests` → 24 passed.
- `dotnet test tests/DocumentIDE.SecurityTests` → 91 passed.
- `dotnet test tests/DocumentIDE.GoldenTests` → 39 passed.
- `npx vitest run` (`src/DocumentIDE.Web`) → **56 files / 398 tests passed, 0 failed**.
- `npx tsc -b` (`src/DocumentIDE.Web`) → exit 0.
- `npx oxlint` (`src/DocumentIDE.Web`) → exit 0; the two `react(only-export-components)` warnings on `RemoteStatus.tsx` are the same class SCM-06 `ChangeActions.tsx` / SCM-07 `CommitBox.tsx` already carry for co-locating the exported API helper (`getRemoteStatus`, `fetchRemote`) with the component — the established convention. No new warning class and no errors.

## Acceptance criteria

- [x] **No-upstream state is explicit** — `NoUpstreamIsATypedFirstClassState`
  (service: `GitRemoteOutcome.NoUpstream`, resolved, `Ahead`/`Behind` null) +
  `RemoteEndpointReportsNoUpstreamTypedly` (API: `status:"NoUpstream"`, null
  counters, no failure) + `RemoteStatus.test.tsx` ("no upstream" rendered, no
  `remote-ahead-behind` region, Fetch disabled).
- [x] **Fetch never changes document contents** —
  `FetchUpdatesTrackingStateWithoutChangingWorkingTreeContent` (path/length/mtime/
  SHA-256 + porcelain status + staged-index name list identical before/after) +
  `FetchEndpointFetchesAndReturnsUpdatedState` (same assertions through the API).
- [x] **Remote errors are typed** — `FetchHostUnreachableIsTyped`,
  `FetchAuthenticationRequiredIsTypedWithoutHanging`,
  `FetchWhenGitUnavailableIsTypedGitUnavailable`,
  `FetchOutsideRepositoryIsTypedNoRepository`, `FetchOnDisappearedWorktreeIsTyped`,
  `FetchWithNoUpstreamIsTypedAndRunsNoGit`; the API maps every outcome to HTTP 200
  with a typed body and the frontend surfaces failures in `role="alert"`.

Required tests from the sprint doc:

- [x] Configured upstream is parsed correctly — `ConfiguredUpstreamIsParsedCorrectly` (the mandatory RED-first test).
- [x] Ahead/behind counts match fixture remote — `AheadBehindCountsMatchFixtureRemote` (ahead 2 / behind 1).
- [x] Fetch updates tracking state without changing working-tree content — `FetchUpdatesTrackingStateWithoutChangingWorkingTreeContent` + the API variant.

Implementation steps:

- [x] Step 1 read upstream/tracking info — `GetRemoteStateAsync`.
- [x] Step 2 render ahead/behind — `RemoteStatus.tsx` counters + API payload.
- [x] Step 3 implement fetch — `FetchAsync` + typed classification.
- [x] Step 4 local bare-remote fixtures — `CreateBareRemote` + `CreateRepositoryWithUpstream` + `CloneRemote` (all filesystem paths, no network).

Additional failure/edge cases from the sprint doc:

- [x] Git not installed / not discoverable — `RemoteStateWhenGitUnavailableIsTypedGitUnavailable`, `FetchWhenGitUnavailableIsTypedGitUnavailable`.
- [x] Repository/worktree disappears during an operation — `FetchOnDisappearedWorktreeIsTyped`.
- [x] Path contains spaces/Unicode/special characters — `FetchHandlesPathsWithSpacesUnicodeAndSpecialCharacters`.
- [x] Hook / permission / authentication / conflict / remote rejection — `AuthenticationRequired`, `PermissionDenied`, `Rejected`, `HostUnreachable`, generic `Failed` are typed (conflict semantics remain SCM-14's; a conflicted worktree does not block a fetch).
- Index-lock is a local mutation concern (SCM-06/07); `git fetch` does not write the index, so it is N/A here.

## Deviations / spec conflicts

- One extra Core file beyond the planned list (`GitRemoteState.cs`); it is the
  typed public contract consumed by Infrastructure/API and follows the
  SCM-02/06/07 Core placement precedent. No lane file touched; no higher-level
  requirement weakened.
- **Fetch is gated on a configured upstream.** With no configured upstream the
  service returns typed `NoUpstream`, runs **no** `git fetch`, and the frontend
  disables the action. This is a conservative reading of spec 13 §14.2 (there is
  no upstream to fetch from) rather than a bare `git fetch` (which would fetch the
  default remote's configured refspecs even with no tracking branch). See Risks.
- **Frontend test path** follows the CAN-05 batch-B ruling (colocated
  `RemoteStatus.test.tsx`), consistent with all prior feature components.
- **Fetch failure classification is local to `GitRemoteService`.** The SCM-01
  `GitFailureClassifier.cs` was out of this sprint's ownership, so remote-specific
  signatures (host unreachable, remote rejected, permission) are matched against
  the runner's `LC_ALL=C`-pinned diagnostics, with the SCM-01 `ErrorKind`
  (`GitAuthRequired`, `GitPushRejected`, `GitPermissionDenied`, `GitNotInstalled`)
  taken first where it already exists.

## Risks discovered

- **`fetch` is classified as a mutation** by the SCM-01 `GitCommandClassifier`, so
  it always runs through the per-worktree mutation queue. That is the desired
  serialization (proven by `FetchRunsThroughThePerWorktreeMutationQueue`) but it
  means a fetch serializes behind (and ahead of) stage/commit on the same
  worktree — no read-only fast path. Acceptable for correctness; a future sprint
  could add a parallel read-only lane if needed.
- **Fetch with no upstream is a no-op** (see Deviations). A consumer that wants to
  fetch a remote that has no tracking branch configured must scope that explicitly;
  the current contract reports `NoUpstream` and changes nothing.
- **Remote-error classification is text-based** and therefore pinned to
  `LC_ALL=C` (the SCM-01 runner policy). A locale override would degrade an
  unmapped failure to the generic `Failed` outcome — never to a silent success.
- **`HostUnreachable` covers "remote path is not a usable repository"** (e.g. a
  local `origin` URL pointing at a missing directory or a `repository not found`
  message), matching the test fixture; a genuinely distinct "remote is not a git
  repository" outcome is not split out.
- **A `Failed` fetch leaves the prior tracking state as-is**; the frontend keeps
  rendering the last known counters and shows the typed error, rather than
  pretending the counters are stale-zero.
- **No retry affordance** for transient network failures — surfaced typed and left
  to the pane.

Consumer notes for SCM-13 (push):

- Push must be a **separate** explicit flow with its own typed outcomes; do not
  attach it to commit or fetch. Reuse `GitRemoteService`'s remote-state read to
  render the new ahead counter after a push, and keep the SCM-01 per-worktree
  queue discipline (pass the workspace/active-document path and let the service
  resolve the true worktree root — that root is the queue key).
- The `SourceControlRemoteResponse` / `SourceControlFetchResponse` shape and the
  "HTTP 400 for input rejection / HTTP 200 typed body for every execution outcome"
  split are the contract to preserve.

## Next eligible sprint(s)

- SCM-13 (Push) depends on SCM-11 and SCM-07 (both DONE) — eligible per its own
  `Depends On` row. This sprint deliberately opened no push/pull/merge/branch
  surface.
- SCM-08 (History) and SCM-09 (Discard) are unaffected by this change.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the only
  writes are Git's own remote-tracking ref updates from `git fetch` through the
  SCM-01 controlled runner; the service writes no document and never touches the
  index (proven by the content/mtime/index assertions), and every route carries
  the privileged localhost session guard (`RemoteRoutesRejectMissingInvalidTokenAndHostileOrigin`).
- [x] **no arbitrary shell command can be injected through a path** — no shell
  exists (structured `ArgumentList`); the invocation is exactly `["fetch"]` and the
  path is used only as the working directory, resolved by discovery, never
  concatenated into a command line (`FetchCarriesOnlyStructuredFetchArguments`,
  `FetchHandlesPathsWithSpacesUnicodeAndSpecialCharacters`).
- [x] **no repository escape is possible through path handling** — discovery
  validates containment through the SCM-01 `GitRepositoryService`
  (`rev-parse --show-toplevel`) and the fetch runs at the resolved true worktree
  root; non-Git folders are typed `NoRepository` without Git running.
- [x] **no secret/credential output is exposed in UI logs** — responses carry typed
  outcomes and Git's own diagnostics only; the runner keeps `GIT_TERMINAL_PROMPT=0`
  and closes stdin so no interactive credential is ever collected, and
  `FetchAuthenticationRequiredIsTypedWithoutHanging` asserts no `Username`/host text
  is copied into the surfaced message.
- [x] **no destructive action silently broadened its scope** — fetch is pinned to
  exactly `["fetch"]` (no `--prune`/`--all`/`--tags`/`--force`, no
  `pull`/`push`/`merge`/`rebase`), and with no upstream it runs nothing at all.
- [x] **relevant higher-level safety rules remain stronger than convenience** — the
  local filesystem and the real Git index/refs remain the source of truth (no
  parallel persistence of remote state); all classification is literal and
  `LC_ALL=C`-pinned (no semantic/fuzzy matching); typed failures instead of silent
  fallbacks; no push/pull/merge/branch behavior added. The file-safety
  snapshot/temp/atomic-replace rule is N/A in one sentence: this sprint performs no
  in-place mutation of user file content — it reads tracking state and updates
  remote-tracking refs through Git's own commands.
