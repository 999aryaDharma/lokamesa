# Sprint Handoff — BAT-10 — Strict Batch Rename

**Status:** DONE
**Branch:** `security/bat-10-strict-batch-rename`
**Commits:** `5c3b9a5` — `security(batch): strict batch rename` (implementation + tests); this handoff follows in a docs commit.

## What changed
- New Core operation plug-in `BatchRenameOperation` (`DocumentIDE.Core.Batch.Operations`), basename-only by contract (spec 07 §29, master §101.5):
  - **Step 1 — preview:** `BuildPreview` returns a deterministic (DocumentId ordinal) old/new basename entry for every reviewed file, surfacing FS-07 basename-rule violations and batch collisions as typed per-file failure codes; read-only, never mutates.
  - **Steps 2–3 — validation/collisions:** `ApplyAsync` validates every target with `PathSafetyValidator` (separators, `.`/`..`, rooted paths, traversal components, illegal Windows characters, reserved device names, ADS/`:` syntax, trailing dot/space, `Path.GetFileName(name) == name`), plus classic MAX_PATH (260) target length, duplicate internal target paths, existing destinations and malformed targets (empty DocumentId / relative reviewed path). Any failure fails the whole batch closed (`Blocked`) **before the first file is touched**; valid members report `Skipped/BatchBlocked`.
  - **Step 4 — journaled apply:** each file is renamed only through `IPhysicalFileOperations.RenameItemAsync` (FS-07), so the collision check and the move-intent journal (`IMoveCorrelationService.RecordMoveIntent`) are the existing FS-07 path; never `MoveItemAsync`/delete/duplicate. Per-file flow runs under `Core.Filesystem.FileOperationLocks` (the `BatchApplyEngine` per-file serialization discipline). The reviewed path and SHA-256 content fingerprint are re-verified immediately before the rename (same bounded/reparse-guarded read semantics as BAT-03/04/05 via the existing internal `SearchReplaceOperation.ReadBoundedAsync`).
  - **Step 5 — identity:** on success the same `Document` row is updated (`LastKnownPath`, `CurrentPath`, `Filename`, `Extension`, `UpdatedAt`) — `Id`, content fingerprint, size, modified time, availability/index status and all DocumentId-keyed canvas/graph/collection references are untouched. Rename never rewrites content (spec 07 §21, spec 04 §22).
  - **Typed outcomes, fail closed:** `BatchRenameResult`/`BatchRenameFileResult` reuse the BAT-04 `BatchApplyResultStatus`/`BatchApplyFileOutcome` vocabulary. Stale file (`Skipped/Stale`), vanished file (`Failure/Unavailable`), unknown document, collision, over-length path, physical rename failure, metadata-update failure, and cancellation (`Canceled` + `Skipped/Cancelled` for everything not attempted) are all inspectable, never silent. Runtime per-file failures do not stop remaining valid files.
- No existing shared file was modified: reuse is read-only (`IPhysicalFileOperations`, `PathSafetyValidator`, `FileOperationLocks`, `SearchReplaceOperation.ReadBoundedAsync`). `BatchApplyEngine` is untouched.
- `MaxTargetPathLength = 260` is the conservative Windows ceiling for spec 07 §21 "path length" validation.

## Files changed
- `src/DocumentIDE.Core/Batch/Operations/BatchRenameOperation.cs` (new, 386 lines)
- `tests/DocumentIDE.IntegrationTests/Batch/BatchRenameTests.cs` (new, 591 lines, 29 test cases)

## Verification
SDK: `C:/Users/User/AppData/Local/Programs/FlyEnv-Data/app/dotnet/10.0.302/dotnet.exe`.

- Focused: `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~BatchRename"` → **29/29 passed, 0 skipped** (red→green per behavior; RED evidence in the task report).
- Affected suite: full `DocumentIDE.IntegrationTests` → **250 passed, 2 failed, 0 skipped**. Both failures are pre-existing/environmental release-tooling tests (`ScriptTests.Release_Build_Embeds_Production_Web_Assets`, `InstallerTests.Build_Release_Script_Packages_Wxs_Installer_And_ShareHost`, missing `artifacts/desktop/DocumentIDE.Desktop.exe` / npm release build); reproduced identically on the pristine base tree with this diff stashed, so not caused by BAT-10.
- Affected suite: full `DocumentIDE.Core.Tests` → **440/440 passed**; `DocumentIDE.ArchitectureTests` → **7/7**; `DocumentIDE.SecurityTests` → **52/52**.
- `scripts/verify.ps1` — controller runs full repository verification on this worktree.

