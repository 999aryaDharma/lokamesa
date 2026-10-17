# Sprint Handoff — SCM-06

**Status:** DONE
**Branch:** `feat/git-stage-unstage`
**Commits:** single commit — see `git log` for `feat(scm): git-stage-unstage`

## What changed

- New `GitStageService` (Infrastructure) implements the four file-level
  operations against the real Git index (spec 13 §9.4/§10/§19/§23):
  - `StageAsync` → `git add -- <path>`,
  - `UnstageAsync` → `git restore --staged -- <path>`,
  - `StageAllAsync` → `git add --all -- .`,
  - `UnstageAllAsync` → `git restore --staged -- .`
  - every mutation runs through the SCM-01 `IGitProcessRunner` (structured
    `ArgumentList`, no shell) with the true worktree root
    (`git rev-parse --show-toplevel` via `GitRepositoryService.DiscoverAsync`)
    as the working directory — which is also the SCM-01 per-worktree mutation
    queue key, so concurrent requests serialize per worktree.
- **Typed, inspectable outcomes** (new Core contract `GitStageResult`):
  `Succeeded` / `RejectedPath` / `NoRepository` / `GitUnavailable` / `Failed`,
  never a silent fallback. Rejected paths carry an `ErrorCode`; Git failures
  carry the originating `GitProcessResult`.
- **Path containment before Git runs** (`ResolveContainedPath`): blank,
  absolute/rooted/drive-qualified, `:`-carrying, non-normalized
  (`./`, `..`, `//`, empty segments), escaping and directory targets are all
  rejected typed with zero Git invocations. Accepted paths are passed
  verbatim (forward-slash form) after a `--` separator with
  `GIT_LITERAL_PATHSPECS=1` so a file named `notes[1].md` stages exactly that
  file and never widens into a pathspec glob.
- **Refresh after every mutation:** `MutateAsync` re-reads status from Git
  (`GetStatusAsync`) after each successful mutation; the API success payload
  always embeds that fresh `SourceControlStatusResponse`. Failed mutations are
  test-pinned to have changed nothing (e.g. index-lock contention stages
  nothing), so there is no stale staging state to misread.
- **No proprietary staging area:** the service holds no index snapshot of its
  own; every read is a fresh Git query (proved by a cross-instance test).
- API (`SourceControlController`, same privileged localhost guard as SCM-03):
  `POST api/source-control/stage|unstage` (body `{path, changePath}`) and
  `stage-all|unstage-all` (body `{path}`). `RejectedPath` → HTTP 400
  `{errorCode, error}`; `NoRepository|GitUnavailable|Failed` → HTTP 200
  typed `SourceControlStageResponse{outcome,isSuccess,errorCode,message,status,failure}`;
  success → `status` with the re-read snapshot. The GET status/context paths
  were refactored into shared helpers (`MapSnapshotAsync`, `FailureStatus`,
  `PathRequiredBadRequest`, `ToStageResponseAsync`) — regression-verified by
  the full SourceControl filter.
- Frontend: `ChangeActions.tsx` (new) renders Stage/Unstage for a selected
  entry and Stage All/Unstage All for the whole worktree, posts through the
  privileged API (`postChangeAction`, `X-DocumentIDE-Session-Token`), disables
  while busy and when Git is unavailable, shows failures in a `role="alert"`
  region, and hands the re-read status back via `onStatusRefreshed`.
  `SourceControlPanel.tsx` gained an optional `renderChangeActions` render prop
  threaded to every row (wrapped in a span that stops click/double-click
  propagation so hosted actions never select or open the row).

## Files changed

Planned (sprint doc) → delivered:

