# Source Control Release Checklist

**Sprint:** SCM-17 — Source Control Regression & Release Hardening

**Specs:** `docs/specs/13-source-control-git-worktree-spec.md` (§14, §16, §18, §19, §20, §21, §23, §27), `docs/specs/11-testing-strategy.md` (§2, §26, §28)

**Evidence snapshot:** 2026-10-18, Windows, git 2.51.0.windows.2, .NET 10

This checklist is the Source Control release gate. Every item names the test that
proves it. A gate item is **not** satisfied by "it was tested in sprint N"; it is
satisfied only when the named test still exists and passes in the release
verification run.

## How to run the gate

```powershell
dotnet build DocumentIDE.sln
dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControl"
dotnet test tests/DocumentIDE.IntegrationTests
dotnet test tests/DocumentIDE.SecurityTests --filter "FullyQualifiedName~GitProcessSecurityTests"
dotnet test tests/DocumentIDE.SecurityTests
dotnet test tests/DocumentIDE.ArchitectureTests
dotnet test tests/DocumentIDE.Core.Tests
```

Frontend (run from `src/DocumentIDE.Web` in an installed workspace):

```powershell
npx vitest run
npx tsc -b
npx oxlint
```

`tests/DocumentIDE.IntegrationTests` is expected to report exactly **two**
environmental failures in a fresh worktree where `artifacts/` has not been
produced: `ScriptTests.Release_Build_Embeds_Production_Web_Assets` and
`Release.InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`.
They assert the existence of gitignored build outputs; they are not Source
Control gate items.

## 1. Automated Source Control gate

