# Sprint Handoff — SCM-15

**Status:** DONE
**Branch:** `feat/git-worktree-discovery-open-existing`
**Commits:** single commit — see `git log` for `feat(scm): git-worktree-discovery-open-existing`

## What changed

- Git worktree discovery derived from Git itself, never from a parallel
  registry: `GitWorktreeService.ListWorktreesAsync` runs exactly one
  read-only invocation, `git worktree list --porcelain`, exclusively through
  the SCM-01 `IGitProcessRunner` (structured arguments, no shell). No new
  `Process` spawning exists anywhere in `src/`.
- Porcelain parser (spec 13 §16.1): blank-line-separated entries with
  `worktree <path>`, `HEAD <oid>`, exactly one of `branch <ref>`/`detached`/
  `bare`, plus optional `locked [reason]`/`prunable [reason]`. Unknown
  attribute lines are skipped (Git documents the format as extensible);
  structural violations and non-fully-qualified paths fail typed with
  `GitWorktreeListStatus.ParseError` — never a partial list. Verified against
  the real format of git 2.51.0.windows.2 (raw forward-slash paths, blank-line
  separated, main worktree listed first).
- Every reported path is canonicalized (`Path.GetFullPath`, fully qualified
  only — relative/unescapable paths are rejected, never resolved against the
  app's current directory). Spaces and Unicode paths are covered by a real-Git
  test fixture.
- Open-existing-worktree action (spec 13 §16.2):
  - request path must be a fully qualified canonical local path
    (`InvalidRequest` otherwise);
  - the canonical target must be an exact match (trimmed, case-insensitive on
    Windows) of a location Git registered as a worktree for the queried
    repository — otherwise typed `NotRegistered`; traversal forms (`..`
    escape), unregistered paths, and non-existent directories never become
    active context;
  - a registered-but-missing directory yields typed `Unavailable`;
  - an unattached worktree yields `RequiresAttachmentFlow` — the explicit
    FS-01 folder-source attachment flow is reused (`IFolderSourceService`);
    nothing is attached/detached/reorganized silently;
  - on success the matched read-only metadata becomes `ActiveWorktree`
    (in-memory session context, `Volatile` read/write);
  - the whole open flow issues only the one read-only porcelain listing — no
    checkout, no `switch`/`branch`/`update-ref`, proven by asserting the exact
    argument vector the runner receives.
- Read-only branch metadata: `GitWorktreeInfo.BranchName` is a computed
  display value (full ref minus `refs/heads/`); nothing in the sprint creates,
  switches, renames, or deletes branches.
- Canvas spatial memory preserved: opening a worktree writes nothing to
  canvas/workspace stores; a test snapshots `CanvasPositionService.ExportSnapshot()`
  (positions, modes, pinning) before/after and asserts byte-identical output.
- Frontend `WorktreeList.tsx`: presentational, props-driven list showing the
  canonical path and read-only branch/detached/main/locked/prunable metadata
  with explicit hidden states (`git-unavailable`, `no-repository`, `empty`,
  `loading`), an active-context marker, per-row open action with busy
  disabling, and a typed `role="alert"` error slot. No branch-management
  controls exist (asserted by test). The component does not call `fetch`
  itself; the API endpoint/host wiring is deliberately deferred (see Risks).

## Files changed

Production (new):

- `src/DocumentIDE.Core/SourceControl/GitWorktreeInfo.cs` — `GitWorktreeListStatus`, `GitWorktreeInfo`, `GitWorktreeListResult` (typed list outcomes; `IsSuccess`, `BranchName`).
- `src/DocumentIDE.Core/SourceControl/GitWorktreeOpenResult.cs` — `GitWorktreeOpenStatus`, `GitWorktreeOpenRequest`, `GitWorktreeOpenResult`.
- `src/DocumentIDE.Core/SourceControl/IGitWorktreeService.cs` — Core contract (list/open/`ActiveWorktree`).
- `src/DocumentIDE.Infrastructure/SourceControl/GitWorktreeService.cs` — service + porcelain parser (planned file).
- `src/DocumentIDE.Web/src/features/source-control/WorktreeList.tsx` — read-only worktree list UI (planned file).

Tests (new):

- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeDiscoveryTests.cs` — 16 tests (planned file).
- `src/DocumentIDE.Web/src/features/source-control/WorktreeList.test.tsx` — 9 colocated vitest tests.

**Planned-file mapping / extra files:** the three planned files were created
as planned. The three Core contract files are the minimum split needed for the
typed Core-level contracts this sprint requires (statuses, request/result
records); they deliberately do not touch the SCM-02 lane files
(`GitRepositoryContext.cs`, `GitStatusEntry.cs`, `GitRepositoryService.cs`,
`GitStatusTests.cs` remain untouched). The frontend test is colocated, not in
`tests/frontend/**` — per the CAN-05 batch-B ruling: "`tests/frontend/**` is a
legacy directory not wired into the vitest runner (vitest root is
`src/DocumentIDE.Web`); tests live next to the implementation."

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~GitWorktreeDiscoveryTests"` → **PASS (16/16, 0 skipped)**.
- `dotnet test tests/DocumentIDE.IntegrationTests` → **552 passed / 2 failed / 0 skipped (554 total)**.
  - The 2 failures are the known pre-existing release-artifact existence checks
    (`ScriptTests.Release_Build_Embeds_Production_Web_Assets`,
    `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`).
    They assert the mere existence of `artifacts/desktop/DocumentIDE.Desktop.exe`
    and `artifacts/installer/DocumentIDE-Setup.msi` — gitignored outputs of
    `scripts/build-release.ps1` that do not exist in a fresh worktree. Evidence
    they are environmental and not caused by this change: `artifacts/` is absent
    in the sprint worktree, present in the primary checkout where the same two
    tests **PASS (2/2)**; this change touches no packaging/script/release input.
    Same failures are documented pre-existing in SCM-01/BAT-08/QA-01/REL-03 handoffs.
- `dotnet test tests/DocumentIDE.SecurityTests` → **PASS (91/91, 0 skipped)**.
- `npm install --no-audit --no-fund` in `src/DocumentIDE.Web` (fresh worktree had no `node_modules`) → **195 packages added, exit 0** (audit intentionally skipped by the flag).
- `npx vitest run` in `src/DocumentIDE.Web` → **PASS (42 files, 246 tests, 0 skipped)**.
- `npx tsc -b` in `src/DocumentIDE.Web` → **exit 0** (build gate: `tsc -b && vite build`).
- `npx oxlint src/features/source-control` → **exit 0**.
- `dotnet test tests/DocumentIDE.ArchitectureTests` → **PASS (12/12, 0 skipped)** — includes the SCM-01 process-boundary guards (no `Process` spawning outside `Infrastructure/SourceControl`, frontend cannot invoke Git directly), substantiating the no-new-`Process` claim for this change.

TDD evidence:

- Backend cycle 1 (first failing test, `MainAndLinkedWorktreesAreListed`):
  RED confirmed at compile time — the service/contract types did not exist
  (`CS0246`, `CS7036` before implementation); GREEN after the parser + list
  implementation; full focused suite 16/16.
- Backend cycle 2 (open-existing + hardening): the open-action tests drove
  `OpenWorktreeAsync`. Disclosure for transparency: after cycle-1's
  implementation, the parser/metadata failure-mode tests (spaces/Unicode,
  detached/locked/prunable, malformed output, relative path) passed on first
  run — they are regression guards for parser behavior, not independent
  red→green cycles.
- Frontend cycle: `WorktreeList.test.tsx` RED first — `Failed to resolve
  import "./WorktreeList"` (component did not exist); GREEN after the
  component; one test then failed on an ambiguous regex caused by the test
  fixture path (`Found multiple elements with the text: /detached/i`) and was
  corrected in the test to exact-text assertions; final 9/9. `tsc -b` flagged
  an unused `React` import in the new component (automatic JSX runtime) which
  was removed before the final green run.

## Acceptance criteria

- [x] **Worktree list is derived from Git** — `GitWorktreeService` runs only
  `git worktree list --porcelain` via the SCM-01 runner (exact argument vector
  asserted); real-Git tests `MainAndLinkedWorktreesAreListed`,
  `WorktreePathsWithSpacesAndUnicodeAreCanonicalized`,
  `DetachedLockedAndPrunableWorktreesExposeTypedMetadata` prove main + linked
  worktrees, canonical paths, and locked/detached/prunable metadata.
- [x] **Current branch metadata is read-only** — `BranchName` is computed from
  the porcelain `branch <ref>` value; `OpeningWorktreeIssuesOnlyTheReadOnlyWorktreeListInvocation`
  proves the open flow's single invocation is the read-only listing (no
  `checkout`/`switch`/`branch` arguments ever reach Git); the UI test asserts
  branch text is displayed and no branch-management control exists.
- [x] **Opening worktree does not create a branch or alter Git history** —
  `OpeningWorktreeDoesNotSwitchBranchesCreateBranchesOrAlterHistory` compares
  `symbolic-ref HEAD`, `rev-parse HEAD` (both worktrees), and
  `for-each-ref refs/heads/` before/after the open; all identical.

Required tests from the sprint doc:

- [x] Main and linked worktrees are listed — `MainAndLinkedWorktreesAreListed` (the first failing test; RED reason: missing new types, compile-stage).
- [x] Open existing worktree updates active filesystem context — `OpenExistingWorktreeUpdatesActiveFilesystemContext` (FS-01 `FolderSourceService` reuse; both sources stay attached), plus typed rejections: `OpeningUnattachedWorktreeRequiresExplicitAttachmentFlow`, `UnregisteredPathIsRejectedAndActiveContextIsUnchanged`, `DisappearingWorktreeIsReportedAsTypedUnavailable`, `OpenWithInvalidArgumentsYieldsTypedInvalidRequest`, `OpenFromNonRepositoryWorkingDirectoryYieldsTypedListFailure`.
- [x] No branch switch occurs when opening a worktree — `OpeningWorktreeDoesNotSwitchBranchesCreateBranchesOrAlterHistory` + `OpeningWorktreeIssuesOnlyTheReadOnlyWorktreeListInvocation`.

Implementation steps:

- [x] Step 1 worktree list parser — porcelain parser + `MalformedPorcelainOutputYieldsTypedParseErrorAndNoPartialList`, `RelativeWorktreePathInPorcelainOutputIsRejectedAsParseError`.
- [x] Step 2 resolve worktree paths safely — canonicalization + spaces/Unicode test + containment tests above.
- [x] Step 3 wire open action to workspace/source context — `OpenWorktreeAsync` + FS-01 attachment check + `ActiveWorktree`; typed open statuses.
- [x] Step 4 preserve canvas spatial memory — `OpeningWorktreePreservesCanvasSpatialMemory`.

## Deviations / spec conflicts

- Extra Core contract files beyond the planned list (recorded above); no
  higher-level requirement weakened, no SCM-02 file touched.
- Frontend test path mapping recorded above (CAN-05 ruling).
- No API/DI-wiring changes in this sprint: the sprint's planned files contain
  no API artifact, no controller/endpoint exists for worktrees yet, and
  `WorktreeList.tsx` is intentionally props-driven until a host page and route
  exist. The service is DI-ready: register as
  `builder.Services.AddSingleton<IGitWorktreeService, GitWorktreeService>();`
  (needs `IGitProcessRunner` from SCM-01 and `IFolderSourceService` from
  FS-01, both already registered). Recommended follow-up (consumer sprint):
  expose `GET /api/source-control/worktrees` + open action endpoint, then wire
  `WorktreeList` to it. Recorded as a risk/consumer note, not as sprint scope.
- TDD ordering nuances for the backend cycle-2 parser tests disclosed above.

## Risks discovered

- `ActiveWorktree` is in-memory per-service-instance session context. It must
  be registered as a singleton (or workspace-scoped) so list/open consumers
  share it; registering it transient would silently lose the active context.
- Open-flow `WorkingDirectory` is validated non-empty but not canonicalized:
  it only selects *which repository* is queried; the opened path must still be
  Git-registered there and attached to the workspace via FS-01, so no
  unreviewed path becomes active context. SCM-16 should resolve repository
  roots via `git rev-parse --show-toplevel` to comply with the SCM-01
  per-worktree queue-key rule.
- Consumer notes for SCM-16 (`GitWorktreeMutationService`):
  - reuse `IGitProcessRunner` (all Git invocations; mutating worktree commands
    classify as mutation and auto-route through the SCM-01 queue);
  - `GitWorktreeInfo` is the canonical read-only worktree shape; the frontend
    `WorktreeListEntry` mirrors it by hand (no codegen in this repo) — keep in
    sync when fields change;
  - `TestGitRepository.RunGit` returns only an exit code; SCM-15 added a
    file-local `RunGitCapture` (stdout capture, structured arguments). Promote
    it to the shared fixture if a second consumer needs captured stdout rather
    than duplicating it again;
  - `GitWorktreeParseException` is the typed parse failure; do not catch it
    broadly — `GitWorktreeService` already converts it to `ParseError`.
- `git worktree list --porcelain` unknown attributes are skipped for forward
  compatibility; a future Git version adding semantic attributes (e.g. new
  lock forms) will not break listing, but new *meaning* must be surfaced by a
  later sprint, not silently ignored forever.

## Next eligible sprint(s)

- SCM-16 (Safe Git Worktree Create & Remove) — both dependencies satisfied
  once this sprint's SPRINT-STATUS row is set DONE (SCM-15 DONE + FS-07 DONE).
  A separate docs update should flip SCM-16 BLOCKED → READY (this sprint
  updates only its own row).
- SCM-02 (Git Repository, Worktree & Status Domain) is already READY and
  unblocked independently.

## Security & file-safety gate

- [x] **no user file was mutated through an unreviewed/bypass path** — the
  service performs no file writes at all: discovery is a read-only Git listing
  (SCM-01 classifier treats exactly `worktree list` as read-only) and the open
  action only reads FS-01 folder sources and sets in-memory context.
- [x] **no arbitrary shell command can be injected through a filename/path** —
  no shell is involved (SCM-01 runner, structured `ArgumentList`); the
  invocation arguments are a constant array (`worktree list --porcelain`) —
  user-controlled path values never enter command arguments, only parser
  input. Spaces/Unicode/special-character paths are covered by a real-Git test.
- [x] **no repository escape is possible through path handling** — opening
  accepts only an exact canonical match against the set of locations Git
  itself registered for the queried repository; relative paths are rejected
  at request validation and at parse time; unregistered paths and `..`
  traversal forms are rejected typed (`NotRegistered`) without changing the
  active context; missing directories yield `Unavailable`. All pinned by tests.
- [x] **no secret/credential output is exposed in UI logs** — the UI renders
  only canonical paths and read-only Git metadata; no stdout/stderr, env, or
  remote/auth data is surfaced; the SCM-01 runner's prompt-disabled
  environment policy applies unchanged to the single listing call.
- [x] **no destructive action silently broadened its scope** — no destructive
  action exists in this sprint (read-only list + context switch); branch
  create/switch/delete are not implemented or reachable; FR, index lock,
  DOCX-changed, hook/remote failure edge cases are N/A for a read-only
  listing with no index/working-tree writes (justified here in one sentence
  each — no lock is taken, no working-tree file is read-modified, no hook or
  remote is contacted by `worktree list`).
- [x] **relevant higher-level safety rules remain stronger than convenience** —
  local filesystem stays the source of truth (no duplicated registry);
  unattached worktrees require the explicit FS-01 attachment flow instead of
  auto-attaching; canvas spatial memory is byte-identically preserved across
  the context switch; every failure is a typed, inspectable status (no silent
  fallback), per spec 13 §16 and AGENTS.md product rules.
