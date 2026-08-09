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
| QA-03 | Privileged Local API Security Audit | READY | FND-02 |  |  |
| FND-04 | Background Job Runtime | DONE | FND-03 | feat/fnd-04-background-job-runtime (065e335) | docs/sprints/handoffs/FND-04-HANDOFF.md |
| FND-05 | Settings & Secret Storage | DONE | FND-02, FND-03 | security/fnd-05-settings-secret-storage (87de9be) | docs/sprints/handoffs/FND-05-HANDOFF.md |
| FND-06 | Structured Logging & Diagnostics | READY | FND-01, FND-03 |  |  |
| SHR-01 | Share Domain & Sanitized Manifest | READY | FND-03 |  |  |
| FND-07 | Desktop/WebView Production Host | READY | FND-02, FND-05 |  |  |
| CAN-01 | Canvas Foundation | BLOCKED | FND-07, FND-03 |  |  |
| FND-08 | Build, Verify & Artifact Scripts | BLOCKED | FND-01, FND-07 |  |  |
| FS-01 | Folder Source Attachment | BLOCKED | FND-03, FND-07 |  |  |
| REL-01 | Windows Installer Packaging | BLOCKED | FND-08, QA-03 |  |  |
| SHR-02 | Separate ShareHost Process | BLOCKED | SHR-01, FND-08 |  |  |
| CAN-02 | Folder Canvas Object | BLOCKED | FS-01, CAN-01 |  |  |
| FS-02 | Stable Document Identity | BLOCKED | FND-03, FS-01 |  |  |
| SHR-03 | Share Token Authentication | BLOCKED | SHR-02, FND-05 |  |  |
| SHR-09 | ngrok Transport | BLOCKED | SHR-02, FND-05 |  |  |
| SHR-10 | ShareHost OS Isolation Spike | BLOCKED | SHR-02 |  |  |
| FS-03 | Runtime File Watcher | BLOCKED | FND-04, FS-02 |  |  |
| FS-07 | Explicit Physical File Operations | BLOCKED | FS-02, FND-02 |  |  |
| HIST-01 | Recovery Snapshot Storage | BLOCKED | FND-03, FS-02 |  |  |
| IDX-01 | DOCX Package Resource Preflight | BLOCKED | FND-04, FS-02 |  |  |
| IDX-03 | Markdown Structural Parser | BLOCKED | FND-04, FS-02 |  |  |
| WORD-01 | Open DOCX in Microsoft Word | BLOCKED | FND-07, FS-02 |  |  |
| FS-04 | Hybrid Startup Reconciliation | BLOCKED | FS-02, FS-03 |  |  |
| FS-05 | Rename & Move Correlation | BLOCKED | FS-02, FS-03 |  |  |
| FS-08 | Reparse Point & Source Containment | BLOCKED | FS-01, FS-07 |  |  |
| IDX-02 | DOCX Structural Parser | BLOCKED | IDX-01 |  |  |
| MD-01 | Source-First Markdown Editor | BLOCKED | FND-07, IDX-03 |  |  |
| WORD-02 | Word Editing/Lock Detection | BLOCKED | WORD-01, FS-03 |  |  |
| WORD-03 | Dedicated STA Word Automation Queue | BLOCKED | FND-04, WORD-01 |  |  |
| FS-06 | Missing File & Folder Recovery | BLOCKED | FS-04 |  |  |
| DOCX-01 | Logical DOCX Text/Run Map | BLOCKED | IDX-02 |  |  |
| IDX-04 | Metadata Persistence | BLOCKED | IDX-02, IDX-03, FND-03 |  |  |
| MD-02 | Markdown Preview & Sanitization | BLOCKED | MD-01 |  |  |
| MD-03 | Frontmatter & Explicit Link Index | BLOCKED | IDX-03, MD-01 |  |  |
| PDF-01 | Single DOCX PDF Export | BLOCKED | WORD-03 |  |  |
| DOCX-02 | Logical Search Across Runs | BLOCKED | DOCX-01 |  |  |
| CAN-03 | Document Paper Object & Thumbnail Shell | BLOCKED | IDX-04, CAN-01 |  |  |
| DOCX-04 | Document Lens Metadata | BLOCKED | IDX-04 |  |  |
| IDX-05 | FTS5 Search Index | BLOCKED | IDX-04 |  |  |
| SHR-08 | Share CSP, Sanitization & Rate Limits | BLOCKED | SHR-03, MD-02 |  |  |
| GR-01 | Relation Store & Extraction | BLOCKED | MD-03, FND-03 |  |  |
| MD-04 | Wikilink Resolution & Backlink Inputs | BLOCKED | MD-03 |  |  |
| PDF-02 | Batch Separate PDF Export | BLOCKED | PDF-01, FND-04 |  |  |
| DOCX-03 | Strict DOCX Text Mutation | BLOCKED | DOCX-02 |  |  |
| CAN-04 | Folder Picker & File Release | BLOCKED | CAN-02, CAN-03 |  |  |
| WORD-04 | High-Fidelity Quick Look | BLOCKED | WORD-03, DOCX-04 |  |  |
| IDX-06 | Incremental Index Scheduler | BLOCKED | IDX-05, FS-03 |  |  |
| SRCH-01 | Structured Query Parser | BLOCKED | IDX-05 |  |  |
| QA-05 | Public Share Security Audit | BLOCKED | SHR-08, SHR-10 |  |  |
| GR-02 | Backlinks & Reference Inspector | BLOCKED | GR-01, MD-04 |  |  |
| PDF-03 | Streaming Ordered PDF Merge | BLOCKED | PDF-02 |  |  |
| BAT-01 | Batch Operation Plan Domain | BLOCKED | FND-03, DOCX-03 |  |  |
| QA-01 | Golden DOCX Regression Corpus | BLOCKED | DOCX-03 |  |  |
| CAN-05 | Spatially Indexed Assisted Placement | BLOCKED | CAN-04 |  |  |
| SHR-04 | Snapshot Share | BLOCKED | SHR-03, WORD-04, MD-02 |  |  |
| AI-01 | Embedding Provider Abstraction | BLOCKED | FND-05, IDX-06 |  |  |
| QA-02 | Filesystem & Indexing Soak | BLOCKED | FS-08, IDX-06 |  |  |
| SRCH-02 | Exact Ranked Search | BLOCKED | SRCH-01 |  |  |
| CTX-01 | Context Pack Core | BLOCKED | MD-03, GR-02 |  |  |
| GR-03 | Graph View | BLOCKED | GR-02, CAN-01 |  |  |
| CAN-06 | Position Persistence & Pinning | BLOCKED | CAN-05, FS-05 |  |  |
| SHR-05 | Membership-Safe Live Share | BLOCKED | SHR-04, FS-03 |  |  |
| SHR-07 | Share Download Permissions | BLOCKED | SHR-04, PDF-01 |  |  |
| AI-02 | Local Embedding Provider | BLOCKED | AI-01 |  |  |
| SRCH-03 | Metadata Filters | BLOCKED | SRCH-02 |  |  |
| SRCH-04 | Fuzzy Exploratory Search | BLOCKED | SRCH-02 |  |  |
| CTX-02 | Explicit Dependency Expansion | BLOCKED | CTX-01, GR-02 |  |  |
| CTX-03 | DOCX Context Representation | BLOCKED | CTX-01, IDX-02 |  |  |
| GR-04 | Manual Relations | BLOCKED | GR-01, GR-03 |  |  |
| GR-05 | Bounded Graph Rendering | BLOCKED | GR-03 |  |  |
| CAN-07 | Multi-Select, Group Drag & Tidy | BLOCKED | CAN-06 |  |  |
| SHR-06 | Share-Scoped Search & Graph | BLOCKED | SHR-05, SRCH-02, GR-03 |  |  |
| SHR-11 | Owner Share Management UI | BLOCKED | SHR-05, SHR-07, SHR-09 |  |  |
| SRCH-05 | Realtime Search UI & Selection | BLOCKED | SRCH-03, SRCH-04, FND-07 |  |  |
| CTX-04 | Token Estimate & Context Export | BLOCKED | CTX-02, CTX-03 |  |  |
| AI-04 | Semantic Graph Suggestions | BLOCKED | AI-02, GR-05 |  |  |
| AI-03 | Semantic Search Mode | BLOCKED | AI-02, SRCH-05 |  |  |
| BAT-02 | Batch Scope Resolver | BLOCKED | FS-01, SRCH-05, BAT-01 |  |  |
| CAN-08 | List View & View Switch | BLOCKED | CAN-03, SRCH-05 |  |  |
| QA-06 | Performance Benchmark Gate | BLOCKED | CAN-07, SRCH-05, GR-05, PDF-03 |  |  |
| BAT-03 | Batch Preview API & Review Shell | BLOCKED | BAT-01, BAT-02 |  |  |
| PDF-04 | PDF Finalization UX | BLOCKED | CAN-08, PDF-03 |  |  |
| BAT-04 | Transactional Safe Apply Engine | BLOCKED | BAT-03, HIST-01, FS-07 |  |  |
| BAT-05 | Exact Search & Replace Operation | BLOCKED | BAT-04, DOCX-03 |  |  |
| BAT-08 | Strict Font & Font-Size Operations | BLOCKED | BAT-04, DOCX-03 |  |  |
| BAT-09 | Strict Page-Layout Operations | BLOCKED | BAT-04, DOCX-03 |  |  |
| BAT-10 | Strict Batch Rename | BLOCKED | BAT-04, FS-07 |  |  |
| HIST-02 | Conflict-Aware Undo | BLOCKED | HIST-01, BAT-04, FS-03 |  |  |
| BAT-06 | Occurrence Review UX | BLOCKED | BAT-05 |  |  |
| BAT-11 | Batch Results & History UI | BLOCKED | BAT-10, HIST-02 |  |  |
| QA-04 | Batch Safety Regression Suite | BLOCKED | BAT-10, HIST-02 |  |  |
| QA-07 | Crash & Interrupted-Operation Recovery | BLOCKED | FND-04, HIST-02 |  |  |
| BAT-07 | Regex Advanced Replace | BLOCKED | BAT-06 |  |  |
| REL-02 | Release Verification Pipeline | BLOCKED | REL-01, QA-01, QA-02, QA-04, QA-05, QA-06, QA-07 |  |  |
| REL-03 | V1 Readiness Gate | BLOCKED | REL-02, SHR-11, CTX-04, AI-04, PDF-04 |  |  |
