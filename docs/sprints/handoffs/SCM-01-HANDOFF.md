# Sprint Handoff — SCM-01

**Status:** DONE
**Branch:** `feat/git-capability-controlled-process-runner`
**Commits:** (see git log for `feat(scm): git-capability-controlled-process-runner`)

## What changed

- New controlled Git process boundary, split Core (contracts) vs
  Infrastructure (process spawning):
  - `GitProcessRunner` spawns the system Git executable as a direct child
    process — never through a shell — with `ArgumentList` (structured
    arguments), a controlled working directory, and a pinned environment
    policy (`GIT_TERMINAL_PROMPT=0`, `GIT_PAGER=cat`, `LC_ALL=C`,
    `GIT_EDITOR=:`, `GIT_SEQUENCE_EDITOR=:`) that request-level overrides
    cannot disable.
  - `git --version` capability probe always returns a typed capability state
    (`Available`/`Unavailable`); missing or unusable Git never throws.
  - Cancellation and timeouts kill the whole process tree
    (`Process.Kill(entireProcessTree: true)`) and are reaped before the call
    returns, so no orphaned Git/hook/helper processes survive. Both are
    returned as typed `GitProcessStatus.Cancelled`/`TimedOut` — never an
    `OperationCanceledException` escaping to callers. Closing child stdin
    makes accidental interactive prompts fail fast instead of hanging.
  - stdout/stderr are captured; exit codes and both streams are inspectable on
    `GitProcessResult`.
- Deterministic classifiers:
  - `GitCommandClassifier` decides mutation vs read-only from the Git
    vocabulary only (read-only whitelist; unknown subcommands are
    conservatively treated as mutation). Global options before the subcommand
    are skipped; `git worktree list` is read-only while every other `worktree`
    action is mutation.
  - `GitFailureClassifier` maps unambiguous Git signatures (spec 13 §20) to
    typed `GitProcessErrorKind` values; anything ambiguous stays `Unknown`
    (no guessing, no silent fallback).
- `GitWorktreeMutationQueue`: per-worktree mutation serialization (concurrency
  exactly 1 per worktree, different worktrees independent; proven via
  `MaxObservedConcurrency`). Mutating invocations auto-route through the queue
  when the runner is constructed with it; read-only invocations stay
  unserialized (spec 13 §19). Cancellation while queued never executes the
  operation and returns typed `Cancelled`.
- DI wiring in `src/DocumentIDE.Api/Program.cs` (singleton options + queue +
  `IGitProcessRunner → GitProcessRunner`).
- Architecture guards (`SourceControlProcessBoundaryTests`): the browser
  frontend has no process-invocation surface, C# process spawning lives only
  in `Infrastructure/SourceControl` (plus the documented Word desktop
  ShellExecute launcher), Core contracts contain no spawning, and only the
  API composition root names the concrete runner.

## Files changed

Production:

- `src/DocumentIDE.Core/SourceControl/GitProcessResult.cs` (new — statuses, error kinds, result record)
- `src/DocumentIDE.Core/SourceControl/GitCapabilityInfo.cs` (new — typed capability state)
- `src/DocumentIDE.Core/SourceControl/GitProcessRequest.cs` (new — structured invocation request)
- `src/DocumentIDE.Core/SourceControl/IGitProcessRunner.cs` (new — backend process boundary contract)
- `src/DocumentIDE.Infrastructure/SourceControl/GitProcessRunner.cs` (new — controlled runner)
- `src/DocumentIDE.Infrastructure/SourceControl/GitWorktreeMutationQueue.cs` (new — per-worktree queue)
- `src/DocumentIDE.Infrastructure/SourceControl/GitCommandClassifier.cs` (new — mutation classifier)
- `src/DocumentIDE.Infrastructure/SourceControl/GitFailureClassifier.cs` (new — typed failure mapping)
- `src/DocumentIDE.Infrastructure/SourceControl/GitProcessRunnerOptions.cs` (new — timeouts + protected env policy)
- `src/DocumentIDE.Api/Program.cs` (modified — DI registration, +12 lines)

Tests:

