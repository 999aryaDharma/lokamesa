# Sprint Handoff — FND-05

**Status:** REVIEW  
**Branch:** `security/fnd-05-settings-secret-storage`  
**Commits:** `security(foundation): settings & secret storage` (pending)

## What changed
- Created `AppSettings.cs` in `DocumentIDE.Core/Settings/` with non-sensitive defaults (`Theme`, `AutoSaveIntervalSeconds`, `MaxRecoveryRetentionDays`, `RecoveryCapacityBudgetBytes`, `DefaultView`, `TelemetryOptOut`).
- Created `ISettingsService` and `SettingsService` in `DocumentIDE.Infrastructure/Settings/` reading/writing non-sensitive settings to `%LOCALAPPDATA%\DocumentIDE\settings.json`.
- Created `SecretValue.cs` in `DocumentIDE.Core/Security/` redacting values in `ToString()` and JSON serialization (`"[REDACTED]"`).
- Created `ISecretStore` and `WindowsSecretStore` in `DocumentIDE.Infrastructure/Security/` utilizing `System.Security.Cryptography.ProtectedData` (Windows DPAPI `DataProtectionScope.CurrentUser`) to persist encrypted secret blobs in `%LOCALAPPDATA%\DocumentIDE\secrets\<key>.bin`.
- Created integration test suite [`SecretStoreTests.cs`](file:///D:/document-ide/tests/DocumentIDE.IntegrationTests/Security/SecretStoreTests.cs) testing settings round-tripping, DPAPI encryption/decryption, and secret redaction.

## Files changed
- `src/DocumentIDE.Core/Settings/AppSettings.cs`
- `src/DocumentIDE.Core/Settings/ISettingsService.cs`
- `src/DocumentIDE.Core/Security/SecretValue.cs`
- `src/DocumentIDE.Core/Security/ISecretStore.cs`
- `src/DocumentIDE.Infrastructure/DocumentIDE.Infrastructure.csproj`
- `src/DocumentIDE.Infrastructure/Settings/SettingsService.cs`
- `src/DocumentIDE.Infrastructure/Security/WindowsSecretStore.cs`
- `tests/DocumentIDE.IntegrationTests/Security/SecretStoreTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/FND-05-HANDOFF.md`

## Verification
- `dotnet test` → PASS (16 tests total: 5 Core unit tests + 11 Integration tests)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed)

## Acceptance criteria
- [x] No plaintext long-lived API/ngrok credential in app database → Credentials encrypted using Windows DPAPI (`ProtectedData.Protect`) as binary blobs under application secrets directory
- [x] Startup session secret is not persisted here → Ephemeral startup session token managed in-memory by `ISessionTokenProvider` per session

## Security & File-Safety Gate
- no user file was mutated through an unreviewed/bypass path: N/A (Settings and secret persistence only)
- no arbitrary path or secret is exposed: Verified (Secrets redacted in logs/serialization and stored encrypted)
- dependency/security boundaries remain intact: Verified (DPAPI encryption scope restricted to CurrentUser)
- no destructive action silently broadened its scope: N/A (No destructive actions)
- relevant higher-level safety rules remain stronger than convenience: Verified (Secrets never written to database or settings JSON in plaintext)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- FND-06 (Structured Logging & Diagnostics)
- FND-07 (Desktop/WebView Production Host)
- QA-03 (Privileged Local API Security Audit)
- SHR-01 (Share Domain & Sanitized Manifest)
