# REL-01 — Installer Smoke Test Specification

## Objective
Verify the end-to-end clean installation, prerequisite validation, desktop application execution, ShareHost execution, and non-destructive uninstallation on Windows win-x64 target environments.

## Test Scenarios

### 1. Clean Environment Installation & App Execution
- **Environment**: Clean Windows 11 / Windows 10 x64 system.
- **Action**: Run `DocumentIDE-Setup.msi` (built from `installer/DocumentIDE.wxs` via `scripts/build-release.ps1`).
- **Expected Outcome**:
  - Application installs into `C:\Program Files\Document IDE\` (or `%LocalAppData%\Programs\Document IDE`).
  - Start Menu shortcut `Document IDE` is created.
  - Add/Remove Programs displays `Document IDE` version 1.0.0.0.
  - Launching `DocumentIDE.Desktop.exe` starts without requiring dev server or external .NET runtime installation (self-contained win-x64).
  - Web interface loads embedded static assets from `wwwroot/index.html`.

### 2. WebView2 Evergreen Prerequisite Check
- **Condition**: On launch, `DocumentIDE.Desktop` evaluates WebView2 Evergreen runtime via `PrerequisiteValidator.ValidateStartupPrerequisites()`.
- **Expected Outcome**:
  - If WebView2 Evergreen runtime is installed, initialization proceeds smoothly.
  - If WebView2 Evergreen runtime is missing, application presents an inspectable diagnostic error dialog indicating missing WebView2 runtime requirements.

### 3. Word Prerequisite Contract
- **Condition**: External Microsoft Word Desktop is required for DOCX deep editing and Word COM automation features.
- **Expected Outcome**:
  - App starts cleanly even if Microsoft Word is not installed.
  - Word-dependent operations verify Word COM prerequisite before attempting automation.

### 4. Uninstall Protection Rules (%USERPROFILE% User Document Safety)
- **Action**: Create test documents (`.docx`, `.md`, `.sqlite` database files) inside `%USERPROFILE%\Documents\TestWorkspace` and `%USERPROFILE%\Desktop`.
- **Action**: Run uninstaller via Control Panel / Settings or `msiexec /x {ProductCode}`.
- **Expected Outcome**:
  - `DocumentIDE-Setup` uninstaller removes installed binaries and assets under `[INSTALLFOLDER]`.
  - Uninstaller NEVER touches, modifies, or deletes any files or folders inside `%USERPROFILE%` or user document locations.
  - User documents in `%USERPROFILE%\Documents\TestWorkspace` remain intact and un-mutated.
