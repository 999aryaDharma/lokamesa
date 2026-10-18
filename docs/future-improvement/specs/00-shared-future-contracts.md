# Future Improvement — Shared UX & Domain Contracts

These contracts apply across versions 1.1.0–1.9.x.

## 1. Feature objects remain non-authoritative for document contents

Command metadata, review sessions, saved views, health findings, work sessions, and canvas notes are application/workspace metadata. They must not become a second authoritative copy of DOCX or Markdown contents.

## 2. Derived state must be rebuildable

Search results, health findings, smart collection membership, quick-open ranking, and session activity summaries may be rebuilt from authoritative sources.

## 3. Destructive actions remain explicit

Future features must route document mutations through existing filesystem/batch/history safety mechanisms. Health checks may recommend a fix; they do not silently apply one. Review approval does not itself mutate files.

## 4. Git remains Git

Source Control continues to use real Git repository state. Future features may expose existing Git actions but must not create a parallel commit model. Product-level branch management remains out of scope.

## 5. Canvas notes are virtual objects

A note placed near a document is a canvas/workspace object. Moving a note or document on the canvas changes presentation state, not physical filesystem paths.

## 6. Accessibility

Every major action must have a keyboard path where practical, visible focus state, semantic labels, and an accessible text representation. Canvas-only affordances must have a non-canvas alternative.

## 7. Performance

Features must remain responsive for the V1 target of fewer than 1,000 documents per workspace. UI lists must virtualize or paginate where evidence shows it is necessary; expensive scans run through the existing background-job model.