- `tests/DocumentIDE.IntegrationTests/SourceControl/GitProcessRunnerTests.cs` (new, 12 tests)
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitWorktreeMutationQueueTests.cs` (new, 7 tests)
- `tests/DocumentIDE.IntegrationTests/SourceControl/GitClassifierTests.cs` (new, 94 theory cases)
- `tests/DocumentIDE.IntegrationTests/SourceControl/TestGitRepository.cs` (new fixture)
- `tests/DocumentIDE.IntegrationTests/SourceControl/HangingCommitHook.cs` (new fixture)
- `tests/DocumentIDE.IntegrationTests/Security/GitArgumentInjectionTests.cs` (new, 4 tests)
- `tests/DocumentIDE.ArchitectureTests/SourceControlProcessBoundaryTests.cs` (new, 5 tests)

**Planned-file mapping (required by sprint doc "equivalent file" rule):** the
sprint planned `tests/DocumentIDE.UnitTests/SourceControl/GitProcessRunnerTests.cs`;
no `DocumentIDE.UnitTests` project exists in this repository, so the tests live
in `tests/DocumentIDE.IntegrationTests/SourceControl/GitProcessRunnerTests.cs`
(they exercise real Git processes, so an integration suite is the correct
home; recorded here as the documented mapping).

**Extra files beyond the three planned:** `GitCapabilityInfo`,
`GitProcessRequest`, `IGitProcessRunner` (Core) and `GitCommandClassifier`,
`GitFailureClassifier`, `GitProcessRunnerOptions` (Infrastructure) are the
minimum split required by the sprint's own capabilities (typed capability
state, structured arguments, typed failures, mutation routing, controlled
environment). No behavior outside SCM-01 was added.

## Verification

- `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControl|FullyQualifiedName~GitArgumentInjection"` → **PASS (117/117, 0 skipped)**
  - GitProcessRunnerTests 12/12, GitWorktreeMutationQueueTests 7/7, GitClassifierTests 94/94, GitArgumentInjectionTests 4/4
- `dotnet test DocumentIDE.sln` (full repository) → **536/538 passed in IntegrationTests; every other suite 100%**: ArchitectureTests 12/12, SecurityTests 91/91, Core.Tests 459/459, SafetyTests 24/24, GoldenTests 39/39, SoakTests 6/6, PerformanceTests 3/3
  - The 2 failures (`ScriptTests.Release_Build_Embeds_Production_Web_Assets`, `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`) are the known pre-existing release-artifact checks (`artifacts/` absent in a fresh worktree; they assert outputs of `scripts/build-release.ps1`). Not attributable to this change — no packaging inputs were touched and the same failures are documented in BAT-08/QA-01/REL-03 handoffs; REL-03 documents the sanctioned local exclusion filter for these two.
- `dotnet build DocumentIDE.sln --nologo` → **Build succeeded, 0 errors**; warning audit shows no warnings originate from any SCM-01 file (one intermediate xUnit1012 warning in the new classifier test file was fixed before the final runs).
- Manual frontend-boundary evidence: repository-wide scan of `src/DocumentIDE.Web` for `child_process|execFile(|execSync(|spawnSync(|spawn(|git.exe|shell: true` → **0 matches**, now pinned by the architecture test.

## Acceptance criteria

- [x] **Frontend cannot invoke `git.exe` directly** — `SourceControlProcessBoundaryTests` scan of `src/DocumentIDE.Web` (PASS) + zero-match manual grep; all Git spawning confined to `Infrastructure/SourceControl` by test; Core contracts contain no process spawning; only the API composition root names the concrete runner (this test was RED before the DI wiring, GREEN after).
- [x] **Git process failures are typed and inspectable** — `GitProcessResult` (status + nullable exit code + both streams + `ErrorKind` + message); `GitFailureClassifierTests` cover the spec 13 §20 kinds; runtime tests cover not-installed, not-a-repository, invalid worktree, non-zero exit hands.
- [x] **Background operations can be cancelled without orphaned Git processes** — cancellation/timeout tests kill and reap the process tree (`ActiveProcessCount` back to 0); a hanging commit hook fixture proves true tree kill: with a parent-only kill the hook descendant stays alive and writes a completion marker, with the runner's tree kill the marker never appears.

