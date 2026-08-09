# Release Check

## Description

Evaluate a release candidate using documented release gates.

## Steps

1. Verify release sprint dependencies and independent reviews are DONE.
2. Run repository release-verification script.
3. Run:
   - golden DOCX,
   - file-safety,
   - localhost API security,
   - public-share security,
   - Word COM serialization,
   - SQLite concurrency,
   - performance regression gates,
   - installer smoke test.
4. Verify no required suite is skipped.
5. Verify artifact commit/checksums.
6. Report `RELEASE READY` or `RELEASE BLOCKED`.
