# Sprint Handoff — SCM-16

**Status:** DONE
**Branch:** `feat/safe-git-worktree-create-remove`
**Commits:** single commit — see `git log` for `feat(scm): safe-git-worktree-create-remove`

## What changed

- New `GitWorktreeMutationService` provides the three SCM-16 operations,
  all through the SCM-01 `IGitProcessRunner` (structured arguments, no shell,
  no new `Process` spawning): create from an existing ref, dirty-preflighted
  removal, and safe refresh/prune. The service resolves the true worktree root
  (`git rev-parse --show-toplevel`) before any mutation so the SCM-01
  per-worktree mutation queue keys correctly; it reuses SCM-15's
  `IGitWorktreeService` for listings/porcelain parsing instead of duplicating a
  registry.
- **Create never creates a branch** (spec 13 §16.3, acceptance criterion 1):
  - the ref must resolve to an existing commit
    (`rev-parse --verify --quiet <ref>^{commit}`, else typed `RefNotFound`);
  - when the ref names an existing local branch (`refs/heads/<name>` verified),
    the worktree is checked out with `git worktree add <path> <shortName>` —
    the short name is used **only** after that verification, so Git cannot
    DWIM-invent a branch, and no `-b`/`-B` ever appears (exact argument vector
    asserted by test). The short-name passing is empirically pinned: on
    git 2.51.0.windows.2, `worktree add <path> refs/heads/<name>` creates a
    DETACHED worktree, while the short name checks out the branch; branch/tag
    ambiguity warns on stderr and deterministically resolves to the branch;
  - tags, commits, remote-tracking refs, and qualified non-heads refs are added
    with `--detach` at the resolved commit;
  - a branch already checked out elsewhere fails typed
    (`BranchAlreadyCheckedOut`) with the conflicting location, instead of a raw
    Git failure.
- **Windows path safety** (Step 1, FS-07 reuse): the raw path is validated
  segment-by-segment **before** `Path.GetFullPath` (Windows silently strips
  trailing dots/spaces — `wt.` must be rejected, not normalized into `wt`),
  skipping drive designators; each segment must satisfy
  `PathSafetyValidator.IsValidBasename` (FS-07); relative navigation, `.git`
  segments, filesystem roots, and unusable paths are rejected typed. Occupied
  targets are `TargetAlreadyExists` except an existing empty directory, which
  Git accepts. Spaces/Unicode/special-character paths succeed against real Git.
- **Removal is explicit, reviewed, never forced** (spec 13 §16.4, acceptance
  criteria 2–3): the worktree must be Git-registered at its canonical path
  (`NotRegistered` otherwise), not the main working tree (`IsMainWorktree`),
  not locked (`WorktreeLocked` — unlock in Git first), present
  (`Unavailable`; deliberate fail-closed: real Git exits 0 on a missing
  directory and would silently unregister it), and clean. The dirty preflight
  runs `git status --porcelain=v2 -z --branch --no-optional-locks` inside the
  target worktree through the SCM-01 parser; any parsed entry (including
  untracked files) yields `DirtyWorktree` with the typed `Changes` list.
  Removal passes Git's **registered canonical path**, not the raw caller
  string, and invokes `git worktree remove <path>` with no `--force`/`-f`
  (exact argument vector asserted by test). Windows workaround pinned by
  real-Git probing: when the resolved root equals the target (caller is inside
  the worktree being removed), the invocation runs from the main worktree
  directory — `git worktree remove` with cwd inside the target fails
  "Permission denied" (exit 255) and leaves a partial unregistered-but-present
  state. The fallback stays queue-serialized (queue key = invocation working
  directory).
- **Safe refresh/prune** (Step 5, spec 13 §16.5): `git worktree prune` followed
  by a fresh `worktree list --porcelain`; valid and locked worktrees are never
  deleted — Git itself only prunes metadata of missing, unlocked worktrees.
