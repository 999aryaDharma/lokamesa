# Sprint Handoff — SHR-09 — ngrok Transport

**Status:** DONE  
**Branch:** `feat/shr-09-ngrok-transport`  
**Commits:** `feat(sharing): ngrok transport`

## What changed
- Created `INgrokProcessRunner` and `NgrokTransport` in `DocumentIDE.Infrastructure/Sharing/` to manage background `ngrok` tunnel lifecycle.
- Configured local loopback binding to proxy incoming requests securely to ShareHost.
- Stored ngrok tokens securely via Settings/Secret Storage (`FND-05`).
- Added unit and integration test fixtures (`MockNgrokProcessRunner`, `NgrokTransportTests.cs`).

## Files changed
- `src/DocumentIDE.Infrastructure/Sharing/INgrokProcessRunner.cs`
- `src/DocumentIDE.Infrastructure/Sharing/NgrokTransport.cs`
- `tests/DocumentIDE.IntegrationTests/Sharing/MockNgrokProcessRunner.cs`
- `tests/DocumentIDE.IntegrationTests/Sharing/NgrokTransportTests.cs`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/SHR-09-HANDOFF.md`

## Verification
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter NgrokTransportTests` → PASS
- Tunnel creation, health monitoring, and shutdown verify cleanly.

## Security & File-Safety Gate Confirmation
- [x] ngrok auth tokens managed securely in secret storage.
- [x] ShareHost process remains isolated on localhost binding.
- [x] Process lifecycle strictly controlled with proper disposal on stop.

## Acceptance criteria
- [x] ngrok transport wrapper manages process start/stop and public URL retrieval.
- [x] Integration tests confirm tunnel lifecycle and error handling.