| # | Gate item (spec) | Proof |
|---|---|---|
| SC-01 | Repository/worktree discovery is derived from Git; a non-Git folder is a typed state (§5, §20) | `GitStatusTests.Repository_Worktree_Discovery_Fixtures_Pass`, `GitStatusTests.NonGitFolder_Discovery_Returns_TypedNoRepositoryState`, `SourceControlRegressionSuite.FixtureMatrix_PlainFolder_EveryRouteReturnsNoRepositoryWithoutThrowing` |
| SC-02 | Status is read from machine-readable porcelain v2 and never localization (§6) | `GitStatusTests.Status_PorcelainV2_Covers_Staged_Unstaged_Untracked_And_Conflict_States`, `GitStatusTests.Parser_PorcelainV2Z_Covers_TypedStates_For_All_EntryRecordTypes` |
| SC-03 | Markdown diff is textual and read-only; DOCX diff is document-aware and read-only (§9) | `MarkdownGitDiffTests.WorkingTree_HeadToWorkingTreeDiff_MatchesFixture`, `DocxGitDiffTests.GitVersions_OfADocx_CanBeComparedWithoutMutation`, `DocxGitDiffTests.Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists` |
| SC-04 | Staging writes the real Git index only; Document IDE keeps no staging snapshot (§10) | `GitStageTests.StageUpdatesIndexStateOnly`, `GitStageTests.StagingStateLivesInTheGitIndexNotInServiceMemory`, `GitStageTests.UnstagePreservesWorkingTreeEdits` |
| SC-05 | Commit records staged changes only, never creates an empty commit, keeps hooks, never auto-pushes (§11, §14.4) | `GitCommitTests.CommitContainsStagedChangesOnly`, `GitCommitTests.CleanWorktreeReportsNothingStagedWithoutCreatingAnEmptyCommit`, `GitCommitTests.FailingPreCommitHookIsATypedFailureAndCreatesNoCommit`, `GitCommitTests.CommitNeverTouchesARemote`, `GitPushTests.CommitDoesNotAutoPush` |
| SC-06 | Git history and file history are read-only and paginated; Document IDE History stays separate (§12, §4.3) | `GitHistoryTests.KnownFixtureHistoryRendersCorrectly`, `GitHistoryTests.HistoryAndVersionReadsAreStrictlyReadOnly`, `GitHistoryTests.LargeHistoryLoadsIncrementally`, `WorkSessionCorrelationTests.GitCommitRemainsValidWhenTheCorrelationDataIsDeleted` |
| SC-07 | Remote state is Git-derived; "no upstream" is explicit, never a fake 0/0 (§14.2) | `GitFetchTests.ConfiguredUpstreamIsParsedCorrectly`, `GitFetchTests.NoUpstreamIsATypedFirstClassState`, `SourceControlRegressionSuite.FixtureMatrix_DirtyWorktreeWithRemote_ReturnsTypedBlockingOutcome` |
| SC-08 | Fetch updates only tracking state, with structured arguments (§14.1, §18) | `GitFetchTests.FetchUpdatesTrackingStateWithoutChangingWorkingTreeContent`, `GitFetchTests.FetchCarriesOnlyStructuredFetchArguments`, `SourceControlRegressionSuite.Remote_BareRemoteFetchPullAndPushRoundTrip` |
| SC-09 | Pull is fast-forward only; dirty and conflicted worktrees are refused before Git runs; divergence never merges or rebases (§14.3, §21) | `GitPullSafetyTests.CleanFastForwardPullSucceeds`, `GitPullSafetyTests.DivergedHistoryIsRefusedWithoutMergeOrRebase`, `GitPullSafetyTests.DirtyWorktreeRefusalLeavesEveryFileByteIdentical`, `GitPullSafetyTests.ConflictedWorktreeIsRefusedAndConflictStateIsPreserved`, `SourceControlRegressionSuite.FixtureMatrix_DivergedRepository_PullRefusedTypedWithoutMergeCommit` |
| SC-10 | Push is explicit, upstream-scoped, never forced, and a rejection preserves local history (§14.4, §23) | `GitPushTests.PushToLocalBareRemoteSucceeds`, `GitPushTests.RejectedPushIsTypedAndPreservesLocalCommits`, `GitPushTests.PushCarriesOnlyStructuredPushArguments`, `GitPushTests.PushWithNoUpstreamIsTypedAndRunsNoGit`, `SourceControlRegressionSuite.Remote_PushRejectedWhenRemoteMovedAhead_IsTypedAndKeepsLocalHistory` |
| SC-11 | Discard/restore requires an explicit review token, writes recovery coverage first, and is file-scoped (§15) | `GitRestoreSafetyTests.DirtyFileDiscardRequiresExplicitReview`, `GitRestoreSafetyTests.RecoverySnapshotExistsBeforeTheMutation`, `GitRestoreSafetyTests.FailedRecoverySnapshotBlocksTheMutation`, `GitRestoreSafetyTests.DiscardPreservesStagedContentAndNeverRewritesTheIndex` |
| SC-12 | Conflict state is reported and unsafe actions are blocked typed; DOCX conflicts are never auto-merged (§21) | `GitConflictTests.ConflictFixtureIsDetected`, `GitConflictTests.CommitAndCommitAllAreRefusedWhileUnmergedAndRepositoryIsUnchanged`, `GitConflictTests.DocxConflictIsReportedAndNeverAutoMerged`, `SourceControlRegressionSuite.FixtureMatrix_ConflictedRepository_WriteRoutesRefusedTyped` |
| SC-13 | Worktree discovery/open is read-only; opening never attaches or switches branches implicitly (§16.1, §16.2) | `GitWorktreeDiscoveryTests.MainAndLinkedWorktreesAreListed`, `GitWorktreeDiscoveryTests.OpeningWorktreeDoesNotSwitchBranchesCreateBranchesOrAlterHistory`, `GitWorktreeDiscoveryTests.OpeningWorktreeIssuesOnlyTheReadOnlyWorktreeListInvocation`, `GitWorktreeApiTests.OpenRegisteredUnattachedWorktreeRequiresAttachmentFlowAndAttachesNothing` |
| SC-14 | Worktree create uses an existing ref and never creates a branch (§16.3) | `GitWorktreeMutationTests.CreateInvokesGitWithExactArgumentVectorFromTheResolvedRoot`, `GitWorktreeMutationTests.CreateFromTagCommitAndRemoteRefUsesDetachedHeadWithoutCreatingBranches`, `GitWorktreeMutationTests.CreateIsBlockedWhenBranchIsAlreadyCheckedOutElsewhere`, `GitWorktreeApiTests.CreateFromExistingBranchCreatesRealWorktree` |
| SC-15 | Worktree removal is explicit, never forced; dirty/main/locked/missing worktrees are blocked typed; prune never deletes a valid worktree (§16.4, §16.5) | `GitWorktreeMutationTests.RemoveDirtyWorktreeIsBlockedWithTypedChangeList`, `GitWorktreeMutationTests.RemoveRefusesMainLockedMissingAndUnregisteredWorktrees`, `GitWorktreeMutationTests.RemoveInvokesGitStatusAndRemovalWithExactArgumentsFromTheResolvedRoot`, `GitWorktreeMutationTests.RefreshPrunesMissingWorktreeMetadataAndKeepsValidOnes`, `SourceControlRegressionSuite.FixtureMatrix_WorktreeSet_ListOpenRemoveAndRefreshAreTyped` |
| SC-16 | The worktree lifecycle is reachable through the privileged API as a thin typed surface (§18) | `GitWorktreeApiTests.ListReturnsMainAndCreatedWorktree`, `GitWorktreeApiTests.RemoveDirtyWorktreeIsRefusedAndStillExists`, `GitWorktreeApiTests.PruneReturnsRereadRegistryAndDeletesNoValidWorktree`, `SourceControlRegressionSuite.Remote_WorktreeApiLifecycle_CreateOpenRemovePrune` |
| SC-17 | Index/repository mutations serialize **exactly one at a time per worktree**; different worktrees stay independent (§19) | `GitWorktreeMutationQueueTests.EnqueueAsync_SerializesMutationOperations_ForTheSameWorktree`, `GitWorktreeMutationQueueTests.EnqueueAsync_DoesNotSerialize_AcrossDifferentWorktrees`, `SourceControlRegressionSuite.Concurrency_StageCommitFetchPullDiscard_SerializePerWorktree`, `SourceControlRegressionSuite.Concurrency_DifferentWorktrees_ProceedIndependently` |
| SC-18 | Concurrent stage/commit operations never corrupt the index (§19) | `SourceControlRegressionSuite.Concurrency_ConcurrentStages_DoNotCorruptIndex` (8 concurrent stages, exact staged set, no `index.lock`, clean `git fsck`) |
| SC-19 | A mutation cancelled while queued **never runs**, including the release-vs-cancel race (§19) | `GitWorktreeMutationQueueTests.EnqueueAsync_CancelledWhileQueued_OperationNeverRuns`, `GitWorktreeMutationQueueTests.EnqueueAsync_CancelledConcurrentlyWithRelease_OperationNeverRuns`, `GitWorktreeMutationQueueTests.EnqueueAsync_AlreadyCancelledToken_OperationNeverRuns` |
| SC-20 | Every read/write route returns a typed outcome for unborn, no-repository, remote, diverged, dirty, conflicted and worktree fixtures — never an unhandled exception (§20) | `SourceControlRegressionSuite.FixtureMatrix_UnbornHead_EveryRouteReturnsTypedOutcome`, `SourceControlRegressionSuite.FixtureMatrix_PlainFolder_EveryRouteReturnsNoRepositoryWithoutThrowing`, `SourceControlRegressionSuite.FixtureMatrix_DirtyWorktreeWithRemote_ReturnsTypedBlockingOutcome`, `SourceControlRegressionSuite.FixtureMatrix_DivergedRepository_PullRefusedTypedWithoutMergeCommit`, `SourceControlRegressionSuite.FixtureMatrix_ConflictedRepository_WriteRoutesRefusedTyped` |
| SC-21 | All privileged Source Control routes require the localhost session token and a valid Origin/Host (§18, master spec) | `SourceControlPanelTests.SourceControlRoutesRejectMissingInvalidTokenAndHostileOrigin`, `GitStageTests.StageRoutesRejectMissingInvalidTokenAndHostileOrigin`, `GitCommitTests.CommitRoutesRejectMissingInvalidTokenAndHostileOrigin`, `GitPullSafetyTests.PullRouteRejectsMissingInvalidTokenAndHostileOrigin`, `GitPushTests.PushRouteRejectsMissingInvalidTokenAndHostileOrigin`, `GitWorktreeApiTests.WorktreeRoutesRejectMissingInvalidTokenAndHostileOriginOrHost` |
| SC-22 | No branch-management surface exists in V1 (§2, §26) | `SourceControlPanelTests.BranchManagementRoutesDoNotExist`, `GitWorktreeMutationTests.CreateInvokesGitWithExactArgumentVectorFromTheResolvedRoot`, `WorktreeCreateDialog.test.tsx` (frontend, colocated) |
| SC-23 | Source Control is optional: non-Git workflows keep working and no route initializes Git outside a repository (§5) | `SourceControlRegressionSuite.FixtureMatrix_PlainFolder_EveryRouteReturnsNoRepositoryWithoutThrowing`, `WorkSessionCorrelationTests.CorrelationWorksForANonGitFolderWithoutInitialisingGit`, `WorkSessionCorrelationTests.BatchOperationHistoryRemainsUsableWithoutGit`, `GitHistoryTests.HistoryOutsideAnyRepositoryIsTypedNoRepositoryWithoutRunningLog` |

