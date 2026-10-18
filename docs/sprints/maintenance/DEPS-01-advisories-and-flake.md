# DEPS-01 — Dependency Advisories and the Last Intermittent Test

**Status:** READY
**Priority:** P1
**Kind:** maintenance
**Recommended branch:** `chore/dependency-hygiene`

## Goal

Remove the known package advisories from the build, and close the last intermittent test recorded in the
v1.0.0 readiness report.

## Depends On

- REL-03 (v1.0.0) — DONE

## In Scope

### 1. Package advisories

The release build reports:

- `HtmlSanitizer` 8.0.601 — GHSA-43cp-6p3q-2pc4 (moderate) and GHSA-j92c-7v7g-gj3f (moderate)
- `AngleSharp` 0.17.1 — GHSA-pgww-w46g-26qg (moderate)
- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 — GHSA-2m69-gcr7-jv3q (high)

Also present: `NU1603` warnings (the restored version does not satisfy the declared minimum for
HtmlSanitizer and Markdig). Fix the declared versions while you are there.

Rules:

- Upgrade to the **lowest patched version** that clears the advisory, not automatically the newest.
- Read the release notes / breaking-change list for each package before bumping a major.
- The sanitizer is security-critical: `ShareWebSecurityTests`, `SecurityTests`, the ShareHost isolation
  tests and the MarkdownPreview suite must all pass with the upgraded version. If an upgrade changes
  sanitized output, update the **expectations only when the new output is still safe and the reason is
  written down**.
- If the NuGet registry is unreachable, say so plainly and stop; do not invent version numbers, do not
  vendor packages.

### 2. The last intermittent test

`MoveCorrelationChallengerTests.DuplicateContents_OriginalSourceStillExists_FlagsAmbiguous` failed once in a
full run and passed 3/3 in isolation. Reproduce it under load (repeat the class while the rest of the suite
runs, or run the full suite repeatedly), find the shared state or timing assumption, and fix the **cause**.

Acceptable fixes: isolate the fixture's temp/DB state, remove an order dependency, or put the class in a
non-parallel collection when the test genuinely measures process-wide state. Not acceptable: retries,
skipping, or widening an assertion.

## Out of Scope

- Feature work, new packages unrelated to the advisories, the frontend dependency tree unless a frontend
  advisory is actually reported.
- `docs/quality/**`, `scripts/verify-release.ps1`, `.pen`.

## Required Tests

- [ ] Build emits **zero** `NU1902`/`NU1903` warnings for the three packages above.
- [ ] `dotnet list package --vulnerable --include-transitive` (or the SDK equivalent) reports none of them.
- [ ] Security, Safety, MarkdownPreview and ShareHost isolation suites pass.
- [ ] The previously intermittent test is stable across repeated full-suite runs, with the mechanism named.

## Definition of Done

- [ ] required tests pass; affected suites green; no skipped required test
- [ ] handoff written (`docs/sprints/handoffs/DEPS-01-HANDOFF.md`) including the exact version before/after
- [ ] board row left to the controller

## Commit Guidance

```text
chore(deps): clear the package advisories and fix the last intermittent test
```
