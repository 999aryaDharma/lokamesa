# Sprint Handoff — REL-03

**Status:** REVIEW (sprint artifacts complete; product verdict **BLOCKED** — see verdict below)
**Branch:** `chore/rel-03-v1-readiness-gate`
**Commits:** `chore(release): v1 readiness gate` (single coherent commit; exact hash in sprint report thread)

## Verdict

`docs/quality/v1-readiness-report.md` → **BLOCKED**.

16 of 22 spec-12 completion gates PASS; 6 FAIL. The tested subsystem core is substantial and REL-02's release pipeline is fail-closed, but six locked behaviors are not deliverable in the shipped application (`DocumentIDE.Desktop.exe` + `DocumentIDE.ShareHost.exe`, the only executables published by `scripts/build-release.ps1`):

1. **Graph view is a placeholder** (G1) — no node/edge rendering, no explicit-vs-inferred semantics, no select/navigate/open; GR-03 steps 1/3/4/5 unmet; GR-05/AI-04 helpers are test-only.
2. **Batch Apply/undo unreachable** (G2) — Apply disabled in UI (`BatchPreviewPanel.tsx:147`), no apply/undo route; history UI presentational only.
3. **Open in Word 404 in the shipped host** (G4) — desktop host serves only `BatchPreviewController`; `BatchPreviewHostTests.cs:91` codifies the 404.
4. **Search unreachable** (G3) — no `/api/search` endpoint exists; `SearchPanel` unmounted.
5. **Share lifecycle unreachable** (G5) — no create/stop/revoke endpoints; SHR-11 callbacks unwired (admitted in `SHR-11-HANDOFF.md:45`).
6. **Workspace/canvas bootstrap not wired** (G6) — canvas store never populated outside tests; folder sources cannot be attached in the product.
7. **Markdown editor is a stub** (G7) — `MarkdownEditor.tsx` demo (dummy id, empty load, `console.log` save), unmounted.
8. **E2E administrative test missing** (G9) — required by spec 11 §20/§27.

Per the sprint rule, the SPRINT-STATUS row is **REVIEW**, not DONE (DONE would require a GO).

## What changed

