# Sprint Handoff — QA-06

**Status:** DONE
**Branch:** `perf/qa-06-performance-benchmark-gate`
**Commits:** `fa3686a` (amended with hash fill-in; see final hash below)

## What changed

Performance benchmark gate over the real CAN-05/CAN-07/SRCH-02-05/GR-05/PDF-03
code paths. No production code touched (black-box tests only).

- **Backend gate (new).** `tests/DocumentIDE.PerformanceTests/` (xunit, net10.0,
  Core+Infrastructure refs — same convention as `Core.Tests`, added to
  `DocumentIDE.sln`):
  - `PerformanceBenchmarks.Fts_WarmSearch_ResponsiveDuringIndexWrites` — warm
    FTS5 search (real `FtsIndex` + `DocumentSearchService`, 500 docs) stays at
    p95 1.1–1.7ms / max 1.8–4.4ms while 100 index writes commit; asserts WAL
    journal mode (spec 11 §28). Thresholds: p95 < 250ms, max < 1,000ms.
  - `PerformanceBenchmarks.Graph_NeighborhoodLookup_BoundedNotFullScan` —
    real `BacklinkService`: exactly 1 scoped source + 1 scoped target lookup
    and 0 full scans per inspector call at 500- and 2,000-doc graphs;
    neighborhood/full cost ratio 0.014–0.031 (< 0.5), 4x-growth cost
    0.31–0.89x (< 2.0x).
  - `PerformanceBenchmarks.PdfMerge_PeakMemory_Bounded` — real
    `PdfMergeService`: peak additional 19.3% of total sources (< 50% spec
    bound; converges with the PDF-03 18.0% measurement).
- **Canvas gate (new).** `.../canvas/layout/Qa06CanvasPerformanceGate.test.ts`:
  100/500/1,000 placement re-runs CAN-05 numbers exactly (68.2
  examined/placement, 325 queries at every size) with the machine-independent
  gates (≤100 examined/placement, 1.5x size-independence, < brute-force/10);
  tidy re-runs CAN-07 band (1.1/2.5/6.4ms, index-backed).
- **Graph-render gate (new).** `.../graph/Qa06GraphPerformanceGate.test.ts`:
  bounded view 154/250/250 visible nodes for 300/1,000/3,000 total;
  layout work ratio 0.25 (≤ 0.3); async off-UI-thread contract; Top-K 200→5.
- **Baseline (new).** `docs/quality/performance-baseline.md` — all measured
  numbers, encoded thresholds, re-run commands, REL-02 regression policy.

Test-path mapping (CAN-05 ruling): no `tests/frontend/**` file was created;
TS gates live next to their implementation (vitest root is
`src/DocumentIDE.Web`). The sprint-planned `PerformanceBenchmarks.cs` was
created as planned; `performance-baseline.md` as planned.

## Files changed

- `tests/DocumentIDE.PerformanceTests/DocumentIDE.PerformanceTests.csproj` (new)
- `tests/DocumentIDE.PerformanceTests/PerformanceBenchmarks.cs` (new)
- `DocumentIDE.sln` (+1 project line)
- `src/DocumentIDE.Web/src/features/canvas/layout/Qa06CanvasPerformanceGate.test.ts` (new)
- `src/DocumentIDE.Web/src/features/graph/Qa06GraphPerformanceGate.test.ts` (new)
- `docs/quality/performance-baseline.md` (new)
- `docs/sprints/SPRINT-STATUS.md` (QA-06 row → DONE)
- `docs/sprints/handoffs/QA-06-HANDOFF.md` (this file)

## Verification

