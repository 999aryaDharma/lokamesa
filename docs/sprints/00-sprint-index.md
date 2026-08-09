# Sprint Index

**Total implementation sprints:** 98

## Operating rule

Implement one sprint at a time. A sprint becomes READY only when every dependency is DONE.

Initial ready queue:

- `FND-01` — Repository Bootstrap

## Sprint table

| Sprint | Title | Priority | Area | Depends On | Initial State |
|---|---|---:|---|---|---|
| `FND-01` | Repository Bootstrap | P0 | foundation | — | READY |
| `FND-02` | Privileged Local API Security | P0 | foundation | `FND-01` | BLOCKED |
| `FND-03` | SQLite Persistence Foundation | P0 | foundation | `FND-01` | BLOCKED |
| `QA-03` | Privileged Local API Security Audit | P0 | quality | `FND-02` | BLOCKED |
| `FND-04` | Background Job Runtime | P0 | foundation | `FND-03` | BLOCKED |
| `FND-05` | Settings & Secret Storage | P1 | foundation | `FND-02`, `FND-03` | BLOCKED |
| `FND-06` | Structured Logging & Diagnostics | P1 | foundation | `FND-01`, `FND-03` | BLOCKED |
| `SHR-01` | Share Domain & Sanitized Manifest | P0 | sharing | `FND-03` | BLOCKED |
| `FND-07` | Desktop/WebView Production Host | P0 | foundation | `FND-02`, `FND-05` | BLOCKED |
| `CAN-01` | Canvas Foundation | P0 | canvas | `FND-07`, `FND-03` | BLOCKED |
| `FND-08` | Build, Verify & Artifact Scripts | P0 | foundation | `FND-01`, `FND-07` | BLOCKED |
| `FS-01` | Folder Source Attachment | P1 | filesystem | `FND-03`, `FND-07` | BLOCKED |
| `REL-01` | Windows Installer Packaging | P0 | release | `FND-08`, `QA-03` | BLOCKED |
| `SHR-02` | Separate ShareHost Process | P0 | sharing | `SHR-01`, `FND-08` | BLOCKED |
| `CAN-02` | Folder Canvas Object | P1 | canvas | `FS-01`, `CAN-01` | BLOCKED |
| `FS-02` | Stable Document Identity | P0 | filesystem | `FND-03`, `FS-01` | BLOCKED |
| `SHR-03` | Share Token Authentication | P0 | sharing | `SHR-02`, `FND-05` | BLOCKED |
| `SHR-09` | ngrok Transport | P1 | sharing | `SHR-02`, `FND-05` | BLOCKED |
| `SHR-10` | ShareHost OS Isolation Spike | P1 | sharing | `SHR-02` | BLOCKED |
| `FS-03` | Runtime File Watcher | P0 | filesystem | `FND-04`, `FS-02` | BLOCKED |
| `FS-07` | Explicit Physical File Operations | P0 | filesystem | `FS-02`, `FND-02` | BLOCKED |
| `HIST-01` | Recovery Snapshot Storage | P0 | history | `FND-03`, `FS-02` | BLOCKED |
| `IDX-01` | DOCX Package Resource Preflight | P0 | indexing | `FND-04`, `FS-02` | BLOCKED |
| `IDX-03` | Markdown Structural Parser | P1 | indexing | `FND-04`, `FS-02` | BLOCKED |
| `WORD-01` | Open DOCX in Microsoft Word | P1 | word | `FND-07`, `FS-02` | BLOCKED |
| `FS-04` | Hybrid Startup Reconciliation | P0 | filesystem | `FS-02`, `FS-03` | BLOCKED |
| `FS-05` | Rename & Move Correlation | P1 | filesystem | `FS-02`, `FS-03` | BLOCKED |
| `FS-08` | Reparse Point & Source Containment | P0 | filesystem | `FS-01`, `FS-07` | BLOCKED |
| `IDX-02` | DOCX Structural Parser | P0 | indexing | `IDX-01` | BLOCKED |
| `MD-01` | Source-First Markdown Editor | P1 | markdown | `FND-07`, `IDX-03` | BLOCKED |
| `WORD-02` | Word Editing/Lock Detection | P0 | word | `WORD-01`, `FS-03` | BLOCKED |
| `WORD-03` | Dedicated STA Word Automation Queue | P0 | word | `FND-04`, `WORD-01` | BLOCKED |
| `FS-06` | Missing File & Folder Recovery | P1 | filesystem | `FS-04` | BLOCKED |
| `DOCX-01` | Logical DOCX Text/Run Map | P0 | docx | `IDX-02` | BLOCKED |
| `IDX-04` | Metadata Persistence | P1 | indexing | `IDX-02`, `IDX-03`, `FND-03` | BLOCKED |
| `MD-02` | Markdown Preview & Sanitization | P0 | markdown | `MD-01` | BLOCKED |
| `MD-03` | Frontmatter & Explicit Link Index | P1 | markdown | `IDX-03`, `MD-01` | BLOCKED |
| `PDF-01` | Single DOCX PDF Export | P0 | pdf | `WORD-03` | BLOCKED |
| `DOCX-02` | Logical Search Across Runs | P0 | docx | `DOCX-01` | BLOCKED |
| `CAN-03` | Document Paper Object & Thumbnail Shell | P1 | canvas | `IDX-04`, `CAN-01` | BLOCKED |
| `DOCX-04` | Document Lens Metadata | P1 | docx | `IDX-04` | BLOCKED |
| `IDX-05` | FTS5 Search Index | P0 | indexing | `IDX-04` | BLOCKED |
| `SHR-08` | Share CSP, Sanitization & Rate Limits | P0 | sharing | `SHR-03`, `MD-02` | BLOCKED |
| `GR-01` | Relation Store & Extraction | P1 | graph | `MD-03`, `FND-03` | BLOCKED |
| `MD-04` | Wikilink Resolution & Backlink Inputs | P1 | markdown | `MD-03` | BLOCKED |
| `PDF-02` | Batch Separate PDF Export | P1 | pdf | `PDF-01`, `FND-04` | BLOCKED |
| `DOCX-03` | Strict DOCX Text Mutation | P0 | docx | `DOCX-02` | BLOCKED |
| `CAN-04` | Folder Picker & File Release | P1 | canvas | `CAN-02`, `CAN-03` | BLOCKED |
| `WORD-04` | High-Fidelity Quick Look | P1 | word | `WORD-03`, `DOCX-04` | BLOCKED |
| `IDX-06` | Incremental Index Scheduler | P0 | indexing | `IDX-05`, `FS-03` | BLOCKED |
| `SRCH-01` | Structured Query Parser | P0 | search | `IDX-05` | BLOCKED |
| `QA-05` | Public Share Security Audit | P0 | quality | `SHR-08`, `SHR-10` | BLOCKED |
| `GR-02` | Backlinks & Reference Inspector | P1 | graph | `GR-01`, `MD-04` | BLOCKED |
| `PDF-03` | Streaming Ordered PDF Merge | P1 | pdf | `PDF-02` | BLOCKED |
| `BAT-01` | Batch Operation Plan Domain | P0 | batch | `FND-03`, `DOCX-03` | BLOCKED |
| `QA-01` | Golden DOCX Regression Corpus | P0 | quality | `DOCX-03` | BLOCKED |
| `CAN-05` | Spatially Indexed Assisted Placement | P0 | canvas | `CAN-04` | BLOCKED |
| `SHR-04` | Snapshot Share | P0 | sharing | `SHR-03`, `WORD-04`, `MD-02` | BLOCKED |
| `AI-01` | Embedding Provider Abstraction | P1 | semantic | `FND-05`, `IDX-06` | BLOCKED |
| `QA-02` | Filesystem & Indexing Soak | P1 | quality | `FS-08`, `IDX-06` | BLOCKED |
| `SRCH-02` | Exact Ranked Search | P0 | search | `SRCH-01` | BLOCKED |
| `CTX-01` | Context Pack Core | P1 | context-packs | `MD-03`, `GR-02` | BLOCKED |
| `GR-03` | Graph View | P1 | graph | `GR-02`, `CAN-01` | BLOCKED |
| `CAN-06` | Position Persistence & Pinning | P1 | canvas | `CAN-05`, `FS-05` | BLOCKED |
| `SHR-05` | Membership-Safe Live Share | P0 | sharing | `SHR-04`, `FS-03` | BLOCKED |
| `SHR-07` | Share Download Permissions | P0 | sharing | `SHR-04`, `PDF-01` | BLOCKED |
| `AI-02` | Local Embedding Provider | P1 | semantic | `AI-01` | BLOCKED |
| `SRCH-03` | Metadata Filters | P1 | search | `SRCH-02` | BLOCKED |
| `SRCH-04` | Fuzzy Exploratory Search | P1 | search | `SRCH-02` | BLOCKED |
| `CTX-02` | Explicit Dependency Expansion | P1 | context-packs | `CTX-01`, `GR-02` | BLOCKED |
| `CTX-03` | DOCX Context Representation | P1 | context-packs | `CTX-01`, `IDX-02` | BLOCKED |
| `GR-04` | Manual Relations | P1 | graph | `GR-01`, `GR-03` | BLOCKED |
| `GR-05` | Bounded Graph Rendering | P1 | graph | `GR-03` | BLOCKED |
| `CAN-07` | Multi-Select, Group Drag & Tidy | P1 | canvas | `CAN-06` | BLOCKED |
| `SHR-06` | Share-Scoped Search & Graph | P1 | sharing | `SHR-05`, `SRCH-02`, `GR-03` | BLOCKED |
| `SHR-11` | Owner Share Management UI | P0 | sharing | `SHR-05`, `SHR-07`, `SHR-09` | BLOCKED |
| `SRCH-05` | Realtime Search UI & Selection | P0 | search | `SRCH-03`, `SRCH-04`, `FND-07` | BLOCKED |
| `CTX-04` | Token Estimate & Context Export | P1 | context-packs | `CTX-02`, `CTX-03` | BLOCKED |
| `AI-04` | Semantic Graph Suggestions | P1 | semantic | `AI-02`, `GR-05` | BLOCKED |
| `AI-03` | Semantic Search Mode | P1 | semantic | `AI-02`, `SRCH-05` | BLOCKED |
| `BAT-02` | Batch Scope Resolver | P0 | batch | `FS-01`, `SRCH-05`, `BAT-01` | BLOCKED |
| `CAN-08` | List View & View Switch | P0 | canvas | `CAN-03`, `SRCH-05` | BLOCKED |
| `QA-06` | Performance Benchmark Gate | P1 | quality | `CAN-07`, `SRCH-05`, `GR-05`, `PDF-03` | BLOCKED |
| `BAT-03` | Batch Preview API & Review Shell | P0 | batch | `BAT-01`, `BAT-02` | BLOCKED |
| `PDF-04` | PDF Finalization UX | P1 | pdf | `CAN-08`, `PDF-03` | BLOCKED |
| `BAT-04` | Transactional Safe Apply Engine | P0 | batch | `BAT-03`, `HIST-01`, `FS-07` | BLOCKED |
| `BAT-05` | Exact Search & Replace Operation | P0 | batch | `BAT-04`, `DOCX-03` | BLOCKED |
| `BAT-08` | Strict Font & Font-Size Operations | P1 | batch | `BAT-04`, `DOCX-03` | BLOCKED |
| `BAT-09` | Strict Page-Layout Operations | P1 | batch | `BAT-04`, `DOCX-03` | BLOCKED |
| `BAT-10` | Strict Batch Rename | P0 | batch | `BAT-04`, `FS-07` | BLOCKED |
| `HIST-02` | Conflict-Aware Undo | P0 | history | `HIST-01`, `BAT-04`, `FS-03` | BLOCKED |
| `BAT-06` | Occurrence Review UX | P1 | batch | `BAT-05` | BLOCKED |
| `BAT-11` | Batch Results & History UI | P1 | batch | `BAT-10`, `HIST-02` | BLOCKED |
| `QA-04` | Batch Safety Regression Suite | P0 | quality | `BAT-10`, `HIST-02` | BLOCKED |
| `QA-07` | Crash & Interrupted-Operation Recovery | P0 | quality | `FND-04`, `HIST-02` | BLOCKED |
| `BAT-07` | Regex Advanced Replace | P1 | batch | `BAT-06` | BLOCKED |
| `REL-02` | Release Verification Pipeline | P0 | release | `REL-01`, `QA-01`, `QA-02`, `QA-04`, `QA-05`, `QA-06`, `QA-07` | BLOCKED |
| `REL-03` | V1 Readiness Gate | P0 | release | `REL-02`, `SHR-11`, `CTX-04`, `AI-04`, `PDF-04` | BLOCKED |

## Recommended usage

1. Open `SPRINT-STATUS.md`.
2. Pick the highest-priority READY sprint.
3. Open its sprint file.
4. Use its embedded agent prompt.
5. Review and mark DONE only after Definition of Done.