## 2. Process-boundary and injection gate

| # | Gate item (§18, §23) | Proof |
|---|---|---|
| SC-24 | Git is invoked only through the controlled process runner; no shell command string is ever built | `SourceControlProcessBoundaryTests.CSharpProcessSpawning_IsConfinedToInfrastructureSourceControl_AndTheDocumentedWordLauncher`, `SourceControlProcessBoundaryTests.WebFrontend_HasNoProcessInvocationOrGitExecutableSurface`, `SourceControlProcessBoundaryTests.ApiCompositionRoot_IsTheOnlyCSharpPlaceWiringTheGitRunner`, `GitProcessRunnerTests.RunAsync_StructuredArguments_NoShell_ResolveRootOfRepositoryInPathWithSpacesAndUnicode` |
| SC-25 | Filenames with spaces, Unicode, emoji, leading dashes, quotes and shell metacharacters are staged verbatim as **one structured argument**, never shell text | `GitProcessSecurityTests.HostileFileName_IsStagedVerbatim_AsOneStructuredArgument` (11 cases), `GitProcessSecurityTests.LeadingDashFileName_IsAPathAfterSeparator_NotAnOption`, `GitProcessSecurityTests.CommitMessage_WithShellMetacharacters_IsOneStructuredArgument`, `GitArgumentInjectionTests.InjectionFixture_FilenamesWithShellMetacharacters_AreStagedVerbatim_WithoutShellSemantics` |
| SC-26 | Paths containing characters Windows cannot name (`|`, `>`, `"`, `?`, `*`) remain data: one argument, typed failure, no shell | `GitProcessSecurityTests.ClientSuppliedShellMetacharacterPath_IsDataNotAShellCommand` (5 cases) |
| SC-27 | Traversal, absolute/UNC/drive-root, colon, NUL, malformed and non-normalized paths are rejected typed; index mutations never run | `GitProcessSecurityTests.DiffService_RejectsUnsafePath_WithoutSpawningGit` (13 cases), `GitProcessSecurityTests.StageService_RejectsUnsafePath_WithoutAnyIndexMutation` (9 cases), `GitProcessSecurityTests.NulInPath_IsRejectedBeforeAnyIndexMutation`, `GitStageTests.EscapingAndNonFilePathArgumentsAreRejectedBeforeGitRuns`, `GitStageTests.AbsolutePathsAreRejectedEvenWhenTheyPointInsideTheWorktree` |
| SC-28 | Control characters (other than NUL) cannot escape the shell or mutate anything | `GitProcessSecurityTests.ControlCharacterPath_IsDataAndFailsTyped_WithoutAShell` (3 cases) |
| SC-29 | Worktree target paths and ref tokens are validated before Git runs; unsafe values spawn no process | `GitProcessSecurityTests.WorktreeCreate_RejectsUnsafeRef_WithoutSpawningGit` (17 cases), `GitProcessSecurityTests.WorktreeCreate_RejectsUnsafeTargetPath_WithoutSpawningGit` (4 cases), `GitWorktreeMutationTests.CreateRejectsUnsafeTargetPathsWithoutInvokingGit`, `GitWorktreeApiTests.UnsafePathsAndMalformedRefsAreRejectedBeforeGitRuns` |
| SC-30 | No repository escape through a file path; `.git` paths cannot become staged changes | `GitArgumentInjectionTests.NoRepositoryEscape_AddOutsideWorktree_FailsTyped_AndStagesNothing`, `DocxGitDiffTests.EscapingOrRootedPaths_AreRejected_BeforeAnyGitOrFileAccess`, `GitRestoreSafetyTests.HostilePathsAreRejectedBeforeAnyDestructiveGitRuns`, `GitProcessSecurityTests.DotGitPath_IsNeverStaged_AndTheDiffReadIsReadOnly` |
| SC-31 | Repository paths with spaces, Unicode and emoji work on Windows | `GitProcessSecurityTests.RepositoryPathWithSpacesUnicodeAndEmoji_StagesNormally`, `GitPullSafetyTests.PullHandlesPathsWithSpacesUnicodeAndSpecialCharacters`, `GitWorktreeMutationTests.CreateIntoTargetPathWithSpacesAndUnicodeSucceeds` |
| SC-32 | No credential/secret material is surfaced in a typed failure or log line | `GitProcessSecurityTests.FailedFetch_DoesNotExposeCredentialsFromTheRemoteUrl`, `GitPushTests.PushFailureNeverSurfacesCredentialMaterialFromRemoteUrl`, `GitPushTests.PushFailureRedactsPasswordContainingAnAtSign`, `GitConflictTests.ConflictFailureDiagnosticsNeverExposeRemoteUserInfo`, `GitArgumentInjectionTests.RunResults_DoNotEchoEnvironmentSecrets` |
| SC-33 | Cancellation/timeout terminate the Git process tree; no orphaned processes | `GitProcessRunnerTests.RunAsync_Cancellation_TerminatesGitProcessTree_WithoutOrphanedProcesses`, `GitProcessRunnerTests.RunAsync_Timeout_TerminatesGitProcessTree_AndYieldsTypedTimedOut`, `GitWorktreeMutationQueueTests.GitProcessRunner_CancelledWhileWaitingForWorktreeQueue_YieldsTypedCancelled` |

