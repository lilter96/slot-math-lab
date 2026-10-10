# Graph execution performance

The full Dog House model is compiled from the same UI graph and typed expression
AST. The sampled execution plan uses indexed, private state frames, precompiled
expression delegates, immutable typed arrays, parsed constants and prebuilt alias
tables. Each logical PRNG chunk owns a runner and resets its frame between rounds.
State snapshots are materialized only for Play/replay, with defensive copies of
cached defaults. Integer arithmetic avoids unnecessary rational reduction.
Hot expressions are compiled to IL, consecutive state and draw nodes run as one
loop, never-written initial state is folded into the expressions that read it,
and every sampling worker leases a private replica of the plan.

Exact evaluation continues to consume the canonical immutable Slot program.
Programs using plugins, transform/evaluator Map nodes, reel-product Draw nodes,
or legacy loops retain the general interpreter. Composing further monadic effects
over an optimized program also retains the canonical interpreter's semantics.
This optimization does not claim to accelerate every graph or exact enumeration.

The API caches eligible compiled plans by canonical graph SHA-256, with one
compilation for simultaneous requests, ten-minute idle expiration and a 32 MiB
estimated-size budget. This accounts conservatively for graph objects and code;
it is not an exact heap quota. Input edits produce a different key. Registry and
plugin-dependent programs are not cached. Final run evidence includes
`samplingEngine` (`compiled-state-plan-v1` or `reference-interpreter`).

## Sampled throughput

Full Dog House graph, seed 42, 8,388,608 rounds after 400,000 warmup rounds, live
progress callbacks on, .NET 10.0.12 Release, Server GC with dynamic adaptation
off, i5-13600KF. Workers are pinned to distinct performance cores unless noted.
Each row is one of three consecutive measurements; the four-worker row is their
median (1,052,000–1,071,000):

| Workers | Rounds/second | Bytes/round |
| ---: | ---: | ---: |
| 1 | 308,000 | 987 |
| 2 | 575,000 | 987 |
| 4 | 1,067,000 | 987 |
| 4, not pinned | 1,071,000 | 987 |
| 8, not pinned | 1,727,000 | 987 |

The same build with code generation switched off (`--tier interpreted`) reaches
308,000 rounds/second at four workers, and the canonical interpreter
(`--reference`) 19,600. Before this work one worker reached about 34,000.
The seeded mean payout, `0.9742173612117765`, is identical in every row, at every
worker count and in every tier.

Through the API (`POST /api/runs` with the payload the Simulate page sends: four
workers, a progress snapshot every 1,000 rounds), a warm process measures
0.94–1.10 million rounds/second of worker time for 8,388,608 rounds and about
1.0 million for 1,000,000 rounds. The first run of a new graph pays roughly
0.2 s for code generation; the first run after process start pays about 1 s.

### What made it slow, and what changed

1. **Per-node dispatch.** Each node was a closure calling the next one, about
   27 ns per node, and about half the nodes of the Dog House graph are trace
   recorders that do nothing outside Play. A run of consecutive state and draw
   nodes is now one loop over a step array, and a field assigned to itself costs
   a single check.
2. **Expression evaluation.** Expressions ran as trees of delegates over boxed
   values. A hot expression (64 evaluations) is now compiled to IL through
   `System.Linq.Expressions`. Code is generated once per expression *shape*;
   slots, constants and nested closures are passed as operands, so twenty
   payline evaluators share one method and a plan replica reuses its original's
   code. An expression the generator does not support stays interpreted.
3. **Constant state.** Initial state that no node writes (library parameters,
   feature switches) is folded into the expressions that read it, and a
   conditional on such a value keeps only the branch it takes. A runner whose
   initial state overrides one of these keys uses an unfolded plan.
4. **Workers sharing one plan.** Four workers running one plan instance were
   24–32% slower in total than four private instances. Each worker now
   leases its own replica for the duration of a run. The mechanism behind the
   penalty was not identified; `perf` was not available on the measuring host.
5. **PRNG and alias sampler.** `SeededRandom` keeps its state in locals and is
   inlined into its callers; the alias table is one array of 16-byte entries;
   `WeightSet.AliasTable` is read without a lock. One core, one draw:
   raw 64-bit output 2.4 → 0.8 ns, weighted draw 9.4 → 7.7 ns, uniform draw
   7.1 → 4.8 ns, weighted draw through the `WeightSet` cache 15.3 → 8.3 ns.
   Output is bit-identical: the table is still built by first-in, first-out
   pairing, because the pairing order decides which outcome a seeded draw
   returns, and a threshold is still the plain double quotient while the
   weights fit the double range. Beyond it (totals above about 2^1024, where
   the previous build produced infinities) the quotient is computed from the
   leading 64 bits, so a positive weight keeps a positive share as long as a
   double can represent it.
6. **Collector mode.** See below.

### Runtime configuration

- **Garbage collector.** `SlotMath.Api` and `SlotMath.Benchmarks` set Server GC
  with `GarbageCollectionAdaptationMode` 0. With the .NET 9+ default (DATAS) the
  four-worker figure falls from 1,067,000 to 853,000 rounds/second: ten times as
  many gen0 collections and 3.3 of 4 cores used.
- **CPU quota.** Give the API container at least as many CPUs as the worker
  limit (eight). Under `--cpus 2 --memory 2g` the benchmark measures 424,000
  rounds/second with four workers and 571,000 with two; under `--cpus 4`,
  1,112,000 with four; under `--cpus 8`, 1,571,000 with eight (134,217,728
  rounds, no out-of-memory kill). Eight workers on this machine include
  hyper-threads and efficiency cores, so they do not double the four-worker
  figure.
