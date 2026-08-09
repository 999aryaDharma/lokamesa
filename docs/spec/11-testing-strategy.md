# 11 — Verification & Testing Strategy

**Project:** Document IDE  
**Status:** Quality baseline  
**Depends on:** All subsystem specifications

---

## 1. Purpose

Defines the required testing strategy for a file-safety-sensitive Windows desktop application.

This product manipulates real office documents. Tests must prioritize evidence that the application does not corrupt or unexpectedly alter user files.

---

## 2. Testing Pyramid

Required layers:

```text
Unit
Integration
Golden fixtures
End-to-end
Manual compatibility
Performance
Security
```

---

## 3. Unit Tests

### Workspace

- workspace creation,
- group nesting max depth,
- collection membership,
- position-mode rules.

### Search

- query parser,
- exact phrase,
- exclusions,
- metadata filters,
- ranking.

### Filesystem

- canonical paths,
- identity comparison,
- rename correlation,
- stale fingerprint logic.

### Batch

- scope resolution,
- plan creation,
- stale rejection,
- occurrence selection.

### Markdown

- frontmatter,
- links,
- wikilinks,
- headings.

---

## 4. DOCX Golden Fixture Corpus

Maintain fixtures under version control.

Recommended categories:

```text
simple.docx
split-runs.docx
mixed-formatting.docx
tables.docx
headers-footers.docx
multi-section.docx
mixed-margins.docx
orientation.docx
images.docx
hyperlinks.docx
numbering.docx
custom-styles.docx
fields.docx
tracked-changes.docx
```

---

## 5. DOCX Assertions

For each strict operation:

- package opens before,
- package opens after,
- requested property changed,
- unrelated properties unchanged,
- content outside target unchanged as far as structurally reasonable.

---

## 6. Search & Replace Split-Run Test

Fixture contains:

```text
NUSA-
DUA-1
```

across OOXML runs.

Test:

```text
search "NUSA-DUA-1"
→ 1 logical match
replace with "NUSA DUA 1"
→ resulting visible text correct
→ unrelated run formatting preserved
```

---

## 7. Safe-Write Failure Tests

Simulate:

- invalid mutated DOCX,
- write failure,
- destination lock,
- disk error where injectable.

Assert:

```text
original fingerprint unchanged
```

---

## 8. Stale Preview Test

```text
preview operation
→ externally change file
→ apply old plan
```

Expected:

```text
rejected as stale
```

---

## 9. Word Integration Tests

On Windows test agent with Microsoft Word installed:

- open file,
- detect document state,
- export PDF,
- ensure source fingerprint unchanged,
- close automation cleanly.

Tests must not kill pre-existing user Word instances.

---

## 10. Filesystem Watcher Tests

Simulate:

- duplicate modify events,
- rename,
- rapid save sequence,
- delete/create sequence,
- watcher overflow fallback where feasible.

Assert no duplicate document identities.

---

## 11. Startup Reconciliation Tests

Scenario:

1. close simulated app,
2. modify files,
3. rename file,
4. add file,
5. delete file,
6. reopen/reconcile.

Assert:

- renamed document preserves `DocumentId`,
- modified document reindexes,
- new document appears,
- deleted document marked missing.

---

## 12. Canvas Tests

Unit/interaction tests:

- new files placed near folder,
- no overlap with pinned objects,
- manual position not globally changed,
- search filtering does not persist layout changes,
- tidy selection excludes pinned objects.

Visual regression tests can be used for stable canvas object rendering.

---

## 13. Search Tests

Dataset fixture with known documents.

Test:

- exact > fuzzy ranking,
- metadata filters,
- folder scope,
- search snippets,
- incremental update.

---

## 14. Semantic Boundary Test

Ensure destructive Search & Replace does not call semantic provider.

Use mocked provider that throws if invoked.

---

## 15. History Tests

- snapshot created before mutation,
- undo restores file,
- later external modification creates undo conflict,
- expired snapshots deleted after 30 days.

---

## 16. PDF Tests

- single export,
- batch separate export,
- ordered merge,
- collision handling,
- source fingerprint unchanged.

Visual PDF fidelity can use page image comparison with tolerance for rasterization variance.

---

## 17. Markdown Tests

- safe save,
- frontmatter parsing,
- wikilink resolution,
- ambiguous wikilink detection,
- backlink update,
- Mermaid code not executed as arbitrary script.

---

## 18. Graph Tests

- explicit relation solid semantic type,
- semantic relation marked inferred,
- graph layout persists,
- share graph excludes nonmember nodes.

---

