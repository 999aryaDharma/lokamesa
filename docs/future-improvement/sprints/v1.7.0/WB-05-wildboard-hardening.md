# WB-05 — Wildboard Recovery, Security & Performance Hardening

**Label:** `future-improvement`
**Roadmap version:** `v1.7.0`
**Status:** SPECIFIED
**Priority:** `P1`
**Kind:** hardening
**Recommended branch:** `test/wb-05-wildboard-hardening`

## Goal

Close the loop on `1.7.0`: reconcile vault files against `space_objects` after a crash, prove the security boundary, pin an explicit performance budget for 1,000 objects, and lock the kind-gating regression across every workspace-scoped route.

## Depends On

- `WB-04` — Quick Capture
- `QA-06` — Performance Benchmark Gate — **DONE**
- `QA-07` — Crash & Interrupted-Operation Recovery — **DONE**

Hard rule: do not start unless `WB-04` is `DONE` and the V1 dependencies are `DONE`.

## Required Reading

- `AGENTS.md`
- `docs/specs/11-testing-strategy.md`
- `docs/quality/performance-baseline.md`
- `docs/future-improvement/specs/1.7.0-wildboard-core.md` §9–§16
- `docs/decisions/ADR-003-wildboard-workspace-kind-and-vault.md`
- `src/DocumentIDE.Core/Filesystem/ApplyTempFiles.cs`
- `tests/DocumentIDE.ArchitectureTests/ShareHostDependencyTests.cs`
- `tests/DocumentIDE.SecurityTests` (existing patterns)

## In Scope

- Recovery service and `POST …/wildboard/recover`.
- Missing-object UI (badge, recreate empty, remove with confirmation).
- Security test suite for wildboard routes and the ShareHost boundary.
- Architecture boundary test.
- Explicit performance budget and gate.
- Structured logging for wildboard operations.
- Table-driven kind-gating regression across all guarded routes.

## Out of Scope

- Relinking a missing object to a different existing file (deferred).
- Vault relocation and backup automation.
- Media recovery (`1.9.0`).
- Backup/restore of the vault.

## Planned Files / Artifacts

**Create**

- `src/DocumentIDE.Core/Wildboard/WildboardRecoveryService.cs`
- `tests/DocumentIDE.IntegrationTests/Wildboard/WildboardRecoveryTests.cs`
- `tests/DocumentIDE.IntegrationTests/Wildboard/WildboardKindGatingRegressionTests.cs`
- `tests/DocumentIDE.SecurityTests/Wildboard/WildboardSecurityTests.cs`
- `tests/DocumentIDE.ArchitectureTests/WildboardBoundaryTests.cs`
- `src/DocumentIDE.Web/src/features/wildboard/MissingObjectBadge.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/MissingObjectBadge.test.tsx`

**Modify**

- `src/DocumentIDE.Api/Controllers/WildboardObjectsController.cs` — add `POST …/wildboard/recover`.
- `src/DocumentIDE.Core/Wildboard/IWildboardObjectService.cs` — `RecoverAsync`.
- `src/DocumentIDE.Web/src/features/wildboard/wildboardApi.ts` — `recoverWildboard`.
- `src/DocumentIDE.Web/src/features/wildboard/WildboardCanvas.tsx` — call recover on mount; render the missing badge.
- `src/DocumentIDE.Web/src/features/wildboard/WildboardCanvas.perf.test.tsx` — final budget.

## Interfaces & Contracts

**Consumes**

- `IWildboardVault`, `ISpaceObjectRepository`, `IWildboardObjectService` (`WB-02`).
- `ApplyTempFiles.IsApplyTempFileName(string)`.
- The FND-06 structured logging abstraction already used by controllers.

**Produces**

```csharp
public sealed record RecoveryReport(
    IReadOnlyList<string> MarkedMissing,
    IReadOnlyList<string> MarkedAvailable,
    int TempFilesRemoved);

public interface IWildboardRecoveryService
{
    Task<RecoveryReport> RecoverAsync(string workspaceId, CancellationToken ct = default);
}
```

`POST api/workspaces/{workspaceId}/wildboard/recover` → `200 RecoveryReport`.

Recovery rules:

1. For every file-backed object, if the file is absent, set `availability_status = 'missing'`; if present, set `'available'`.
2. Remove files matching `ApplyTempFiles.IsApplyTempFileName` from `notes/`, `links/`, `assets/` that are not referenced by any object.
3. Never delete a `space_objects` row during recovery and never change placement.
4. The operation is idempotent; a second run returns empty lists and `TempFilesRemoved: 0`.

