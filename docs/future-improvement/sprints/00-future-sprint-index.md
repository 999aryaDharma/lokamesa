# Future Improvement Sprint Index

**Total future sprints:** 32

These sprints are planning artifacts for roadmap versions `1.1.0 → 1.9.0`. They are not part of the active V1 sprint count until promoted.

## Sprint table

| Sprint | Title | Priority | Version | Depends On | State |
|---|---|---:|---|---|---|
| `PAL-01` | Command Registry & Capability Model | P0 | v1.1.0 | — | SPECIFIED |
| `PAL-02` | Command Palette Keyboard UX | P0 | v1.1.0 | PAL-01 | SPECIFIED |
| `PAL-03` | Quick Open Documents & Recent Items | P0 | v1.1.0 | PAL-01 | SPECIFIED |
| `PAL-04` | Command Palette & Quick Open Integration Hardening | P1 | v1.1.0 | PAL-02, PAL-03 | SPECIFIED |
| `REV-01` | Review Session Domain & Baseline Contract | P0 | v1.2.0 | SCM-08, SCM-05, HIST-01 | SPECIFIED |
| `REV-02` | Review Workspace Shell & Queue | P0 | v1.2.0 | REV-01 | SPECIFIED |
| `REV-03` | Review Diff Orchestration | P0 | v1.2.0 | REV-02, SCM-04, SCM-05 | SPECIFIED |
| `REV-04` | Review Approval & Commit Preparation | P1 | v1.2.0 | REV-03, SCM-06 | SPECIFIED |
| `REV-05` | Review Regression & Safety Hardening | P0 | v1.2.0 | REV-04, HIST-02 | SPECIFIED |
| `VIEW-01` | Saved View Domain & Query Snapshot | P0 | v1.3.0 | SRCH-05, CAN-08 | SPECIFIED |
| `VIEW-02` | Smart Collection Predicate & Membership | P0 | v1.3.0 | VIEW-01, IDX-04 | SPECIFIED |
| `VIEW-03` | Saved View & Smart Collection Canvas Objects | P1 | v1.3.0 | VIEW-02, CAN-07 | SPECIFIED |
| `VIEW-04` | Views & Collections Regression Hardening | P1 | v1.3.0 | VIEW-03, SRCH-03, SRCH-04 | SPECIFIED |
| `HLTH-01` | Health Report & Check Registry | P0 | v1.4.0 | IDX-06, FS-06 | SPECIFIED |
| `HLTH-02` | Markdown Health Checks | P0 | v1.4.0 | HLTH-01, MD-04 | SPECIFIED |
| `HLTH-03` | DOCX Health & Consistency Checks | P0 | v1.4.0 | HLTH-01, DOCX-04 | SPECIFIED |
| `HLTH-04` | Health UI & Safe Repair Routing | P0 | v1.4.0 | HLTH-02, HLTH-03, BAT-03 | SPECIFIED |
| `HLTH-05` | Health Policy & Regression Hardening | P1 | v1.4.0 | HLTH-04, QA-01 | SPECIFIED |
| `WS-01` | Work Session Domain & Persistence | P0 | v1.5.0 | HIST-02, CAN-06, SCM-09 | SPECIFIED |
| `WS-02` | Work Session Resume & Context Restoration | P0 | v1.5.0 | WS-01 | SPECIFIED |
| `WS-03` | Work Session Activity Aggregation | P1 | v1.5.0 | WS-01, SCM-08, REV-02 | SPECIFIED |
| `WS-04` | Work Session UX & Retention Hardening | P1 | v1.5.0 | WS-02, WS-03 | SPECIFIED |
| `NOTE-01` | Canvas Note Domain & Persistence | P0 | v1.6.0 | CAN-06, FND-03 | SPECIFIED |
| `NOTE-02` | Canvas Note Object & Document Anchoring | P0 | v1.6.0 | NOTE-01, CAN-07 | SPECIFIED |
| `NOTE-03` | Note Editor, Status & Review Integration | P0 | v1.6.0 | NOTE-02, REV-02 | SPECIFIED |
| `NOTE-04` | Note Search & Navigation Integration | P1 | v1.6.0 | NOTE-03, SRCH-05 | SPECIFIED |
| `NOTE-05` | Canvas Note Regression & Release Hardening | P1 | v1.6.0 | NOTE-04, QA-06 | SPECIFIED |
| `WB-01` | Workspace Kind, Workspace API & Subsystem Gating | P0 | v1.7.0 | FND-02, FND-03, FND-07 | SPECIFIED |
| `WB-02` | Space-Object Domain, Vault & Atomic Writes | P0 | v1.7.0 | WB-01, FS-07, FS-08 | SPECIFIED |
| `WB-03` | Wildboard Canvas Surface | P0 | v1.7.0 | WB-02, CAN-01, CAN-06, CAN-07, MD-01 | SPECIFIED |
| `WB-04` | Quick Capture | P0 | v1.7.0 | WB-03, FND-07, CAN-05 | SPECIFIED |
| `WB-05` | Wildboard Recovery, Security & Performance Hardening | P1 | v1.7.0 | WB-04, QA-06, QA-07 | SPECIFIED |
