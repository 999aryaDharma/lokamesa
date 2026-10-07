# Sprint Handoff — SHR-08 — Share CSP, Sanitization & Rate Limits

**Status:** DONE  
**Branch:** `security/shr-08-share-csp-sanitization-rate-limits`  
**Commits:** `security(sharing): share csp, sanitization & rate limits`

## What changed
- Added `ViewerSecurityHeaders` middleware: strict CSP baseline from master spec §101.11 (`default-src 'none'; script-src 'self'; connect-src 'self'; img-src 'self' data:; font-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'`), plus `style-src 'self'` (additive; otherwise `default-src 'none'` blocks every viewer stylesheet). Sets `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` (keeps the secret share URL out of `Referer`), `X-Frame-Options: DENY` on every response, including 403/404/429 responses.
- Added `RenderedHtmlSanitizer`: re-sanitizes rendered HTML/Markdown preview artifacts served through `/share/{shareId}/artifacts/{*relativePath}` with the maintained Ganss.Xss HtmlSanitizer (same maintained sanitizer family as MD-02's owner-side pipeline). Oversized HTML artifacts (> 10 MiB) are refused with 413 instead of being served unbounded.
- Added `ShareRateLimits` + `ShareRateLimitOptions`: fully server-side chained global limiter — general 120/min/IP, search 30/min/IP, downloads 10/min/IP, concurrent downloads 2/IP, and a process-wide expensive-work concurrency cap (default 8). `429 Too Many Requests` responses carry `Retry-After` and JSON `{ "error": "rate_limit_exceeded", "retryAfterSeconds": n }`.
- Removed the superseded `Security/Placeholder.cs` ("Real security implementations can be added later").
- Added `ShareWebSecurityTests` (16 test cases) that boot the real ShareHost host via `WebApplicationFactory` (same mechanism as `ShareHostDependencyTests`) and assert real headers, real bodies, and real 429 responses.

## Files changed
- `src/DocumentIDE.ShareHost/Security/ViewerSecurityHeaders.cs` (planned)
- `src/DocumentIDE.ShareHost/Security/ShareRateLimits.cs` (planned; contains `ShareRateLimitOptions`)
- `src/DocumentIDE.ShareHost/Security/RenderedHtmlSanitizer.cs` (added for Step 3 — see deviations)
- `src/DocumentIDE.ShareHost/Program.cs` (middleware wiring + sanitize-on-serve for `text/html` artifacts)
- `src/DocumentIDE.ShareHost/DocumentIDE.ShareHost.csproj` (`HtmlSanitizer` 9.2.1039 package reference)
- `src/DocumentIDE.ShareHost/Security/Placeholder.cs` (deleted, superseded by real security implementations)
- `tests/DocumentIDE.IntegrationTests/Sharing/ShareWebSecurityTests.cs` (planned path, unchanged mapping; no frontend tests involved)
- `docs/sprints/handoffs/SHR-08-HANDOFF.md`

## Verification
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~ShareWebSecurityTests"` → PASS (16/16)
- `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~IntegrationTests.Sharing"` → PASS (21/21; includes SHR-09 ngrok regressions)
- `dotnet test tests/DocumentIDE.ArchitectureTests/DocumentIDE.ArchitectureTests.csproj` → PASS (7/7; proves ShareHost boots with the new middleware and benign HTML artifacts pass through unchanged)
- `scripts/verify.ps1 — controller runs full repository verification on this worktree.`

## Acceptance criteria
- [x] Public request cannot trigger Word COM.
  - `ShareWebSecurityTests.ShareHost_CannotReachWordComOrPrivilegedMutationServices`: the ShareHost assembly references no `*Word*`/`*Interop*` assembly and not `DocumentIDE.Infrastructure`/`Api`/`Desktop`.
  - Existing `ShareHostDependencyTests` (project-file + reflection + HTTP route checks) still pass.

## Security & File-Safety Gate Confirmation
- [x] no user file was mutated through an unreviewed/bypass path — ShareHost remains read-only; sanitization only rewrites the HTTP response body.
- [x] no arbitrary path or secret is exposed — artifact containment unchanged (SHR-02/SHR-03), `nosniff` added, `Referrer-Policy: no-referrer` prevents share-token leakage via `Referer`.
- [x] dependency/security boundaries remain intact — ShareHost still references only `DocumentIDE.Core` plus the maintained `HtmlSanitizer` package; architecture tests pass.
- [x] no destructive action silently broadened its scope — no new mutation route of any kind.
- [x] relevant higher-level safety rules remain stronger than convenience — spec CSP baseline enforced, no `unsafe-eval`/generic `unsafe-inline`, sanitizer enforced at the public boundary, all limits server-side.

## Deviations / spec conflicts
- **Extra planned file:** `Security/RenderedHtmlSanitizer.cs` beyond the two planned ShareHost files. Step 3 ("reuse maintained sanitizer for rendered Markdown") needs a public-boundary sanitizer; ShareHost cannot reference `DocumentIDE.Infrastructure`'s `MarkdownPreviewService` (SHR-02 dependency boundary), so the same maintained sanitizer library (`HtmlSanitizer`/Ganss.Xss) is reused directly with the public viewer policy.
- **`HtmlSanitizer` version:** ShareHost pins 9.2.1039 (includes the CVE-2026-25543 template-tag fix and later URI/CSS hardening); `DocumentIDE.Infrastructure` still declares 8.0.0. They never share a process (separate executables).
- **CSP addition:** `style-src 'self'` added to the spec baseline. The baseline's `default-src 'none'` without a `style-src` directive blocks bundled viewer stylesheets; the addition stays local-only and contains no `unsafe-inline`.
- **`docs/sprints/SPRINT-STATUS.md` intentionally not edited** per the Batch-B controller ruling (controller updates the board after integration).

## Risks discovered
- ASP.NET Core's `PartitionedRateLimiter` partition map grows per distinct client key with no built-in eviction; only source IPs accepted as keys (bounded input, validated IPs) but many-source overload remains a laptop-protection follow-up for QA-05/SHR-10.
- Sanitizer allows only `http`/`https` URI schemes; owner-prepared previews that inline `data:` images would have those attributes stripped. SHR-04 should reference share-scoped artifact URLs instead.
- `X-Forwarded-For` rightmost-entry trust assumes ngrok appends the observed client IP; direct loopback peers could spoof it, but that is the same-user threat already excluded from the SHR-02 process-separation claim.

## Next eligible sprint(s)
- QA-05 — Public Share Security Audit (its SHR-08 dependency is now satisfied; SHR-10 already DONE).
