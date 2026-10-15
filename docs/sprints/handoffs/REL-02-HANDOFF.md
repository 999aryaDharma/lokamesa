# Sprint Handoff — REL-02

**Status:** DONE
**Branch:** `chore/rel-02-release-verification-pipeline`
**Commits:** `chore/rel-02-release-verification-pipeline` (exact hash in sprint report thread)

## What changed

- Added `scripts/verify-release.ps1`: REL-02 release verification orchestration.
  Runs Prerequisites → BackendBuild → BackendFull (all solution suites, trx
  evidence) → named release-blocking projections (Security/Safety/Golden/
  Architecture/Performance/Soak/BackendUnit/BackendIntegration) →
  MarkdownPreview (explicit, not in sln) → FrontendDeps/Typecheck/Lint/Tests/
  Build → WordSuite on capable runner → packaging via `build-release.ps1`
  ONLY on green gates → post-package PackagingVerification → SHA256 checksums
  → `verification-report.json/.md` pinning the exact commit.
- Any gate failure exits nonzero BEFORE packaging (fail-closed; proven).
- No green-by-skipping: Word-absent runners FAIL unless
  `-AllowEnvironmentGatedWordSkip -WordSkipJustification` is passed; the skip
  stays visible in the report. `-SkipPackage` records PackagingVerification as
  EnvironmentGated, never Passed.
- Added `.github/workflows/release.yml`: release pipeline pinned to a
  capable self-hosted Windows+Word runner, uploads report + artifacts (on
  success only). No implicit Word skip.
- Added `docs/quality/release-checklist.md`: manual sign-off mapping every
  gate plus spec 11 §26 release-blocking conditions.
- Added `tests/.../Scripts/ReleaseVerificationTests.cs` (4 tests): script
  exists, script is fail-closed (content markers), failing gate blocks release
  (executes `-SelfTest -FailGate`, asserts nonzero + BLOCKED + no packaging),
  passing self-test writes a commit-pinned report.
- Fixed `DocumentIDE.sln`: `DocumentIDE.SecurityTests` had a Project entry
  but NO ProjectConfigurationPlatforms entries, so `dotnet build/test` on the
  solution silently never built or ran the security suite (91 tests, incl.
  QA-05 attack-surface coverage). Added the 12 standard config lines. Without
  this fix the REL-02 SecurityGate could never pass honestly. `verify.ps1`
  (FND-08) has the same blind spot — flagged, not changed (out of scope).

## Files changed

- `scripts/verify-release.ps1` (new)
- `.github/workflows/release.yml` (new)
- `docs/quality/release-checklist.md` (new)
- `tests/DocumentIDE.IntegrationTests/Scripts/ReleaseVerificationTests.cs` (new)
- `DocumentIDE.sln` (12-line config fix for SecurityTests)
- `docs/sprints/SPRINT-STATUS.md` (REL-02 row → DONE)

## Verification

- `dotnet test --filter ReleaseVerificationTests` → 4/4 PASS (was 0/4 before
  the script existed — Red→Green).
- `verify-release.ps1 -SelfTest -FailGate SyntheticBackend` → exit 1,
  verdict BLOCKED, report written, packaging NOT invoked. PASS (fail-closed).
- `verify-release.ps1 -SelfTest` → exit 0, commit-pinned report. PASS.
- Full gate-only run (`-SkipPackage`, pre-sln-fix): BackendBuild,
  Prerequisites, all frontend gates, WordSuite (real Word present) Passed;
  run correctly BLOCKED on (a) 2 artifact-presence tests that cannot pass
  pre-packaging and (b) phantom-suite rows — both fixed by design (see
  Deviations). This run proved blocking works end-to-end.
- Definitive end-to-end run (`./scripts/verify-release.ps1`, no flags):
  exit 0, verdict PASSED, 19/19 gates Passed (incl. SecurityGate 91 tests,
  BackendUnit 459, BackendIntegration 413, WordSuite against installed Word,
  PackagingVerification), MSI packaged, 591 SHA256 checksums,
  `verification-report.json` pins commit ebdb248.
- Pre-existing failure (NOT caused by this sprint, reproduced on clean base
  via `git stash -u`): `ScriptTests.Release_Build_Embeds_Production_Web_Assets`
  fails on a fresh worktree without packaged artifacts — by design resolved by
  the post-package PackagingVerification ordering.

## Acceptance criteria

- [x] No 'green by skipping tests' policy — skips fail unless explicitly
      environment-gated with justification, recorded in the report.
- [x] Any release-gate failure blocks packaging/release (proven, exit≠0).
- [x] Artifact checksum generated (SHA256 over installer + desktop outputs).
- [x] Verification report names exact commit (`git rev-parse HEAD`).

## Deviations / spec conflicts

1. `DocumentIDE.sln` fix (above): build-config only, no product behavior
   changed; required to satisfy "dotnet test all suites incl. Security".
2. Two artifact-presence tests excluded from pre-package BackendFull and run
   post-packaging as `PackagingVerification` (same tests, correct order —
   they assert packaging outputs). A post-package failure still BLOCKS the
   release verdict and nothing uploads (workflow `if: success()`).
3. `tests/DocumentIDE.InstallTests` holds only `InstallerSmoke.md` (no test
   project); installer coverage comes from `InstallerTests.cs` inside
   IntegrationTests. No separate InstallTests gate row.
4. `MarkdownPreview.Tests` is not in the solution; the gate runs its csproj
   explicitly rather than skipping it.

## Risks discovered

- `scripts/verify.ps1` (FND-08) still silently skips SecurityTests
  (pre-sln-fix blind spot now fixed in sln, so it picks the suite up
  automatically) and never runs MarkdownPreview.Tests (not in sln).
  Recommend a follow-up to align it; not changed here (out of scope).
- GitHub-hosted `windows-latest` runners have no Microsoft Office license;
  releases MUST use the capable self-hosted runner per `release.yml`, or pass
  an explicit justified Word skip (visible in report, REL-03 decision).

## Next eligible sprint(s)

- REL-03 — V1 Readiness Gate (unblocked by this sprint).
