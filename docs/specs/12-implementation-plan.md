# 12 — Document IDE Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Build the Document IDE foundation as a Windows-first, local-first document workspace for DOCX and Markdown, with a clean canvas, real-time search, strict batch operations, Word integration, Markdown graph workflows, PDF finalization, and read-only ngrok sharing.

**Architecture:** Use a Windows-native .NET backend/desktop shell with a WebView-based React/TypeScript frontend. Store only workspace and derived state in SQLite; physical DOCX/Markdown files remain authoritative. Isolate file mutation, Word automation, indexing, and public share routes behind explicit backend services.

**Tech Stack:** C#/.NET, Windows desktop shell (WinUI 3 or WPF), WebView2, ASP.NET Core local API, React, TypeScript, SQLite/FTS5, Open XML SDK, Microsoft Word COM/Interop, local PDF merge library, ngrok adapter.

## Global Constraints

- Physical DOCX and Markdown files are the source of truth.
- DOCX deep editing occurs in Microsoft Word Desktop.
- Markdown is source-first and edited internally.
- Dragging canvas objects never physically moves files.
- All destructive batch operations require deterministic preview before apply.
- Search & Replace includes subfolders by default.
- Semantic/fuzzy matching is never used for destructive Search & Replace.
- Application-originated writes use recovery snapshot + validation + atomic replacement.
- Operation recovery retention is 30 days.
- Core app must work offline.
- Public sharing is read-only and scoped through an unguessable token.
- Public sharing uses ngrok transport in V1 and never exposes arbitrary filesystem paths.
- Expected workspace scale is under 1,000 documents.

---

# Implementation Strategy

Do NOT try to implement the entire product in one branch.

Each milestone below must produce a working, reviewable increment.

---

## Milestone 0 — Repository & Technical Spikes

### Task 0.1: Create project skeleton

**Files:**
- Create solution/project structure for backend, desktop shell, frontend, tests, and docs.

**Produces:**
- desktop shell launches,
- WebView loads frontend,
- local backend health endpoint responds,
- test projects execute.

- [ ] Create solution structure.
- [ ] Add formatting/linting configuration.
- [ ] Add SQLite package and migration infrastructure.
- [ ] Add frontend build and desktop embedding.
- [ ] Write smoke test for local API.
- [ ] Run all smoke tests.
- [ ] Commit: `chore: scaffold document ide application`

### Task 0.2: Spike OpenXML strict replacement

**Deliverable:** isolated prototype proving logical matching across split Word runs.

- [ ] Add `split-runs.docx` fixture.
- [ ] Write failing test for `NUSA-DUA-1` across two runs.
- [ ] Implement logical paragraph/run mapping.
- [ ] Implement minimal strict replace.
- [ ] Assert unrelated formatting remains.
- [ ] Commit: `spike: prove strict docx run replacement`

### Task 0.3: Spike Word automation

- [ ] Implement isolated Word adapter prototype.
- [ ] Test opening an original DOCX.
- [ ] Test PDF export.
- [ ] Assert source fingerprint unchanged.
- [ ] Verify COM cleanup.
- [ ] Commit: `spike: validate microsoft word automation`

### Task 0.4: Spike Windows file identity

- [ ] Read stable file identity.
- [ ] Rename same file.
- [ ] Prove identity remains correlated.
- [ ] Move within same volume.
- [ ] Document cross-volume fallback behavior.
- [ ] Commit: `spike: validate windows file identity`

### Task 0.5: Spike canvas library

- [ ] Render 100 folder/document objects.
- [ ] Pan/zoom.
- [ ] Multi-select.
- [ ] Drag.
- [ ] Persist coordinates.
- [ ] Implement simple collision-aware new placement.
- [ ] Record chosen library decision.
- [ ] Commit: `spike: validate document canvas library`

---

## Milestone 1 — Workspace & Persistence Foundation

### Task 1.1: Implement database schema

**Consumes:** domain model in `02-domain-data-model.md`.

**Produces:** repositories for workspace, source, document, canvas state.

- [ ] Write migration tests.
- [ ] Create core schema migration.
- [ ] Implement repository interfaces.
- [ ] Test create/read/update.
- [ ] Commit: `feat: add workspace persistence model`

### Task 1.2: Named workspace CRUD

- [ ] Write workspace service tests.
- [ ] Implement create/list/open/delete metadata.
- [ ] Persist active view.
- [ ] Add frontend workspace switcher.
- [ ] Verify deleting workspace does not delete source files.
- [ ] Commit: `feat: add named workspaces`

### Task 1.3: Folder source attachment

