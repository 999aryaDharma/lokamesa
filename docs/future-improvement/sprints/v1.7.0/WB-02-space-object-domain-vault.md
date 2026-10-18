# WB-02 — Space-Object Domain, Vault & Atomic Writes

**Label:** `future-improvement`
**Roadmap version:** `v1.7.0`
**Status:** SPECIFIED
**Priority:** `P0`
**Kind:** feature
**Recommended branch:** `feat/wb-02-space-object-domain-vault`

## Goal

Add the `space_objects` / `stack_members` persistence model, a per-workspace file vault with atomic writes, and a domain service plus API for creating, reading, moving, and deleting wildboard objects.

## Depends On

- `WB-01` — Workspace Kind, Workspace API & Subsystem Gating
- `FS-07` — Explicit Physical File Operations — **DONE**
- `FS-08` — Reparse Point & Source Containment — **DONE**

Hard rule: do not start unless `WB-01` is `DONE` in its handoff and the V1 dependencies are `DONE` in `docs/sprints/SPRINT-STATUS.md`.

## Required Reading

- `AGENTS.md`
- `docs/specs/02-domain-data-model.md`
- `docs/specs/04-filesystem-sync-spec.md`
- `docs/future-improvement/specs/1.7.0-wildboard-core.md` §4–§10
- `docs/future-improvement/specs/00-shared-future-contracts.md`
- `docs/decisions/ADR-003-wildboard-workspace-kind-and-vault.md`
- `src/DocumentIDE.Core/Filesystem/PathSafetyValidator.cs`
- `src/DocumentIDE.Core/Filesystem/SourceContainmentPolicy.cs`
- `src/DocumentIDE.Core/Filesystem/ApplyTempFiles.cs`

## In Scope

- Migration `013`: `space_objects`, `stack_members`, indexes.
- Domain types, typed exceptions, repository.
- Vault path resolution and containment validation.
- Atomic vault writer (create-new and replace-existing).
- `WildboardObjectService` covering `text`, `sheet`, `stack` and stack membership.
- `WildboardObjectsController` and desktop host allow-list registration.
- Content size limit for text/sheet.

## Out of Scope

- Canvas rendering and stores (`WB-03`).
- Capture UI (`WB-04`).
- Recovery endpoint and perf hardening (`WB-05`).
- Media/link/webclip kinds (values are accepted by the schema but rejected by the service until `1.9.0`).
- Vault relocation.

## Planned Files / Artifacts

**Create**

- `src/DocumentIDE.Infrastructure/Persistence/Migrations/013_SpaceObjects.cs`
- `src/DocumentIDE.Core/Wildboard/SpaceObjectKind.cs`
- `src/DocumentIDE.Core/Wildboard/SpaceObject.cs`
- `src/DocumentIDE.Core/Wildboard/WildboardErrors.cs`
- `src/DocumentIDE.Core/Wildboard/Repositories/ISpaceObjectRepository.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/SqliteSpaceObjectRepository.cs`
- `src/DocumentIDE.Core/Wildboard/Vault/WildboardVaultPath.cs`
- `src/DocumentIDE.Core/Wildboard/Vault/IWildboardVault.cs`
- `src/DocumentIDE.Infrastructure/Wildboard/WildboardVault.cs`
- `src/DocumentIDE.Core/Wildboard/IWildboardObjectService.cs`
- `src/DocumentIDE.Core/Wildboard/WildboardObjectService.cs`
- `src/DocumentIDE.Api/Controllers/WildboardObjectsController.cs`
- `tests/DocumentIDE.Core.Tests/Wildboard/WildboardVaultPathTests.cs`
- `tests/DocumentIDE.Core.Tests/Wildboard/WildboardObjectServiceTests.cs`
- `tests/DocumentIDE.IntegrationTests/Wildboard/WildboardVaultTests.cs`
- `tests/DocumentIDE.IntegrationTests/Wildboard/SpaceObjectRepositoryTests.cs`
- `tests/DocumentIDE.IntegrationTests/Wildboard/WildboardObjectsControllerTests.cs`

