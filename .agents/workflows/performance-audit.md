# Performance Audit

## Description

Find actual/probable bottlenecks for the documented <1,000-document target.

## Steps

1. Do not optimize blindly.
2. Inspect:
   - SQLite WAL/transaction duration,
   - indexing scheduling,
   - per-keystroke search path,
   - canvas collision complexity,
   - graph layout/render scope,
   - React re-renders,
   - thumbnail/preview cache,
   - Word STA queue,
   - PDF merge memory,
   - embedding concurrency.
3. Separate:
   - measured bottleneck,
   - probable bottleneck,
   - premature concern.
4. Propose a benchmark before any nontrivial optimization.
5. Do not introduce distributed infrastructure.
