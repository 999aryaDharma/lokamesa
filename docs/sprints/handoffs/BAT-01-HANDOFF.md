# Sprint Handoff — BAT-01 — Batch Operation Plan Domain

**Status:** DONE  
**Branch:** `feat/bat-01-batch-operation-plan-domain`  
**Commits:** `3da7d3f`  

## What changed
- Defined operation types (`SearchAndReplace`, `FontChange`, `PageSetup`, `Rename`, `SetFont`, `SetFontSize`, `SetMargins`, `SetPaperSize`, `SetOrientation`, `SetAlignment`, `RenameFiles`) and plan statuses (`Draft`, `Previewed`, `Executing`, `Completed`, `Expired`, `Failed`) in `src/DocumentIDE.Core/Batch/OperationPlan.cs`.
- Created `BatchOperationPlan` and per-file plan persistence models (`BatchOperationPlanFile`) in `src/DocumentIDE.Core/Batch/OperationPlan.cs` containing expected SHA256 file fingerprints, selected occurrence/change IDs, lifecycle timestamps (`CreatedAt`, `ExpiresAt`), scope, parameters, and optional idempotency keys.
- Implemented domain lifecycle and validation logic:
  - Plan expiry check (`IsExpired` / `ExpiresAt` timeout enforcement).
  - Idempotency enforcement (`CanApply`, `MarkExecuting`, `MarkCompleted` preventing completed plans from applying twice).
  - Parameter validation (`ValidateApplyParameters` disallowing apply payloads that differ from reviewed preview parameters or contain unselected/unreviewed files/operation types).
- Created `IBatchPlanRepository` in `src/DocumentIDE.Core/Domain/Repositories/IBatchPlanRepository.cs`.
- Added database migration `007_BatchOperationPlans` in `src/DocumentIDE.Infrastructure/Persistence/Migrations/007_BatchOperationPlans.cs` and registered it in `MigrationRunner.cs`.
- Implemented `BatchPlanRepository` in `src/DocumentIDE.Infrastructure/Persistence/Repositories/BatchPlanRepository.cs` with full SQLite CRUD, file plan cascading persistence, and idempotency key query capability.
- Added comprehensive unit tests in `tests/DocumentIDE.Core.Tests/Batch/OperationPlanTests.cs` and integration tests in `tests/DocumentIDE.IntegrationTests/Batch/BatchPlanRepositoryTests.cs`.

## Files changed
- `src/DocumentIDE.Core/Batch/OperationPlan.cs`
- `src/DocumentIDE.Core/Domain/Repositories/IBatchPlanRepository.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/007_BatchOperationPlans.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/BatchPlanRepository.cs`
- `tests/DocumentIDE.Core.Tests/Batch/OperationPlanTests.cs`
- `tests/DocumentIDE.IntegrationTests/Batch/BatchPlanRepositoryTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/BAT-01-HANDOFF.md`

## Verification
- `& "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302\dotnet.exe" test` → PASS (All 5 `OperationPlanTests` unit tests and 4 `BatchPlanRepositoryTests` integration tests pass cleanly; full test suite passes).
- Strict Red → Green → Refactor TDD cycle followed for all behavioral additions.

## Security & File-Safety Gate Confirmation
- [x] No user file was mutated through an unreviewed/bypass path: `ValidateApplyParameters` enforces that apply endpoints must reference a reviewed plan and matching parameters.
- [x] No arbitrary path or secret is exposed: Plans store document IDs and SHA256 fingerprints without exposing secret keys or unsafe paths.
- [x] Dependency/security boundaries remain intact: Infrastructure persistence isolates database access cleanly through repositories.
- [x] No destructive action silently broadened its scope: Scope JSON and selected file IDs/change IDs are frozen in preview plans.
- [x] Relevant higher-level safety rules remain stronger than convenience: Idempotency and expiry prevent stale or re-applied operations.

## Acceptance Criteria Mapping
- **Plan can be created/read:** Verified by `Plan_CanBeCreatedAndRead_WithExpectedPropertiesAndFingerprints` in `OperationPlanTests` and `SavePlanAsync_And_GetByIdAsync_PersistsPlanAndFilesWithFingerprintsAndChangeIds` in `BatchPlanRepositoryTests`.
- **Completed plan cannot apply twice:** Verified by `CompletedPlan_CannotApplyTwice_EnforcesIdempotency` in `OperationPlanTests` and `UpdateStatusAsync_UpdatesStatusAndEnforcesCompletedPlanCannotApplyTwice` in `BatchPlanRepositoryTests`.
- **Plan records expected fingerprint per file:** Verified by `Plan_CanBeCreatedAndRead_WithExpectedPropertiesAndFingerprints` in `OperationPlanTests` and `SavePlanAsync_And_GetByIdAsync...` in `BatchPlanRepositoryTests`.
- **Apply endpoint references reviewed plan, not arbitrary mutation payload:** Verified by `Plan_DisallowsApplyParameters_NotExplicitlyReviewedInPreview` in `OperationPlanTests` and `ValidateApplyParameters` method.

## Deviations / Spec Conflicts
None.

## Risks Discovered
None.

## Next Eligible Sprint(s)
- `BAT-02` — Batch Scope Resolver (Requires FS-01, SRCH-05, BAT-01)
- `BAT-03` — Batch Preview API & Review Shell (Requires BAT-01, BAT-02)