**Modify**

- `src/DocumentIDE.Infrastructure/Persistence/Migrations/MigrationRunner.cs` — register 13.
- `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs` — register repository, vault, service.
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs:152-175` — add `WildboardObjectsController`.

## Interfaces & Contracts

**Consumes**

- `WorkspaceKind` and `WorkspaceCapabilities` from `WB-01`.
- `InvalidPathException` from `PathSafetyValidator`.
- `ApplyTempFiles.CreatePath(string directory)` for temp names.

**Produces**

```csharp
public static class SpaceObjectKind
{
    public const string Text = "text";
    public const string Sheet = "sheet";
    public const string Stack = "stack";
    public const string Image = "image";   // reserved, 1.9.0
    public const string Video = "video";   // reserved, 1.9.0
    public const string Link = "link";     // reserved, 1.9.0
    public const string Webclip = "webclip"; // reserved, 1.9.0
    public const string Label = "label";   // reserved, 1.8.0
    public static bool IsFileBacked(string kind); // text | sheet | image | video | link | webclip
}

public sealed class SpaceObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("D");
    public string WorkspaceId { get; init; } = "";
    public string Kind { get; init; } = SpaceObjectKind.Text;
    public double CanvasX { get; set; }
    public double CanvasY { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? VaultRelativePath { get; init; }
    public string? MimeType { get; init; }
    public string? Title { get; set; }
    public string AvailabilityStatus { get; set; } = "available";
    public string CreatedAt { get; init; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}
```

Typed exceptions (all in `WildboardErrors.cs`):

```csharp
public sealed class WildboardObjectNotFoundException : Exception;      // 404 WildboardObjectNotFound
public sealed class WildboardConfirmationRequiredException : Exception;  // 409 ConfirmationRequired
public sealed class WildboardContentTooLargeException : Exception;      // 413 ContentTooLarge
public sealed class WildboardStackConstraintException : Exception;      // 400 StackConstraintViolation
// WorkspaceKindNotSupportedException comes from WB-01; reuse it. // 409 WorkspaceKindNotSupported
```

```csharp
public static class WildboardVaultPath
{
    public static string GetVaultRoot(string workspaceId, string? localAppDataOverride = null);
    public static string NormalizeRelative(string relativePath);            // throws InvalidPathException
    public static string ResolveContained(string vaultRoot, string relativePath); // throws InvalidPathException
}

public interface IWildboardVault
{
    Task<string> WriteNewAsync(string workspaceId, string relativePath, string content, CancellationToken ct = default);
    Task WriteReplaceAsync(string workspaceId, string relativePath, string content, CancellationToken ct = default);
    Task<string?> ReadAsync(string workspaceId, string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string workspaceId, string relativePath, CancellationToken ct = default);
    bool Exists(string workspaceId, string relativePath);
}
```

```csharp
public interface IWildboardObjectService
{
    Task<SpaceObject> CreateTextAsync(string workspaceId, string body, double x, double y, string? title = null, CancellationToken ct = default);
    Task<SpaceObject> CreateSheetAsync(string workspaceId, string body, double x, double y, string? title = null, CancellationToken ct = default);
    Task<SpaceObject> CreateStackAsync(string workspaceId, double x, double y, CancellationToken ct = default);
    Task<SpaceObject> GetAsync(string workspaceId, string objectId, CancellationToken ct = default);
    Task<IReadOnlyList<SpaceObject>> ListAsync(string workspaceId, CancellationToken ct = default);
    Task<string> ReadContentAsync(string workspaceId, string objectId, CancellationToken ct = default);
    Task UpdateContentAsync(string workspaceId, string objectId, string body, CancellationToken ct = default);
    Task UpdatePlacementAsync(string workspaceId, string objectId, double x, double y, double width, double height, CancellationToken ct = default);
    Task DeleteAsync(string workspaceId, string objectId, bool confirmDeleteFile, CancellationToken ct = default);
    Task AddToStackAsync(string workspaceId, string stackId, string objectId, CancellationToken ct = default);
    Task RemoveFromStackAsync(string workspaceId, string objectId, CancellationToken ct = default);
}
```

Constants: `MaxTextObjectBytes = 1_048_576`; default sizes `text 220×140`, `sheet 520×700`, `stack 200×140`.

**API**

| Method | Route |
|---|---|
| `GET` | `api/workspaces/{workspaceId}/wildboard/objects` |
| `POST` | `api/workspaces/{workspaceId}/wildboard/objects` — `{ kind, body?, title?, x, y, width?, height? }` |
| `GET` | `api/workspaces/{workspaceId}/wildboard/objects/{objectId}` |
| `GET` | `api/workspaces/{workspaceId}/wildboard/objects/{objectId}/content` → `{ body }` |
| `PUT` | `api/workspaces/{workspaceId}/wildboard/objects/{objectId}/content` — `{ body }` |
| `PATCH` | `api/workspaces/{workspaceId}/wildboard/objects/{objectId}/placement` — `{ x, y, width, height }` |
| `DELETE` | `api/workspaces/{workspaceId}/wildboard/objects/{objectId}?confirm=true` |
| `POST` | `api/workspaces/{workspaceId}/wildboard/objects/{stackId}/members` — `{ objectId }` |
| `DELETE` | `api/workspaces/{workspaceId}/wildboard/objects/{objectId}/stack` |

## Implementation Steps

### Step group A — schema

- [ ] **A1.** Failing test `SpaceObjects_Table_Exists_After_Migration` in `SpaceObjectRepositoryTests.cs` (assert `sqlite_master` contains `space_objects` and `stack_members`).
- [ ] **A2.** Run `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~SpaceObjectRepositoryTests"`. Expected: FAIL.
- [ ] **A3.** Implement `Migration_013_SpaceObjects` (`Version => 13`):

```sql
CREATE TABLE space_objects (
  id TEXT PRIMARY KEY,
  workspace_id TEXT NOT NULL,
  kind TEXT NOT NULL CHECK (kind IN ('text','sheet','image','video','link','webclip','stack','label')),
  canvas_x REAL NOT NULL DEFAULT 0,
  canvas_y REAL NOT NULL DEFAULT 0,
  width REAL NOT NULL,
  height REAL NOT NULL,
  vault_relative_path TEXT,
  mime_type TEXT,
  title TEXT,
  availability_status TEXT NOT NULL DEFAULT 'available' CHECK (availability_status IN ('available','missing')),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY (workspace_id) REFERENCES workspaces(id) ON DELETE CASCADE
);
CREATE INDEX idx_space_objects_workspace_id ON space_objects(workspace_id);
CREATE TABLE stack_members (
  stack_id TEXT NOT NULL,
  object_id TEXT NOT NULL,
  PRIMARY KEY (stack_id, object_id),
  FOREIGN KEY (stack_id) REFERENCES space_objects(id) ON DELETE CASCADE,
  FOREIGN KEY (object_id) REFERENCES space_objects(id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX idx_stack_members_object ON stack_members(object_id);
```

- [ ] **A4.** Re-run A2. Expected: PASS.
- [ ] **A5.** Commit: `feat(wildboard): add space object schema`.

### Step group B — path safety

- [ ] **B1.** Write `WildboardVaultPathTests` covering: absolute path rejected, `..` segment rejected, `:` ADS rejected, empty rejected, control characters rejected, `notes/abc.md` accepted, escape attempt (`notes/../../x.md`) throws `InvalidPathException`.
- [ ] **B2.** Run `dotnet test tests\DocumentIDE.Core.Tests\DocumentIDE.Core.Tests.csproj --filter "FullyQualifiedName~WildboardVaultPathTests"`. Expected: FAIL.
- [ ] **B3.** Implement `WildboardVaultPath`: normalize separators to `\`, split on separators, reject empty/`.`/`..` segments, reject `Path.IsPathRooted`, reject `:` and control chars, then `ResolveContained` compares the canonicalized result against `vaultRoot + separator` using `OrdinalIgnoreCase`.
- [ ] **B4.** Re-run B2. Expected: PASS.
- [ ] **B5.** Commit: `feat(wildboard): add vault path containment`.

### Step group C — vault writer

- [ ] **C1.** Write `WildboardVaultTests`:
  - `WriteNew_Creates_Utf8_File_Without_Bom`
  - `WriteNew_Does_Not_Overwrite_Existing_File`
  - `WriteReplace_Replaces_Content`
  - `WriteReplace_CreatesRecoverySnapshot_OfPreviousContent`
  - `Replace_IsBlocked_WhenRecoverySnapshotCannotBeCreated` — inject a snapshot failure; assert the target file is byte-identical and the outcome is typed
  - `Delete_MovesFileToTrash_AndIsRestorable`
  - `PurgeTrash_RemovesOnlyRequestedTrashEntries`
  - `Failed_Write_Leaves_Previous_Content_Intact` — inject failure through an internal `Func<Stream, Task>` hook
  - `Write_Over_Content_Limit_Is_Rejected`
- [ ] **C2.** Run the filtered suite. Expected: FAIL.
- [ ] **C3.** Implement `WildboardVault`. For create: write to `ApplyTempFiles.CreatePath(dir)`, flush, then `File.Move(temp, target, overwrite: false)`. For replace: **first snapshot the existing file into `<vault>/.recovery/<objectId>/<utc-ticks>.md`; if the snapshot cannot be written, return a typed failure and do not touch the target**, then temp write and `File.Replace(temp, target, null)`. For delete: move the file into `<vault>/.trash/<objectId>-<utc-ticks>.md` (never `File.Delete`). Delete the temp in a `finally` on failure. Note: `IAtomicFileReplacer` cannot be used here because it requires an existing target (`AtomicFileReplacer.PrepareAsync` throws `FileNotFoundException`).
- [ ] **C4.** Re-run C2. Expected: PASS.
- [ ] **C5.** Commit: `feat(wildboard): add atomic vault writer`.

### Step group D — repository and service

- [ ] **D1.** Write `SpaceObjectRepositoryTests`: round-trip all fields, workspace cascade delete, stack-member cascade on object delete, unique member index (second stack assignment throws).
- [ ] **D2.** Run → FAIL → implement `SqliteSpaceObjectRepository` → PASS.
- [ ] **D3.** Write `WildboardObjectServiceTests`:
  - `CreateText_In_Documents_Workspace_Is_Rejected` (`WorkspaceKindNotSupportedException`)
  - `CreateText_Writes_File_And_Returns_Available`
  - `CreateSheet_Uses_Sheet_Default_Size`
  - `CreateStack_Has_No_Vault_Path`
  - `UpdateContent_Replaces_File`
  - `Delete_Without_Confirm_Throws`
  - `Delete_With_Confirm_MovesFileToTrash_AndRemovesRow`
  - `Delete_With_Confirm_Is_Restorable`
  - `AddToStack_Rejects_Stack_As_Member`
  - `AddToStack_Rejects_Cross_Workspace_Member`
  - `AddToStack_Moves_Member_Between_Stacks`
  - `DeleteStack_Keeps_Member_Objects`
  - `Unsupported_Kind_For_17_Is_Rejected` (`image`/`video`/`link`/`webclip`/`label` → `NotSupportedException`)
- [ ] **D4.** Run → FAIL → implement `WildboardObjectService` → PASS. Store paths as `notes/{id}.md`. Derive `Title` from the first non-empty line, capped at 120 characters, when no explicit title is supplied.
- [ ] **D5.** Commit: `feat(wildboard): add object service and repository`.

### Step group E — API

- [ ] **E1.** Write `WildboardObjectsControllerTests` over `WebAppHost.StartAsync` with the authorized client: create text, list, read content, update content, patch placement, delete without confirm (`409 ConfirmationRequired`), delete with confirm (`204`), stack add/remove, documents workspace (`409 WorkspaceKindNotSupported`), oversized body (`413`), missing token (`401`), unknown object id (`404`).
- [ ] **E2.** Run → FAIL → implement `WildboardObjectsController` and register it in `WebAppHost` + `PrivilegedSurfaceRegistration` → PASS.
- [ ] **E3.** Commit: `feat(api): add wildboard object routes`.

## Required Tests

- [ ] `WildboardVaultPathTests` — traversal, absolute, ADS, control characters.
- [ ] `WildboardVaultTests` — create, no-overwrite, replace, recovery snapshot before replace, blocked replace when the snapshot fails, delete-to-trash and restore, trash purge, failure preserves content, size limit.
- [ ] `SpaceObjectRepositoryTests` — round-trip, cascades, unique member.
- [ ] `WildboardObjectServiceTests` — 12 cases listed in D3.
- [ ] `WildboardObjectsControllerTests` — routes, status codes, token.

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `WildboardVaultPathTests.Escape_Attempt_Throws`

## Failure / Edge Cases

- `workspaceId` belongs to a `documents` workspace → `409`, nothing written.
- Object id not a GUID or not in the workspace → `404`, never a path lookup.
- Content larger than 1 MiB → `413`, no temp file left behind.
- Vault directory missing or read-only → typed error; no partial row inserted (write file **before** inserting the row).
- Deleting a file that is already gone → row removed, operation succeeds idempotently.
- Concurrent duplicate create with the same id → primary-key violation surfaces as `409`/`500` without orphan file (delete the written file on repository failure).
- Stack member from another workspace → `400 StackConstraintViolation`.
- Unicode and very long titles → stored verbatim; no filesystem name is derived from the title.

## Security & File-Safety Gate

- [ ] Every vault path is containment-checked before any file operation.
- [ ] No absolute path, `..`, or ADS segment can reach the filesystem.
- [ ] No filename or title reaches a command line or shell.
- [ ] Deleting a backing file requires `confirm=true`.
- [ ] Wildboard routes are not exposed by ShareHost and require the session token.
- [ ] No document mutation path is reachable.

## Acceptance Criteria

- [ ] Migration 13 creates both tables and indexes.
- [ ] Creating a `text` or `sheet` writes a real `.md` file under the vault and a matching row.
- [ ] Creating a `stack` writes no file.
- [ ] Failing a write leaves prior content intact and leaves no temp file.
- [ ] Deleting requires confirmation and removes both row and file.
- [ ] Stack membership enforces one-stack-per-object, same-workspace, and no nesting.
- [ ] All routes reject a `documents` workspace with `409`.

## Definition of Done

- [ ] implementation steps complete
- [ ] required tests pass
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` passes
- [ ] no required test skipped
- [ ] no unresolved placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] ADR-003 evidence section updated
- [ ] handoff written to `docs/future-improvement/handoffs/WB-02-HANDOFF.md`
- [ ] coherent implementation commit

## Commit Guidance

```text
feat(wildboard): space object domain, vault and atomic writes
```

## Ready-to-Run Agent Prompt

```text
Implement WB-02 — Space-Object Domain, Vault & Atomic Writes only.

Read AGENTS.md, docs/specs/02 and 04, docs/future-improvement/specs/1.7.0-wildboard-core.md,
docs/future-improvement/specs/00-shared-future-contracts.md, and ADR-003 first.
This sprint is part of roadmap v1.7.0; do not implement WB-03 or later.

Use strict TDD. Preserve all existing V1 filesystem and safety semantics.
Reuse ApplyTempFiles for temp names. Do not reuse IAtomicFileReplacer for new files:
it requires an existing target and will throw FileNotFoundException.
Do not modify documents or workspace_documents.

At completion:
1. run the required tests and scripts/verify.ps1,
2. self-review git diff,
3. map acceptance criteria to evidence,
4. update ADR-003's evidence section,
5. write docs/future-improvement/handoffs/WB-02-HANDOFF.md,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the commit guidance.
```
