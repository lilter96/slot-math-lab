# Nine Lives graph execution performance

Nine Lives mechanics are editable JSON subgraphs composed from existing atoms
and expression capabilities. The compiler optimizes generic Map, Filter, Fold
and Count loops, scoped variables and constant arrays. Collection membership
avoids enumerator allocation; raw integer counters reuse immutable boxes.
No game, mechanic, transform identifier or authored graph pattern selects a
kernel implementation. No new atom or expression function was added.

The benchmark measures complete paid rounds including triggered bonuses,
statistics and live callbacks. It excludes HTTP, queueing, SignalR and rendering.
All final runs used the same .NET 10.0.12 Release binary, seed 42, 100,000 warmup
rounds and `splitmix64-chunk-65536`. Measurements ran sequentially with task-owned
validation jobs stopped. The host is shared, without CPU pinning; these are local
measurements, not a D7 runner certificate.

## Current graph

Config hash: `adec4b6e71e42c50e653674fd7b23e42d2113085f6d048a9bea6a51157283ce0`.

| Measurement | Complete rounds/second | Bytes/round |
| --- | ---: | ---: |
| One worker, median of three one-million-round repeats | 33,100 | 21,438 |
| Four workers, median of three one-million-round repeats | 105,050 | 21,438 |
| Four workers, ten million rounds | 117,512 | 21,474 |

The one-worker repeats were 32,489, 33,100, 33,939;
the four-worker repeats were 100,026, 105,050, 123,733.
The required **39,800 rounds/second per worker remains unmet**. A four-worker
total must not be compared directly with that per-worker requirement.

See [one-worker evidence](verification/lab-demo-performance.json),
[four-worker evidence](verification/lab-demo-performance-four-workers.json) and
[ten-million-round evidence](verification/lab-demo-sampled.json).

## Comparison

Commit `aa9727c` remains in this branch. Its one-million-round measurement was
the previous demo with multiple workers. A fresh comparison on the current
binary confirmed that throughput remains available:

| Graph, four workers | Complete rounds/second | Bytes/round |
| --- | ---: | ---: |
| Previous demo, median of three repeats | 1,143,821 | 941 |
| Original Nine Lives composition, one repeat | 36,769 | 80,474 |
| Optimized Nine Lives composition, median of three repeats | 105,050 | 21,438 |

The graph composition alone is 2.86 times faster on this paired
comparison, with 3.75 times less allocation. The original and optimized Nine Lives
reports have different config hashes; their one-million-round payout statistics
and histograms are identical. The original composition also benefits from the
new generic compiler optimizations in this comparison.

Before these optimizations, the old binary measured 31,694 rounds/second total
on four workers and 123,302 bytes/round. This historical diagnostic is retained
in [the earlier comparison](verification/lab-demo-throughput-diagnostic.json).
The current total is 3.31 times that observation, with approximately
5.75 times less allocation; host load and repeat counts differ.
The earlier shared-load one-worker observation was 5,563 rounds/second and is
not a controlled baseline for a speedup claim.

See [original-composition evidence](verification/lab-demo-optimization-baseline.json)
and [previous-demo evidence](verification/lab-demo-prior-graph-performance.json).

## Changes in composition

Compaction stages six columns and reconstructs the thirty-cell board with one
Map instead of thirty separate board maps. Immutable geometry replaces repeated
coordinate arithmetic. Four neighbor discoveries are one Fold. Disjoint visited
and claimed arrays are appended without redundant membership checks. Conservative
size and edge bounds skip impossible symbol sweeps. In untraced rounds without
Wilds, a necessary neighbor-degree bound skips small components; detailed traces
and Wild claiming retain the original traversal order. Balanced conditional
selection builds initial boards and prize vectors with existing Map/If syntax.

The independent Fraction corpus contains 862 component boards and 22 complete
rounds. It checks traced results, untraced payouts and final boards. Shared-Wild
claiming, all-Wild boards, source RNG order and exact cap termination are retained.
Expression tests exercise shared code, nested/shadowed variables, nullable and
symbol values, lazy reads, errors, cancellation and state restoration.

## RTP evidence

Ten million rounds completed in 85.098 seconds. The sample mean is
**96.370975%**, variance 395.426987, and the fixed-count 95% normal
approximation interval is **95.138468%–97.603482%**.
Maximum observed payout is 7,899.3 original stakes; hit frequency is
37.07432%. The target is 0.590 standard errors from the observation.
This matches the original composition's ten-million-round payout statistics.
It is Sampled evidence with uncertainty, not an Exact or bounded-tail certificate.

The source target of 96% is design intent. Full Exact RTP within sixty seconds
has not been established; the reduced component fixture has independently
verified Exact expectation 1/40. See [Lab Demo](LAB_DEMO.md).

## Reproduce

```sh
dotnet build backend/SlotMath.Benchmarks -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo.json \
  --warmup 100000 --rounds 1000000 --repeats 3 --workers 1 --live \
  --report /tmp/nine-lives-performance.json

dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo.json \
  --warmup 100000 --rounds 10000000 --repeats 1 --workers 4 --live \
  --report /tmp/nine-lives-sample.json
```

Use `--workers 4` for a multi-worker total and `--reference` for the canonical
interpreter. Pin hardware and remove concurrent load for acceptance timing.

A separately proposed [Fold bound](FOLD_BOUNDS_PROPOSAL.md) could make larger
pure compositions fit the expression budget. It is not implemented, and its
performance gain has not been established. It requires explicit authorization.
