# Sprint Handoff - SCM-17

**Status:** DONE
**Branch:** `feat/source-control-regression-release-hardening`
**Commits:** single commit — see `git log` for `test(scm): source-control-regression-release-hardening`

## What changed

- **Real contract bug fixed (the sprint's only production change).**
  `GitWorktreeMutationQueue.EnqueueAsync` documented that a token cancelled
  while queued means "the operation never runs", but `SemaphoreSlim.WaitAsync`
  checks the token only on entry: if a `Release()` raced with `Cancel()`, the
  acquire could succeed with an already-cancelled token and the Git mutation
  then ran. A `cancellationToken.ThrowIfCancellationRequested()` re-check now
  runs immediately after the acquire and before any work; the `finally {
  gate.Release(); }` remains correct. This restores the documented behavior and
  changes nothing else.
- **`docs/quality/source-control-release-checklist.md`** (new): the SCM
  release gate. 33 automated gate items each mapped to the test that proves it,
  the sprint's security/file-safety gate with evidence, three explicitly
  justified manual Windows checks, an evidence snapshot of this sprint's runs,
  and eight residual risks (including the missing `GET
  api/source-control/diff` route and the duplicated SCM registration in
  `Program.cs` + `SourceControlServiceRegistration`).
- **`SourceControlRegressionSuite`** (new, 13 tests): fixture matrix (unborn
  HEAD, plain folder/no repository, dirty+remote, diverged, conflicted, worktree
  set) proving every read/write route returns a typed outcome instead of
  throwing; concurrency (stage/commit/fetch/pull/discard serialize exactly once
  per worktree, different worktrees stay independent, eight concurrent stages
  leave a valid index); remote integration against a real local bare remote
  (fetch/pull/push round trip, non-fast-forward push rejection, unreachable
  remote); and the worktree create/open/remove/prune lifecycle through the
  privileged HTTP API.
- **`GitProcessSecurityTests`** (new, 70 cases in `DocumentIDE.SecurityTests`):
  hostile filenames (spaces, Unicode, emoji, leading dash, quotes, `& ; %VAR%
  ! $() ^`, brackets, backticks) staged verbatim as one structured argument;
  Windows-illegal shell metacharacters (`| > " ? *`) kept as data and failed
  typed; traversal/absolute/UNC/drive-root/colon/NUL/malformed paths rejected
  with no index mutation; control characters forwarded as data and failed
  typed; worktree ref/target injection rejected before any Git process; a real
  repository path with spaces+Unicode+emoji; and credential redaction from a
  remote URL. Every test asserts the recorded argument vector, and rejected
  local validation asserts zero process spawns.
- Queue tests extended append-only with two new regression tests plus a
  deterministic reproduction of the release-vs-cancel race (see TDD evidence).

## Files changed

Production:

- `src/DocumentIDE.Infrastructure/SourceControl/GitWorktreeMutationQueue.cs` —
  acquire-time cancellation guard (the contract fix).

Tests (new):

- `tests/DocumentIDE.IntegrationTests/SourceControl/SourceControlRegressionSuite.cs` — 13 tests.
- `tests/DocumentIDE.SecurityTests/SourceControl/GitProcessSecurityTests.cs` — 70 cases (new folder `SourceControl/` in that project).

Tests (append-only):

- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeMutationQueueTests.cs` — 2 new tests + a test-only single-threaded `SynchronizationContext` helper.

Docs (new):

- `docs/quality/source-control-release-checklist.md`
- `docs/sprints/handoffs/SCM-17-HANDOFF.md` (this file)

Not touched (verified by `git status`): `docs/sprints/SPRINT-STATUS.md`,
`scripts/verify-release.ps1`, `docs/quality/v1-readiness-report.md`, manifests,
roadmaps, `App.tsx`, `.pen` files, any frontend source, any other SCM service.

## Verification

- `dotnet build DocumentIDE.sln` → **0 errors** (162 warnings, all pre-existing;
  the new test file introduced no new compiler warning — the one xUnit1031
  warning from the deterministic pumping test is suppressed locally with a
  documented pragma).
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControlRegressionSuite"` → **PASS 13/13, 0 skipped**.
- `dotnet test tests/DocumentIDE.SecurityTests --filter "FullyQualifiedName~GitProcessSecurityTests"` → **PASS 70/70, 0 skipped**.
- `dotnet test tests/DocumentIDE.IntegrationTests` → **1029 passed / 2 failed / 0 skipped (1031 total)**.
  - Both failures are the known environmental release-artifact checks, exactly
    as the sprint brief predicted for a fresh worktree without `artifacts/`:
    `ScriptTests.Release_Build_Embeds_Production_Web_Assets` and
    `Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`.
- Full suite repeated for flake evidence:
  - Run 2 → **1028 passed / 3 failed / 0 skipped**: the two environmental
    failures **plus** `Pdf.PdfMergeMemoryTests.Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes`.
  - Run 3 → **1029 passed / 2 failed / 0 skipped** (environmental only).
  - The PDF memory test passed in isolation (296 ms) and in the other two full
    runs. It is a **pre-existing, non-SCM flake**: the test samples process-wide
    `GC.GetTotalMemory(forceFullCollection: true)` every 5 ms against a 12 MB
    bound while xUnit runs other classes in parallel. Not disabled, not
    quarantined; recorded as residual R-6 in the release checklist.
- `dotnet test tests/DocumentIDE.SecurityTests` → **PASS 161/161, 0 skipped**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **PASS 12/12, 0 skipped**.
- `dotnet test tests/DocumentIDE.Core.Tests` → **PASS 459/459, 0 skipped**.
- Cancellation loop: the existing queued-cancellation test **and** the new
  deterministic race test were run **10 consecutive times** with `--no-build`,
  **10/10 both green, 0 failures** (20/20 test executions).
- Frontend suites were not re-run: this worktree has no
  `src/DocumentIDE.Web/node_modules` (untracked, not shared), and no frontend
  file changed. The colocated Vitest suites remain the automated evidence and
  run in the release pipeline (residual R-7).

## TDD evidence

- **Cycle 1 — queue cancellation guard (the only behavioral change).**
  - The controller-suggested form (already-cancelled token) was written first
    and **passed before the fix**, because `SemaphoreSlim.WaitAsync` checks
    `IsCancellationRequested` before it ever touches the gated state. That form
    is kept as a cheap regression test (`EnqueueAsync_AlreadyCancelledToken_OperationNeverRuns`)
    but it is *not* the RED demonstration.
  - A deterministic release-vs-cancel reproduction was then written:
    `EnqueueAsync_CancelledConcurrentlyWithRelease_OperationNeverRuns`. It
    installs a test-owned single-threaded `SynchronizationContext`, releases
    the gate, cancels the token, and only then pumps the queued continuation —
    eliminating timing luck. **RED confirmed**: before the guard the queued
    continuation ran, `queued.IsCanceled` was `false` and the operation flag
    was set.
  - After adding `ThrowIfCancellationRequested()` immediately after the acquire:
    **GREEN 9/9** queue tests, then **10/10** repetitions of both cancellation
    tests. `EnqueueAsync_CancelledWhileQueued_OperationNeverRuns` (the test that
    failed once under load in the controller's full-suite run) is stable.
- **Cycle 2 — regression/characterization suites.** These add no product
  behavior; they pin the already-approved SCM-02…SCM-18 contract. Per the
  sprint's hard constraint ("no features, no product behavior change except the
  queue guard"), they cannot have a legitimate RED stage against missing
  behavior. They were authored from the contract, run against real Git
  repositories, and passed. Where a first draft encoded an assumption that was
  wrong, the *test* was corrected to the observed, already-specified behavior
  and the finding recorded rather than the product changed:
  - Windows cannot name files containing `| > " ? *`, so the real-file matrix
    was narrowed to Windows-valid names and a separate client-supplied-path test
    proves those characters still travel as one structured argument and fail
    typed (no shell).
  - `.git/config` is silently ignored by `git add` (exit 0, no index change), so
    the test pins "never staged" instead of a non-success outcome and records
    the imprecise typed outcome as R-4.
  - The diff surface resolves a contained `.git/config` and can render its
    text; not changed (outside the sprint's hard constraint), recorded as R-3.

## Acceptance criteria

- [x] **All SCM release-gate tests are automated or explicitly justified manual
  Windows tests.** `docs/quality/source-control-release-checklist.md` §1–§3 map
  SC-01…SC-33 to named automated tests; §4 lists exactly three manual checks
  (Word save → debounce with real Word/WebView, credential-helper behavior,
  OneDrive/UNC/>260-char roots) with the automated seam that covers the logic
  and the reason real-hardware execution is required.
- [x] **No branch-management scope has leaked into V1.** No branch lifecycle was
  added. `SourceControlPanelTests.BranchManagementRoutesDoNotExist`,
  `GitWorktreeMutationTests.CreateInvokesGitWithExactArgumentVectorFromTheResolvedRoot`
  (no `-b`/`-B`), and the colocated `WorktreeCreateDialog.test.tsx` remain the
  evidence; the only production file touched is the mutation queue.
- [x] **Source Control can be disabled without breaking non-Git Document IDE
  workflows.** A plain folder yields typed `NoRepository`/`NotARepository` on
  every route (`FixtureMatrix_PlainFolder_EveryRouteReturnsNoRepositoryWithoutThrowing`),
  Git is never initialized by correlation/history reads, and Document IDE
  History remains usable without Git
  (`WorkSessionCorrelationTests.CorrelationWorksForANonGitFolderWithoutInitialisingGit`,
  `BatchOperationHistoryRemainsUsableWithoutGit`). Caveat recorded: there is no
  explicit runtime feature flag; "disabled" means "no repository / Git
  unavailable", which the subsystem treats as inert, typed state.

Sprint implementation steps:

- [x] Step 1 fixture repository matrix — `FixtureMatrix_*` (6 fixtures: unborn,
  plain folder, dirty+remote, diverged, conflicted, worktree set).
- [x] Step 2 Windows path/Unicode/injection — 70 security cases + the
  path/Unicode cases in the remote/worktree suites.
- [x] Step 3 concurrency — serialize-per-worktree, independent worktrees,
  index-corruption and cancellation-race tests.
- [x] Step 4 remote/worktree integration — bare remote round trip, push
  rejection, unreachable remote, worktree API lifecycle.
- [x] Step 5 release evidence + residual risks —
  `docs/quality/source-control-release-checklist.md`.

Required tests from the sprint doc:

- [x] Full SCM fixture suite passes on Windows — 13/13 regression + the existing
  200+ SCM integration tests; full project 1029 pass / 2 environmental.
- [x] No shell injection or repository-escape regression — SC-25…SC-31.
- [x] Stage/commit mutation queue shows no index corruption — SC-17/SC-18.
- [x] Remote/worktree fixture suite passes — SC-08/SC-09/SC-10/SC-16.

Security & file-safety gate: all six items confirmed with evidence in checklist
§3; the one N/A (recovery snapshot + temp mutation + atomic replace) is
justified in one sentence there (Source Control delegates content mutation to
Git's transactional commands; that rule governs the Search & Replace pipeline).

## Deviations / spec conflicts

- **`GET api/source-control/diff` does not exist** (spec 13 §18 lists it).
  `GitDiffService` and `DocxGitDiffProvider` are not DI-registered and no
  controller exposes them; diff is proven at service level only. Fixing it
  would add product surface, which this hardening sprint forbids. Recorded as
  release-checklist R-1 (release-gate gap; follow-up sprint).
- **The suggested "already-cancelled token" RED test is not RED.** Documented
  above; the mandatory failing-first behavior is demonstrated by the
  deterministic release-vs-cancel test, which genuinely failed before the guard.
- **Two residual security findings were found and recorded, not fixed**, because
  the sprint forbids product behavior changes beyond the queue guard:
  `.git/config` can be rendered by the diff working-tree read (R-3, privileged
  API, contained) and staging a `.git/...` path reports `Succeeded` with no
  index change (R-4, Git silently ignores its own directory).
- **`docs/sprints/SPRINT-STATUS.md` was not edited** (controller owns the board;
  the sprint brief explicitly forbids it). The Definition of Done item "board
  updated" is therefore the controller's action.
- **No new runtime dependency, no branch-management scope, no frontend change.**
- The queued-cancellation test previously failed once under full-suite parallel
  load in the controller's run; after the guard it is stable 10/10 here. The
  PDF memory flake observed in one of three full runs is pre-existing and
  unrelated (R-6); it was not hidden, quarantined, or skipped.

## Risks discovered

- **R-1** missing diff HTTP route / diff services unwired — release-gate gap.
- **R-2** SCM graph wired twice (`Program.cs` inline + `AddSourceControlServices`
  for the desktop host) — composition drift risk; consolidation follow-up.
- **R-3** `.git/config` readable through the diff surface (credentials may be
  embedded in remote URLs); path is contained and the API is authenticated.
- **R-4** `.git/...` stage reports `Succeeded` with no change.
- **R-5** control characters other than NUL reach Git as literal pathspecs and
  fail typed (cannot name a Windows file; no shell).
- **R-6** pre-existing PDF peak-memory flake under parallel load (process-wide
  GC sampling vs. per-suite bound).
- **R-7** frontend suites not re-run here (no `node_modules` in the worktree).
- **R-8** bare repositories are refused typed by the worktree preflight
  (fail-closed, SCM-16 carry-over).
- Environmental, not a risk: `artifacts/` is absent in a fresh worktree, so the
  two release-artifact integration tests fail by design until a release build
  produces the outputs.

## Next eligible sprint(s)

- The SCM lane's next work is the release-checklist follow-ups: wire the diff
  route (R-1), consolidate DI registration (R-2), and reject `.git` segments in
  path validation (R-3/R-4). No downstream sprint was started or modified here.