- [ ] Implement native folder picker.
- [ ] Canonicalize selected source.
- [ ] Persist source.
- [ ] Enumerate supported DOCX/Markdown.
- [ ] Display folder object with file count.
- [ ] Commit: `feat: attach local folder sources`

---

## Milestone 2 — Filesystem Identity & Reconciliation

### Task 2.1: Document identity service

- [ ] Test identity from stable Windows file ID.
- [ ] Implement path + identity mapping.
- [ ] Persist current/last-known path.
- [ ] Test rename preservation.
- [ ] Commit: `feat: track stable document identities`

### Task 2.2: Runtime watcher

- [ ] Write watcher coalescing tests.
- [ ] Implement watcher per source.
- [ ] Debounce duplicate events.
- [ ] Queue document refresh.
- [ ] Commit: `feat: watch local document changes`

### Task 2.3: Startup reconciliation

- [ ] Test changed-while-closed scenario.
- [ ] Load cached workspace first.
- [ ] Scan light metadata.
- [ ] Detect new/modified/renamed/missing.
- [ ] Queue only stale parses.
- [ ] Commit: `feat: reconcile workspace with local filesystem`

### Task 2.4: Missing source/file UX

- [ ] Add missing document state.
- [ ] Add Locate action.
- [ ] Add missing folder state.
- [ ] Preserve canvas coordinates.
- [ ] Commit: `feat: preserve workspace state for missing files`

---

## Milestone 3 — Indexing & Metadata

### Task 3.1: DOCX parser

- [ ] Create fixture tests.
- [ ] Extract body/tables/headers/footers.
- [ ] Extract section metadata.
- [ ] Extract heading candidates.
- [ ] Persist `DocumentMetadata`.
- [ ] Commit: `feat: index docx structure and metadata`

### Task 3.2: Markdown parser

- [ ] Test headings/frontmatter/links/wikilinks.
- [ ] Parse Markdown.
- [ ] Persist structured metadata.
- [ ] Commit: `feat: index markdown structure`

### Task 3.3: FTS5 index

- [ ] Create FTS migration.
- [ ] Index filename/body/headings/table/header/footer.
- [ ] Add incremental update.
- [ ] Add rebuild command.
- [ ] Commit: `feat: add local full text index`

---

## Milestone 4 — Clean Canvas

### Task 4.1: Folder and paper object visuals

- [ ] Implement white canvas.
- [ ] Render folder object.
- [ ] Render paper thumbnail shell.
- [ ] Render exact filename above paper.
- [ ] Persist zoom/pan.
- [ ] Commit: `feat: add clean document canvas`

### Task 4.2: Folder picker interaction

- [ ] Single click opens picker.
- [ ] Multi-select files.
- [ ] `Open selected`.
- [ ] Double-click folder opens all.
- [ ] Commit: `feat: open folder documents onto canvas`

### Task 4.3: Assisted placement

- [ ] Write placement tests.
- [ ] New file anchor near source folder.
- [ ] Avoid overlap.
- [ ] Respect manual objects.
- [ ] Respect pinned objects.
- [ ] Apply only local reflow.
- [ ] Commit: `feat: add assisted freeform placement`

### Task 4.4: Selection and tidy

- [ ] Multi-select rectangle.
- [ ] Ctrl-click.
- [ ] Group drag.
- [ ] Tidy selection.
- [ ] Tidy canvas.
- [ ] Pin action.
- [ ] Commit: `feat: add canvas selection and tidy actions`

---

## Milestone 5 — Search

### Task 5.1: Query parser

- [ ] Tests for exact phrases.
- [ ] Tests for exclusions.
- [ ] Tests for metadata filters.
- [ ] Implement structured query model.
- [ ] Commit: `feat: parse document search queries`

### Task 5.2: Realtime search API

- [ ] Implement workspace search.
- [ ] Add snippets.
- [ ] Add result-source labels.
- [ ] Add ranking.
- [ ] Commit: `feat: add realtime local document search`

### Task 5.3: Fuzzy exploratory search

- [ ] Add typo-tolerance tests.
- [ ] Keep fuzzy lower priority than exact.
- [ ] Mark matching mode in result.
- [ ] Commit: `feat: add fuzzy document search`

### Task 5.4: Search/canvas integration

- [ ] Highlight/fade matches without moving coordinates.
- [ ] Select all current results.
- [ ] Feed selected IDs to operation scope.
- [ ] Commit: `feat: integrate search with canvas selection`

---

## Milestone 6 — DOCX Lens & Word

### Task 6.1: Inspector

- [ ] Show metadata for DOCX.
- [ ] Show headings.
- [ ] Show Word/open state.
- [ ] Add Reveal in Explorer.
- [ ] Commit: `feat: add document inspector`