- `docs/quality/v1-readiness-report.md` (new — the sprint's only planned artifact): full requirement→evidence mapping (spec 12 gates, master spec §82–90/§97/§101.1–101.16, testing strategy §26–28), scan results, offline verification, residual-risk review, blocker detail G1–G9 with `file:line` evidence, and the conditions required to flip the verdict to GO.
- `tests/DocumentIDE.IntegrationTests/Scripts/V1ReadinessGateTests.cs` (new — Required Tests): report exists; exactly one machine-readable verdict; verdict consistency (GO ⇒ no FAIL/UNVERIFIED rows; BLOCKED ⇒ at least one itemized blocker); verbatim coverage of all 22 spec-12 completion gates; scan/offline/risk evidence present; **no skipped tests in any release-blocking suite** (Security/Safety/Golden/Architecture/Performance/Soak).
- `docs/sprints/SPRINT-STATUS.md` (REL-03 → REVIEW with blockers listed; no other rows touched).
- `docs/sprints/handoffs/REL-03-HANDOFF.md` (this file).
- **No production code was modified.**

## Files changed

- `docs/quality/v1-readiness-report.md` (new)
- `tests/DocumentIDE.IntegrationTests/Scripts/V1ReadinessGateTests.cs` (new)
- `docs/sprints/SPRINT-STATUS.md` (REL-03 row + review notes)
- `docs/sprints/handoffs/REL-03-HANDOFF.md` (new)

## Verification

- **Red:** `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~V1ReadinessGateTests"` → 5/6 FAIL, error "V1 readiness report should exist at …" (expected: deliverable absent).
- **Green:** same command after writing the report → **6/6 PASS**.
- **Affected suite:** `dotnet test tests/DocumentIDE.IntegrationTests/… --filter "FullyQualifiedName!~Release_Build_Embeds_Production_Web_Assets&FullyQualifiedName!~Packages_Wxs_Installer_And_ShareHost"` → **419 passed / 0 failed / 0 skipped**. The two excluded tests assert packaged `artifacts/` outputs and are run post-packaging as REL-02's `PackagingVerification` (documented exclusion, not a skip).
- **Placeholder scan:** `git grep -n -i -E "TODO|FIXME|NotImplementedException|placeholder" -- src …` → 3 material hits classified in report §6.1 (graph placeholder, Markdown stub, stale ShareTokenMiddleware comment) + benign HTML/comment matches; no TODO/FIXME/NotImplementedException.
- **Skipped-test scan:** zero skips across `src/` + `tests/`; now continuously enforced for release-blocking suites by `NoSkippedTests_InReleaseBlockingSuites`.
- **Offline check:** no outbound calls in core src (loopback only); ngrok opt-in via `NgrokTransport`; external embeddings disabled by default (`AppSettings.cs:11`, blocked in `EmbeddingCoordinator.cs:65,136`); no remote assets in `index.html`.
- **`git diff` self-reviewed** before commit (docs + one test file only).

## Acceptance criteria

- [x] **V1 is evidence-based, not feature-count based** — every locked gate is mapped to concrete test/code evidence or an explicit gap with citations; verdict BLOCKED because the evidence does not support GO.
- [x] Required test — **Every locked requirement maps to implementation evidence** (`ReadinessReport_MapsEverySpec12CompletionGate` over all 22 spec-12 gates).
- [x] Required test — **No skipped safety/security gate** (scan + `NoSkippedTests_InReleaseBlockingSuites`).
- [x] Required test — **Residual risks are explicit** (report §7; sprint DoD prompt answered in report §9).

## Security & File-Safety Gate Confirmation

- [x] no user file was mutated through an unreviewed/bypass path (docs/test-only change; no filesystem-touching code added)
- [x] no arbitrary path or secret is exposed (report cites repository paths and test names only; no tokens/keys/personal paths beyond the repo)
- [x] dependency/security boundaries remain intact (no production code, no DI/host/controller changes)
- [x] no destructive action silently broadened its scope (no destructive behavior changed)
- [x] relevant higher-level safety rules remain stronger than convenience (no spec wording weakened; the gate deliberately returns BLOCKED instead of papering over gaps)

## Deviations / spec conflicts

- The sprint's Planned-Files rule mentions modifying an equivalent earlier file instead of creating duplicates — `docs/quality/` had no equivalent readiness report, so the planned path was created as specified.
- REL-03 did not implement any of the G1–G9 fixes: AGENTS.md's "work exactly one sprint" rule and the sprint's Out of Scope limit this sprint to the gate; the fixes require their own integration sprints (see below).
- Scanning/verification is intentionally limited to repository evidence. No clean-machine install/run of the packaged MSI was performed by this sprint (REL-02's `PackagingVerification` covers artifact presence/checksums; a clean-machine smoke remains a release-runner activity).

## Risks discovered

- The repository currently allows sprint handoffs to claim DONE while production reachability is deferred ("downstream owns it"): SHR-11:45, BAT-11:18, PDF-04:98-101, and the GR-03/MD-01 handoffs vs their artifacts. Future gates should treat handoff labels as claims, not evidence — this report is the corrective precedent.
- Four handoffs of V1-relevant sprints record no integration risk (SRCH-05, AI-04: "Risks discovered: None."), while their UI/helpers are unmounted. Recommend an explicit "product reachability" checklist item in the sprint DoD follow-up.
- `ShareTokenMiddleware.cs:9-11` doc comment contradicts its implementation; update before any future audit quotes it.
- All accepted residual risks from prior sprints remain (SHR-10 same-user, QA-04 move TTL, QA-01 compression ratio, QA-07 orphan snapshot dirs, REL-02 `verify.ps1` blind spot, Word-runner requirement, IntegrationTests SQLite flake) — recorded in report §7.

## Next eligible sprint(s)

Integration sprints required before V1 (one per gap, or a consolidated integration milestone):

- Graph view implementation (G1)
- Batch apply/undo + history wiring (G2)
- Search endpoint + shell integration (G3)
- Desktop privileged host surface (Word/QuickLook/folder sources/rebind) (G4)
- Share lifecycle wiring (G5)
- Workspace/canvas bootstrap + shell composition (G6)
- Markdown editor real implementation (G7)
- PDF finalization wiring (G8)
- E2E administrative workflow test (G9)

Then re-run `scripts/verify-release.ps1` and re-issue the readiness report for a new REL-03-style gate.

## Final gate run - verdict GO

Run: `pwsh -File scripts/verify-release.ps1` on `integration/parallel-batches` at commit `aba47ecaf45bfb55c77cfd9ff68543ac0d33c3f7`.
Result: **19/19 gates passed, 0 failed, 0 environment-gated**, `workingTreeDirty=false`, packaging invoked and verified, Word/COM suite executed on this machine. Machine report: `artifacts/release-verify/verification-report.json` (`verdict: PASSED`).

`docs/quality/v1-readiness-report.md` was re-issued against that run: exactly one `**Verdict:** GO` line, all 22 spec-12 completion-gate strings verbatim, zero `FAIL`/`UNVERIFIED` rows, scans recorded (placeholder, skipped-test, offline, spec-drift) and residual risks listed. `V1ReadinessGateTests` 6/6 passes.

Before the run the gate blocked once on two test-isolation defects (a shared `%TEMP%\scm01\outside.md` across two parallel test classes, and a process-wide memory sample running alongside other classes). Both were fixed at the root in `aba47ec` - the repo fixture now gives every repository its own parent, and the memory test runs in a non-parallel collection. The gate was then re-run, not retried until green.

