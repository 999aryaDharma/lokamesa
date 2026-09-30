# Future Improvement Roadmap — 1.1.0 → 1.6.x

## Release map

| Version | Feature line | Primary outcome | Dependencies | Status |
|---|---|---|---|---|
| `1.1.0` | Command Palette + Quick Open | Fast keyboard-first navigation and command execution | Search, filesystem, canvas, source control | SPECIFIED |
| `1.2.0` | Review Workspace | Structured review queue and diff-first approval flow | SCM, DOCX diff, Markdown diff, History | SPECIFIED |
| `1.3.0` | Saved Views / Smart Collections | Reusable live lenses over workspace data | Search, canvas, metadata | SPECIFIED |
| `1.4.0` | Document Health / Linter | Deterministic health checks with explicit repair routing | Indexing, Markdown, DOCX metadata, batch | SPECIFIED |
| `1.5.0` | Work Session | Resume-able units of human work across app restarts | History, SCM, workspace state, Review | SPECIFIED |
| `1.6.0` | Document Comments / Review Notes | Canvas-native notes anchored to documents | Canvas, Review, workspace persistence | SPECIFIED |

## Version philosophy

### 1.1.0 — Command Palette + Quick Open
Make the product fast for power users without adding new document semantics.

### 1.2.0 — Review Workspace
Turn the existing diff, history, and document inspection capabilities into a coherent review loop.

### 1.3.0 — Saved Views / Smart Collections
Allow users to preserve useful lenses and dynamic document sets without creating physical folders.

### 1.4.0 — Document Health / Linter
Continuously surface document integrity and consistency problems, while routing all mutations through existing safe mechanisms.

### 1.5.0 — Work Session
Give the user a durable concept for “the work I am doing now” without conflating it with Git commits.

### 1.6.0 — Document Comments / Review Notes
Add small note objects directly onto the canvas near documents, with document anchoring and review lifecycle.

## Global non-goals

- no product-level branch management,
- no replacement for Word,
- no cloud-first document storage,
- no automatic commit/push,
- no silent destructive fixes from linter/health checks,
- no proprietary document content format,
- no real-time multiplayer editor.