- `src/DocumentIDE.Infrastructure/SourceControl/GitStageService.cs` — delivered as planned (new).
- `src/DocumentIDE.Api/Controllers/SourceControlController.cs` — delivered as planned (extended: DTOs + 4 routes + GET helper refactor, same file, no duplicate controller).
- `src/DocumentIDE.Web/src/features/source-control/ChangeActions.tsx` — delivered as planned (new).
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitStageTests.cs` — delivered as planned (new; two classes: `GitStageTests` 25 tests + `GitStageApiTests` 9 tests = 34).

Additional files needed to satisfy the sprint (mapping recorded):

- `src/DocumentIDE.Core/SourceControl/GitStageResult.cs` — new typed outcome
  contract; consumed by Infrastructure + API and mirrors the SCM-02
  `GitStatusResult` placement in Core.
- `src/DocumentIDE.Api/Program.cs` — DI singleton registration of
  `GitStageService` after the `GitRepositoryService` factory.
- `src/DocumentIDE.Web/src/features/source-control/sourceControlModels.ts` —
  `GitStageOutcome` + `SourceControlStageResult` model types.
- `src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.tsx` +
  `SourceControlPanel.test.tsx` — render-prop seam so the changes pane can host
  the actions (panel still owns no mutation logic).
- `src/DocumentIDE.Web/src/features/source-control/ChangeActions.test.tsx` —
  8 colocated component tests (CAN-05 batch-B ruling).
- `tests/DocumentIDE.IntegrationTests/SourceControl/TestGitRepository.cs` —
  **additive-at-end only**: appended `RunGitCaptureUtf8`, which runs the system
  Git and decodes stdout as UTF-8 (the fixture's default console-encoding
  capture mangles non-ASCII paths on Windows). Nothing existing was modified.

Lane safety: `DocxGitDiffProvider.cs`, `DocxDiffPanel.tsx`,
`DocxGitDiffTests.cs`, `GitDiffService.cs`, `MarkdownGitDiff.tsx` were **not
touched** (verified with `git diff --name-only <base> -- <lane files>` →
empty).

## TDD evidence

Backend cycles were strict RED-first; the mandated first behavior was

> Stage updates index state only.

which failed first (`StageUpdatesIndexStateOnly`), then GREEN with the minimal
`StageAsync` (`git add -- <path>` + status refresh), then refactored.

- Cycle 1 (stage single): RED `StageUpdatesIndexStateOnly` → GREEN.
- Cycle 2 (unstage single): RED `UnstagePreservesWorkingTreeEdits` → GREEN;
  a follow-up RED (`UnstageNewlyAddedFileReturnsItToUntrackedWithoutDeletingIt`)
  pinned the index-only restore semantics.
- Cycle 3 (all): RED `StageAllAndUnstageAllMatchExpectedGitState` → GREEN with
  `add --all -- .` / `restore --staged -- .`.
- Cycle 4 (paths): RED `PathsWithSpacesAndUnicodeRemainCorrect` exposed Git
  globbing a bracketed filename; GREEN after `GIT_LITERAL_PATHSPECS=1` +
  literal `--` argument handling, pinned by the rejection theory.
- Cycle 5 (API): RED API tests (endpoints/DTOs absent) → GREEN controller +
  DI; `StageRoutesRejectMissingInvalidTokenAndHostileOrigin` added on the same
  pass.
- Hardening batch (each RED before its fix): queue serialization
  (`StagingIsSerializedThroughThePerWorktreeMutationQueue`),
  `StageWithMissingGitIsTypedGitUnavailable`,
  `StageInNonGitFolderIsTypedNoRepository`,
  `StageWhenWorktreeDisappearsIsTypedFailureNotAnException`,
  `StageWithIndexLockContentionIsTypedAndStagesNothing`,
  `UnstageBeforeFirstCommitIsTypedFailureAndLeavesIndexUnchanged`,
  `StagingStateLivesInTheGitIndexNotInServiceMemory`,
  `StageIsScopedToTheActiveWorktreeOnly`.
- The `GitStageApiTests` class (9 tests) pins the HTTP contract end to end with
  `WebApplicationFactory<Program>`: single/all happy paths with refreshed
  status, verbatim spaces/Unicode, 400 for escaping/absolute/blank paths, typed
  200 for non-Git folders, and auth/origin rejection.
- Frontend: 8 colocated tests cover both modes, busy/disable behavior, typed
  failure alert, client rejection errors, and the `onStatusRefreshed` handoff;
  `SourceControlPanel.test.tsx` gained a test proving hosted actions do not
  change selection or open a document.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitStage"` → **34 passed / 0 failed / 0 skipped** (25 service + 9 API; the sprint-filter evidence).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **695 passed / 2 failed / 0 skipped** (697 total). Both failures are the documented REL-03-sanctioned release-artifact checks (`ScriptTests.Release_Build_Embeds_Production_Web_Assets` — missing `artifacts/desktop/DocumentIDE.Desktop.exe`; `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost` — missing `artifacts/installer/DocumentIDE-Setup.msi`): `artifacts/` is gitignored, absent in a fresh worktree, and this diff touches neither `scripts/` nor packaging. Same two failures are recorded in the SCM-01…SCM-04, SCM-15 and SCM-16 handoffs.
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName!~Release_Build_Embeds_Production_Web_Assets&FullyQualifiedName!~Packages_Wxs_Installer_And_ShareHost"` → **695 passed / 0 failed / 0 skipped** (REL-03 sanctioned local exclusion, documented, not a skip).
- `dotnet test tests/DocumentIDE.SecurityTests` → **91 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **12 passed / 0 failed / 0 skipped**.
- `dotnet test tests/DocumentIDE.Core.Tests` → **459 passed / 0 failed / 0 skipped** (Core touched via `GitStageResult.cs`).
- `npx vitest run` (`src/DocumentIDE.Web`) → **46 files / 304 tests passed, 0 failed**.
- `npx tsc -b` (`src/DocumentIDE.Web`) → exit 0.
- `npx oxlint` (`src/DocumentIDE.Web`) → exit 0 (one warning-class note in `ChangeActions.tsx` for co-locating the exported `postChangeAction` helper with the component — the same convention as existing action files such as `PdfActions.tsx`; not an error, no new warnings elsewhere).

