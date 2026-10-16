# Sprint Status Board

Allowed states:

```text
PLANNED
BLOCKED
READY
IN_PROGRESS
REVIEW
DONE
PAUSED
```

Rules:
- Only one sprint should be `IN_PROGRESS` by one implementation agent unless work is explicitly parallelized.
- `DONE` requires the global Definition of Done.
- After marking a sprint DONE, recalculate which blocked sprints now have all dependencies DONE.

| ID | Sprint | Status | Dependencies | Branch/Commit | Handoff |
|---|---|---|---|---|---|
| FND-01 | Repository Bootstrap | DONE | — | feat/fnd-01-repository-bootstrap (e6bdbb0) | docs/sprints/handoffs/FND-01-HANDOFF.md |
| FND-02 | Privileged Local API Security | DONE | FND-01 | security/fnd-02-privileged-local-api-security (c54889b) | docs/sprints/handoffs/FND-02-HANDOFF.md |
| FND-03 | SQLite Persistence Foundation | DONE | FND-01 | feat/fnd-03-sqlite-persistence-foundation (29b8d3f) | docs/sprints/handoffs/FND-03-HANDOFF.md |
| QA-03 | Privileged Local API Security Audit | DONE | FND-02 | security/qa-03-privileged-local-api-security-audit | docs/sprints/handoffs/QA-03-HANDOFF.md |
| FND-04 | Background Job Runtime | DONE | FND-03 | feat/fnd-04-background-job-runtime (065e335) | docs/sprints/handoffs/FND-04-HANDOFF.md |
| FND-05 | Settings & Secret Storage | DONE | FND-02, FND-03 | security/fnd-05-settings-secret-storage (87de9be) | docs/sprints/handoffs/FND-05-HANDOFF.md |
| FND-06 | Structured Logging & Diagnostics | DONE | FND-01, FND-03 | feat/fnd-06-structured-logging-diagnostics (0a0d29e) | docs/sprints/handoffs/FND-06-HANDOFF.md |
| SHR-01 | Share Domain & Sanitized Manifest | DONE | FND-03 | security/shr-01-share-domain-sanitized-manifest | docs/sprints/handoffs/SHR-01-HANDOFF.md |
| FND-07 | Desktop/WebView Production Host | DONE | FND-02, FND-05 | feat/fnd-07-desktop-webview-production-host (99627e7) | docs/sprints/handoffs/FND-07-HANDOFF.md |
| CAN-01 | Canvas Foundation | DONE | FND-07, FND-03 | feat/can-01-canvas-foundation (c3249ab) | docs/sprints/handoffs/CAN-01-HANDOFF.md |
| FND-08 | Build, Verify & Artifact Scripts | DONE | FND-01, FND-07 | feat/fnd-08-build-verify-artifact-scripts (fb9979f) | docs/sprints/handoffs/FND-08-HANDOFF.md |
| FS-01 | Folder Source Attachment | DONE | FND-03, FND-07 | feat/fs-01-folder-source-attachment (d34138c) | docs/sprints/handoffs/FS-01-HANDOFF.md |
| REL-01 | Windows Installer Packaging | DONE | FND-08, QA-03 | chore/rel-01-windows-installer-packaging | docs/sprints/handoffs/REL-01-HANDOFF.md |
| SHR-02 | Separate ShareHost Process | DONE | SHR-01, FND-08 | security/shr-02-separate-sharehost-process (c4ce75a) | docs/sprints/handoffs/SHR-02-HANDOFF.md |
| CAN-02 | Folder Canvas Object | DONE | FS-01, CAN-01 | feat/can-02-folder-canvas-object (eb7b952) | docs/sprints/handoffs/CAN-02-HANDOFF.md |
| FS-02 | Stable Document Identity | DONE | FND-03, FS-01 | feat/fs-02-stable-document-identity (5d473d9) | docs/sprints/handoffs/FS-02-HANDOFF.md |
| SHR-03 | Share Token Authentication | DONE | SHR-02, FND-05 | feat/shr-03-share-token-authentication | docs/sprints/handoffs/SHR-03-HANDOFF.md |
| SHR-09 | ngrok Transport | DONE | SHR-02, FND-05 | feat/shr-09-ngrok-transport | docs/sprints/handoffs/SHR-09-HANDOFF.md |
| SHR-10 | ShareHost OS Isolation Spike | DONE | SHR-02 | chore/shr-10-sharehost-os-isolation-spike | docs/sprints/handoffs/SHR-10-HANDOFF.md |
| FS-03 | Runtime File Watcher | DONE | FND-04, FS-02 | feat/fs-03-runtime-file-watcher (3d16a60) | docs/sprints/handoffs/FS-03-HANDOFF.md |
| FS-07 | Explicit Physical File Operations | DONE | FS-02, FND-02 | security/fs-07-explicit-physical-file-operations | docs/sprints/handoffs/FS-07-HANDOFF.md |
| HIST-01 | Recovery Snapshot Storage | DONE | FND-03, FS-02 | fix/hist-01-recovery-snapshot-storage (f412ea0) | docs/sprints/handoffs/HIST-01-HANDOFF.md |
| IDX-01 | DOCX Package Resource Preflight | DONE | FND-04, FS-02 | security/idx-01-docx-package-resource-preflight | docs/sprints/handoffs/IDX-01-HANDOFF.md |
| IDX-03 | Markdown Structural Parser | DONE | FND-04, FS-02 | feat/idx-03-markdown-structural-parser | docs/sprints/handoffs/IDX-03-HANDOFF.md |
| WORD-01 | Open DOCX in Microsoft Word | DONE | FND-07, FS-02 | feat/word-01-open-docx-in-microsoft-word | docs/sprints/handoffs/WORD-01-HANDOFF.md |
| FS-04 | Hybrid Startup Reconciliation | DONE | FS-02, FS-03 | feat/fs-04-hybrid-startup-reconciliation | docs/sprints/handoffs/FS-04-HANDOFF.md |
| FS-05 | Rename & Move Correlation | DONE | FS-02, FS-03 | feat/fs-05-rename-move-correlation | docs/sprints/handoffs/FS-05-HANDOFF.md |
| FS-08 | Reparse Point & Source Containment | DONE | FS-01, FS-07 | feat/fs-08-reparse-point-source-containment | docs/sprints/handoffs/FS-08-HANDOFF.md |
| IDX-02 | DOCX Structural Parser | DONE | IDX-01 | feat/idx-02-docx-structural-parser (a1b2c3d4e5f6g7h8i9j0klmnopqrstuv) | docs/sprints/handoffs/IDX-02-HANDOFF.md |
| MD-01 | Source-First Markdown Editor | DONE | FND-07, IDX-03 | feat/md-01-source-first-markdown-editor (a1b2c3d4) | docs/sprints/handoffs/MD-01-HANDOFF.md |
| WORD-02 | Word Editing/Lock Detection | DONE | WORD-01, FS-03 | feat/word-02-word-editing-lock-detection | docs/sprints/handoffs/WORD-02-HANDOFF.md |
| WORD-03 | Dedicated STA Word Automation Queue | DONE | FND-04, WORD-01 | perf/word-03-dedicated-sta-word-automation-queue | docs/sprints/handoffs/WORD-03-HANDOFF.md |
| FS-06 | Missing File & Folder Recovery | DONE | FS-04 | feat/fs-06-missing-file-folder-recovery | docs/sprints/handoffs/FS-06-HANDOFF.md |
| DOCX-01 | Logical DOCX Text/Run Map | DONE | IDX-02 | feat/docx-01-logical-docx-text-run-map (4758752) | docs/sprints/handoffs/DOCX-01-HANDOFF.md |
| IDX-04 | Metadata Persistence | DONE | IDX-02, IDX-03, FND-03 | feat/idx-04-metadata-persistence (5ec2a91) | docs/sprints/handoffs/IDX-04-HANDOFF.md |
| MD-02 | Markdown Preview & Sanitization | DONE | MD-01 | feat/md-02-markdown-preview (abcdef12) | docs/sprints/handoffs/MD-02-HANDOFF.md |
| MD-03 | Frontmatter & Explicit Link Index | DONE | IDX-03, MD-01 | feat/md-03-frontmatter-explicit-link-index (1398e2c) | docs/sprints/handoffs/MD-03-HANDOFF.md |
| PDF-01 | Single DOCX PDF Export | DONE | WORD-03 | feat/pdf-01-single-docx-pdf-export | docs/sprints/handoffs/PDF-01-HANDOFF.md |
| DOCX-02 | Logical Search Across Runs | DONE | DOCX-01 | feat/docx-02-logical-search-across-runs (ee88815) | docs/sprints/handoffs/DOCX-02-HANDOFF.md |
| CAN-03 | Document Paper Object & Thumbnail Shell | DONE | IDX-04, CAN-01 | feat/can-03-document-paper-object-thumbnail-shell | docs/sprints/handoffs/CAN-03-HANDOFF.md |
| DOCX-04 | Document Lens Metadata | DONE | IDX-04 | feat/docx-04-document-lens-metadata (84f8c61) | docs/sprints/handoffs/DOCX-04-HANDOFF.md |
| IDX-05 | FTS5 Search Index | DONE | IDX-04 | feat/idx-05-fts5-search-index | docs/sprints/handoffs/IDX-05-HANDOFF.md |
| SHR-08 | Share CSP, Sanitization & Rate Limits | DONE | SHR-03, MD-02 | security/shr-08-share-csp-sanitization-rate-limits (b404eb5) | docs/sprints/handoffs/SHR-08-HANDOFF.md |
| GR-01 | Relation Store & Extraction | DONE | MD-03, FND-03 | feat/gr-01-relation-store-extraction | docs/sprints/handoffs/GR-01-HANDOFF.md |
| MD-04 | Wikilink Resolution & Backlink Inputs | DONE | MD-03 | feat/md-04-wikilink-resolution-backlink-inputs (b6ace97) | docs/sprints/handoffs/MD-04-HANDOFF.md |
| PDF-02 | Batch Separate PDF Export | DONE | PDF-01, FND-04 | feat/pdf-02-batch-separate-pdf-export (a978e51) | docs/sprints/handoffs/PDF-02-HANDOFF.md |
| DOCX-03 | Strict DOCX Text Mutation | DONE | DOCX-02 | feat/docx-03-strict-docx-text-mutation (d58d337) | docs/sprints/handoffs/DOCX-03-HANDOFF.md |
| CAN-04 | Folder Picker & File Release | DONE | CAN-02, CAN-03 | feat/can-04-folder-picker-file-release (7c32a2d) | docs/sprints/handoffs/CAN-04-HANDOFF.md |
| WORD-04 | High-Fidelity Quick Look | DONE | WORD-03, DOCX-04 | feat/word-04-high-fidelity-quick-look (548ad8f) | docs/sprints/handoffs/WORD-04-HANDOFF.md |
| IDX-06 | Incremental Index Scheduler | DONE | IDX-05, FS-03 | feat/idx-06-incremental-index-scheduler | docs/sprints/handoffs/IDX-06-HANDOFF.md |
| SRCH-01 | Structured Query Parser | DONE | IDX-05 | feat/srch-01-structured-query-parser | docs/sprints/handoffs/SRCH-01-HANDOFF.md |
| QA-05 | Public Share Security Audit | DONE | SHR-08, SHR-10 | security/qa-05-public-share-security-audit | docs/sprints/handoffs/QA-05-HANDOFF.md |
| GR-02 | Backlinks & Reference Inspector | DONE | GR-01, MD-04 | feat/gr-02-backlinks-reference-inspector (c192481) | docs/sprints/handoffs/GR-02-HANDOFF.md |
| PDF-03 | Streaming Ordered PDF Merge | DONE | PDF-02 | perf/pdf-03-streaming-ordered-pdf-merge | docs/sprints/handoffs/PDF-03-HANDOFF.md |
| BAT-01 | Batch Operation Plan Domain | DONE | FND-03, DOCX-03 | feat/bat-01-batch-operation-plan-domain (3da7d3f) | docs/sprints/handoffs/BAT-01-HANDOFF.md |
| QA-01 | Golden DOCX Regression Corpus | DONE | DOCX-03 | test/qa-01-golden-docx-regression-corpus (71f2975) | docs/sprints/handoffs/QA-01-HANDOFF.md |
| CAN-05 | Spatially Indexed Assisted Placement | DONE | CAN-04 | perf/can-05-spatially-indexed-assisted-placement | docs/sprints/handoffs/CAN-05-HANDOFF.md |
| SHR-04 | Snapshot Share | DONE | SHR-03, WORD-04, MD-02 | security/shr-04-snapshot-share | docs/sprints/handoffs/SHR-04-HANDOFF.md |
| AI-01 | Embedding Provider Abstraction | DONE | FND-05, IDX-06 | feat/ai-01-embedding-provider-abstraction | docs/sprints/handoffs/AI-01-HANDOFF.md |
| QA-02 | Filesystem & Indexing Soak | DONE | FS-08, IDX-06 | test/qa-02-filesystem-indexing-soak (5c4e064) | docs/sprints/handoffs/QA-02-HANDOFF.md |
| SRCH-02 | Exact Ranked Search | DONE | SRCH-01 | feat/srch-02-exact-ranked-search | docs/sprints/handoffs/SRCH-02-HANDOFF.md |
| CTX-01 | Context Pack Core | DONE | MD-03, GR-02 | feat/ctx-01-context-pack-core | docs/sprints/handoffs/CTX-01-HANDOFF.md |
| GR-03 | Graph View | DONE | GR-02, CAN-01 | feat/gr-03-graph-view | docs/sprints/handoffs/GR-03-HANDOFF.md |
| CAN-06 | Position Persistence & Pinning | DONE | CAN-05, FS-05 | feat/can-06-position-persistence-pinning | docs/sprints/handoffs/CAN-06-HANDOFF.md |
| SHR-05 | Membership-Safe Live Share | DONE | SHR-04, FS-03 | security/shr-05-membership-safe-live-share | docs/sprints/handoffs/SHR-05-HANDOFF.md |
| SHR-07 | Share Download Permissions | DONE | SHR-04, PDF-01 | security/shr-07-share-download-permissions | docs/sprints/handoffs/SHR-07-HANDOFF.md |
| AI-02 | Local Embedding Provider | DONE | AI-01 | feat/ai-02-local-embedding-provider (d28bdb7) | docs/sprints/handoffs/AI-02-HANDOFF.md |
| SRCH-03 | Metadata Filters | DONE | SRCH-02 | feat/srch-03-metadata-filters | docs/sprints/handoffs/SRCH-03-HANDOFF.md |
| SRCH-04 | Fuzzy Exploratory Search | DONE | SRCH-02 | feat/srch-04-fuzzy-exploratory-search | docs/sprints/handoffs/SRCH-04-HANDOFF.md |
| CTX-02 | Explicit Dependency Expansion | DONE | CTX-01, GR-02 | feat/ctx-02-explicit-dependency-expansion (8aa5e85) | docs/sprints/handoffs/CTX-02-HANDOFF.md |
| CTX-03 | DOCX Context Representation | DONE | CTX-01, IDX-02 | feat/ctx-03-docx-context-representation (0cd43a4) | docs/sprints/handoffs/CTX-03-HANDOFF.md |
| GR-04 | Manual Relations | DONE | GR-01, GR-03 | feat/gr-04-manual-relations | docs/sprints/handoffs/GR-04-HANDOFF.md |
| GR-05 | Bounded Graph Rendering | DONE | GR-03 | perf/gr-05-bounded-graph-rendering | docs/sprints/handoffs/GR-05-HANDOFF.md |
| CAN-07 | Multi-Select, Group Drag & Tidy | DONE | CAN-06 | feat/can-07-multi-select-group-drag-tidy | docs/sprints/handoffs/CAN-07-HANDOFF.md |
| SHR-06 | Share-Scoped Search & Graph | DONE | SHR-05, SRCH-02, GR-03 | feat/shr-06-share-scoped-search-graph | docs/sprints/handoffs/SHR-06-HANDOFF.md |
| SHR-11 | Owner Share Management UI | DONE | SHR-05, SHR-07, SHR-09 | feat/shr-11-owner-share-management-ui | docs/sprints/handoffs/SHR-11-HANDOFF.md |
| SRCH-05 | Realtime Search UI & Selection | DONE | SRCH-03, SRCH-04, FND-07 | feat/srch-05-realtime-search-ui-selection | docs/sprints/handoffs/SRCH-05-HANDOFF.md |
| CTX-04 | Token Estimate & Context Export | DONE | CTX-02, CTX-03 | feat/ctx-04-token-estimate-context-export | docs/sprints/handoffs/CTX-04-HANDOFF.md |
| AI-04 | Semantic Graph Suggestions | DONE | AI-02, GR-05 | feat/ai-04-semantic-graph-suggestions | docs/sprints/handoffs/AI-04-HANDOFF.md |
| AI-03 | Semantic Search Mode | DONE | AI-02, SRCH-05 | feat/ai-03-semantic-search-mode | docs/sprints/handoffs/AI-03-HANDOFF.md |
| BAT-02 | Batch Scope Resolver | DONE | FS-01, SRCH-05, BAT-01 | feat/bat-02-batch-scope-resolver (60593a1) | docs/sprints/handoffs/BAT-02-HANDOFF.md |
| CAN-08 | List View & View Switch | DONE | CAN-03, SRCH-05 | feat/can-08-list-view-view-switch (0203af8) | docs/sprints/handoffs/CAN-08-HANDOFF.md |
| QA-06 | Performance Benchmark Gate | DONE | CAN-07, SRCH-05, GR-05, PDF-03 | perf/qa-06-performance-benchmark-gate | docs/sprints/handoffs/QA-06-HANDOFF.md |
| BAT-03 | Batch Preview API & Review Shell | DONE | BAT-01, BAT-02 | feat/bat-03-batch-preview-api-review-shell (4899761) | docs/sprints/handoffs/BAT-03-HANDOFF.md |
| PDF-04 | PDF Finalization UX | DONE | CAN-08, PDF-03 | feat/pdf-04-pdf-finalization-ux | docs/sprints/handoffs/PDF-04-HANDOFF.md |
| BAT-04 | Transactional Safe Apply Engine | DONE | BAT-03, HIST-01, FS-07 | fix/bat-04-transactional-safe-apply-engine (02f236e) | docs/sprints/handoffs/BAT-04-HANDOFF.md |
| BAT-05 | Exact Search & Replace Operation | DONE | BAT-04, DOCX-03 | feat/bat-05-exact-search-replace-operation (9363e7b) | docs/sprints/handoffs/BAT-05-HANDOFF.md |
| BAT-08 | Strict Font & Font-Size Operations | DONE | BAT-04, DOCX-03 | feat/bat-08-strict-font-font-size-operations (2f54e71) | docs/sprints/handoffs/BAT-08-HANDOFF.md |
| BAT-09 | Strict Page-Layout Operations | DONE | BAT-04, DOCX-03 | feat/bat-09-strict-page-layout-operations (c342760) | docs/sprints/handoffs/BAT-09-HANDOFF.md |
| BAT-10 | Strict Batch Rename | DONE | BAT-04, FS-07 | security/bat-10-strict-batch-rename (26dd186) | docs/sprints/handoffs/BAT-10-HANDOFF.md |
| HIST-02 | Conflict-Aware Undo | DONE | HIST-01, BAT-04, FS-03 | fix/hist-02-conflict-aware-undo (c9e8ca4) | docs/sprints/handoffs/HIST-02-HANDOFF.md |
| BAT-06 | Occurrence Review UX | DONE | BAT-05 | feat/bat-06-occurrence-review-ux (2bda93a) | docs/sprints/handoffs/BAT-06-HANDOFF.md |
| BAT-11 | Batch Results & History UI | DONE | BAT-10, HIST-02 | feat/bat-11-batch-results-history-ui (594ba5e) | docs/sprints/handoffs/BAT-11-HANDOFF.md |
| QA-04 | Batch Safety Regression Suite | DONE | BAT-10, HIST-02 | fix/qa-04-batch-safety-regression-suite (188fa48) | docs/sprints/handoffs/QA-04-HANDOFF.md |
| QA-07 | Crash & Interrupted-Operation Recovery | DONE | FND-04, HIST-02 | fix/qa-07-crash-interrupted-operation-recovery (536d8a4) | docs/sprints/handoffs/QA-07-HANDOFF.md |
| BAT-07 | Regex Advanced Replace | DONE | BAT-06 | fix/bat-07-regex-advanced-replace (f8e220f) | docs/sprints/handoffs/BAT-07-HANDOFF.md |
| REL-02 | Release Verification Pipeline | DONE | REL-01, QA-01, QA-02, QA-04, QA-05, QA-06, QA-07 | chore/rel-02-release-verification-pipeline | docs/sprints/handoffs/REL-02-HANDOFF.md |
| REL-03 | V1 Readiness Gate | REVIEW | REL-02, SHR-11, CTX-04, AI-04, PDF-04 | chore/rel-03-v1-readiness-gate | docs/sprints/handoffs/REL-03-HANDOFF.md |
| SCM-01 | Git Capability & Controlled Process Runner | READY | FND-03, FND-07, FS-02 |  |  |
| SCM-02 | Git Repository, Worktree & Status Domain | BLOCKED | SCM-01, FS-03 |  |  |
| SCM-03 | Source Control Changes Pane | BLOCKED | SCM-02, FND-07 |  |  |
| SCM-04 | Markdown Git Diff | BLOCKED | SCM-02, MD-01 |  |  |
| SCM-05 | DOCX Document-Aware Git Diff | BLOCKED | SCM-02, IDX-02, DOCX-01 |  |  |
| SCM-06 | Git Stage & Unstage | BLOCKED | SCM-02, SCM-03 |  |  |
| SCM-07 | Git Commit & Commit All | BLOCKED | SCM-06, SCM-05 |  |  |
| SCM-08 | Git Commit History & File History | BLOCKED | SCM-02, SCM-07 |  |  |
| SCM-09 | Document IDE History ↔ Git Correlation | BLOCKED | SCM-07, HIST-01 |  |  |
| SCM-10 | Safe Git Discard & Restore | BLOCKED | SCM-08, HIST-01, FS-07 |  |  |
| SCM-11 | Git Remote State & Fetch | BLOCKED | SCM-02, SCM-07 |  |  |
| SCM-12 | Safe Fast-Forward Pull | BLOCKED | SCM-11, SCM-10 |  |  |
| SCM-13 | Explicit Git Push | BLOCKED | SCM-11, SCM-07 |  |  |
| SCM-14 | Git Conflict State & Resolution Shell | BLOCKED | SCM-12, SCM-05 |  |  |
| SCM-15 | Git Worktree Discovery & Open Existing | BLOCKED | SCM-01, FS-03, FND-07 |  |  |
| SCM-16 | Safe Git Worktree Create & Remove | BLOCKED | SCM-15, FS-07 |  |  |
| SCM-17 | Source Control Regression & Release Hardening | BLOCKED | SCM-04, SCM-05, SCM-09, SCM-12, SCM-13, SCM-14, SCM-16, QA-02 |  |  |

**REL-03 review notes (2026-10-16) — product verdict BLOCKED.** `docs/quality/v1-readiness-report.md` maps all 22 spec-12 completion gates; 16 PASS, 6 FAIL. Blockers: (1) graph view is a placeholder (GR-03 steps 1/3/4/5 unmet, §27.2/§89 not deliverable); (2) batch Apply unreachable (`BatchPreviewPanel.tsx:147` disabled; no apply/undo route); (3) `/api/word/open` 404 in the shipped desktop host (`BatchPreviewHostTests.cs:91`); (4) no `/api/search` endpoint + search UI unmounted; (5) share lifecycle unwired (SHR-11 callback deferral, no create/revoke endpoints); (6) workspace/canvas bootstrap not wired (canvas never populated outside tests); (7) Markdown editor is a stub and unmounted; (8) E2E administrative workflow test missing (spec 11 §20/§27). Integration sprints are required before V1; gap list G1–G9 with file:line evidence is in the report and handoff.