## 3. Security & file-safety gate (sprint checklist)

- [x] **No user file was mutated through an unreviewed/bypass path.** Mutations are explicit services only; discard/restore requires a review token and a
  recovery snapshot (`GitRestoreSafetyTests.DirtyFileDiscardRequiresExplicitReview`,
  `GitRestoreSafetyTests.RecoverySnapshotExistsBeforeTheMutation`). Read routes
  (`GitStatusTests`, `GitHistoryTests`, `MarkdownGitDiffTests`) never write.
- [x] **No arbitrary shell command can be injected through a filename/path.** SC-25,
  SC-26, SC-27, SC-28, SC-29; every recorded invocation is one structured
  argument vector, and a sentinel file survives every metacharacter fixture.
- [x] **No repository escape is possible through path handling.** SC-27, SC-30;
  `GitDocumentPath` rejects rooted/`..`/colon/empty-segment paths and re-checks
  containment on the combined absolute path.
- [x] **No secret/credential output is exposed in UI logs.** SC-32. Residual:
  `.git/config` can be rendered by the diff surface (see R-3).
- [x] **No destructive action silently broadened its scope.** Stage/commit are
  file- or worktree-scoped by construction; pull is `--ff-only --no-rebase`;
  push is upstream-scoped with no force/refspec; removal is non-forced and
  refuses dirty/main/locked/missing; restore is one reviewed path.