- `dotnet test tests/DocumentIDE.PerformanceTests/...` → PASS (3/3)
- `npx vitest run` in `src/DocumentIDE.Web` → PASS (38 files, 217 tests)
- `dotnet test tests/DocumentIDE.Core.Tests/...` → PASS (459/459)
- `dotnet test tests/DocumentIDE.IntegrationTests/... --filter PdfMergeMemoryTests` → PASS (6/6, PDF-03 leg unaffected)
- `npm run lint` → PASS (no warnings in new files; only pre-existing elsewhere)
- `npx tsc -b` → clean
- TDD Red evidence (3 genuine first-run failures, all fixed without touching
  production code): (1) new-project run failed MSB1003 before the csproj
  existed; (2) graph gate failed `Expected 1, Actual 2` — warmup lookup
  counted against the post-warmup counter assertion → counters baselined
  after warmup; (3) graph-render gate failed `expected 250 <= 231` — wrong
  size-independence shape (small graphs legitimately show fewer than the
  250 cap) → asserted capped-output equality past the budget instead; the
  layout wall-clock contrast was timer-slack noise (ratio 1.07) → gated on
  deterministic work ratio (0.25) instead, timing recorded only.

## Acceptance criteria

- [x] Thresholds derive from measured baseline, not invented claims — every
  threshold in the tests cites the measured number + headroom rationale;
  full tables in `docs/quality/performance-baseline.md`.

Required tests:
- [x] No O(n²) full scan in measured placement path — examined/placement
  68.2 constant at 100/500/1,000; < 1/10th of brute-force equivalents.
- [x] Interactive search remains responsive under write load — p95
  ~1.1–1.7ms vs 250ms threshold with concurrent commits; WAL asserted.
- [x] PDF merge memory stays bounded — 19.3% vs < 50% bound.

## Security & File-Safety Gate

- [x] no user file was mutated through an unreviewed/bypass path — all
  fixtures live in per-run temp dirs (`Path.GetTempPath()/qa06-*`, temp
  SQLite files); FTS tests index synthetic strings only; PDF tests build
  in-memory minimal PDFs; no source-document paths referenced.
- [x] no arbitrary path or secret is exposed — temp paths only, cleaned in
  `Dispose`; no secrets in code or logs.
- [x] dependency/security boundaries remain intact — zero package additions
  (xunit/Test SDK only, same versions as existing suites); no DI/API/ShareHost
  changes; no Word COM invoked (PDF builder is BCL-only, as in PDF-03).
- [x] no destructive action silently broadened its scope — read/measure-only
  gates; nothing in the repo is mutated by the tests (verified: full suites
  pass before and after).
- [x] relevant higher-level safety rules remain stronger than convenience —
  local-filesystem source of truth untouched; canvas drag semantics untouched
  (pure-geometry modules only re-exercised).

## Deviations / spec conflicts

- None in behavior. Two scoping notes: (1) the C# suite covers the
  backend legs (search/graph-service/PDF) while two small colocated vitest
  gates cover the TypeScript legs (placement/tidy/graph-render) — required
  because CAN-05/CAN-07/GR-05 implementations are TypeScript and a C#
  re-implementation would not bear on real code paths; (2) provisional
  tight thresholds used for the Red run were replaced by measured-baseline
  thresholds per the sprint acceptance criterion (thresholds must derive
  from measurement).

## Risks discovered

- PDF peak-memory ratio varies run to run (2.6–19.3% observed; PDF-03 saw
  18.0%) — GC-timing noise in `MeasurePeakAdditionalBytesAsync` at small
  payloads. The 50% bound has 2.5x headroom over the worst observation;
  if CI shows flake, raise payload toward the PDF-03 4MB shape rather than
  touching the bound.
- Search max latency varies with write-load timing (1.8–4.4ms); thresholds
  (250/1,000ms) absorb this 50–200x over.
- vitest wall-clocks are informational only; structural gates carry the
  regression signal on shared CI hardware.

## Next eligible sprint(s)

- `REL-02` — Release Verification Pipeline (QA-06 leg now DONE; still
  blocked on REL-01, QA-01, QA-02, QA-04, QA-05, QA-07 — check board).
