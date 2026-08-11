# Project: Document IDE

## Architecture
- Subsystem boundaries: Filesystem, History/Recovery, Indexing/Parsers, Word Integration, Security & Sharing, Foundation Log/Diagnostics.
- Local filesystem source of truth, recovery snapshots before edits, Word COM concurrency = 1, privileged API authorization.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | FS-04 | Hybrid Startup Reconciliation | Cluster 1 | ORIGINAL_REQUEST.md |
| 2 | FS-05 | Rename & Move Correlation | Cluster 1 | ORIGINAL_REQUEST.md |
| 3 | FS-07 | Explicit Physical File Operations | Cluster 1 | ORIGINAL_REQUEST.md |
| 4 | HIST-01 | Recovery Snapshot Storage | Cluster 1 | ORIGINAL_REQUEST.md |
| 5 | IDX-01 | DOCX Package Resource Preflight | Cluster 2 | ORIGINAL_REQUEST.md |
| 6 | IDX-03 | Markdown Structural Parser | Cluster 2 | ORIGINAL_REQUEST.md |
| 7 | WORD-01 | Open DOCX in Microsoft Word | Cluster 3 | ORIGINAL_REQUEST.md |
| 8 | SHR-01 | Share Domain & Sanitized Manifest | Cluster 4 | ORIGINAL_REQUEST.md |
| 9 | QA-03 | Privileged Local API Security Audit | Cluster 4 | ORIGINAL_REQUEST.md |
| 10 | FND-06 | Structured Logging & Diagnostics | Cluster 4 | ORIGINAL_REQUEST.md |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| 1 | Cluster 1 (Filesystem & Recovery) | FS-04, FS-05, FS-07, HIST-01 | FS-02, FS-03, FND-02, FND-03 | IN_PROGRESS |
| 2 | Cluster 2 (Indexing & Document Parsers) | IDX-01, IDX-03 | FND-04, FS-02 | IN_PROGRESS |
| 3 | Cluster 3 (Word Integration) | WORD-01 | FND-07, FS-02 | IN_PROGRESS |
| 4 | Cluster 4 (Security, Sharing & Foundation) | SHR-01, QA-03, FND-06 | FND-01, FND-02, FND-03 | IN_PROGRESS |

## Interface Contracts
- Standard API contracts defined in respective sprint specs in `docs/sprints/`.
- Local filesystem actions emit watcher events and update state in SQLite.
- Pre-mutation recovery snapshot must complete before any physical mutation in FS-07 / BAT operations.
- Word COM queue ensures max 1 concurrent Word process.

## Code Layout
- `src/` (or repository structure per codebase)
- `docs/sprints/` for sprint specifications and handoff reports in `docs/sprints/handoffs/`.
