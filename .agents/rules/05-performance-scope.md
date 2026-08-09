# Performance & Scope — Always On

Target workspace scale:

```text
< 1,000 documents
```

Do not introduce distributed infrastructure for this target.

Preserve these constraints:

- no DOCX parsing per search keystroke,
- SQLite WAL and short writes,
- expensive parsing outside DB write transactions,
- spatial index for repeated canvas collision queries,
- bounded/neighborhood graph rendering at scale,
- graph layout off interactive UI thread,
- sequential/streaming PDF merge,
- Word automation concurrency = 1,
- bounded background jobs/semantic embedding work.

Profile and benchmark before architectural optimization.
Do not use “performance” as justification for weakening safety guarantees.
