# Nine Lives graph execution performance

Nine Lives is authored in editable JSON subgraphs. The only added graph
capability is the explicitly approved 64-bit finite-set expression family:
Union, Intersect, Except, Shift, Count and First. The compiler optimizes generic
expressions, storage and control flow; game names, mechanic identifiers and
authored graph patterns do not select kernel implementations.

## Workload and current target

The current acceptance target is **2,000,000 complete paid rounds/second on
eight workers on this machine**. Every measured round includes triggered free
spins and cascades, complete payout statistics, histograms and live callbacks.
HTTP, queueing, SignalR and rendering are outside this kernel benchmark.
The current Release build exceeds the target in all three ten-million-round
repeats.

The current model hash is
`09e4fca19d064284290c6d0e869acc5872684ff4e92e1e039720876a3fdc3ae7`.
Measurements use .NET 10.0.12 Release, seed 42 and logical streams
`splitmix64-chunk-65536`. The host has twenty logical CPUs with mixed core
speeds and concurrent unrelated work. These are local observations; a D7
runner certificate requires its specified hardware and workload conditions.

## Measured changes

The following comparisons compile both variants once in one process, warm
each with two million complete rounds, then alternate ABBA/BAAB over eight
five-million-round repeats. The table gives the median for each variant.
Only the indicated optimization changes within a comparison. Different pairs
ran at different times under different shared-host load; their medians should
not be combined into a cumulative speedup calculation.

| Change | Before rounds/s | After rounds/s | Before CPU µs/round | After CPU µs/round |
| --- | ---: | ---: | ---: | ---: |
| Remove redundant no-Wild guards after exact seed selection | 1,490,013 | 1,628,239 | 5.172 | 4.754 |
| Author two required frontier expansions before the remaining loop | 1,369,743 | 1,464,687 | 5.672 | 5.277 |
| Keep RNG state in registers across the existing alias pair | 1,468,233 | 1,485,296 | 5.319 | 5.249 |
| Capture initial scatter count from existing bit planes | 1,415,347 | 1,427,993 | 5.477 | 5.393 |
| Reuse temporary arrays within a paid round | 1,309,162 | 1,442,051 | 5.935 | 5.349 |
| Reuse immutable boxed numeric zero on cell export | 1,604,322 | 1,636,921 | 4.818 | 4.670 |
| Reuse temporary arrays by exact length across different request orders | 1,764,854 | 1,812,251 | 4.367 | 4.259 |
| Select the existing alias result with an integer mask | 1,629,934 | 1,799,076 | 4.712 | 4.295 |
| Convert the existing 53-bit RNG fraction through a positive signed integer | 1,725,790 | 1,781,085 | 4.457 | 4.372 |

The array comparison reduced allocation from 2,479.7 to 453.8 bytes/round.
Cached numeric zero reduced it to 273.9 bytes/round; matching buffers by exact
length reduced it further to 64.7 bytes/round in its comparison. Arrays remain
distinct for the entire current round; exports recursively copy them. The
bounded arena is disabled for persistent state and custom measurement
collection. Live payout statistics and progress callbacks are retained.

The full payout mean, variance, hit frequency, observed maximum, histogram
and loop evidence agree within each storage/RNG comparison. Authored loop
rewrites preserve payout statistics while their loop evidence describes the
new graph. [Experiment reports](verification/lab-demo-optimization-experiments.json)
retain individual timings, CPU time, GC pauses, allocation and statistics.
[The optimization record](verification/NINE_LIVES_OPTIMIZATION.md) explains
native profiles, independent regressions and rejected experiments.

The current standalone eight-worker run measured **2,061,865 / 2,059,413 /
2,065,394 complete rounds/s** (median **2,061,865**), with live statistics,
three ten-million-round repeats and two-million-round warmup. Allocation was
65 B/round; GC pauses were 0 / 2 / 1 ms. The [current report](verification/lab-demo-performance-eight-workers.json)
retains the model hash and all payout and loop statistics. Concurrent unrelated
work was left running. The [one-worker report](verification/lab-demo-performance.json)
measured a median of 310,987 complete rounds/s over
three one-million-round repeats with the same two-million-round warmup.

## Real UI execution

The earlier UI on port 4473 ran a Debug API with a saved pre-optimization
constructor graph. Its pinned JSON contained no finite-set expressions;
updating the source fixture did not replace that draft or its saved run.
That launch measured about 63,000 rounds/s. Replaying its pinned graph through
the current Release kernel measured 196,349 rounds/s over one million rounds.
It was a different graph and build from the two-million-round/s benchmark.

The corrected UI on port 4474 uses the Release API on port 5453 and the current
constructor graph. A real Chromium browser selected eight workers, warmed the
plan with two million rounds, then launched 100 million rounds through the UI.
With the owner's old eight-worker Debug run still active, it completed in
73.737 seconds at **1,356,171 rounds/s**, with 910 live progress messages.
After the owner authorized cancellation of that competing run, the same UI
workload completed in **49.523 seconds at 2,019,264 rounds/s**, with 615 live
progress messages. The UI displayed **2.0m/s**. Both runs completed all
100 million paid rounds with zero interrupted, cancelled or failed rounds;
the complete payout statistics, histograms and loop evidence were identical.
The old run retained its 39,774,166 completed rounds and terminal result.

These measurements include the API's execution timer, live progress
publication and a subscribed browser. They are separate from the standalone
kernel benchmark above. The [UI report](verification/lab-demo-ui-performance.json)
records pinned config hashes, binary fingerprints, execution settings,
results and concurrent workload; the [screenshot](verification/simulation-live-release.png)
shows the completed UI run.

To reproduce, start the API with `dotnet run --project backend/SlotMath.Api -c Release --no-launch-profile` and the normal database/URL environment.
Point Vite's `SLOT_MATH_API_URL` at that API. In Build, click
**Nine Lives · target 96%** to load the current authored graph before selecting
Simulate, eight workers and the paid-round count. Existing drafts and saved
runs preserve their original graphs. Warm the plan before comparing throughput
and record any other active simulations competing for CPU.

## RTP and correctness

The pinned ten-million-round sample has mean **96.370975%**, variance
395.426987, hit frequency 37.07432%, and maximum 7,899.3 original stakes.
Its fixed-count 95% normal-approximation interval is
95.138468%–97.603482%. These payout statistics remain identical across the
optimized graphs and samplers. This is Sampled evidence; complete exact RTP
for the full cascade/bonus graph is unavailable. The reduced component fixture
has independently derived Exact expectation 1/40.

The independent Python Fraction corpus checks 862 component boards and 22
complete rounds. Exhaustive independent set growth checks all 295 connected
four-cell and 766 connected five-cell placements. Generic tests cover full-width
integers and masks, scoped aliases, lazy reads, error order, cancellation,
separate runners, retained snapshots, cache-budget fallback and trajectories.

## Reproduce

```sh
dotnet build backend/SlotMath.Benchmarks -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo.json \
  --warmup 2000000 --rounds 10000000 --repeats 3 --workers 8 --live \
  --report /tmp/nine-lives-performance-eight-workers.json
```

For the per-worker observation, use `--workers 1 --rounds 1000000`.
Record the model hash, runtime, warmup, worker count and host load with the
result. The canonical interpreter is available through `--reference`.
