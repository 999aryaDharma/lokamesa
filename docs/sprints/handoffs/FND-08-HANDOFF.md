# Sprint Handoff — FND-08

**Status:** REVIEW  
**Branch:** `feat/fnd-08-build-verify-artifact-scripts`  
**Commits:** `feat(foundation): build, verify & artifact scripts` (pending)

## What changed
- Created `scripts/bootstrap.ps1` to automate repository onboarding, verifying .NET 10.0.302, Node.js, restoring solution NuGet packages, and installing frontend packages (`npm ci`).
- Refined `scripts/verify.ps1` to run all .NET build, .NET test, Vite frontend build, and Vitest test suites, returning non-zero exit codes on any failure.
- Created `scripts/build-release.ps1` to build the production React bundle, embed static assets into `DocumentIDE.Desktop/wwwroot`, publish the self-contained desktop executable, and emit release artifacts to `artifacts/desktop/`.
- Created `.vscode/tasks.json` configuring standard VS Code developer tasks (`bootstrap`, `verify`, `build-release`, `build`, `test`).
- Created integration test suite [`ScriptTests.cs`](file:///D:/document-ide/tests/DocumentIDE.IntegrationTests/Scripts/ScriptTests.cs) verifying bootstrap structure, verification script error handling, and release artifact embedding.

## Files changed
- `scripts/bootstrap.ps1`
- `scripts/verify.ps1`
- `scripts/build-release.ps1`
- `.vscode/tasks.json`
- `src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj`
- `tests/DocumentIDE.IntegrationTests/Scripts/ScriptTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/FND-08-HANDOFF.md`

## Verification
- `dotnet test` → PASS (22 tests total: 5 Core unit tests + 17 Integration tests)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed)

## Acceptance criteria
- [x] One command verifies repository health → `powershell -File scripts/verify.ps1` executes full build, backend tests, frontend builds, and frontend tests
- [x] No manual copy of frontend assets required → `scripts/build-release.ps1` automatically builds Vite assets, embeds them into `wwwroot/`, and publishes self-contained desktop binaries into `artifacts/desktop/`

## Security & File-Safety Gate
- no user file was mutated through an unreviewed/bypass path: N/A (Build and verification scripts only)
- no arbitrary path or secret is exposed: Verified (Artifact output restricted to repository `artifacts/` folder)
- dependency/security boundaries remain intact: Verified (Build and publish scripts use pinned .NET and npm scripts)
- no destructive action silently broadened its scope: N/A (No destructive file actions)
- relevant higher-level safety rules remain stronger than convenience: Verified (Release scripts fail fast on any compilation, lint, or test failure)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- REL-01 (Windows Installer Packaging)
- SHR-02 (Separate ShareHost Process)
- CAN-01 (Canvas Foundation)
- FS-01 (Folder Source Attachment)
- FND-06 (Structured Logging & Diagnostics)
