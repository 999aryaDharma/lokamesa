# Sprint Handoff — REL-01

**Status:** DONE  
**Branch:** `chore/rel-01-windows-installer-packaging`  
**Commits:** `chore(release): windows installer packaging`

## What changed
- Created `installer/DocumentIDE.wxs` WiX v7 manifest for self-contained win-x64 desktop app and ShareHost packaging, including Start Menu shortcut, uninstall entries, and WebView2 Evergreen prerequisite search.
- Updated `scripts/build-release.ps1` to publish WPF Desktop and ShareHost binaries for win-x64, embed React production assets into `wwwroot/`, and compile `DocumentIDE-Setup.msi` installer package into `artifacts/installer/`.
- Created `src/DocumentIDE.Core/Security/PrerequisiteValidator.cs` providing runtime diagnostics for WebView2 Evergreen runtime presence and verifying uninstall safety policies.
- Created `tests/DocumentIDE.InstallTests/InstallerSmoke.md` documenting clean installer smoke testing and uninstall safety rules.
- Created automated integration suite [`InstallerTests.cs`](file:///D:/document-ide/tests/DocumentIDE.IntegrationTests/Release/InstallerTests.cs) asserting manifest structure, WebView2 prerequisite detection, uninstall user document protection, and release script artifact outputs.

## Files changed
- `installer/DocumentIDE.wxs`
- `scripts/build-release.ps1`
- `scripts/verify.ps1`
- `src/DocumentIDE.Core/Security/PrerequisiteValidator.cs`
- `src/DocumentIDE.Web/package.json`
- `tests/DocumentIDE.InstallTests/InstallerSmoke.md`
- `tests/DocumentIDE.IntegrationTests/Release/InstallerTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/REL-01-HANDOFF.md`

## Verification
- `dotnet test` → PASS (408 tests total: 7 Architecture tests, 277 Core unit tests, 124 Integration tests)
- `powershell -File scripts/build-release.ps1` → PASS (Published win-x64 Desktop, ShareHost, wwwroot, and compiled `artifacts/installer/DocumentIDE-Setup.msi`)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed)

## Acceptance criteria
- [x] Package the self-contained win-x64 desktop app and ShareHost into a normal Windows installer without bundling user documents -> Verified via `DocumentIDE.wxs` manifest and `build-release.ps1`
- [x] Check/install WebView2 Evergreen prerequisite -> Verified via `PrerequisiteValidator` and WiX `RegistrySearch` for EdgeUpdate client key `{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`
- [x] Create Start Menu / uninstall entries -> Verified in `DocumentIDE.wxs` (`ApplicationShortcut` and uninstall registry entries)
- [x] Uninstall never removes source documents -> Verified via `VerifyUninstallProtectionPolicy` assertion in `InstallerTests.cs` confirming no `RemoveFile` / `RemoveFolder` targets user profile or document extensions
- [x] Word remains external prerequisite for Word-dependent features -> Verified (Word Desktop remains external COM dependency for Word-dependent features; app starts without Microsoft Word)

## Security & File-Safety Gate
- no user file was mutated through an unreviewed/bypass path: Verified (Installer files confined to `[INSTALLFOLDER]`)
- no arbitrary path or secret is exposed: Verified (No secrets or arbitrary paths exported in installer package)
- dependency/security boundaries remain intact: Verified (ShareHost built as separate process win-x64 self-contained binary)
- no destructive action silently broadened its scope: Verified (Installer uninstall policy explicitly restricted from touching user directories)
- relevant higher-level safety rules remain stronger than convenience: Verified (Uninstall protection policy verified statically and dynamically in test suite)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- SHR-02 (Separate ShareHost Process)
- CAN-01 (Canvas Foundation)
- FS-01 (Folder Source Attachment)