Required tests from the sprint doc:

- [x] `git --version` capability detection succeeds or yields a typed unavailable state — first failing test (RED: `error CS0234`, `DocumentIDE.Core.SourceControl` namespace missing), now `GitVersionCapabilityDetection_SucceedsOrYieldsTypedUnavailableState` + missing-executable test.
- [x] Argument injection fixtures cannot alter Git command semantics — `GitArgumentInjectionTests` (metacharacter filenames with sentinel survival + exact staged set; leading-dash filenames treated as paths; outside-worktree path fails typed with nothing staged; secrets not echoed).
- [x] Two index-mutating operations against one worktree are serialized — `GitWorktreeMutationQueueTests` (same-worktree serialization with `MaxObservedConcurrency == 1`, cross-worktree independence, cancel-while-queued, faulted-op recovery, runner routing proofs).

## Deviations / spec conflicts

- Test-path mapping and extra files: recorded above; no spec requirement was
  weakened.
- TDD ordering nuance (disclosed for transparency): the process-tree
  kill/timeout plumbing landed inside the shared wait pipeline during the
  structured-invocation cycle, and its dedicated cancellation/timeout tests
  were added immediately after and passed on first run. Likewise the
  classifier theory tests and architecture guards are regression tests for
  behavior that already existed at that point (the architecture DI test was
  genuinely red→green across the `Program.cs` wiring; the classifier tests
  went red→green once over an incorrect fixture message in the test itself,
  not production code).
- `GitFailureClassifier` deliberately maps "insufficient permission for
  adding an object…" to `Unknown` rather than guessing; documented by test.

## Risks discovered

- **Queue key contract:** `GitWorktreeMutationQueue` keys by the caller's
  working directory. Callers must pass the worktree/repository root
  (SCM-02+ resolve it via `git rev-parse --show-toplevel`); passing a
  subdirectory would let two operations on the same index bypass mutual
  exclusion. Documented in code; downstream sprints must comply.
- **`--` before paths stays a caller responsibility:** the runner guarantees
  structured arguments and no shell, but inserting `--` before file paths is
  done at command-construction sites (SCM-02+). Spec 13 §23 rule is preserved
  but its enforcement point is downstream.
- Capability probe runs in `Environment.CurrentDirectory`; a deleted cwd
  yields a typed unavailable result (worktree-invalid hand), never an
  exception.
- The protected environment policy is intentionally non-overridable; future
  sprints must not need to weaken it (e.g. legitimate pager/editor use is
  out of scope for a headless backend).

## Next eligible sprint(s)

- SCM-02 (Git Repository, Worktree & Status Domain) — unblocked (SCM-01 + FS-03 DONE)
- SCM-15 (Git Worktree Discovery & Open Existing) — unblocked (SCM-01 + FS-03 + FND-07 DONE)

## Security & file-safety gate

- [x] no user file was mutated through an unreviewed/bypass path — the runner never touches user files itself; its only writes are the child Git process's own semantics, and mutating commands are auto-serialized per worktree; no destructive action exists in this sprint.
- [x] no arbitrary shell command can be injected through a filename/path — no shell is ever used (`UseShellExecute=false`, `ArgumentList` only); injection fixtures (spaces, Unicode, parens, semicolons, quotes, leading dashes like `--upload-pack=`) cannot alter command semantics or produce repo escape.
- [x] no repository escape is possible through path handling — working directory is canonicalized and must exist; injection tests prove out-of-worktree paths fail with a typed result and nothing staged.
- [x] no secret/credential output is exposed in UI logs — prompts are disabled (`GIT_TERMINAL_PROMPT=0`, closed stdin); auth failures surface as typed `GitAuthRequired` without echoing credentials; a test asserts secret-like env values never appear in returned streams.
- [x] no destructive action silently broadened its scope — no destructive operation implemented; unknown Git subcommands classify conservatively as mutating (serialized), never as safe.
- [x] relevant higher-level safety rules remain stronger than convenience — spec 13 §18/§19/§20/§23 rules (no shell, structured args, per-worktree serialization, typed errors, no orphaned processes) are enforced by tests and pinned by architecture guards.
