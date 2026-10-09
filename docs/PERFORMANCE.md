# Graph execution performance

The full Dog House model is compiled from the same UI graph and typed expression
AST. The sampled execution plan uses indexed, private state frames, precompiled
expression delegates, immutable typed arrays, parsed constants and prebuilt alias
tables. Each logical PRNG chunk owns a runner and resets its frame between rounds.
State snapshots are materialized only for Play/replay, with defensive copies of
cached defaults. Integer arithmetic avoids unnecessary rational reduction.

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

## Measured results

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

Browser tests use a larger requested sample size for recovery/cancellation so
the faster engine is still running during those checks. They cancel after a
small actual prefix. No artificial delay is added to execution.