## 19. Sharing Security Tests

Required:

- random invalid token denied,
- stopped token denied,
- share token cannot access nonmember ID,
- `../` traversal denied,
- absolute path parameter ignored/rejected,
- download OFF enforced server-side,
- no mutation endpoint available publicly.

---

## 20. End-to-End Administrative Scenario

```text
Create Administrasi workspace
→ attach BA folder
→ index
→ open selected files on canvas
→ search "NUSA-DUA-1"
→ select result set
→ preview recursive Search & Replace
→ deselect one occurrence
→ apply
→ inspect history
→ open changed DOCX in Word
→ save
→ watcher reindexes
→ export PDF
→ undo batch
```

---

## 21. End-to-End Programmer Scenario

```text
Create project workspace
→ attach docs folder
→ open Markdown
→ edit
→ save
→ backlinks update
→ graph reflects relation
→ exact search
→ semantic mode
→ create collection
→ create context pack
```

Context Pack may remain staged until that phase is implemented.

---

## 22. Performance Tests

Representative dataset:

- 100 DOCX,
- 300 DOCX,
- 500 Markdown,
- mixed total under 1,000.

Measure:

- startup cache render,
- reconciliation,
- search latency,
- index update latency,
- canvas pan/zoom with visible objects,
- thumbnail cache hit/miss.

Do not set fake performance claims; record actual baselines in CI/manual profiling.

---

## 23. Soak Tests

Run watcher/indexer for extended session while:

- editing files in Word,
- adding/removing documents,
- switching workspaces.

Watch for:

- memory leaks,
- COM leaks,
- duplicated jobs,
- SQLite lock issues.

---

## 24. Crash Recovery Tests

Interrupt:

- during indexing,
- during preview,
- before atomic replace,
- after snapshot before replace,
- after replace before history finalization.

On restart:

- source files valid,
- operation state repairable,
- temp files cleanable.

---

## 25. CI Strategy

Cross-platform tests may run where possible for pure logic.

Windows CI agent required for:

- filesystem identity,
- Recycle Bin behavior,
- WebView shell integration,
- Word COM tests.

Word tests may be a dedicated integration pipeline if licensing/CI environment requires it.

---

## 26. Release Gate

No release if:

- source corruption test fails,
- stale plan can apply,
- public share path traversal exists,
- undo snapshot missing for destructive operations,
- DOCX mutation regression fails,
- Word automation leaves orphan processes repeatedly.

---

## 27. Acceptance Criteria

Testing strategy is implemented when:

- every safety-critical subsystem has automated tests,
- golden DOCX corpus exists,
- E2E admin workflow passes,
- startup reconciliation passes,
- share security tests pass,
- release gate can fail build/release.

---

# 28. Post-Audit Mandatory Regression Tests

Add the following release-blocking test groups.

### Privileged localhost API

- foreign `Origin` rejected,
- `Origin: null` rejected for privileged browser calls,
- DNS-rebinding-style Host mismatch rejected,
- missing/incorrect startup session secret rejected,
- valid WebView Origin + secret accepted,
- no wildcard CORS,
- no state-changing GET,
- session secret not persisted to localStorage/disk.

### DOCX resource exhaustion

Fixtures for ZIP entry explosion, oversized uncompressed declaration, oversized single part, highly compressed XML, DTD/external entity, and parser-budget exhaustion. Expected result: safe warning, source unchanged, app remains responsive.

### Filesystem containment

A junction/symlink inside a configured source that points outside the root is not indexed, shared, or included in batch scope.

### Word COM

Queue concurrent export/preview callers and prove maximum Word COM concurrency is exactly `1` on the dedicated STA worker.

### SQLite

Run interactive FTS reads while background indexing writes. Assert WAL is active and expensive parsing/embedding is outside writer transactions.

### Recovery capacity

Verify warning threshold, fail-closed destructive apply when snapshots cannot fit, no silent eviction of unexpired snapshots, and normal cleanup of expired 30-day history.

### Share security

- malformed/oversized token rejected cheaply,
- fixed-time token verification path covered,
- Live Share does not auto-add new files/Collection members,
- ShareHost cannot resolve privileged mutation/Word services,
- CSP + sanitizer neutralize script injection,
- rate limits return `429`,
- public preview request does not invoke Word COM.

### Canvas/graph/PDF performance

- canvas benchmark at 100/500/1,000 objects using spatial-neighbor lookup,
- graph >250 nodes defaults to bounded view and layout stays off UI thread,
- PDF merge peak memory does not scale linearly with total source bytes.

Any failure above blocks release.
