# WB-01 — Workspace Kind, Workspace API & Subsystem Gating

**Label:** `future-improvement`
**Roadmap version:** `v1.7.0`
**Status:** SPECIFIED
**Priority:** `P0`
**Kind:** feature
**Recommended branch:** `feat/wb-01-workspace-kind-gating`

## Goal

Introduce `workspaces.kind` (`documents` | `wildboard`), add a workspace read/create API that reports a capability set, and make every workspace-scoped document route reject a `wildboard` workspace.

## Depends On

- `FND-02` — Privileged Local API Security — **DONE**
- `FND-03` — SQLite Persistence Foundation — **DONE**
- `FND-07` — Desktop/WebView Production Host — **DONE**

Hard rule: do not start unless all three are `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `AGENTS.md`
- `docs/specs/00-master-product-technical-spec.md`
- `docs/specs/01-system-architecture.md`
- `docs/specs/02-domain-data-model.md`
- `docs/future-improvement/specs/00-shared-future-contracts.md`
- `docs/future-improvement/specs/1.7.0-wildboard-core.md`
- `docs/decisions/ADR-003-wildboard-workspace-kind-and-vault.md`
- `docs/agent/01-spec-precedence-and-change-control.md`
- `docs/agent/05-tdd-and-testing-policy.md`

## In Scope

- Migration `012` adding `workspaces.kind`, backfilling `documents`.
- `Workspace.Kind` and repository round-trip.
- `WorkspaceCapabilities` and `WorkspaceKind` domain types.
- Workspace API: list, get (with capabilities), create.
- Backend kind guard applied to every workspace-scoped document route.
- Desktop host controller allow-list update.
- Frontend capability hook and navigation gating.

## Out of Scope

- `space_objects`, vault, file writes (`WB-02`).
- Wildboard canvas rendering (`WB-03`).
- Quick Capture (`WB-04`).
- Workspace deletion, rename, or switching UI beyond creating a wildboard.
- Any change to `documents`/`workspace_documents`.

## Planned Files / Artifacts

**Create**

- `src/DocumentIDE.Infrastructure/Persistence/Migrations/012_WorkspaceKind.cs`
- `src/DocumentIDE.Core/Workspaces/WorkspaceKind.cs`
- `src/DocumentIDE.Core/Workspaces/WorkspaceCapabilities.cs`
- `src/DocumentIDE.Api/Controllers/WorkspacesController.cs`
- `src/DocumentIDE.Api/Security/WorkspaceKindGuard.cs`
- `tests/DocumentIDE.Core.Tests/Workspaces/WorkspaceCapabilitiesTests.cs`
- `tests/DocumentIDE.IntegrationTests/Workspace/WorkspaceKindTests.cs`
- `tests/DocumentIDE.IntegrationTests/Workspace/WorkspaceKindGuardTests.cs`
- `src/DocumentIDE.Web/src/features/workspace/useWorkspaceCapabilities.ts`
- `src/DocumentIDE.Web/src/features/workspace/useWorkspaceCapabilities.test.tsx`

**Modify**

- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs:14-31` — register migration 12.
- `src/DocumentIDE.Core/Domain/Workspace.cs` — add `Kind`.
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/WorkspaceRepository.cs` — select/insert/update `kind`.
- `src/DocumentIDE.Api/Controllers/FolderSourcesController.cs` — guard.
- `src/DocumentIDE.Api/Controllers/ReconciliationController.cs` — guard.
- `src/DocumentIDE.Api/Controllers/ShareController.cs` — guard (verify route shape first).
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs:152-175` — add `WorkspacesController` to the allow-list.
- `src/DocumentIDE.Web/src/features/workspace/workspaceApi.ts` — `fetchWorkspace`, `createWorkspace`.
- `src/DocumentIDE.Web/src/App.tsx:197-238` — mount document-only toggles only when the capability is true.

Enumerate guarded routes before starting:

```powershell
Select-String -Path src\DocumentIDE.Api\Controllers\*.cs -Pattern 'workspaces/\{workspaceId\}'
```

Every match must either gain the guard or be recorded in the handoff with a one-line justification.