Performance budget (explicit, from `WB-03`'s baseline):

| Metric | Budget |
|---|---|
| Initial mount, 1,000 objects | < 2000 ms |
| Single-object update re-render | exactly 1 node |
| Single-object update total | < 50 ms |
| Group drag of 50 selected objects | < 100 ms |
| Zoom/pan frame with 1,000 objects | < 16 ms in the pure viewport path |

The suite records the measured values as log output; the assertion ceiling includes a 3× variance headroom for CI.

## Implementation Steps

### Step group A — recovery

- [ ] **A1.** Write failing `WildboardRecoveryTests`:
  - `Marks_Missing_When_File_Is_Absent`
  - `Restores_Available_When_File_Returns`
  - `Removes_Orphan_Temp_Files`
  - `Keeps_Referenced_Temp_Name_Untouched` (a temp name that is a real referenced file must not be removed — assert the rule is "unreferenced")
  - `Does_Not_Delete_Placement_Or_Rows`
  - `Is_Idempotent`
  - `Non_Text_Kinds_Are_Checked_Too` (`links/` path prepared for 1.9.0)
- [ ] **A2.** Run `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~WildboardRecoveryTests"`. Expected: FAIL.
- [ ] **A3.** Implement `WildboardRecoveryService` and `RecoverAsync` on the object service; expose it through `POST …/recover`.
- [ ] **A4.** Re-run A2. Expected: PASS.
- [ ] **A5.** Commit: `fix(wildboard): reconcile vault files and clean temp files`.

### Step group B — missing-object UI

- [ ] **B1.** Write failing `MissingObjectBadge.test.tsx`: renders `role="status"` text "Missing file"; "Recreate empty" calls `createObject` with the same id? — **No**: recreate writes an empty file at the original relative path through a dedicated API. Use `POST …/objects/{id}/recreate`; assert it is called. "Remove" requires confirmation.
- [ ] **B2.** Run → FAIL → implement `MissingObjectBadge`, the `recreate` action, and canvas integration (call `recoverWildboard` on mount; render the badge when `availabilityStatus === 'missing'`).
- [ ] **B3.** Write `WildboardCanvas.test.tsx` additions: `Missing_Object_Shows_Badge`, `Recreate_Empty_Restores_Available`, `Remove_Requires_Confirmation`.
- [ ] **B4.** Run the frontend suite. Expected: PASS.
- [ ] **B5.** Commit: `feat(wildboard): surface and repair missing objects`.

### Step group C — security and boundary

- [ ] **C1.** Write `WildboardSecurityTests`:
  - `Wildboard_Routes_Require_Session_Token` → `401`.
  - `Wildboard_Routes_Reject_Cross_Site` → `403` with `Sec-Fetch-Site: cross-site`.
  - `Wildboard_Routes_Reject_Non_Loopback_Host` → `403`.
  - `Wildboard_Path_Traversal_Object_Id_Returns_404` (e.g. `..%2F..%2Fapp.db`).
  - `Wildboard_Response_Body_Contains_No_Absolute_Path`.
- [ ] **C2.** Run `dotnet test tests\DocumentIDE.SecurityTests\DocumentIDE.SecurityTests.csproj --filter "FullyQualifiedName~WildboardSecurityTests"`. Expected: PASS (no new production code expected; if any fails, fix the route rather than the test).
- [ ] **C3.** Write `WildboardBoundaryTests` in the architecture project: `ShareHost_Assembly_Does_Not_Reference_Wildboard` and `Core_Wildboard_Types_Do_Not_Reference_Batch_Or_Word` (reflect over namespaces).
- [ ] **C4.** Run `dotnet test tests\DocumentIDE.ArchitectureTests\DocumentIDE.ArchitectureTests.csproj`. Expected: PASS.
- [ ] **C5.** Commit: `test(wildboard): prove security and architecture boundaries`.

### Step group D — kind-gating regression

- [ ] **D1.** Write `WildboardKindGatingRegressionTests`, table-driven over every route enumerated by:

```powershell
Select-String -Path src\DocumentIDE.Api\Controllers\*.cs -Pattern 'workspaces/\{workspaceId\}'
```

Assert each route returns `409 WorkspaceKindNotSupported` for a wildboard workspace (excluding the wildboard routes and `WorkspacesController`).
- [ ] **D2.** Run → PASS. Any route that does not return `409` is a defect from `WB-01`: fix the guard, not the test.
- [ ] **D3.** Commit: `test(wildboard): lock workspace kind gating across routes`.

### Step group E — performance and logging

- [ ] **E1.** Extend `WildboardCanvas.perf.test.tsx` with the five budgets in the table above; log measured values with a `WB05_PERF` prefix.
- [ ] **E2.** Run `npx vitest run src/features/wildboard/WildboardCanvas.perf.test.tsx`. Expected: PASS; if a budget fails, fix the render path (memoization, avoid recreating handlers) rather than raising the budget.
- [ ] **E3.** Add structured log events for create/update/delete/recover using the FND-06 abstraction: workspace id, object id, kind, duration, outcome. Never log content, absolute paths, or tokens.
- [ ] **E4.** Add `Wildboard_Logs_Contain_No_Content_Or_Absolute_Path` (capture the logger output in a test).
- [ ] **E5.** Commit: `perf(wildboard): pin budgets and add structured diagnostics`.

## Required Tests

- [ ] `WildboardRecoveryTests` — seven cases in A1.
- [ ] `MissingObjectBadge.test.tsx` + three canvas cases.
- [ ] `WildboardSecurityTests` — five cases.
- [ ] `WildboardBoundaryTests` — two cases.
- [ ] `WildboardKindGatingRegressionTests` — table-driven over all guarded routes.
- [ ] `WildboardCanvas.perf.test.tsx` — five budgets.
- [ ] Logging redaction test.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `WildboardRecoveryTests.Marks_Missing_When_File_Is_Absent`

## Failure / Edge Cases

- Crash between file write and row insert → recovery reports an unreferenced file; the sprint must **not** silently delete it. Report it as `TempFilesRemoved` only when the name matches the temp pattern; otherwise leave it and log a warning.
- Two recovery calls concurrently → both idempotent; no row deleted; no duplicate report entries.
- Vault directory missing entirely → recovery returns empty lists, creates nothing, logs once.
- Read-only vault → typed error surfaces to the UI; no rows are flipped to `missing` (a permission failure is not an absence).
- Very long content and Unicode titles → unchanged by recovery.
- 1,000-object board with one missing file → only that node re-renders.
- Route added later without a guard → the table-driven regression test fails until fixed.

## Security & File-Safety Gate

- [ ] Recovery never deletes a `space_objects` row or changes placement.
- [ ] Recovery deletes files only in the recognized temp-name pattern and only when unreferenced.
- [ ] No absolute path, content, or token appears in logs or API responses.
- [ ] ShareHost exposes no wildboard route.
- [ ] Kind gating is enforced for every workspace-scoped document route.
- [ ] No document mutation path is reachable.

## Acceptance Criteria

- [ ] Recovery marks missing files, restores availability, removes orphan temp files, and is idempotent.
- [ ] Missing objects are visible in the UI and can be recreated empty or removed with confirmation.
- [ ] Wildboard routes reject unauthenticated, cross-site, and non-loopback requests.
- [ ] ShareHost and Core boundary tests pass.
- [ ] All five performance budgets pass at 1,000 objects.
- [ ] Every guarded document route returns `409` for a wildboard workspace.
- [ ] Logs contain no content, absolute paths, or tokens.

## Definition of Done

- [ ] implementation steps complete
- [ ] required tests pass
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` passes
- [ ] no required test skipped
- [ ] no unresolved placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] ADR-003 evidence section complete; ADR remains `Accepted` or is reopened
- [ ] `docs/future-improvement/FUTURE-STATUS.md` updated to `DONE` for `1.7.0` after handoff
- [ ] handoff written to `docs/future-improvement/handoffs/WB-05-HANDOFF.md`
- [ ] coherent implementation commit

## Commit Guidance

```text
test(wildboard): recovery, security and performance hardening
```

## Ready-to-Run Agent Prompt

```text
Implement WB-05 — Wildboard Recovery, Security & Performance Hardening only.

Read AGENTS.md, docs/specs/11, docs/quality/performance-baseline.md,
docs/future-improvement/specs/1.7.0-wildboard-core.md, and ADR-003 first.
This sprint closes roadmap v1.7.0; do not implement 1.8.0 or 1.9.0.

Use strict TDD. Do not raise a performance budget to make a test pass — fix the render path.
Recovery must never delete a space_objects row or change placement.
Never log content, absolute paths, or tokens.
If a route fails the kind-gating table test, fix the guard from WB-01 rather than the test.

At completion:
1. run the required tests and scripts/verify.ps1,
2. self-review git diff,
3. map acceptance criteria to evidence,
4. complete ADR-003's evidence section,
5. write docs/future-improvement/handoffs/WB-05-HANDOFF.md,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the commit guidance.
```
