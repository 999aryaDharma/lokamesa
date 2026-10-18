# Sprint Handoff — FND-07

**Status:** REVIEW  
**Branch:** `feat/fnd-07-desktop-webview-production-host`  
**Commits:** `feat(foundation): desktop/webview production host` (pending)

## What changed
- Created `WebAppHost.cs` in `DocumentIDE.Desktop/WebView/` managing the in-process Kestrel ASP.NET Core backend server lifecycle on a dynamic loopback port (`127.0.0.1:0`).
- Configured virtual host name mapping `appassets.html2sav` in `MainWindow.xaml.cs` to serve static production React bundle assets locally without requiring external Vite processes.
- Updated `LocalSessionBridge.cs` in `DocumentIDE.Desktop/WebView/` with `ShouldInjectHeader` foreign navigation protection, ensuring privileged session tokens are strictly injected for loopback API targets and stripped for foreign domains.
- Updated `MainWindow.xaml` and `MainWindow.xaml.cs` with styled WPF `ErrorPanel` diagnostic fallback for WebView2 runtime failures and graceful backend shutdown on app close.
- Created integration test suite [`WebAppHostTests.cs`](file:///D:/document-ide/tests/DocumentIDE.IntegrationTests/Desktop/WebAppHostTests.cs) verifying standalone production launch, foreign navigation security stripping, and clean process termination.

## Files changed
- `src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj`
- `src/DocumentIDE.Desktop/MainWindow.xaml`
- `src/DocumentIDE.Desktop/MainWindow.xaml.cs`
- `src/DocumentIDE.Desktop/WebView/WebAppHost.cs`
- `src/DocumentIDE.Desktop/WebView/LocalSessionBridge.cs`
- `tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj`
- `tests/DocumentIDE.IntegrationTests/Desktop/WebAppHostTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/FND-07-HANDOFF.md`

## Verification
- `dotnet test` → PASS (19 tests total: 5 Core unit tests + 14 Integration tests)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed)

## Acceptance criteria
- [x] User launches one Windows app → Desktop host starts embedded Kestrel backend and hosts React bundle in a single process
- [x] React static assets are local in production → Mapped via `CoreWebView2.SetVirtualHostNameToFolderMapping("appassets.html2sav", ...)`
- [x] WebView origin remains deterministic for Origin validation → Mapped to virtual host origin `appassets.html2sav`

## Security & File-Safety Gate
- no user file was mutated through an unreviewed/bypass path: N/A (Desktop host infrastructure only)
- no arbitrary path or secret is exposed: Verified (Session token restricted to loopback API, foreign origins stripped)
- dependency/security boundaries remain intact: Verified (Kestrel restricted to loopback interfaces, Host/Origin/Token guard active)
- no destructive action silently broadened its scope: N/A (No destructive file actions)
- relevant higher-level safety rules remain stronger than convenience: Verified (Foreign domain navigation loses privileged bridge capabilities)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- CAN-01 (Canvas Foundation)
- FND-06 (Structured Logging & Diagnostics)
- FND-08 (Build, Verify & Artifact Scripts)
- FS-01 (Folder Source Attachment)
- QA-03 (Privileged Local API Security Audit)