## Interfaces & Contracts

**Produces (backend)**

```csharp
public static class WorkspaceKind
{
    public const string Documents = "documents";
    public const string Wildboard = "wildboard";
    public static bool IsValid(string? kind);
}

public sealed record WorkspaceCapabilities(
    bool Word, bool Batch, bool PdfExport, bool Sharing, bool FolderSources, bool SourceControl)
{
    public static WorkspaceCapabilities For(string kind); // throws ArgumentException on unknown kind
}

public enum WorkspaceKindCheck { Document, Wildboard, NotFound }

public static class WorkspaceKindGuard
{
    public const string ErrorCode = "WorkspaceKindNotSupported";
    public static Task<WorkspaceKindCheck> CheckAsync(IWorkspaceRepository repo, string workspaceId);
}
```

**Produces (API)**

| Method | Route | Success | Errors |
|---|---|---|---|
| `GET` | `api/workspaces` | `200 WorkspaceDto[]` | — |
| `GET` | `api/workspaces/{id}` | `200 WorkspaceDto` | `404 { errorCode: "WorkspaceNotFound" }` |
| `POST` | `api/workspaces` | `201 WorkspaceDto` | `400 { errorCode: "InvalidKind" }`, `400 { errorCode: "NameRequired" }` |

```csharp
public sealed record WorkspaceDto(string Id, string Name, string Kind, WorkspaceCapabilities Capabilities);
public sealed record CreateWorkspaceRequest(string Name, string? Kind); // Kind defaults to "documents"
```

**Produces (frontend)**

```ts
export interface WorkspaceCapabilities {
  word: boolean; batch: boolean; pdfExport: boolean;
  sharing: boolean; folderSources: boolean; sourceControl: boolean;
}
export interface WorkspaceDto {
  id: string; name: string; kind: 'documents' | 'wildboard'; capabilities: WorkspaceCapabilities;
}
export function fetchWorkspace(workspaceId: string, fetchImpl?: typeof fetch): Promise<WorkspaceDto | null>;
export function createWorkspace(name: string, kind: 'documents' | 'wildboard', fetchImpl?: typeof fetch): Promise<WorkspaceDto>;
export function useWorkspaceCapabilities(workspaceId: string): {
  kind: 'documents' | 'wildboard'; capabilities: WorkspaceCapabilities; isLoading: boolean;
};
```

`fetchWorkspace` resolves `null` on `404`. On network error the hook falls back to `documents` capabilities and `isLoading:false`; the backend guard remains authoritative.

## Implementation Steps

### Step group A — migration and entity

- [ ] **A1.** Write the failing test `Migration_012_Adds_Kind_Defaulting_To_Documents` in `tests/DocumentIDE.IntegrationTests/Workspace/WorkspaceKindTests.cs` using the `SqliteConcurrencyTests` pattern (temp `docide_test_{Guid}.db`, `new AppDb(path)`, `await appDb.InitializeAsync()`): insert a workspace row **without** `kind`, assert `SELECT kind` returns `"documents"`.
- [ ] **A2.** Run `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~WorkspaceKindTests"`. Expected: FAIL (`no such column: kind`).
- [ ] **A3.** Implement `Migration_012_WorkspaceKind : IMigration` with `Version => 12`, `Name => "012_WorkspaceKind"`, DDL `ALTER TABLE workspaces ADD COLUMN kind TEXT NOT NULL DEFAULT 'documents' CHECK (kind IN ('documents','wildboard'));`. Register it in `MigrationRunner`.
- [ ] **A4.** Re-run A2. Expected: PASS.
- [ ] **A5.** Write `Migration_012_Rejects_Unknown_Kind` (raw `UPDATE workspaces SET kind='nope'` must throw `SqliteException`) and run it. Expected: PASS.
- [ ] **A6.** Add `public string Kind { get; set; } = WorkspaceKind.Documents;` to `Workspace`; extend `WorkspaceRepository` SELECT/INSERT/UPDATE to carry `kind`. Write `Workspace_RoundTrips_Kind` (create wildboard → `GetByIdAsync` returns `wildboard`). Run it. Expected: FAIL → implement → PASS.
- [ ] **A7.** Commit: `feat(workspace): add workspace kind migration and entity`.

