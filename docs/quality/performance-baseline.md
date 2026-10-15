# Performance Baseline & Regression Thresholds (QA-06)

**Status:** recorded, gated by automated tests.
**Origin sprint:** QA-06 — Performance Benchmark Gate.
**Depends on measurements from:** CAN-05 (spatial index), CAN-07 (tidy),
SRCH-05 (realtime search), GR-05 (bounded graph rendering), PDF-03 (streaming merge).

Machine for the numbers below (wall-clocks are informational; the structural
gates — examined-entry bounds, lookup counts, memory ratios — are normative
and machine-independent): Windows, .NET SDK 10.0.302, Node v24.11.0.

## How to re-run

```powershell
# Backend legs (search / graph-backend / pdf-merge)
dotnet test tests/DocumentIDE.PerformanceTests/DocumentIDE.PerformanceTests.csproj
# Frontend legs (canvas placement+tidy / graph render bounding)
cd src/DocumentIDE.Web
npx vitest run src/features/canvas/layout/Qa06CanvasPerformanceGate.test.ts src/features/graph/Qa06GraphPerformanceGate.test.ts
```

Per-test `QA06_*` console lines print the measured numbers.

## Leg 1 — Canvas placement (CAN-05 spatial-index path)

Gate: `Qa06CanvasPerformanceGate.test.ts` — 25 placements near the origin anchor
on scattered 100/500/1,000-object canvases via `placeDocumentsNearOrigin`.

| Canvas size | Wall-clock | Target | Examined / placement | Index queries | Intersection tests |
|---|---|---|---|---|---|
| 100 objects | 2.0ms | 2,000ms | 68.2 | 325 | 1,706 |
| 500 objects | 0.9ms | 5,000ms | 68.2 | 325 | 1,706 |
| 1,000 objects | 1.0ms | 10,000ms | 68.2 | 325 | 1,706 |

Reproduces the CAN-05 handoff numbers exactly (68.2 / 325 / ~1.7k at every size).

Encoded thresholds (machine-independent first):
- examined entries per placement ≤ 100 (`MAX_EXAMINED_PER_PLACEMENT`),
- examined[1,000] ≤ examined[100] × 1.5 and examined[500] ≤ examined[100] × 1.5,
- intersection tests < (index queries × canvas size) / 10 at every size
  (order of magnitude below the brute-force full scan),
- wall-clock below the recorded targets above.

## Leg 2 — Canvas tidy (CAN-07 path)

Gate: `Qa06CanvasPerformanceGate.test.ts` — `tidyCanvas` over overlapping
jittered rows at 100/500/1,000 objects.

| Canvas size | Wall-clock | Target | Moves | Index queries |
|---|---|---|---|---|
| 100 objects | 1.1ms | 2,000ms | 99 | 100 |
| 500 objects | 2.5ms | 5,000ms | 499 | 500 |
| 1,000 objects | 6.4ms | 10,000ms | 999 | 1,000 |

Consistent with the CAN-07 handoff band (0 / 3–5 / 7–9ms).

Encoded thresholds: wall-clock below targets; moves > 0 (gate cannot pass
vacuously); index queries > 0 (probes stayed index-backed, never a full scan).

## Leg 3 — Warm FTS search during index writes (SRCH-02/05 paths)

Gate: `PerformanceBenchmarks.Fts_WarmSearch_ResponsiveDuringIndexWrites` —
real `FtsIndex` (SQLite FTS5, WAL) + real `DocumentSearchService`; 500 seeded
docs, 10 warmup searches, then 40 timed warm searches while a background task
commits 4 × 25 index writes.

| Metric | Measured | Encoded threshold |
|---|---|---|
| p50 | 0.48–0.57ms | — (recorded) |
| p95 | 1.09–1.74ms | < 250ms |
| max (under write load) | 1.77–4.42ms | < 1,000ms |
| journal mode | `wal` (asserted) | must stay `wal` |
| writer completion | no SQLITE_BUSY failure | must complete |

Thresholds carry ~100x headroom: they trip only on structural regressions
(lost WAL concurrency, FTS fallback to full scan), never on machine variance.

## Leg 4 — Graph neighborhood vs full graph

Backend gate: `PerformanceBenchmarks.Graph_NeighborhoodLookup_BoundedNotFullScan` —
real `BacklinkService` over chain graphs (500 docs/~1,000 rels vs 2,000
docs/~4,000 rels).

| Metric | Measured | Encoded threshold |
|---|---|---|
| neighborhood lookup | 0.01–0.06ms | < 500ms |
| full-scan contrast (~4,000 rels) | 0.63ms | — (recorded) |
| neighborhood / full ratio | 0.014–0.031 | < 0.5 |
| 4x-graph-growth cost | 0.31–0.89x | < 2.0x |
| scoped lookups per inspector call | exactly 1 source + 1 target | exactly 1 + 1 |
| full `GetRelationsAsync` calls | 0 | 0 |

Render gate: `Qa06GraphPerformanceGate.test.ts` — real GR-05
`computeBoundedGraph` / `computeLayoutAsync` / `filterSemanticEdgesTopK`.

| Total nodes | Visible nodes | Visible edges | Bound time |
|---|---|---|---|
| 300 | 154 | 305 | 0.36ms |
| 1,000 | 250 | 497 | 0.51ms |
| 3,000 | 250 | 497 | 0.81ms |

Encoded thresholds: visible ≤ 250 at every size; visible[3,000] ==
visible[1,000] (capped output is size-independent past the budget); layout
work ratio (visible/total at 1,000 nodes) = 0.25 ≤ 0.3; layout is async
(off-UI-thread contract); semantic Top-K collapses 200 edges to 5.

## Leg 5 — Streaming PDF merge peak memory (PDF-03 path)

Gate: `PerformanceBenchmarks.PdfMerge_PeakMemory_Bounded` — real
`PdfMergeService.MergeOrderedAsync` + `MeasurePeakAdditionalBytesAsync`,
6 inputs × ~512KB.

| Metric | Measured | Encoded threshold |
|---|---|---|
| peak additional / total sources | 19.3% this run (2.6–19.3% observed band; PDF-03 measured 18.0% at 6 × ~4.2MB) | < 50% |
| wall-clock (6 × 512KB) | 16–56ms | — (recorded) |
| page count / order | 6/6, markers in order | must hold |

The < 50% bound is the PDF-03 spec bound: peak ≈ one input file + 80KB
buffer, never the corpus.

## Regression policy (for REL-02)

- Any gate failure blocks release (spec 11 §28: canvas 100/500/1,000
  spatial-neighbor benchmark, graph >250 bounded view off UI thread, PDF
  merge sub-linear memory are release-blocking).
- Wall-clock thresholds are generous by design (~100–1000x headroom); a
  wall-clock trip means a structural regression, not a slow machine.
- When hardware changes materially, re-run, record new numbers here, and
  adjust only the wall-clock targets — never weaken the structural gates
  (examined-entry bounds, lookup counts, memory ratio, WAL assertion).
