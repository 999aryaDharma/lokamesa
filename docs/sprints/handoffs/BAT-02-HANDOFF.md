# Sprint Handoff — BAT-02 — Batch Scope Resolver

**Status:** DONE
**Branch:** `feat/bat-02-batch-scope-resolver`
**Commits:** `60593a1` (implementation); completion metadata in the following docs commit.

## What changed
- Reconciled the old DONE handoff against baseline `9b1aa78`: its claimed resolver and tests were absent. Implemented them with observed RED → GREEN cycles.
- Added Selection, Folder, Workspace, and Collection resolution from trusted backend document/source/membership snapshots. Selection and collection IDs never expand into siblings or a search query. Sources and collections are workspace-scoped.
- Folder recursion defaults ON; exclusions are canonicalized below the folder root. `Archive` exclusion does not remove `Archive2`. Results deduplicate IDs and use deterministic ordinal ordering.
- Reused FS-08 `SourceContainmentPolicy.IsContained`. Unknown, outside-source, missing, malformed, unsupported, and reparse targets have typed document-level failures while valid files remain available.
- Returned file counts before content matching and subfolder ancestors for resolved files. No content is parsed, matched, or mutated.
- Corrected preexisting TypeScript build errors using type-only imports, unused-import/binding removal, a local listener type matching the existing store pattern, and an unused parameter rename. No compiler gates were disabled.
- Corrected a preexisting security assertion to identify the exact reported share root instead of guessing it from directory-name substrings; access assertions and production sharing behavior remain unchanged.

## Files changed
- `src/DocumentIDE.Core/Batch/BatchScopeResolver.cs`
- `tests/DocumentIDE.Core.Tests/Batch/BatchScopeResolverTests.cs`
- `src/DocumentIDE.Web/src/features/canvas/DocumentCanvas.tsx`
- `src/DocumentIDE.Web/src/features/canvas/nodes/DocumentNode.test.tsx`
- `src/DocumentIDE.Web/src/features/graph/{DocumentGraph.test.tsx,GraphPerformance.test.ts,graphLayoutWorker.ts,graphStore.ts}`
- `src/DocumentIDE.Web/src/features/inspector/RelationsInspector.test.tsx`
- `src/DocumentIDE.Web/src/features/markdown/MarkdownEditor.test.tsx`
- `src/DocumentIDE.Web/src/features/search/{MetadataFilterChips.test.tsx,SearchModeSwitch.test.tsx,SearchPanel.test.tsx,SearchPanel.tsx,searchStore.ts}`
- `tests/DocumentIDE.SecurityTests/ShareHostIsolationTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/BAT-02-HANDOFF.md`

## Verification
SDK executable: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- Baseline `dotnet test --no-restore --filter 'FullyQualifiedName~Batch|FullyQualifiedName~SourceContainment' --nologo -m:1` → 13 passed.
- `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj --no-restore --filter FullyQualifiedName~BatchScopeResolverTests --nologo -m:1` → 19 passed, 0 skipped.
- RED evidence: empty folder result (2 failures), missing scope/exclusion/failure behavior (14 failures), missing-folder and junction guards (2 failures). GREEN after each corresponding implementation. Ignored logs: `artifacts/bat-02-{red,green}-*.log`.
- `npm run build` in `src/DocumentIDE.Web` → PASS after correcting observed TypeScript failures.
- `npm test -- --maxWorkers=2` in `src/DocumentIDE.Web` → 16 files / 59 tests passed, 0 skipped.
- `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj --no-restore --nologo -m:1` → 52 passed, 0 skipped. Explicit invocation is necessary because current solution configuration omits this project from the solution test run.
- `scripts/verify.ps1` → PASS on the final fresh run: .NET build, 426 Core + 158 Integration + 7 Architecture tests, frontend build, and 59 frontend tests. Evidence: `artifacts/bat-02-final-verification.log`. Explicit SecurityTests rerun → 52 passed; evidence: `artifacts/bat-02-final-security.log`.
- Fresh-context read-only review approved resolver and later compile/security-test corrections: no Critical/Important findings. One reporting minor recorded below.

## Acceptance criteria
- [x] Recursive folder inclusion and source immutability: `FolderScope_RecursivelyIncludesNestedFiles_WithoutMatchingOrMutation`; nonrecursive test also passes.
- [x] Excluded Archive branch absent: `ExcludedArchiveBranch_IsAbsent_WithoutExcludingArchiveSibling`.
- [x] Selection never expands: explicit selection, deduplication, empty-selection, and unknown-ID tests.
- [x] Workspace/Collection resolution: workspace-source isolation and collection membership tests.
- [x] Counts before matching: hand-checked fixture resolves two files and two included subfolder levels without searching contents.
- [x] FS-08 containment reused: uncontained selection, outside-folder, and real junction escape tests.

## Security & file-safety gate
- [x] No user source mutation: path/attribute inspection only; fixture contents remain unchanged.
- [x] No arbitrary-path or secret endpoint: domain resolver only, no public API; failures expose document IDs and typed reasons.
- [x] Boundaries intact: no new package, process, database identity, or mutation services.
- [x] No silent scope expansion: explicit IDs, workspace ownership, exclusions, recursion setting, and deduplication tested.
- [x] Safety over convenience: unsafe/unavailable targets are excluded with inspectable reasons; reparse traversal fails closed.

## Deviations / spec conflicts
- Reopened the unsupported DONE status and temporarily blocked BAT-03 until all gates pass.
- Collection persistence does not exist yet. `BatchCollectionSnapshot` consumes backend-owned explicit membership without adding collection CRUD/UI. BAT-03 must load trusted snapshots when integrating the resolver; public input must not provide membership authority.
- Existing FS-08 utilities perform lexical containment and incorrectly describe `GetFullPath` as link resolution. This resolver conservatively rejects all reparse traversal, including contained links, pending reliable shared target resolution.
- Compile-only frontend changes and the security fixture assertion correction were necessary for repository verification; no downstream feature was implemented.

## Risks discovered / deferred review minor
- `IncludedSubfolders` counts ancestors of resolved supported files. Empty, unsupported-only, and unavailable-only directories are absent; downstream UI must label that meaning rather than claim an exhaustive physical directory tree.
- Resolution is a snapshot, not mutation authorization. BAT-03/BAT-04 must revalidate paths/containment, fingerprints, and lock state before mutation.
- Existing NuGet warnings: fallback Markdig/HtmlSanitizer versions, moderate AngleSharp/HtmlSanitizer advisories, high SQLitePCLRaw.lib.e_sqlite3 advisory, and Windows platform warnings. These predate BAT-02 and remain release risks.
- One intermediate full run failed `One_Word_Like_Burst_Becomes_One_Logical_Modification` and `AppOriginatedMove_CorrelatesWatcherDeleteCreateSequence` with empty event lists. Both use fixed 250 ms waits. Both passed a focused recheck; subsequent full sequential verification and the final fresh full verification passed all 158 integration tests. Timing sensitivity remains a QA-02 risk; no assertion or production watcher code was weakened.
- Solution configuration does not select SecurityTests; the explicit command above supplies evidence for this sprint.
- Reviewer did not rerun tests or assess the preexisting isolation evaluator's evidence quality. The implementing agent inspected command results; no OS sandbox claim is made.

## Next eligible sprint(s)
- `BAT-03` — Batch Preview API & Review Shell (READY; both dependencies DONE).
- Remaining V1/SCM work is unfinished; future-improvement versions remain SPECIFIED and unpromoted.
