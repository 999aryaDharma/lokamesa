# Release Checklist

Manual sign-off for a Document IDE release. The automated gate is
`scripts/verify-release.ps1` (CI: `.github/workflows/release.yml`);
this checklist covers what automation cannot assert.

## 1. Preconditions

- [ ] Release runs from the exact commit named in `verification-report.json`
      (`git rev-parse HEAD` matches the tag/commit being released).
- [ ] Working tree clean at packaging time (report `workingTreeDirty: false`);
      if dirty, record why and re-run after committing.
- [ ] Runner is Windows with .NET SDK 10.0.302, Node.js, and WiX available.
- [ ] Runner has Microsoft Word Desktop installed (Word/COM suite must
      really execute — `WordSuite: Passed`, not `EnvironmentGated`).
      If Word is unavailable, the release is BLOCKED unless an explicit
      `-WordSkipJustification` is recorded in the report.

## 2. Automated gates (all must be `Passed` in the report)

- [ ] `Prerequisites` — clean-machine dependencies present.
- [ ] `BackendBuild` — solution builds.
- [ ] `BackendFull` + `BackendUnit` + `BackendIntegration` — full suite green.
- [ ] `SecurityGate` — privileged API, ShareHost isolation, share security.
- [ ] `SafetyGate` — batch safety, crash/interrupted-operation recovery.
- [ ] `GoldenGate` — DOCX golden regression corpus.
- [ ] `ArchitectureGate` — ShareHost dependency boundaries intact.
- [ ] `PerformanceGate` — canvas/graph/PDF benchmarks within baseline
      (`docs/quality/performance-baseline.md`).
- [ ] `SoakGate` — filesystem/indexing soak, no leaks or lock issues.
- [ ] `InstallerTests` (class inside IntegrationTests) + `PackagingVerification`
      — installer manifest/uninstall-protection expectations, then
      artifact-presence proofs against freshly packaged outputs.
- [ ] `FrontendTypecheck` / `FrontendLint` / `FrontendTests` / `FrontendBuild`.
- [ ] `WordSuite` — executed against real Word (concurrency exactly 1,
      no orphan WINWORD processes, source fingerprints unchanged).

Release-blocking conditions (spec 11 §26 — any one blocks release):

- [ ] No source-corruption test failure.
- [ ] No stale plan can apply.
- [ ] No public share path traversal.
- [ ] Undo snapshot present for every destructive operation.
- [ ] No DOCX mutation regression.
- [ ] No repeated Word automation orphan processes.

## 3. Artifacts

- [ ] `artifacts/installer/DocumentIDE-Setup.msi` exists.
- [ ] `artifacts/desktop/DocumentIDE.Desktop.exe` and
      `DocumentIDE.ShareHost.exe` exist with embedded `wwwroot/index.html`.
- [ ] `checksums.sha256` generated and every listed file verifies
      (`Get-FileHash -Algorithm SHA256` matches).

## 4. Post-packaging spot checks (clean VM where possible)

- [ ] Install, launch, attach a folder, search, preview a batch operation.
- [ ] Uninstall preserves user source documents (installer never removes them).
- [ ] Upgrade preserves `%LOCALAPPDATA%\DocumentIDE\` app data.

## 5. Sign-off

- Release commit: _______________
- Verification report verdict: _______________
- Environment-gated skips (must be empty or justified): _______________
- Signed: _______________ Date (UTC): _______________