## Acceptance criteria

- [x] **Document IDE does not maintain a proprietary staging area** —
  `StagingStateLivesInTheGitIndexNotInServiceMemory`: stage with one service
  instance, then read the staged state with a brand-new instance over the same
  worktree; the real Git index is the only authority. The service stores no
  index snapshot; every response status comes from a fresh `GetStatusAsync`.
- [x] **Every stage action re-reads status** —
  `MutateAsync` calls `GetStatusAsync(worktreeRoot)` after every successful
  mutation, and the API success payload embeds that fresh
  `SourceControlStatusResponse` (`StageEndpointStagesOneFileAndReturnsRefreshedStatus`,
  `UnstageEndpointRestoresIndexSideAndKeepsWorkingTreeEdit`,
  `StageAllAndUnstageAllEndpointsMatchExpectedGitState` assert the re-read
  branch/entries). Mutations Git rejects are proven to have changed nothing
  (`StageWithIndexLockContentionIsTypedAndStagesNothing`,
  `UnstageBeforeFirstCommitIsTypedFailureAndLeavesIndexUnchanged`), and
  rejected paths never invoke Git at all (scripted-runner theory), so no stale
  staging state can be misreported.
- [x] **Staging never widens scope beyond selected repository paths** —
  `ResolveContainedPath` rejects blank/absolute/non-normalized/escaping/
  directory targets before Git runs; `EscapingAndNonFilePathArgumentsAreRejectedBeforeGitRuns`
  (11-case theory with a scripted runner proving zero Git invocations),
  `AbsolutePathsAreRejectedEvenWhenTheyPointInsideTheWorktree`; accepted paths
  are sent verbatim after `--` with literal pathspecs
  (`PathsWithSpacesAndUnicodeRemainCorrect`,
  `StageEndpointCarriesSpacesAndUnicodePathsVerbatim`); queued mutations are
  keyed by the resolved worktree root and `StageIsScopedToTheActiveWorktreeOnly`
  proves a sibling linked worktree is untouched.

Required tests from the sprint doc:

- [x] Stage updates index state only — `StageUpdatesIndexStateOnly` (the mandatory RED-first test; asserts the file content on disk is unchanged while the index side moves).
- [x] Unstage preserves working-tree edits — `UnstagePreservesWorkingTreeEdits` + `UnstageNewlyAddedFileReturnsItToUntrackedWithoutDeletingIt`.
- [x] Stage All and Unstage All match expected Git state — `StageAllAndUnstageAllMatchExpectedGitState` + API `StageAllAndUnstageAllEndpointsMatchExpectedGitState`.
- [x] Path names with spaces and Unicode remain correct — `PathsWithSpacesAndUnicodeRemainCorrect` + API `StageEndpointCarriesSpacesAndUnicodePathsVerbatim`.

Implementation steps:

- [x] Step 1 stage single — `StageAsync` + tests + API route.
- [x] Step 2 unstage single — `UnstageAsync` + tests + API route.
- [x] Step 3 stage/unstage all — `StageAllAsync`/`UnstageAllAsync` + tests + API routes.
- [x] Step 4 refresh status after every mutation — `MutateAsync` re-read + API refreshed payload + frontend `onStatusRefreshed`.
- [x] Step 5 path containment tests — 11-case rejection theory + absolute-path + scoping tests.

## Deviations / spec conflicts

- One extra Core file beyond the planned list (`GitStageResult.cs`); it is the
  typed public contract consumed by Infrastructure/API and follows the SCM-02
  `GitStatusResult` Core placement. No lane file touched; no higher-level
  requirement weakened.
- Frontend test path follows the CAN-05 batch-B ruling (colocated
  `ChangeActions.test.tsx`), consistent with all prior feature components.
- `GIT_LITERAL_PATHSPECS=1` is added per invocation. It only *narrows* Git
  behavior (literal pathspecs, no globbing); the SCM-01 protected environment
  policy remains non-overridable. This was required because bracket/glob
  filenames were silently widened during the RED path test.
- **Unborn HEAD limitation (consumer-critical):** `git restore --staged` fails
  before the first commit (exit 128, "fatal: could not resolve HEAD"). SCM-06
  surfaces this as a typed `Failed` outcome rather than silently falling back
  to `git reset` — pinned by
  `UnstageBeforeFirstCommitIsTypedFailureAndLeavesIndexUnchanged`. A future
  sprint may choose an explicit, reviewed fallback; none exists today.
- Conflicted (`Unmerged`) entries: both Stage and Unstage stay disabled in
  `ChangeActions` — conflict-resolution semantics belong to SCM-14, so the
  conservative choice is to offer no action rather than the wrong one.
- Test-fixture hardening only (no production behavior): `DeleteDirectoryForcefully`
  in `GitStageTests.cs` clears read-only attributes before recursive delete
  because Git's read-only `.git` objects leave partially-deleted directories on
  Windows; the "worktree disappears" test now removes the directory fully and
  asserts the typed `Failed` + `GitWorktreeInvalid` outcome.
- `TestGitRepository.RunGitCaptureUtf8` appended at end of file (test fixture,
  additive). The GET status path in the controller was refactored into shared
  helpers within the same file; regression-verified by the full SourceControl
  filter and the full IntegrationTests run.

## Risks discovered

- **Queue-key nuance:** stage/unstage invocations always carry the resolved
  true worktree root as the working directory, which is also the SCM-01
  mutation queue key. Consumers must not invoke these mutations with a
  subdirectory path of their own — pass the workspace/active-document path to
  discovery and let the service resolve the root.
- **HTTP shape nuance for consumers:** `RejectedPath` is HTTP 400
  (`{errorCode, error}`), while execution failures (`GitUnavailable`, `Failed`,
  `NoRepository`) are HTTP 200 with a typed body (`outcome`/`isSuccess` =
  false). Client code must branch on the body, not on the HTTP status alone.
- **Strict path contract:** only verbatim status paths are accepted; `./foo`,
  `a/../b`, `a//b`, trailing-slash and directory paths are typed
  `PathNotNormalized`/`PathNotAFile` rejections rather than being normalized.
  Clients must send the change path exactly as Git reported it (spec 13 §23).
- **Index-lock contention** is a typed `Failed` with the Git process result
  (e.g. "Unable to create '…/index.lock'"); a retry/UX affordance is left to
  the consuming pane (SCM-07+).