- [x] **Relevant higher-level safety rules remain stronger than convenience.**
  The local filesystem stays authoritative, Git state is always derived from Git,
  Commit never uses `-a`, Commit All never includes ignored/outside files, pull
  never merges/rebases, push is never automatic or forced, and no branch
  lifecycle exists.
- N/A — recovery snapshot + temp mutation + atomic replace: **not applicable to
  Source Control**, which delegates content mutation to Git's own transactional
  commands and only writes recovery snapshots; the rule governs the
  Search & Replace/source-mutation pipeline, which this sprint does not touch.

## 4. Manual Windows checks (explicitly justified)

These cannot be asserted in-process by the test host; run them on the release
candidate before shipping.

1. **Word save → debounced Source Control refresh.** Open a DOCX from a Git
   worktree in Word, save, and confirm the pane refreshes after the debounce
   without Git running inside the watcher callback. The automated unit seam is
   `GitStatusTests.FilesystemEvent_Triggers_DebouncedStatusRefresh_OutsideTheWatcherCallback`;
   the real Word + WebView host path is manual because it requires Word Desktop
   and the packaged WebView host.
2. **Configured credential helper.** Push/pull against an authenticated remote
   with a pre-configured Git credential helper (e.g. GCM) and confirm cached
   credentials work while an *uncached* credential fails promptly as
   `AuthenticationRequired` instead of prompting. Automated coverage pins the
   prompt-disabled environment (`GitFetchTests.FetchProcessEnvironmentDisablesInteractiveCredentials`,
   `GitPushTests.PushProcessIsBoundedAndDisablesInteractiveCredentials`);
   helper-specific behavior depends on the machine's Git configuration.