- **Run limits.** A run takes 1–10,000,000,000 rounds and 1–8 workers. Its
  resource budget is five minutes per started 10,000,000 rounds. Finished
  streams are folded into the totals in stream order as soon as every earlier
  stream has been folded, so memory does not grow with the length of the run.
  A worker does not start a stream that is 16 × workers or more ahead of the
  first unfolded one, so fewer than that many finished streams wait even when
  one stream is much slower than the rest. At 1,570,000 rounds/second the
  largest run takes about 1 hour 46 minutes. Runs above 10,000,000 rounds
  report progress once per stream or after 250 ms, whichever comes first.
- **Run length.** A PRNG stream covers 65,536 rounds, and a stream is never split
  between workers. A 100,000-round run therefore uses two workers whatever was
  requested; four workers need more than 196,608 rounds and eight more than
  458,752.
- **.NET 11.** The same binaries on 11.0.0-rc.1 measure 312,600–314,800 against
  312,400–312,800 rounds/second with one worker and 1,107,500–1,135,200 against
  1,115,000–1,119,800 with four. That is within run-to-run variation, so the
  target framework stays `net10.0`.

Evidence: [four-worker benchmark](verification/performance-sampling-codegen.json).

## Earlier measurement: compiled state plan

Graph: `6ab31814ceb52133e3332855ab8e80aac5af9f9443a886b9361bf5345f0964b4`.
.NET 10.0.12 Release, processor count pinned to two, one sampling worker, seed 42,
100,000 warmup rounds, three measurements of 20,000 complete rounds. Live statistics
callbacks are enabled. Median results:

| Metric | Previous interpreter | Compiled state plan |
| --- | ---: | ---: |
| Complete rounds/second | 5,031 | 39,776 |
| Allocated bytes/round | 600,997 | 16,065 |
| Seeded mean payout | 1.0002349999999924 | 1.0002349999999924 |

Throughput improves **7.91×**, and allocations decrease **97.33%**. Count, mean,
variance, hit frequency, maximum and every histogram bucket are identical.
The benchmark includes sampled statistics/progress calculation; it does not
include HTTP, SignalR serialization, queue wait or browser rendering.

The authenticated production browser check uses the full graph, two workers,
100,000 rounds and seed 42, behind the HTTPS/WebSocket proxies and the API's
two-CPU limit. Worker execution falls from **20.375 s to 3.301 s** (6.17×).
The new launch-to-completion measurement is **4.176 s**. Reload recovers the run;
13 WebSocket snapshots arrive; no browser errors occur. Cached Play requests take
24–35 ms. RTP, uncertainty, hit frequency, volatility and maximum match the old
production run bit for bit. Histogram bucket counts match after normalizing the
previous result's PascalCase property names.

The observed 100,000-round RTP is 99.6719%, with a 95% normal-approximation interval
of 91.1034%–108.2404%; the authored exact expectation remains 98%.
Measurements depend on hardware, graph and warmup.

Evidence: [controlled benchmark](verification/performance-benchmark.json),
[production browser run](verification/performance-production.json),
[dashboard capture](verification/performance-production.png).

## Reproduce

```sh
dotnet build backend/SlotMath.Benchmarks -c Release
DOTNET_PROCESSOR_COUNT=2 dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/DogHouse/dog-house-ui.json \
  --warmup 100000 --rounds 20000 --repeats 3 --workers 1 --live --report /tmp/optimized.json
# Add --reference to measure the canonical interpreter in the current build.
# The recorded pre-change baseline additionally predates the integer shortcuts.

# Multi-worker throughput on four distinct cores:
taskset -c 0,2,4,6 dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/DogHouse/dog-house-ui.json \
  --warmup 400000 --rounds 8388608 --repeats 3 --workers 4 --live
# --tier interpreted|compiled|tiered selects expression execution (default tiered).
# --allocations prints allocated bytes per round by type instead of timings.
```

Profile with .NET EventPipe's `dotnet-sampled-thread-time` profile. The previous
trace identified repeated `ToExprValue`/`Array.ConvertAll`, state dictionary
copying and GC waits; allocation counters quantify the reduction. Thread-time
sampling includes GC waits and must not be interpreted as exclusive CPU cost.

## Correctness gates

The optimized plan is compared with its independent canonical execution path in
1,000 generated full-game replays (including bonus traces), 1,000 generated
control-flow cases and 10,000 generated expression cases, using pinned CsCheck
seeds and automatic shrinking. Tests compare final state, not just payouts.
Additional checks cover all statistics and histograms across 1/2/4 workers,
located evaluation errors, short-circuit evaluation, rational values, iteration
bindings, record/raw-array semantics, caps, cancellation, concurrent runners,
exported-default isolation, cache invalidation and exact PMF equivalence.
The separate small-model exhaustive payout oracle remains in the test suite.

Generated code, shared shapes and constant-state folding have their own gate,
`SamplingTierEquivalenceTests`: 10,000 generated expressions compared with the
interpreted tier on value, located error and final state; 10,000 more with part
of the state folded; 1,000 full-game replays per tier against the reference
interpreter, with and without folding; and bit-identical statistics across the
three tiers at 1, 3 and 8 workers. Four deliberately seeded defects (two
operators sharing a shape, a folded conditional keeping the wrong branch, two
text comparisons sharing a shape, two constant kinds sharing a token) each fail
this gate.

The PRNG is pinned by five reference outputs for seed 42, computed independently
of this code. The alias sampler is pinned by 10,000 generated weight sets compared
bit for bit with a queue-built reference table, and by two recorded seeded draw
sequences.

Browser tests use a larger requested sample size for recovery/cancellation so
the faster engine is still running during those checks. They cancel after a
small actual prefix. No artificial delay is added to execution.