- **Stage All includes tracked deletions** (`git add --all -- .` stages
  modified, deleted and untracked-but-not-ignored files; ignored files are
  never included). This matches Git semantics and spec 13 §9.4/§11.2; the
  frontend recomputes entry state from the refreshed status.
- **Unstage of a newly added file** returns it to untracked on disk (content
  preserved); see the pinned test above.

Consumer notes for SCM-07 (Git Commit & Commit All):

- Reuse the SCM-06 shape: typed result in `DocumentIDE.Core.SourceControl`,
  service in `DocumentIDE.Infrastructure.SourceControl`, singleton DI in
  `Program.cs` after the `GitRepositoryService` factory; every mutation through
  `IGitProcessRunner` with structured arguments and the resolved worktree root
  as working directory (queue key). Commit messages must be structured
  arguments (`-m`), never shell-joined.
- Follow the refresh rule: re-read status (`GetStatusAsync`) after every
  successful mutation; the API success payload should embed the fresh snapshot,
  exactly like `SourceControlStageResponse`.
- Unborn HEAD: unstage (and by extension commit-history reads) must not assume
  a first commit exists — SCM-06 pins the typed failure; SCM-07's
  first-commit UX must account for it.
- `renderChangeActions` in `SourceControlPanel` is the seam for hosting new
  per-row actions; if commit/revert actions are added there, keep the
  `span`+`stopPropagation` wrapper so row interaction semantics stay intact.
- The frontend client helper pattern (`postChangeAction`, API URL + session
  token globals, typed body branching) is the precedent for commit endpoints.

## Next eligible sprint(s)

- SCM-07 (Git Commit & Commit All) remains BLOCKED only until SCM-05
  (DOCX Document-Aware Git Diff) is DONE — SCM-06 satisfies one of its two
  dependencies. No sprint depends on SCM-06 alone.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the only
  writes are Git's own index updates through `git add` / `git restore --staged`
  invoked via the SCM-01 controlled runner; the service writes no file itself
  and every route carries the privileged localhost session guard
  (`StageRoutesRejectMissingInvalidTokenAndHostileOrigin`).
- [x] **no arbitrary shell command can be injected through a filename/path** —
  no shell exists (structured `ArgumentList`); `--` precedes every path; the
  path is validated (no `:`, no absolutes, no escapes) before Git runs, and
  the 11-case rejection theory proves zero Git invocations for hostile input;
  `GIT_LITERAL_PATHSPECS=1` prevents glob/pathspec-magic interpretation of
  filenames.
- [x] **no repository escape is possible through path handling** — containment
  is validated against the resolved true worktree root
  (`rev-parse --show-toplevel`); traversal/absolute/non-normalized/directory
  paths are rejected typed; `StageIsScopedToTheActiveWorktreeOnly` proves a
  sibling linked worktree is untouched.
- [x] **no secret/credential output is exposed in UI logs** — responses carry
  typed outcomes, Git's structured stdout/stderr for failures (no environment
  dumps) and paths only; the UI renders the typed `message`/`errorCode`; the
  SCM-01 prompt-disabled environment policy applies unchanged. (Index
  operations involve no credentials.)
- [x] **no destructive action silently broadened its scope** — stage/unstage
  never delete or rewrite user content; Unstage provably preserves
  working-tree edits and returns newly added files to untracked without
  deleting them; all-actions are `-- .` scoped inside the resolved worktree
  root; failed mutations are test-pinned to have staged nothing; rejected
  inputs never reach Git.
- [x] **relevant higher-level safety rules remain stronger than convenience** —
  the real Git index is the only staging state (no proprietary staging area);
  local filesystem remains source of truth; typed failures instead of silent
  fallbacks (unborn HEAD pinned); no semantic matching anywhere. The
  file-safety snapshot/temp/atomic-replace rule is N/A in one sentence: this
  sprint mutates only the Git index through Git's own transactional commands
  and performs no in-place mutation of user file content (that rule governs the
  source-mutation / Search & Replace pipelines).