- **Inspectable partial state:** the post-create/post-remove re-listing runs on
  both outcomes. A failed `worktree add` (e.g. failing post-checkout hook,
  exit 1, no stderr marker) is `CreateFailed` **plus** a refreshed `ListResult`
  so the partially created/registered state can be inspected and cleaned up
  through the normal removal flow. A failed preflight or unreadable registry is
  `PreflightFailed` with nothing removed (fail closed). Git-unavailable,
  not-a-repository, cancellation, and timeout are typed for every operation.
- Core contracts added: `GitWorktreeCreateStatus/Request/Result`,
  `GitWorktreeRemoveStatus/Request/Result`,
  `GitWorktreeRefreshStatus/Result` (all inspectable: `ProcessResult`,
  `Changes`, `ListResult`, `ErrorMessage`) and
  `IGitWorktreeMutationService`.
- Frontend `WorktreeCreateDialog.tsx` (spec 13 §16.3–§16.5): create form
  accepting only an existing ref ("No branch is ever created…"), no
  branch-management controls anywhere (asserted by test); a two-step explicit
  removal confirmation ("Confirm remove"/"Cancel") per worktree; blocked states
  mirroring the backend preflight order via exported `removalBlockedReason`
  (main → locked → missing → dirty → unknown-cleanliness), so dirty worktrees
  render a typed blocked reason and no remove action at all; a
  "Refresh worktrees" action; all actions disabled while a mutation is in
  flight; typed `role="alert"` error slot; explicit Git-unavailable and
  no-repository states. The component is presentational (props-driven) and
  calls no `fetch`; API/host wiring is deferred exactly as in SCM-15.

## Files changed

Production (new):

- `src/DocumentIDE.Infrastructure/SourceControl/GitWorktreeMutationService.cs` — service: create/remove/refresh, path + ref validation, typed mappings (planned file).
- `src/DocumentIDE.Core/SourceControl/GitWorktreeMutationResult.cs` — typed statuses/requests/results for all three operations (extra Core contract file, see mapping note).
- `src/DocumentIDE.Core/SourceControl/IGitWorktreeMutationService.cs` — Core contract (extra Core contract file, see mapping note).
- `src/DocumentIDE.Web/src/features/source-control/WorktreeCreateDialog.tsx` — create & remove UI (planned file).

Tests (new):

- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeMutationTests.cs` — 45 tests (planned file).
- `src/DocumentIDE.Web/src/features/source-control/WorktreeCreateDialog.test.tsx` — 16 colocated vitest tests.

**Planned-file mapping / extra files:**

- The three planned files were created as planned. The two Core contract
  files are the minimum split needed for the typed Core-level contracts this
  sprint requires (SCM-15 established the same pattern with its Core contract
  files).
- The frontend test is colocated, not in `tests/frontend/**` — per the CAN-05
  batch-B ruling (vitest root is `src/DocumentIDE.Web`; tests live next to the
  implementation).
- First implementation pass kept everything in a single service file that the
  harness had flagged as too large, so the file was split into the two Core
  contract files above; the planned paths were preserved.
- Not touched (verified by `git status`): the SCM-03/SCM-04 lane files
  (`SourceControlController.cs`, `SourceControlPanel.tsx`,
  `sourceControlModels.ts`, `GitDiffService.cs`, `MarkdownGitDiff.tsx`,
  `MarkdownGitDiffTests.cs`), `GitWorktreeService.cs`, `WorktreeList.tsx`,
  `TestGitRepository.cs`.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitWorktreeMutationTests"` → **PASS (45/45, 0 skipped)**.
- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControl"` → **PASS (200/200, 0 skipped)** (all SCM suites).
- `dotnet test tests/DocumentIDE.IntegrationTests` → **623 passed / 2 failed / 0 skipped (625 total)**.
  - The 2 failures are the known pre-existing release-artifact existence checks
    (`ScriptTests.Release_Build_Embeds_Production_Web_Assets`,
    `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`),
    documented in SCM-01/BAT-08/QA-01/REL-03/SCM-15 handoffs. They assert the
    existence of gitignored `artifacts/` outputs of
    `scripts/build-release.ps1` and this sprint touches no
    packaging/script/release input.
- `dotnet test tests/DocumentIDE.SecurityTests` → **PASS (91/91, 0 skipped)**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **PASS (12/12, 0 skipped)** — includes the SCM-01 process-boundary guards (no `Process` spawning outside `Infrastructure/SourceControl`, frontend cannot invoke Git directly).
- `npx vitest run` in `src/DocumentIDE.Web` → **PASS (43 files, 262 tests, 0 skipped)**.
- `npx tsc -b` in `src/DocumentIDE.Web` → **exit 0**.
- `npx oxlint` in `src/DocumentIDE.Web` → **exit 0** (warnings only; the one
  new warning is `react(only-export-components)` for `removalBlockedReason`,
  the identical repo-wide pattern already present in `FolderPicker`,
  `PdfActions`, `MetadataFilterChips`, `ShareManager`, etc.).

TDD evidence:

- Cycle 1 (create, the sprint's mandatory first failing test
  `CreateFromExistingBranchSucceeds`): RED at compile stage (`CS0246`,
  service/contract types did not exist). After the first minimal
  implementation the same test failed again for a real reason — the created
  worktree was detached because `git worktree add <path> refs/heads/<name>`
  detaches on the pinned Git; this was resolved by passing the short branch
  name only after verifying `refs/heads/<name>` exists (DWIM-impossible), then
  GREEN.
- Cycle 2 (removal ×3): RED `NotImplementedException` for `RemoveWorktreeAsync`
  → GREEN after preflight + typed refusals. The real-Git test
  `RemoveWorktreeWhenCallerIsInsideItSucceeds` then surfaced the Windows
  partial-state quirk (cwd inside target → exit 255, unregistered-but-present);
  fixed with the main-worktree invocation fallback, GREEN.
- Cycle 3 (refresh ×2): RED `NotImplementedException` → GREEN.
- Cycle 4 (hardening): a 5-failure batch (raw-segment validation order —
  trailing dot/space normalization; error-message wording forward vs backslash)
  was fixed; final 45/45.
- Frontend: disclosure — the component was authored before its colocated tests
  within this sprint. To prove the tests are not vacuous, a mutation sanity
  check was executed: with two deliberate behavioral mutations (create payload
  trim removed; first "Remove" click executing removal immediately), exactly
  the 3 matching tests failed (`creates a worktree from the submitted existing
  ref…`, `requires an explicit confirmation…`, `cancels a pending removal…`)
  and 13 passed; mutations were reverted and the file returned to GREEN 16/16.

## Acceptance criteria

- [x] **No branch is created implicitly** —
  `CreateInvokesGitWithExactArgumentVectorFromTheResolvedRoot` asserts the
  exact `worktree add` argument vector contains no `-b`/`-B`;
  `CreateFromTagCommitAndRemoteRefUsesDetachedHeadWithoutCreatingBranches`
  proves tags/commits/remote refs become detached worktrees;
  `CreateFromQualifiedHeadsRefAndAmbiguousShortNameCheckOutTheExistingBranch`
  proves an existing branch is checked out (not recreated);
  `CreateIsBlockedWhenBranchIsAlreadyCheckedOutElsewhere` proves typed refusal
  for a branch checked out elsewhere. The UI test asserts no branch-creation
  control exists.
- [x] **Dirty worktree cannot be removed through V1** —
  `RemoveDirtyWorktreeIsBlockedWithTypedChangeList` (typed `DirtyWorktree` +
  `Changes` list); no `--force`/`-f` is ever passed (exact argument vector
  test); the UI renders dirty worktrees as blocked with no remove action.
- [x] **Removal is explicit and reviewed** — two-step confirm UI test
  (first click opens confirmation, only "Confirm remove" invokes the
  callback); backend requires a Git-registered canonical path and refuses
  main/locked/missing/unregistered worktrees typed.

Required tests from the sprint doc:

- [x] Create from existing branch/ref/commit succeeds — `CreateFromExistingBranchSucceeds` (the mandatory RED-first test), plus the detached matrix and qualified-heads tests above.
- [x] Dirty worktree removal is blocked — `RemoveDirtyWorktreeIsBlockedWithTypedChangeList`.
- [x] Clean worktree removal succeeds — `RemoveCleanWorktreeSucceeds`.
- [x] Path normalization/containment is safe on Windows — `CreateRejectsUnsafeTargetPathsWithoutInvokingGit` and `RemoveRejectsUnsafeWorktreePathsWithoutInvokingGit` (13-case theories each, scripted runner proves zero Git invocations on rejection), plus `CreateIntoTargetPathWithSpacesAndUnicodeSucceeds` and the real-Git removal refusals.

Implementation steps:

- [x] Step 1 validate target path — raw-segment validation + FS-07 reuse + root/`.git`/relative rejections (theory tests above).
- [x] Step 2 create from existing ref — create flow + `RefNotFound` ×3 (missing branch, missing tag, missing commit) + empty-directory success + occupied-target refusal.
- [x] Step 3 dirty-state preflight — porcelain-v2 status parsing; dirty/clean/unknown-cleanliness typed.
- [x] Step 4 clean removal — removal flow + main/locked/missing/unregistered refusals + inside-target Windows case.
- [x] Step 5 refresh/prune — `RefreshPrunesMissingWorktreeMetadataAndKeepsValidOnes`, `RefreshRejectsMissingWorkingDirectory`.

## Deviations / spec conflicts

- Two extra Core contract files beyond the planned list (recorded above); no
  higher-level requirement weakened, no lane file touched.
- Frontend test path mapping recorded above (CAN-05 ruling).
- No API/DI wiring: the sprint's planned files contain no API artifact and the
  dialog is intentionally props-driven until a controller/route exists (same
  deferral as SCM-15). The service is DI-ready — see consumer notes.
- `RefreshWorktreesAsync("")` throws `ArgumentException` (empty working
  directory precondition), mirroring the SCM-15 `ListWorktreesAsync`
  precedent; all other failures are typed results.
- TDD ordering: backend cycles were RED-first per the sprint rule; the
  frontend tests were authored after the component within the same commit
  (disclosed above with the mutation sanity check as sensitivity evidence).
- Deliberate fail-closed behaviors that are stricter than raw Git, documented
  and test-pinned: missing-directory removal is `Unavailable` (Git would exit
  0 and silently unregister); unreadable registry/cleanliness is
  `PreflightFailed` with nothing removed.

## Risks discovered

- **Queue-key nuance:** mutating invocations always carry the resolved
  worktree root as the working directory, which is also the SCM-01 mutation
  queue key; the inside-target removal fallback carries the main worktree
  path instead. Both are worktree roots, so serialization remains
  per-worktree as SCM-01 requires, but consumers must not assume the removal
  invocation runs inside the target.
- **Bare-repo listing:** `worktree list --porcelain` in a bare repository can
  trip the SCM-15 parser's structural rules, which surfaces as typed
  `PreflightFailed` (create/remove blocked). Fail closed, but a follow-up may
  choose to handle bare repositories explicitly.
- **Post-checkout hook failures** leave a registered, partially set-up
  worktree (`CreateFailed` + refreshed `ListResult`). Consumers must refresh
  their UI from `ListResult` rather than assuming nothing was created, and can
  remove the leftover through the normal flow.
- **Validation order matters for consumers:** a blank target path is
  `InvalidRequest`; an unsafe/relative/traversal path is `InvalidTargetPath`.
  Raw path segments are validated before Windows normalization, so trailing
  dots/spaces are rejected — pre-normalize user input if a friendlier message
  is desired.
- **`chosen` Git semantics pinned by probing** (git 2.51.0.windows.2):
  `worktree remove` on a missing directory exits 0 and unregisters (hence the
  `Unavailable` block); `worktree remove` from inside the target fails with
  exit 255; a failing post-checkout hook leaves the worktree registered with
  no stderr marker. These are re-validated by real-Git integration tests; if a
  future Git release changes them the tests will fail loudly instead of
  silently weakening safety.

Consumer notes for SCM-17 (and any API wiring sprint):

- DI registration suggestion:
  `builder.Services.AddSingleton<IGitWorktreeMutationService, GitWorktreeMutationService>();`
  — needs `IGitProcessRunner` (SCM-01) and `IGitWorktreeService` (SCM-15),
  both already registered.
- Prefer the `ListResult` returned by create/remove/refresh over an extra
  listing call; the service already re-reads the Git-derived registry after
  each mutation (on both outcomes for create).
- The UI `RemovableWorktree`/`removalBlockedReason` mirror the backend
  preflight precedence (main → locked → missing → dirty → unknown); keep them
  in sync if the backend order changes.
- Dirty = any parsed porcelain-v2 entry, including untracked files; the parsed
  `Changes` list is the canonical typed change list to surface.
- The service never creates branches and the dialog has no branch controls —
  do not add branch lifecycle through these seams without a new sprint.

## Next eligible sprint(s)

- SCM-17 (Source Control Regression & Release Hardening) depends on SCM-16
  plus SCM-04, SCM-05, SCM-09, SCM-12, SCM-13, SCM-14, QA-02 — still blocked
  until those are DONE. No sprint depends solely on SCM-16.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the only
  working-tree writes are Git's own checkout during `worktree add` (no
  `-b`/`-B`, ref must already exist) and Git's non-forced `worktree remove`
  behind the clean, registered, non-main, unlocked preflight. The service
  writes no file directly.
- [x] **no arbitrary shell command can be injected through a filename/path** —
  no shell exists (SCM-01 runner, structured `ArgumentList`); the target path
  is validated segment-by-segment against FS-07 before use; ref tokens reject
  leading `-`, whitespace, and revision-syntax characters (`~^:?*[\`, `@{`,
  `..`) so a ref can never be interpreted as an option or revision expression;
  the rejection theories prove zero Git invocations occur for unsafe input.
- [x] **no repository escape is possible through path handling** — creation
  targets are validated fully-qualified paths with no navigation/`.git`/root
  segments; removal only accepts Git's registered canonical path for the
  queried repository (`NotRegistered` otherwise); the working directory is
  resolved to the true worktree root via `rev-parse --show-toplevel` and must
  be fully qualified.
- [x] **no secret/credential output is exposed in UI logs** — results expose
  only Git's structured stdout/stderr for failures (`ProcessResult`), typed
  statuses, canonical paths, and change entries; the UI renders only typed
  messages and paths. The SCM-01 prompt-disabled environment policy applies
  unchanged.
- [x] **no destructive action silently broadened its scope** — removal is the
  only destructive action: never `--force`/`-f`, exactly one registered
  worktree path per request, blocked on dirty/locked/main/missing/unregistered,
  and requires explicit two-step UI confirmation; refresh only prunes metadata
  Git itself reports and never deletes valid worktrees. The file-safety
  snapshot/temp/atomic-replace rule is N/A here in one sentence: this sprint
  mutates worktree registration through Git's own transactional commands and
  performs no in-place content mutation of user files (that rule governs the
  Search & Replace / source-mutation pipelines).
- [x] **relevant higher-level safety rules remain stronger than convenience** —
  local filesystem stays the source of truth (registry is always Git-derived,
  never duplicated); canvas never moves files; no semantic matching anywhere;
  fail-closed decisions (missing directory, unreadable registry, hook-failure
  partial state) are stricter than raw Git; every failure is a typed,
  inspectable outcome.