3. **Real Windows path edge cases on the target filesystem** (OneDrive/UNC
   workspace roots, >260-character paths): automated fixtures cover spaces,
   Unicode, emoji, drive roots and traversal on the local NTFS temp directory.

## 5. Residual risks

| ID | Risk | Severity | Evidence / recommendation |
|---|---|---|---|
| R-1 | **No `GET api/source-control/diff` route.** Spec 13 §18 lists a diff endpoint; `GitDiffService`/`DocxGitDiffProvider` are not DI-registered and no controller exposes them. Markdown/DOCX diff is proven at service level only, so the packaged app cannot reach document-aware diff through the privileged API today. | Release-gate gap for "Markdown diffs are reviewable" through the product surface | `grep GitDiffService src/DocumentIDE.Api` → no matches; `SourceControlController` route list has no `diff`. Follow-up sprint: wire `GET api/source-control/diff` to the existing services (no new Git behaviour). |
| R-2 | **SCM service registration exists in two places.** `DocumentIDE.Api/Program.cs` wires SCM-01…18 inline, while `SourceControlServiceRegistration.AddSourceControlServices` (used by the desktop WebView host after the UI-03 architecture fix) wires the same graph with `TryAdd*`. The two lists can drift. | Medium (composition drift; not a data-safety risk) | `SourceControlProcessBoundaryTests.ApiCompositionRoot_IsTheOnlyCSharpPlaceWiringTheGitRunner` permits both locations by design. Follow-up: migrate `Program.cs` onto `AddSourceControlServices` and delete the inline block (recorded as a UI-03 residual). |
| R-3 | **`GET`/diff surface can read `.git` internals.** A contained `.git/config` path is accepted by `GitDiffService`'s working-tree read and can be rendered; `.git/config` may contain remote URLs with embedded credentials. The path is contained (no escape) and the route is the authenticated privileged API, not public ShareHost, but callers must not request `.git` paths. | Medium (information exposure, privileged surface) | `GitProcessSecurityTests.DotGitPath_IsNeverStaged_AndTheDiffReadIsReadOnly`. Recommendation: reject `.git` segments in `GitDocumentPath` (separate sprint; behavior change outside SCM-17). |
| R-4 | **Staging a `.git/...` path reports `Succeeded` with no index change.** Git ignores its own directory on `add` (exit 0), so the typed outcome is imprecise even though nothing is staged or exposed. | Low (UX/typed-outcome precision) | Same test as R-3. Recommendation: treat `.git` segments as rejected in the stage path validator. |
| R-5 | **Control characters other than NUL are forwarded to Git.** They cannot name a Windows file and Git fails typed; no shell is involved. | Low | `GitProcessSecurityTests.ControlCharacterPath_IsDataAndFailsTyped_WithoutAShell`. |
| R-6 | **PDF peak-memory test is load-sensitive and flaked once in this sprint's repeated full runs.** `PdfMergeMemoryTests.Merge_PeakMemory_DoesNotScaleLinearlyWithTotalSourceBytes` samples process-wide `GC.GetTotalMemory(forceFullCollection: true)` every 5 ms against a 12 MB bound while xUnit runs other test classes in parallel. It passed in isolation and in the other full runs. Pre-existing, unrelated to Source Control. | Medium for suite reliability (not Source Control) | Run 1: 1028 passed / 3 failed (the two environmental + this). Runs before/after: 1029 / 2. Recommendation: make the measurement process-exclusive or raise the bound (separate QA sprint). |
| R-7 | **Frontend SCM suites were not re-run in this worktree** because `src/DocumentIDE.Web/node_modules` is absent (untracked, not shared across worktrees) and the sprint made no frontend change. The colocated Vitest suites (`WorktreeCreateDialog.test.tsx`, `PullDialog.test.tsx`, etc.) remain the automated evidence and run in the full release pipeline. | Low (environmental) | `workflow`/`scripts/verify-release.ps1` runs `FrontendTests`; run `npx vitest run` in an installed workspace before shipping. |
| R-8 | **Bare repositories are refused by the worktree preflight** (`worktree list --porcelain` parser structural rules) — fail-closed, typed `PreflightFailed`, no mutation. | Low (unsupported fixture shape) | SCM-16 handoff; `GitWorktreeMutationTests.CreateRejectsUnsafeTargetPathsWithoutInvokingGit` and removal refusals. |