## Acceptance criteria
- [x] **Rename cannot become move** — (a) traversal/rooted/separator/ADS/over-long basenames are rejected fail-closed with no file touched (`Apply_TraversalBasenameIsRejectedAndNothingIsTouched` for `..\evil.docx` plus a 15-case malformed-name theory); (b) a successful rename's target stays in the reviewed source directory (`Apply_RenamesThroughFs07JournalAndStaysInSameDirectory`); (c) the reviewed contract exposes only `DocumentId/FilePath/ExpectedFingerprint/NewBaseName` and the physical seam calls only `RenameItemAsync` (`RenameSurface_CannotExpressMoveAndOnlyRenamesPhysically`).
- [x] Required test 1: `..\evil.docx` is rejected — blocked batch, typed `InvalidBaseName`, source untouched, no file created outside the directory, no journal intent. Demonstrated by a failing test before implementation (RED: `NotImplementedException`, then null failure code).
- [x] Required test 2: internal batch target collisions detected before apply — two files → `same.md` yields `Blocked` + `DuplicateTarget` for both, nothing renamed/journaled; the pre-fix RED showed the first file being renamed (`Completed`) before the test caught it (`Apply_InternalBatchTargetCollisionIsDetectedBeforeFirstFileChanges`).
- [x] Required test 3: successful rename keeps DocumentId — `Id` unchanged, only path fields updated, all other metadata and bytes preserved (`Apply_SuccessfulRenameKeepsDocumentIdAndPreservesMetadata`).
- [x] Failure/edge cases typed: changed file after preview `Skipped/Stale`; path changed after preview `Skipped/Stale`; one missing file `Failure/Unavailable` while a valid batch member still succeeds.
- [x] Apply goes through the FS-07 physical operation journal — move intent asserted via `FindActiveIntentByDestination` with the original `DocumentId`.
- [x] No required test skipped; no unresolved placeholders.

## Security & file-safety gate
- [x] **No user file was mutated through an unreviewed/bypass path** — mutation happens only via `RenameItemAsync`; every target carries a reviewed fingerprint that is re-verified, paths are re-checked against the document record, and any pre-flight violation blocks the whole batch before the first rename.
- [x] **No arbitrary path or secret is exposed** — rename input is basename-only; typed reason codes are fixed English codes, no credentials or document content in results/logs.
- [x] **Dependency/security boundaries remain intact** — BCL + existing Core interfaces only; no new packages; no edits to shared types; Core does not reach into Infrastructure.
- [x] **No destructive action silently broadened its scope** — a single invalid/colliding target blocks the entire reviewed batch; runtime failures are per-file typed; nothing is auto-suffixed or auto-skipped silently.
- [x] **Higher-level safety rules remain stronger than convenience** — spec 07 §21/§29 validation is fail-closed; rename still cannot physically move a file (separate `MovePhysicalFile` operation untouched); content is never rewritten.

## Deviations / spec conflicts
- None. No higher-level specification was weakened. Process note: the first TDD cycle initially landed part of the Step-3 collision logic ahead of its test; it was reverted to the exact cycle-1 behavior and re-implemented only after the Step-3 test failed for the right reason (partial application), so every shipped behavior has RED-before-GREEN evidence (details in the task report).

## Risks discovered
- `PhysicalFileOperations.RenameItemAsync` (FS-07, unmodified) falls back to a random GUID for its journal intent when its exact-string repository lookup misses the source path (e.g. stored path casing differs). BAT-10's own repository update keeps the document record correct regardless; watcher correlation can still fall back to NTFS identity/`DocumentIdentityService`. Pre-existing FS-07 behavior, flagged for QA-04.
- Cross-process writers/renamers are out of scope (as everywhere in the safe-apply design); mitigated in-process by the shared `FileOperationLocks` discipline and by fingerprint + destination collision checks, which fail closed if the state changed.
- Rename results are not yet persisted into batch history/undo records — intentionally out of scope (BAT-04 history/undo integration belongs to downstream BAT-11/HIST surfaces); the FS-07 move-intent journal still gives watcher traceability.

## Next eligible sprint(s)
- `BAT-11` — Batch Results & History UI (dep BAT-10, HIST-02) — becomes eligible once this worktree is merged.
- `QA-04` — Batch Safety Regression Suite (dep BAT-10, HIST-02) — becomes eligible once this worktree is merged.