### Step group B — capabilities and guard

- [ ] **B1.** Write `WorkspaceCapabilitiesTests` in `tests/DocumentIDE.Core.Tests/Workspaces/`:
  - `Documents_Enables_All_Document_Surfaces` — all six flags `true`.
  - `Wildboard_Disables_All_Document_Surfaces` — all six flags `false`.
  - `Unknown_Kind_Throws` — `Assert.Throws<ArgumentException>`.
- [ ] **B2.** Run `dotnet test tests\DocumentIDE.Core.Tests\DocumentIDE.Core.Tests.csproj --filter "FullyQualifiedName~WorkspaceCapabilitiesTests"`. Expected: FAIL (type missing).
- [ ] **B3.** Implement `WorkspaceKind` and `WorkspaceCapabilities.For`.
- [ ] **B4.** Re-run B2. Expected: PASS.
- [ ] **B5.** Write `WorkspaceKindGuardTests` (integration, `WebAppHost.StartAsync` pattern from `AppShellSurfaceHostTests`): seed a `wildboard` workspace and assert `GET api/workspaces/{id}/folder-sources` returns `409` with `errorCode = "WorkspaceKindNotSupported"`, and `POST api/workspaces/{id}/reconcile` returns `409`. Expected: FAIL (currently 200/other).
- [ ] **B6.** Implement `WorkspaceKindGuard.CheckAsync` and apply it to `FolderSourcesController` and `ReconciliationController`; enumerate remaining routes with the `Select-String` command above and apply to each workspace-scoped document controller (including `ShareController` if it is workspace-scoped).
- [ ] **B7.** Re-run B5 plus the existing controller tests. Expected: PASS with no regression.
- [ ] **B8.** Commit: `feat(workspace): add capability model and workspace kind guard`.

### Step group C — workspace API

- [ ] **C1.** Write failing integration tests in `WorkspaceKindTests.cs`:
  - `Post_Workspace_Creates_Wildboard` → `201`, body `kind == "wildboard"`, capabilities all `false`.
  - `Post_Workspace_Defaults_To_Documents` → `kind == "documents"`.
  - `Post_Workspace_Rejects_Invalid_Kind` → `400`, `errorCode == "InvalidKind"`.
  - `Get_Workspace_Returns_Kind_And_Capabilities`.
  - `Get_Unknown_Workspace_Returns_404`.
  - `Workspace_Routes_Require_Session_Token` → `401` without the header.
- [ ] **C2.** Run the filtered suite. Expected: FAIL (`404`, route missing).
- [ ] **C3.** Implement `WorkspacesController` (`[ApiController]`, `[Route("api/workspaces")]`) with the three actions. Validate `kind` with `WorkspaceKind.IsValid`; trim and reject an empty `name`.
- [ ] **C4.** Add `WorkspacesController` to `AllowedControllers` in `WebAppHost`. Write `Workspace_Routes_Are_Reachable_In_Desktop_Host` and run it. Expected: PASS.
- [ ] **C5.** Run `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~WorkspaceKindTests"`. Expected: PASS.
- [ ] **C6.** Commit: `feat(api): add workspace routes with capability reporting`.

### Step group D — frontend gating

- [ ] **D1.** Write failing `useWorkspaceCapabilities.test.tsx`:
  - returns `wildboard` + all-`false` capabilities for a wildboard payload;
  - resolves `documents` capabilities on `404`;
  - resolves `documents` capabilities on network error.
- [ ] **D2.** Run `npx vitest run src/features/workspace/useWorkspaceCapabilities.test.tsx`. Expected: FAIL.
- [ ] **D3.** Add `fetchWorkspace`/`createWorkspace` to `workspaceApi.ts` (same `apiBase()` + `X-DocumentIDE-Session-Token` pattern) and implement the hook.
- [ ] **D4.** Re-run D2. Expected: PASS.
- [ ] **D5.** Write a failing `App.test.tsx` case: with a wildboard capability payload, the `Tools` nav does not render the Add-folder, Batch, PDF, and Share toggles.
- [ ] **D6.** Gate those toggles in `App.tsx:197-238` on the corresponding capability. Run `npx vitest run src/App.test.tsx`. Expected: PASS.
- [ ] **D7.** Commit: `feat(web): gate document tools by workspace capability`.

