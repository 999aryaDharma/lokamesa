# Sprint Handoff — QA-03

**Status:** DONE  
**Branch:** `security/qa-03-privileged-local-api-security-audit`  
**Commits:** `security(quality): privileged local api security audit`

## What changed
- Extended security audit suite in `tests/DocumentIDE.SecurityTests/LocalApiAttackSurfaceTests.cs`:
  - Automated hostile Origin request rejection (403 Forbidden without `Access-Control-Allow-Origin` header for foreign origins, null origin, scheme mismatches, and domain suffix spoofing attempts).
  - Automated Host / DNS rebinding mismatch tests covering invalid host headers, IP mismatches, and IPv6 bracket host spoofing.
  - Automated missing, wrong, and malformed startup secret authorization tests (401 Unauthorized for missing token, wrong secret, and malformed Bearer/Basic headers; 200 OK for valid `X-DocumentIDE-Session-Token` or `Authorization: Bearer` token).
  - Automated CORS preflight `OPTIONS` policy and state-changing method rules (POST, PUT, DELETE, PATCH) ensuring hostile origins and unauthenticated requests are rejected before reaching controllers.
  - Automated browser-based attack page fixture simulation testing cross-origin fetch attacks, CSRF attempts, and DNS rebinding attacks against live test host.
- Hardened `PrivilegedRequestGuard.cs` in `src/DocumentIDE.Api/Security/`:
  - Added strict IPv6 bracket parsing in `ExtractHostWithoutPort` to block IPv6 DNS rebinding attempts like `[::1].attacker.com`.
  - Added strict origin matching in `IsAllowedOrigin` to prevent domain suffix spoofing attacks on `appassets.html2sav`.
- Fixed TypeScript type assertion in `src/DocumentIDE.Web/src/features/files/OpenInWordAction.challenger.test.ts`.
- Updated `docs/sprints/SPRINT-STATUS.md` marking `QA-03` as `DONE` and `REL-01` as `READY`.

## Files changed
- `src/DocumentIDE.Api/Security/PrivilegedRequestGuard.cs`
- `tests/DocumentIDE.SecurityTests/LocalApiAttackSurfaceTests.cs`
- `src/DocumentIDE.Web/src/features/files/OpenInWordAction.challenger.test.ts`
- `docs/sprints/SPRINT-STATUS.md`
- `docs/sprints/handoffs/QA-03-HANDOFF.md`

## Verification
- `$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"; dotnet test tests/DocumentIDE.SecurityTests --nologo` → PASS (46/46 security tests passed in 571ms)
- `powershell -File scripts/verify.ps1` → PASS (All 5 repository verification steps passed: .NET build, .NET test 397/397 tests passed, npm ci & build, npm test 27/27 frontend tests passed)

## Acceptance criteria
- [x] All hostile cases rejected before controller → Verified by 403 Forbidden responses on hostile origin, DNS rebinding, and cross-site requests.
- [x] Valid WebView request succeeds → Verified by 200 OK responses on valid loopback origin with valid startup secret token.
- [x] No wildcard CORS → Verified `Access-Control-Allow-Origin` is never set to `*` for any request.
- [x] Release gate blocks on failure → Verified test runner fails deterministically on security rule violations.

## Security & File-Safety Gate
- [x] no user file was mutated through an unreviewed/bypass path → N/A (Security audit test implementation & guard hardening)
- [x] no arbitrary path or secret is exposed → Verified (Startup secret remains in-memory, no local paths exposed to foreign origins)
- [x] dependency/security boundaries remain intact → Verified (Privileged API strictly authenticated and loopback-bound)
- [x] no destructive action silently broadened its scope → N/A (No destructive operations in this sprint)
- [x] relevant higher-level safety rules remain stronger than convenience → Verified (All security rules enforced before controller execution)

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- `REL-01` — Windows Installer Packaging