### Task 6.2: Thumbnail cache

- [ ] Define cache key by fingerprint.
- [ ] Generate low-detail thumbnail.
- [ ] Invalidate on file change.
- [ ] Commit: `feat: cache docx thumbnails`

### Task 6.3: Quick Look

- [ ] Space opens preview.
- [ ] Render multi-page preview lazily.
- [ ] Escape closes.
- [ ] Highlight search matches where possible.
- [ ] Commit: `feat: add read only quick look`

### Task 6.4: Word integration

- [ ] Double click DOCX opens Word.
- [ ] Display editing state.
- [ ] Re-index after save.
- [ ] Handle Word missing.
- [ ] Commit: `feat: integrate microsoft word editing`

---

## Milestone 7 — Strict Batch Engine & History

### Task 7.1: Common operation plan infrastructure

- [ ] Define operation plan types.
- [ ] Persist expected fingerprints.
- [ ] Implement preview/apply separation.
- [ ] Reject stale plans.
- [ ] Commit: `feat: add strict batch operation plans`

### Task 7.2: Recovery snapshots

- [ ] Snapshot only affected files.
- [ ] Persist 30-day expiry.
- [ ] Cleanup expired snapshots.
- [ ] Commit: `feat: add batch recovery snapshots`

### Task 7.3: Search & Replace preview

- [ ] Folder scope includes subfolders by default.
- [ ] Add branch exclusions.
- [ ] Match body/table/header/footer.
- [ ] Add occurrence selection.
- [ ] Commit: `feat: preview strict document search replace`

### Task 7.4: Search & Replace apply

- [ ] Check Word lock.
- [ ] Check stale fingerprint.
- [ ] Copy to temp.
- [ ] Strict replace across runs.
- [ ] Validate.
- [ ] Atomic replace.
- [ ] Re-index.
- [ ] Commit: `feat: safely apply document search replace`

### Task 7.5: Regex advanced mode

- [ ] Add explicit Regex toggle.
- [ ] Add timeout.
- [ ] Preview capture/replacement.
- [ ] Commit: `feat: add regex batch replace mode`

### Task 7.6: Formatting operations

Implement each as a separately reviewable operation:

- [ ] Set font size.
- [ ] Set font.
- [ ] Set margins.
- [ ] Set paper size.
- [ ] Set orientation.
- [ ] Set alignment.
- [ ] Verify strict non-target properties unchanged.
- [ ] Commit each operation separately.

### Task 7.7: Undo

- [ ] Add operation history screen.
- [ ] Undo unchanged post-operation files.
- [ ] Detect later external modifications.
- [ ] Show undo conflicts.
- [ ] Commit: `feat: add conflict aware batch undo`

---

## Milestone 8 — Markdown & Graph

### Task 8.1: Source editor

- [ ] Open Markdown internally.
- [ ] Edit/Preview/Split.
- [ ] Safe save.
- [ ] Syntax highlighting.
- [ ] Commit: `feat: add source first markdown editor`

### Task 8.2: Markdown rendering features

- [ ] Tables.
- [ ] Task lists.
- [ ] Code highlighting.
- [ ] Mermaid.
- [ ] Math.
- [ ] Sanitization.
- [ ] Commit: `feat: add markdown rich preview`

### Task 8.3: Explicit graph

- [ ] Extract links/wikilinks/frontmatter relations.
- [ ] Build backlinks.
- [ ] Add Graph view.
- [ ] Persist graph coordinates.
- [ ] Use solid explicit edges.
- [ ] Commit: `feat: add explicit knowledge graph`

### Task 8.4: Manual relations

- [ ] Create manual relation.
- [ ] Edit relation type.
- [ ] Delete manual relation.
- [ ] Commit: `feat: add manual document relations`

---

## Milestone 9 — PDF Finalization

### Task 9.1: Single Word PDF export

- [ ] Export selected DOCX.
- [ ] Handle collision.
- [ ] Verify source fingerprint unchanged.
- [ ] Commit: `feat: export docx to pdf`

### Task 9.2: Batch separate export

- [ ] Queue multiple Word exports.
- [ ] Show progress.
- [ ] Show partial results.
- [ ] Commit: `feat: batch export docx pdfs`

### Task 9.3: Ordered merge

- [ ] Reorder UI.
- [ ] Render intermediate PDFs.
- [ ] Merge without rasterizing.
- [ ] Clean temp files.
- [ ] Commit: `feat: merge ordered documents to pdf`

---

## Milestone 10 — Local Semantic Search

### Task 10.1: Provider abstraction

