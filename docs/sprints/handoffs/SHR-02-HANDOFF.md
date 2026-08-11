# Sprint Handoff — SHR-02 — Separate ShareHost Process

**Status:** DONE  
**Branch:** `security/shr-02-separate-sharehost-process`  
**Commits:** `security(sharing): separate sharehost process`

## What changed
- Created separate executable project `DocumentIDE.ShareHost` (`src/DocumentIDE.ShareHost/DocumentIDE.ShareHost.csproj` and `Program.cs`) with a narrow dependency graph referencing ONLY `DocumentIDE.Core`.
- Implemented loopback-only binding (`127.0.0.1`) on Kestrel for local ngrok tunnel exposure.
- Added strict share artifact endpoint routing (`/share/{shareId}/manifest`, `/share/{shareId}/documents`, `/share/{shareId}/documents/{docId}`, `/share/{shareId}/artifacts/{*relativePath}`, `/share/{shareId}/documents/{docId}/download-original`, `/share/{shareId}/documents/{docId}/download-pdf`).
- Enforced strict storage isolation serving ONLY share-owned artifact directory (`%LOCALAPPDATA%\DocumentIDE\shares`) with path traversal rejection via `ShareTokenService`.
- Verified arbitrary physical filesystem routes (e.g. `/file?path=...`, `/api/*`, `/admin`) do NOT exist and return 404 Not Found.
- Implemented comprehensive architecture dependency test suite in `tests/DocumentIDE.ArchitectureTests/ShareHostDependencyTests.cs` verifying assembly boundaries, narrow dependencies, absence of Word COM/mutation references, and loopback/artifact isolation.
- Explicitly documented that process separation is a risk reduction boundary, not a full OS sandbox.

## Files changed
- `src/DocumentIDE.ShareHost/DocumentIDE.ShareHost.csproj`
- `src/DocumentIDE.ShareHost/Program.cs`
- `tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj`
- `tests/DocumentIDE.ArchitectureTests/ShareHostDependencyTests.cs`
- `installer/DocumentIDE.wxs`
- `DocumentIDE.sln`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/SHR-02-HANDOFF.md`

## Verification
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj` → PASS (7 passed, 0 failed)
- `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` → PASS (All 5 verification steps passed)
- `powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1` → PASS (MSI Installer & ShareHost executable packaged successfully)
- `ShareHost_Assembly_DoesNotReference_PrivilegedAssemblies` → PASS
- `ShareHost_Types_DoNotDependOn_MutationOrWordServices` → PASS
- `ShareHost_ProjectFile_HasNarrowDependencyGraph` → PASS
- `ShareHost_ArbitraryLocalPathRoutes_DoNotExist` → PASS
- `ShareHost_ServesOnly_ShareOwnedArtifactDirectory` → PASS
- `ShareHost_PathTraversalAttempt_ReturnsForbiddenOrNotFound` → PASS
- `ShareHost_DownloadPermissions_Enforced` → PASS

## Security & File-Safety Gate Confirmation
- [x] No user file was mutated through an unreviewed/bypass path.
- [x] No arbitrary path or secret is exposed in public endpoints.
- [x] Dependency/security boundaries remain intact (ShareHost references only `DocumentIDE.Core`).
- [x] No destructive action silently broadened its scope.
- [x] Relevant higher-level safety rules remain stronger than convenience.

## Acceptance criteria
- [x] Process separation is documented as risk reduction, not full sandbox (demonstrated in `Program.cs`, `ShareHostDependencyTests.cs`, and handoff).
- [x] ShareHost assembly does NOT reference privileged mutation services, Word COM automation, or unrestricted physical filesystem services (demonstrated by `ShareHost_Assembly_DoesNotReference_PrivilegedAssemblies` and `ShareHost_Types_DoNotDependOn_MutationOrWordServices`).
- [x] Serves ONLY share-owned artifact directory (`%LOCALAPPDATA%\DocumentIDE\shares`) and sanitized manifests (demonstrated by `ShareHost_ServesOnly_ShareOwnedArtifactDirectory`).
- [x] Viewer server bound to loopback for ngrok exposure (demonstrated in `Program.cs` Kestrel configuration).
- [x] Arbitrary local path routes DO NOT exist (demonstrated by `ShareHost_ArbitraryLocalPathRoutes_DoNotExist`).

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `SHR-03` — Share Token Authentication (READY)
- `SHR-09` — ngrok Transport (READY)
- `SHR-10` — ShareHost OS Isolation Spike (READY)
