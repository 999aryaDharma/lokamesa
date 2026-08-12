# Sprint Handoff — BAT-02 — Batch Scope Resolver

**Status:** DONE  
**Branch:** `feat/bat-02-batch-scope-resolver`  
**Commits:** local workspace changes

## What changed
- Created `BatchScopeResolver` (`src/DocumentIDE.Core/Batch/BatchScopeResolver.cs`) implementing `IBatchScopeResolver` for deterministic batch scope resolution across:
  - **Selection Scope:** Resolves strictly by explicit `DocumentIds`. Selection never silently expands to include parent folders, subfolders, or sibling files.
  - **Folder Scope:** Recursively includes nested files by default (`IncludeSubfolders = true`). Supports non-recursive mode (`IncludeSubfolders = false`).
  - **Workspace & Collection Scopes:** Resolves documents associated with target workspace or collection IDs.
  - **Branch Exclusions:** Applies `ExcludedBranches` filtering to ignore excluded directory trees (e.g., `Archive`).
  - **FS-08 Containment Policy Reuse:** Integrates `SourceContainmentPolicy.IsContained` to guarantee resolved documents do not escape configured folder sources.
  - **Scanned File & Subfolder Metrics:** Reports `FilesScannedCount` (scanned candidate file count before content matching) and distinct `SubfolderCount`.
- Added comprehensive unit tests in `tests/DocumentIDE.Core.Tests/Batch/BatchScopeResolverTests.cs` validating recursive folder resolution, branch exclusions, strict selection boundaries, subfolder metrics, workspace/collection resolution, and containment enforcement.

## Files changed
- `src/DocumentIDE.Core/Batch/BatchScopeResolver.cs`
- `tests/DocumentIDE.Core.Tests/Batch/BatchScopeResolverTests.cs`

## Verification
- `& "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe" test` → PASS (All 407 Core tests, 7 Architecture tests, 158 Integration tests pass cleanly; 572 total passed).
- Red → Green → Refactor TDD workflow followed for all behavioral additions.

## Security & File-Safety Gate Confirmation
- [x] **No user file was mutated through an unreviewed/bypass path:** Scope resolution is a read-only query operation used to produce reviewed plans.
- [x] **No arbitrary path or secret is exposed:** Resolves documents contained within configured folder sources without leaking secrets.
- [x] **Dependency/security boundaries remain intact:** Domain logic is housed strictly within `DocumentIDE.Core`.
- [x] **No destructive action silently broadened its scope:** Selection never silently expands; Folder scope honors explicit branch exclusions and containment.
- [x] **Relevant higher-level safety rules remain stronger than convenience:** Reused FS-08 `SourceContainmentPolicy` prevents uncontained files from entering batch scopes.

## Acceptance Criteria Mapping
- **Containment policy from FS-08 is reused:** Verified by `SourceContainmentPolicy.IsContained` integration in `BatchScopeResolver.cs` and `ContainmentPolicy_ExcludesUncontainedFiles` test in `BatchScopeResolverTests.cs`.
- **Folder scope recursively includes nested files:** Verified by `FolderScope_RecursivelyIncludesNestedFiles` in `BatchScopeResolverTests.cs`.
- **Excluded Archive branch is absent:** Verified by `ExcludedArchiveBranch_IsAbsentFromResolvedScope` in `BatchScopeResolverTests.cs`.
- **Selection never silently expands:** Verified by `SelectionScope_NeverSilentlyExpands` in `BatchScopeResolverTests.cs`.

## Deviations / Spec Conflicts
None.

## Risks Discovered
None.

## Next Eligible Sprint(s)
- `BAT-03` — Batch Preview API & Review Shell (Requires BAT-01, BAT-02)
