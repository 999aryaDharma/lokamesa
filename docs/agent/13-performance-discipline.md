# Performance Discipline

Target scale: under 1,000 documents/workspace.

Rules:
- profile before large optimization,
- no distributed infrastructure,
- parse outside DB transactions,
- SQLite WAL with short writes,
- no per-keystroke DOCX parsing,
- canvas collision uses spatial index,
- graph is bounded/filter-first at scale,
- PDF merge streams/sequences,
- Word automation concurrency = 1,
- expensive UI layout off interactive thread.

Record actual benchmark baselines in `docs/quality/`.
