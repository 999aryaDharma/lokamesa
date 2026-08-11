# Sprint Handoff — SHR-01 — Share Domain & Sanitized Manifest

**Status:** DONE  
**Branch:** `security/shr-01-share-domain-sanitized-manifest`  
**Commits:** `security(sharing): share domain & sanitized manifest`

## What changed
- Created `Share`, `ShareMember`, `ShareSnapshotArtifact`, `AccessMode`, and `ShareMode` domain models in `DocumentIDE.Core.Domain.Sharing`.
- Implemented SQLite migration `004_ShareDomain` creating `shares`, `share_members`, and `share_snapshot_artifacts` tables with foreign keys and indexes.
- Implemented `ShareRepository` in `DocumentIDE.Infrastructure.Persistence.Repositories` adhering to `IShareRepository` with transaction safety and cascade deletion support.
- Defined sanitized document/display metadata generator `ShareManifest.cs` in `DocumentIDE.Core.Sharing` that strips drive letters, UNC prefixes, traversal sequences (`../`), colons, and physical path details.
- Implemented explicit snapshotting at share creation via `Share.SnapshotMembersFromCollection`, guaranteeing that subsequent collection or workspace additions do not auto-publish new files.
- Ensured application-owned share artifact storage root (`%LOCALAPPDATA%\DocumentIDE\shares`) in `ShareTokenService` with strict path traversal prevention (`GetArtifactPath`, `GetShareRootPath`, `EnsureStorageDirectoryCreated`, `EnsureShareRootExists`).

## Files changed
- `src/DocumentIDE.Core/Domain/Sharing/Share.cs`
- `src/DocumentIDE.Core/Domain/Sharing/ShareMember.cs`
- `src/DocumentIDE.Core/Domain/Sharing/ShareSnapshotArtifact.cs`
- `src/DocumentIDE.Core/Domain/Sharing/AccessMode.cs`
- `src/DocumentIDE.Core/Domain/Sharing/ShareMode.cs`
- `src/DocumentIDE.Core/Domain/Repositories/IShareRepository.cs`
- `src/DocumentIDE.Core/Sharing/ShareManifest.cs`
- `src/DocumentIDE.Core/Sharing/SanitizedDocumentMetadata.cs`
- `src/DocumentIDE.Core/Sharing/ShareTokenService.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Migrations/004_ShareDomain.cs`
- `src/DocumentIDE.Infrastructure/Persistence/Repositories/ShareRepository.cs`
- `tests/DocumentIDE.Core.Tests/Sharing/ShareManifestTests.cs`
- `tests/DocumentIDE.Core.Tests/Sharing/ShareTokenServiceTests.cs`
- `tests/DocumentIDE.Core.Tests/Sharing/ShareSecurityAndManifestChallengerTests.cs`
- `tests/DocumentIDE.Core.Tests/Sharing/ShareAdversarialStressTests.cs`
- `tests/DocumentIDE.IntegrationTests/Persistence/ShareRepositoryTests.cs`
- `tests/DocumentIDE.IntegrationTests/Persistence/ShareChallengerStressTests.cs`
- `docs/sprints/SPRINT-STATUS.md`

## Verification
- `dotnet test` → PASS (397 passed, 0 failed, 0 skipped across `DocumentIDE.Core.Tests` and `DocumentIDE.IntegrationTests`)
- `Manifest_Payload_Has_No_Physical_Path` → PASS
- `Manifest_Contains_Only_Explicit_Member_IDs` → PASS
- `New_Collection_Member_Does_Not_Auto_Enter_Existing_Share` → PASS
- `SnapshotMembersFromCollection_Freezes_Membership_AtCreation` → PASS
- `GetArtifactPath_PreventsPathTraversal` → PASS
- `EnsureStorageDirectoryCreated_CreatesApplicationOwnedShareRoot` → PASS

## Security & File-Safety Confirmation
- [x] No user file was mutated through an unreviewed/bypass path.
- [x] No arbitrary path or secret is exposed in public manifests or tokens.
- [x] Dependency/security boundaries remain intact.
- [x] No destructive action broadened its scope.
- [x] Relevant higher-level safety rules remain stronger than convenience.

## Acceptance criteria
- [x] Live mode does not imply live membership (demonstrated by `Live_Mode_Does_Not_Imply_Live_Membership` and `SnapshotMembersFromCollection_Freezes_Membership_AtCreation`).
- [x] Physical paths are NEVER stored or exposed in public manifests (demonstrated by `Manifest_Payload_Has_No_Physical_Path` and `ShareManifest_NeverExposesPhysicalPaths_UnderAdversarialInputs`).
- [x] Application-owned share storage root created under `%LOCALAPPDATA%\DocumentIDE\shares` (demonstrated by `EnsureStorageDirectoryCreated_CreatesApplicationOwnedShareRoot`).

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `SHR-02` — Separate ShareHost Process (unblocked: SHR-01 and FND-08 are DONE).
