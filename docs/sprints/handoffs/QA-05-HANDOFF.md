# Sprint Handoff — QA-05 Public Share Security Audit

**Status:** DONE
**Branch:** `security/qa-05-public-share-security-audit`
**Commits:** `b56d704`

## What changed

- Added test-only audit lane `tests/DocumentIDE.SecurityTests/PublicShareAttackSurfaceTests.cs`
  (39 tests) attacking the real ShareHost pipeline across all six sprint vectors:
  token cross-share scope, traversal/absolute-path, Live membership leakage,
  sanitizer/CSP script payloads, download/rate-limit bypass, ShareHost
  privileged-service dependency boundary, plus stop/revoke behavior and SHR-10
  residual-risk wording consistency.
- TDD Red stage exposed 2 concrete findings, each fixed minimally in ShareHost:
  1. `DownloadOriginal_Off_CannotBeBypassed_ViaArtifactRoute` — with
     `AllowOriginalDownload=false`, the generic artifact route served
     `originals/...` bytes with 200, bypassing the download-permission gate.
     Fixed in `src/DocumentIDE.ShareHost/Program.cs`: the artifact endpoint now
     returns 403 for `originals/` (`pdfs/`) paths when the share manifest exists
     and the corresponding permission is OFF. Manifest-less artifact serving
     (SHR-08 preview fixtures) is unchanged.
  2. `InjectedScript_Payloads_AreNeutralized` (`<form>` case) — the maintained
     sanitizer allow-list kept `<form action="https://evil.example">`. Fixed in
     `src/DocumentIDE.ShareHost/Security/RenderedHtmlSanitizer.cs`: `form` and
     `button` added to the explicit denylist (consistent with the viewer CSP
     `form-action 'none'` and the read-only viewer having no legitimate forms).
     `input` stays allowed so owner-rendered GFM task-list checkboxes keep fidelity.
- `tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj`: added
  Core + ShareHost (`Aliases="ShareHostApp"`, same pattern as IntegrationTests)
  references so the audit lane can boot the real ShareHost host. No harness was
  duplicated from `LocalApiAttackSurfaceTests.cs` / `ShareHostIsolationTests.cs`;
  only assertion patterns were reused against the public surface.

## Files changed

- `tests/DocumentIDE.SecurityTests/PublicShareAttackSurfaceTests.cs` (new, test-only)
- `tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj` (2 added refs)
- `src/DocumentIDE.ShareHost/Program.cs` (artifact-route download-permission gate)
- `src/DocumentIDE.ShareHost/Security/RenderedHtmlSanitizer.cs` (form/button denylist)

## Verification

- `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj --filter "FullyQualifiedName~PublicShareAttackSurfaceTests"` → PASS (Red: 37/39 with the 2 findings above failing; Green: 39/39)
- `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj` → PASS (91/91)
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj` → PASS (7/7)
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~Sharing"` → PASS (21/21, incl. SHR-08 ShareWebSecurityTests)

## Acceptance criteria

- [x] Residual same-user OS risk documented consistently — `IsolationResidualRisk_IsDocumentedConsistentlyWithShr10` asserts the SHR-10 report wording (`IsOsSandbox=false`, "process separation … not an OS sandbox … arbitrary local paths remain readable"); audit confirms the same posture via `ShareHostIsolationEvaluator`.
- [x] No private document becomes reachable (cross-share + nonmember → 404, manifest exposes no physical paths).
- [x] Injected script does not execute (6 neutralized payloads + CSP baseline on 200/404/403).
- [x] Revoked share fails (manifest withdrawal → 404 on all 5 public routes).
- [x] ShareHost lacks privileged services (assembly refs + type/ctor scan + narrow csproj graph + no mutation routes).

## Deviations / spec conflicts

None. Two minimal ShareHost hardening fixes were required by failing attack tests (documented above); no spec wording was weakened. No Word/Filesystem/Canvas code touched. No other sprint rows edited.

## Risks discovered

- Residual same-user OS risk (per SHR-10): ShareHost runs as the same user, so user-readable paths outside the share root remain technically readable at the OS level; the public HTTP surface never exposes them (proven by traversal/path-privacy tests). Wording is consistent with the SHR-10 spike: process separation is enforced but is not an OS sandbox.
- `originals/`/`pdfs/` artifacts are only gated when a manifest exists; shares without a manifest keep legacy open artifact serving (pre-existing SHR-02 behavior, unchanged).

## Next eligible sprint(s)

- REL-02 (Release Verification Pipeline) — QA-05 dependency now satisfied.