- [ ] Define `EmbeddingProvider`.
- [ ] Implement local provider.
- [ ] Persist model/content fingerprint.
- [ ] Commit: `feat: add semantic embedding abstraction`

### Task 10.2: Smart search

- [ ] Add semantic query mode.
- [ ] Keep exact mode independent.
- [ ] Label semantic result source.
- [ ] Commit: `feat: add local semantic document search`

### Task 10.3: Semantic graph

- [ ] Generate inferred relations.
- [ ] Persist confidence.
- [ ] Render dashed edges.
- [ ] Commit: `feat: add inferred document relations`

---

## Milestone 11 — Read-Only Sharing

### Task 11.1: Restricted share server

- [ ] Create token-scoped share middleware.
- [ ] Implement allowed read-only routes.
- [ ] Ensure no internal mutation routes exposed.
- [ ] Add security tests.
- [ ] Commit: `feat: add restricted read only share server`

### Task 11.2: Snapshot share

- [ ] Create local snapshot manifest.
- [ ] Produce sanitized artifacts.
- [ ] Implement viewer.
- [ ] Commit: `feat: add snapshot document sharing`

### Task 11.3: Live share

- [ ] Update shared artifacts after local save.
- [ ] Scope only share members.
- [ ] Commit: `feat: add live read only sharing`

### Task 11.4: Download permissions

- [ ] Default original OFF.
- [ ] Default PDF OFF.
- [ ] Enforce permissions server-side.
- [ ] Commit: `feat: add share download permissions`

### Task 11.5: NgrokTransport

- [ ] Implement provider adapter.
- [ ] Store credentials securely.
- [ ] Start/stop tunnel.
- [ ] Show public link/status.
- [ ] Commit: `feat: expose shares through ngrok`

---

## Milestone 12 — Command Palette & Power UX

### Task 12.1: Command registry

- [ ] Define command interface.
- [ ] Add contextual enablement.
- [ ] Add command palette.
- [ ] Commit: `feat: add command palette`

### Task 12.2: Keyboard shortcuts

- [ ] Quick file search.
- [ ] Preview Space.
- [ ] Rename.
- [ ] Escape.
- [ ] Context-aware selection.
- [ ] Test conflicts with Markdown editor.
- [ ] Commit: `feat: add document workspace shortcuts`

---

## Milestone 13 — Context Packs

### Task 13.1: Context selection

- [ ] Create context pack from selection/collection.
- [ ] Include deterministic source list.
- [ ] Convert DOCX extracted content to Markdown representation.
- [ ] Commit: `feat: create ai agent context packs`

### Task 13.2: Dependency expansion

- [ ] Optional explicit backlinks/dependencies.
- [ ] Token estimate.
- [ ] Export `context.md`.
- [ ] Commit: `feat: expand context packs with linked docs`

---

## Milestone 14 — Hardening

### Task 14.1: Golden regression suite

- [ ] Add full DOCX fixture corpus.
- [ ] Add strict-operation invariance tests.
- [ ] Commit: `test: expand docx regression coverage`

### Task 14.2: Filesystem soak tests

- [ ] Continuous Word saves.
- [ ] Rename/move/add/delete.
- [ ] Workspace switches.
- [ ] Fix leaks/races discovered.
- [ ] Commit: `test: harden filesystem synchronization`

### Task 14.3: Sharing security audit

- [ ] Path traversal.
- [ ] Token isolation.
- [ ] Download bypass.
- [ ] Internal API exposure.
- [ ] Commit fixes with security-specific messages.

### Task 14.4: Installer/recovery

- [ ] Package Windows installer.
- [ ] Preserve local app data on normal upgrade.
- [ ] Verify uninstall never removes source documents.
- [ ] Commit: `chore: package windows release`

---

# Completion Gates

Before calling V1 complete:

- [ ] Filesystem remains source of truth.
- [ ] Named workspaces persist.
- [ ] Canvas placement preserves spatial memory.
- [ ] Search is incremental/realtime.
- [ ] DOCX opens in Word.
- [ ] Markdown edits internally.
- [ ] Search & Replace is deterministic, recursive, previewable, undoable.
- [ ] 30-day recovery works.
- [ ] PDF single/batch/merge works.
- [ ] Graph distinguishes explicit vs inferred.
- [ ] Public share cannot mutate local files.
- [ ] Security suite passes.
- [ ] Golden DOCX regression suite passes.

---

# Agent Execution Guidance

Recommended implementation method:

1. Work milestone-by-milestone.
2. Use a dedicated branch/worktree for each milestone.
3. For every behavior:
   - write failing test,
   - run test,
   - implement minimum behavior,
   - run test,
   - refactor only after green,
   - commit.
