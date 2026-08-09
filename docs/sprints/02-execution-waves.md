# Dependency-Ready Execution Waves

These are dependency layers, not Scrum timeboxes. Within one wave, sprints may be parallelized only when they do not modify conflicting shared code and each agent uses an isolated branch/worktree.

## Wave 0

- `FND-01` — Repository Bootstrap
## Wave 1

- `FND-02` — Privileged Local API Security
- `FND-03` — SQLite Persistence Foundation
## Wave 2

- `QA-03` — Privileged Local API Security Audit
- `FND-04` — Background Job Runtime
- `FND-05` — Settings & Secret Storage
- `FND-06` — Structured Logging & Diagnostics
- `SHR-01` — Share Domain & Sanitized Manifest
## Wave 3

- `FND-07` — Desktop/WebView Production Host
## Wave 4

- `CAN-01` — Canvas Foundation
- `FND-08` — Build, Verify & Artifact Scripts
- `FS-01` — Folder Source Attachment
## Wave 5

- `REL-01` — Windows Installer Packaging
- `SHR-02` — Separate ShareHost Process
- `CAN-02` — Folder Canvas Object
- `FS-02` — Stable Document Identity
## Wave 6

- `SHR-03` — Share Token Authentication
- `SHR-09` — ngrok Transport
- `SHR-10` — ShareHost OS Isolation Spike
- `FS-03` — Runtime File Watcher
- `FS-07` — Explicit Physical File Operations
- `HIST-01` — Recovery Snapshot Storage
- `IDX-01` — DOCX Package Resource Preflight
- `IDX-03` — Markdown Structural Parser
- `WORD-01` — Open DOCX in Microsoft Word
## Wave 7

- `FS-04` — Hybrid Startup Reconciliation
- `FS-05` — Rename & Move Correlation
- `FS-08` — Reparse Point & Source Containment
- `IDX-02` — DOCX Structural Parser
- `MD-01` — Source-First Markdown Editor
- `WORD-02` — Word Editing/Lock Detection
- `WORD-03` — Dedicated STA Word Automation Queue
## Wave 8

- `FS-06` — Missing File & Folder Recovery
- `DOCX-01` — Logical DOCX Text/Run Map
- `IDX-04` — Metadata Persistence
- `MD-02` — Markdown Preview & Sanitization
- `MD-03` — Frontmatter & Explicit Link Index
- `PDF-01` — Single DOCX PDF Export
## Wave 9

- `DOCX-02` — Logical Search Across Runs
- `CAN-03` — Document Paper Object & Thumbnail Shell
- `DOCX-04` — Document Lens Metadata
- `IDX-05` — FTS5 Search Index
- `SHR-08` — Share CSP, Sanitization & Rate Limits
- `GR-01` — Relation Store & Extraction
- `MD-04` — Wikilink Resolution & Backlink Inputs
- `PDF-02` — Batch Separate PDF Export
## Wave 10

- `DOCX-03` — Strict DOCX Text Mutation
- `CAN-04` — Folder Picker & File Release
- `WORD-04` — High-Fidelity Quick Look
- `IDX-06` — Incremental Index Scheduler
- `SRCH-01` — Structured Query Parser
- `QA-05` — Public Share Security Audit
- `GR-02` — Backlinks & Reference Inspector
- `PDF-03` — Streaming Ordered PDF Merge
## Wave 11

- `BAT-01` — Batch Operation Plan Domain
- `QA-01` — Golden DOCX Regression Corpus
- `CAN-05` — Spatially Indexed Assisted Placement
- `SHR-04` — Snapshot Share
- `AI-01` — Embedding Provider Abstraction
- `QA-02` — Filesystem & Indexing Soak
- `SRCH-02` — Exact Ranked Search
- `CTX-01` — Context Pack Core
- `GR-03` — Graph View
## Wave 12

- `CAN-06` — Position Persistence & Pinning
- `SHR-05` — Membership-Safe Live Share
- `SHR-07` — Share Download Permissions
- `AI-02` — Local Embedding Provider
- `SRCH-03` — Metadata Filters
- `SRCH-04` — Fuzzy Exploratory Search
- `CTX-02` — Explicit Dependency Expansion
- `CTX-03` — DOCX Context Representation
- `GR-04` — Manual Relations
- `GR-05` — Bounded Graph Rendering
## Wave 13

- `CAN-07` — Multi-Select, Group Drag & Tidy
- `SHR-06` — Share-Scoped Search & Graph
- `SHR-11` — Owner Share Management UI
- `SRCH-05` — Realtime Search UI & Selection
- `CTX-04` — Token Estimate & Context Export
- `AI-04` — Semantic Graph Suggestions
## Wave 14

- `AI-03` — Semantic Search Mode
- `BAT-02` — Batch Scope Resolver
- `CAN-08` — List View & View Switch
- `QA-06` — Performance Benchmark Gate
## Wave 15

- `BAT-03` — Batch Preview API & Review Shell
- `PDF-04` — PDF Finalization UX
## Wave 16

- `BAT-04` — Transactional Safe Apply Engine
## Wave 17

- `BAT-05` — Exact Search & Replace Operation
- `BAT-08` — Strict Font & Font-Size Operations
- `BAT-09` — Strict Page-Layout Operations
- `BAT-10` — Strict Batch Rename
- `HIST-02` — Conflict-Aware Undo
## Wave 18

- `BAT-06` — Occurrence Review UX
- `BAT-11` — Batch Results & History UI
- `QA-04` — Batch Safety Regression Suite
- `QA-07` — Crash & Interrupted-Operation Recovery
## Wave 19

- `BAT-07` — Regex Advanced Replace
- `REL-02` — Release Verification Pipeline
## Wave 20

- `REL-03` — V1 Readiness Gate

## Parallelism rule

Before dispatching two READY sprints in parallel:
- check they share no unfinished dependency,
- inspect their Planned Files,
- avoid simultaneous edits to the same subsystem boundary unless coordinated,
- merge/rebase and rerun both affected suites after integration.