## Required Tests

- [ ] `WorkspaceCapabilitiesTests` — three cases (B1).
- [ ] `WorkspaceKindTests` — migration default, unknown-kind rejection, repository round-trip, API create/get/validate, 404, token required.
- [ ] `WorkspaceKindGuardTests` — `409 WorkspaceKindNotSupported` on wildboard for every guarded route.
- [ ] Desktop host allow-list test.
- [ ] `useWorkspaceCapabilities.test.tsx` — three cases.
- [ ] `App.test.tsx` — document toggles absent for wildboard.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `Migration_012_Adds_Kind_Defaulting_To_Documents`

## Failure / Edge Cases

- Unknown or corrupt `kind` value in a row → `WorkspaceCapabilities.For` throws; the API returns `500` rather than silently treating it as `documents`.
- Missing workspace row on a guarded route → `404 WorkspaceNotFound`, not `409`.
- Existing workspace created before migration → `documents`, no behavioural change.
- Concurrent workspace create with the same name → duplicate names allowed (IDs authoritative); must not throw.
- Frontend capability fetch failure → falls back to `documents` capabilities; backend guard still rejects.
- `POST` with `kind: ""` or `null` → default `documents`; with an unknown non-empty value → `400`.

## Security & File-Safety Gate

- [ ] No document mutation is introduced; the guard only rejects.
- [ ] No path, token, or secret is added to a response body.
- [ ] Host/Origin/session middleware remains the only authorization path; no route bypasses it.
- [ ] No new destructive scope.
- [ ] `workspaces.kind` cannot weaken any higher-level rule; document workspaces are unchanged.

## Acceptance Criteria

- [ ] Migration backfills every existing workspace to `documents`.
- [ ] `GET api/workspaces/{id}` reports kind and capabilities.
- [ ] `POST api/workspaces` creates a `wildboard` or `documents` workspace.
- [ ] Every workspace-scoped document route returns `409 WorkspaceKindNotSupported` for `wildboard`.
- [ ] The desktop host serves the workspace routes.
- [ ] The frontend hides document-only toggles for `wildboard`.
- [ ] All existing tests still pass.

## Definition of Done

- [ ] implementation steps complete
- [ ] required tests pass
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` passes
- [ ] no required test skipped
- [ ] no unresolved placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] `docs/decisions/ADR-003` evidence section updated with the results
- [ ] handoff written to `docs/future-improvement/handoffs/WB-01-HANDOFF.md`
- [ ] coherent implementation commit

## Commit Guidance

```text
feat(workspace): workspace kind, workspace API and subsystem gating
```

## Ready-to-Run Agent Prompt

```text
Implement WB-01 — Workspace Kind, Workspace API & Subsystem Gating only.

Read AGENTS.md, docs/specs/00, 01, 02, docs/future-improvement/specs/1.7.0-wildboard-core.md,
docs/future-improvement/specs/00-shared-future-contracts.md, and ADR-003 first.
This sprint is part of roadmap v1.7.0; do not implement WB-02 or later.

Use strict TDD: every behavior starts with a failing test that fails for the stated reason.
Preserve all existing V1 source-of-truth, filesystem, DOCX, Git, and safety semantics.
Do not add product-level branch management. Do not touch the documents or workspace_documents tables.

Enumerate guarded routes with:
  Select-String -Path src\DocumentIDE.Api\Controllers\*.cs -Pattern 'workspaces/\{workspaceId\}'
and record in the handoff which route was guarded or why it was excluded.

At completion:
1. run the required tests and scripts/verify.ps1,
2. self-review git diff,
3. map acceptance criteria to evidence,
4. update ADR-003's evidence section,
5. write docs/future-improvement/handoffs/WB-01-HANDOFF.md,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the commit guidance.
```