4. Do not begin semantic search, sharing, or context packs before file safety/search/batch foundations are stable.
5. If implementation discovery contradicts a product-level requirement, update design documents explicitly rather than silently changing behavior.

---

# Post-Audit Implementation Plan Amendments

These tasks are mandatory and supersede any earlier task wording that conflicts with them.

## Milestone 0A — Security/Concurrency Foundation (before normal feature work)

### Task 0A.1: Harden privileged localhost API

- [ ] Generate 256-bit startup secret in native memory.
- [ ] Bind privileged API to loopback/runtime-selected port.
- [ ] Add Host allowlist and dedicated WebView Origin validation.
- [ ] Reject foreign and `null` origins.
- [ ] Require session secret on every privileged request; prefer native WebView request injection.
- [ ] Ensure no wildcard CORS and no state-changing GET.
- [ ] Add browser-origin/DNS-rebinding-style integration tests.
- [ ] Commit: `security: authenticate privileged localhost api`

### Task 0A.2: Configure SQLite concurrency baseline

- [ ] Enable WAL, foreign keys, and `busy_timeout=5000`.
- [ ] Add concurrent FTS read/index-write test.
- [ ] Ensure expensive parsing occurs outside writer transactions.
- [ ] Commit: `perf: configure sqlite wal concurrency`

### Task 0A.3: Bound DOCX package parsing

- [ ] Add hostile ZIP/XML fixtures.
- [ ] Preflight package metadata before OpenXML deep parsing.
- [ ] Enforce automatic-index resource budgets.
- [ ] Prohibit DTD/external entities.
- [ ] Evaluate parser worker-process need for enforceable timeout/isolation.
- [ ] Commit: `security: bound docx package parsing resources`

### Task 0A.4: Serialize Word automation

- [ ] Create dedicated STA Word automation worker.
- [ ] Set concurrency exactly `1`.
- [ ] Queue concurrent callers and prove serialization.
- [ ] Commit: `fix: serialize word com automation`

## Milestone 4 Amendment — Canvas

- [ ] Select/benchmark spatial hash, R-tree, or quadtree.
- [ ] Use nearby-candidate collision lookup.
- [ ] Benchmark 100/500/1,000 objects.

## Milestone 7 Amendment — Batch Recovery/Rename

- [ ] Rename accepts basename only and rejects traversal/separators.
- [ ] Add 5 GiB default history budget and 80% warning.
- [ ] Preflight snapshot bytes/free disk.
- [ ] Block destructive apply when complete recovery cannot be created.
- [ ] Never silently evict unexpired snapshots.

## Milestone 8 Amendment — Graph

- [ ] Run graph layout off UI thread.
- [ ] Default >250 nodes to neighborhood/filter view.
- [ ] Limit semantic edges to top-K (`K=5` default) plus threshold.

## Milestone 9 Amendment — PDF

- [ ] Use sequential file/stream merge.
- [ ] Add peak-memory regression test.

## Milestone 11 Amendment — Public Sharing

### Task 11A.1: Separate ShareHost

- [ ] Create `DocumentIDE.ShareHost.exe`.
- [ ] Exclude Word/batch/OpenXML mutation/unrestricted filesystem services.
- [ ] Serve only sanitized share artifacts/manifests.
- [ ] Add 256-bit token + hashed fixed-time verification.
- [ ] Add strict CSP + maintained sanitizer.
- [ ] Add concrete server-side rate limits.
- [ ] Ensure public requests cannot invoke Word COM.
- [ ] Commit: `security: isolate public share host`

### Task 11A.2: Evaluate Windows OS isolation

- [ ] Prototype restricted token/AppContainer or equivalent supported isolation.
- [ ] Evaluate ACL-limited artifact directory.
- [ ] Document residual same-user filesystem risk if sandboxing is not shipped.
- [ ] Do not claim strong sandboxing without test evidence.

### Task 11A.3: Freeze Live Share membership

- [ ] Snapshot explicit membership at Share creation.
- [ ] Update content only for current members.
- [ ] Require owner action to add/remove members.
- [ ] Prove new file/Collection member is not automatically public.

## Updated Completion Gates

- [ ] foreign browser origin cannot invoke privileged local API,
- [ ] ShareHost is separate and has no privileged mutation/Word dependencies,
- [ ] Live Share never auto-expands membership,
- [ ] Word COM maximum concurrency is 1,
- [ ] SQLite WAL concurrency test passes,
- [ ] destructive batch fails closed without recovery capacity,
- [ ] hostile DOCX resource tests pass,
- [ ] share CSP/token/rate-limit tests pass,
- [ ] canvas/graph/PDF performance regressions pass.
