# Sprint Handoff: FND-06 — Structured Logging & Diagnostics

## Summary
Implements local structured logging (via Serilog), automatic secret and document body redaction, per-async-flow correlation ID tracing, subsystem event ID categories, and owner-only redacted diagnostics export.

## Key Changes
1. **Subsystem Event IDs**: Defined `SubsystemEventIds` in `src/DocumentIDE.Infrastructure/Diagnostics/SubsystemEventIds.cs` for Indexing, Word, Filesystem, Batch, Share, Security, and Startup.
2. **Local Rolling Logs & Redaction**: Configured Serilog rolling JSON logs under `%LOCALAPPDATA%\DocumentIDE\logs` in `LoggingConfiguration.cs` and automatic secret/document body redaction in `LogRedaction.cs`.
3. **Correlation ID Scope**: Added `CorrelationIdProvider` supporting async flow tracing and `BeginScope()` for batch and background operations.
4. **Owner-Only Diagnostics Export**: Implemented `IDiagnosticsExportService`, `DiagnosticsExportService`, and updated `DiagnosticsController` with owner authorization (`[Authorize(Roles = "Owner")]`) to export redacted diagnostics while strictly excluding source documents and user filesystem files.

## Tests & Verification
- Unit & Integration tests executed via `dotnet test`:
  - `DocumentIDE.Core.Tests`: 274 passed, 0 failed.
  - `DocumentIDE.IntegrationTests`: 120 passed, 0 failed.
  - Total: 394 passed.
- Required Tests:
  - `LogRedactionTests.cs`: Secret, token, API key, and raw document body redaction verified.
  - `CorrelationIdTests.cs`: Async correlation ID flow and scope restoration verified.
  - `DiagnosticsExportTests.cs`: Diagnostics export redaction and source document exclusion verified.

## Security & File Safety Confirmation
- [x] No user file was mutated through an unreviewed/bypass path.
- [x] No arbitrary path or secret is exposed in logs or export endpoints.
- [x] Dependency/security boundaries remain intact.
- [x] No destructive action silently broadened its scope.
- [x] Relevant higher-level safety rules remain stronger than convenience.
