# ADR-001 — Authorize the native app's browser requests

**Status:** Accepted

## Context

The desktop maps its bundled WebView content to `https://appassets.html2sav` and calls its loopback API on a dynamic HTTP port. The existing guard rejects every `Sec-Fetch-Site: cross-site` request before evaluating its allowed application Origin. Direct HTTP host tests did not exercise the combination of native Origin and browser Fetch Metadata, leaving the batch review UI's request path unverified.

Master §101.1 requires a dedicated application Origin allowlist, Host validation, startup secret authorization and Fetch Metadata checks as defense in depth. It does not require rejecting the application's own authorized requests.

## Decision

Admit cross-site Fetch Metadata only when Origin is exactly `https://appassets.html2sav`, compared ordinally without case sensitivity. Keep Host validation before this check and all subsequent Origin and startup-secret validation. Do not extend the exception to subdomains, alternate schemes/ports, localhost browser origins, missing or null origins. Standard recognized-origin OPTIONS remains a preflight response without privileged state.

## Alternatives Considered

- Reject all cross-site requests: preserves the current defect and prevents the native review UI from reaching its API.
- Remove the Fetch Metadata check: unnecessarily broadens the accepted browser surface.
- Rewrite browser metadata in the native bridge: hides the source relationship rather than validating it.
- Move the entire asset hosting surface to the API origin: changes a larger existing integration than this sprint needs.

## Consequences

### Positive

The precise native application origin can use the authenticated loopback API. Foreign browser sites remain rejected and no wildcard CORS or new privileged route is introduced.

### Negative / residual risk

Origin and Fetch Metadata are browser-enforced headers, not credentials for arbitrary native clients. Every actual privileged request still needs the startup secret. This decision does not claim protection after arbitrary native code execution, change the broader preexisting Origin allowlist, or replace end-to-end WebView verification at the release gate.

## Security & File-Safety Impact

This is an explicit security-boundary refinement for native application usability. Session secrets remain mandatory and memory-only; Host/foreign Origin denial remains intact. Batch preview remains read-only; source mutation, recovery and Word concurrency are unaffected. ShareHost is separate and unchanged.

## Test / Experiment Evidence

Task 4 observed the expected 403 RED before the exception. The actual loopback-host test then passed with the native Origin, cross-site metadata, CORS preflight, authenticated preview and source-byte equality. Missing/invalid secret and hostile Host/Origin variants remain denied. Focused desktop/security integration: 21 passed; attack-surface security: 46 passed. Scoped fresh-context review approved the changed boundary without findings.

Controller verification on commit `14108e1`: `scripts/verify.ps1` passed build, 428 Core + 172 Integration + 7 Architecture tests, frontend build and 78 frontend tests. Explicit `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj --no-restore --nologo -m:1 -v:minimal` passed 52 tests. No required test was skipped. Logs: `artifacts/bat-03-final-verification.log` and `artifacts/bat-03-final-security.log`.

One earlier combined run failed an existing Origin-denial test without diagnostic detail; isolated and subsequent combined/full runs passed. Its cause remains unproven and is recorded in the sprint handoff rather than attributed to database isolation. These are HTTP host tests; browser-native policy and rendering remain release-level end-to-end verification work.

## Supersedes

None.

## Superseded By

None.