## 6. Evidence snapshot

| Command | Result |
|---|---|
| `dotnet build DocumentIDE.sln` | 0 errors (162 warnings, all pre-existing) |
| `dotnet test tests/DocumentIDE.IntegrationTests --filter "FullyQualifiedName~SourceControlRegressionSuite"` | 13 passed / 0 failed / 0 skipped |
| `dotnet test tests/DocumentIDE.SecurityTests --filter "FullyQualifiedName~GitProcessSecurityTests"` | 70 passed / 0 failed / 0 skipped |
| `dotnet test tests/DocumentIDE.IntegrationTests` (run 1) | 1029 passed / 2 failed / 0 skipped (both environmental: `artifacts/` absent) |
| `dotnet test tests/DocumentIDE.IntegrationTests` (run 2) | 1028 passed / 3 failed / 0 skipped (environmental + R-6 PDF flake) |
| `dotnet test tests/DocumentIDE.IntegrationTests` (run 3) | 1029 passed / 2 failed / 0 skipped (both environmental) |
| `dotnet test tests/DocumentIDE.SecurityTests` | 161 passed / 0 failed / 0 skipped |
| `dotnet test tests/DocumentIDE.ArchitectureTests` | 12 passed / 0 failed / 0 skipped |
| `dotnet test tests/DocumentIDE.Core.Tests` | 459 passed / 0 failed / 0 skipped |

## 7. Sign-off

- [ ] Every SC-01…SC-33 item green in the release verification report.
- [ ] The two environmental `artifacts/` failures are the only IntegrationTests
      failures in the report (or the report was produced after a release build).
- [ ] R-1 accepted or fixed before release (diff is specified for V1).
- [ ] Residual risks reviewed; R-2/R-3 tracked for the next Source Control sprint.
- [ ] Manual Windows checks (§4) executed on the release candidate.

Signed: _______________ Date (UTC): _______________
