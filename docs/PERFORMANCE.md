# Nine Lives graph execution performance

Nine Lives is authored in editable JSON subgraphs. The only added graph
capability is the explicitly approved 64-bit finite-set expression family:
Union, Intersect, Except, Shift, Count and First. The compiler optimizes generic
expressions, storage and control flow; game names, mechanic identifiers and
authored graph patterns do not select kernel implementations.

The original `TestData/LabDemo/lab-demo.json` measurements below and the ported
`TestData/Games/nine-lives.json` shared-subgraph model are different workloads.
The shared graph measured 2.49–2.53M complete paid rounds/s through the Release
UI in three 100M-round runs on eight workers. Its separate evidence is recorded in
[TGSlots optimization evidence](verification/TGSLOTS_OPTIMIZATION.md).

Exact results for games too large to enumerate come from a separate evaluator,
described under [Structured exact evaluation](#structured-exact-evaluation).

## Workload and current target

The current acceptance target is **2,000,000 complete paid rounds/second on
eight workers on this machine**. Every measured round includes triggered free
spins and cascades, complete payout statistics, histograms and live callbacks.
HTTP, queueing, SignalR and rendering are outside this kernel benchmark.
The earlier Release measurement exceeded the target in all three
ten-million-round repeats; concurrent host workloads affect subsequent runs.

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

The earlier standalone eight-worker run measured **2,061,865 / 2,059,413 /
2,065,394 complete rounds/s** (median **2,061,865**), with live statistics,
three ten-million-round repeats and two-million-round warmup. Allocation was
65 B/round; GC pauses were 0 / 2 / 1 ms. The [earlier report](verification/lab-demo-performance-eight-workers.json)
retains the model hash and all payout and loop statistics. Concurrent unrelated
work was left running. The [one-worker report](verification/lab-demo-performance.json)
measured a median of 310,987 complete rounds/s over
three one-million-round repeats with the same two-million-round warmup.

## Benchmark cap units

Review corrected the standalone benchmark's statistics cap: wins are divided
by the compiled WinScale before collection, so the cap must remain **9,999
original stakes**, rather than being multiplied by the scale. The API already
used the correct units; its live UI evidence below is unaffected. Earlier
standalone reports retain historical measurements, but their cap accounting
must not be used as evidence. The payout mean, variance, hit frequency and
observed maximum agree with the corrected run because those samples remained
below the actual cap.

The [post-review report](verification/lab-demo-performance-cap-check.json)
records the corrected scale and cap, cap hits, payout statistics and loop
terminations. Its three ten-million-round repeats measured **1,732,968 /
1,676,031 / 1,608,031 rounds/s** while a Core test process in another checkout
was running and a game started during measurement. These shared-host readings
do not establish throughput on an idle D7 runner. The processes were left
running; their start times and the measurement interval are recorded.

CI also runs `scripts/verification/benchmark_cap.py --check` against a generic
two-point payout graph with WinScale 10 and cap 1×. Seed 42 yields 5,038 cap
hits in 10,000 rounds, mean 0.7519, and maximum 1×. This independently pinned
control fails if the CLI multiplies its statistics cap by the payout scale.

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

## Structured exact evaluation

### Generic matrix-law and transform comparison (2026-10-21)

The generic controls below ran in Release on .NET 10.0.13 on this shared
20-logical-CPU host. Variants ran sequentially in ABBA/BAAB order, eight process
launches per workload and six evaluations per launch. The first two evaluations
of each launch were excluded, leaving sixteen warm observations per variant.
The harness forces collection before each evaluation; the one-minute host load
was approximately 8.48 at the start and 9.48 at the end. These are local
comparisons for the stated inputs, not performance promises for other games.

| Workload | Control kernel median | Optimized kernel median | Control wall median | Optimized wall median | Managed bytes per evaluation, control → optimized |
| --- | ---: | ---: | ---: | ---: | ---: |
| Generic deterministic two-phase loop, cap 100,000 | 153.56 ms | 0.726 ms | 153.82 ms | 0.918 ms | 338,554,948 → 142,776 |
| Sum of two independent 4,096-outcome numeric draws, complete law | 258.53 ms | 19.78 ms | 258.74 ms | 20.02 ms | 4,876,856 → 5,108,112 |

The loop comparison disables/enables loop matrices in the same binary. Auto
can now prove the complete reward law from zero conditional variance without
carrying intermediate payout totals in its state. It retains the one-atom law,
reduces terms from 100,018 to 20, and work from 800,048 to 611. The observed
kernel speedup is about 212x, wall speedup about 168x and managed allocation
reduction 99.958%. This example is deterministic; it is not evidence for those
multipliers on a general random reward chain.

The numeric-sum comparison disables/enables the adaptive exact NTT/CRT path.
Both variants return all 8,191 payout atoms. The observed kernel speedup is
13.07x and wall speedup 12.93x, with 4.74% more managed allocation. Work counters
are 16,818,199 versus 434,453; these deterministic counters are not elapsed-time
multipliers. Small, sparse and excessive-span laws retain direct convolution.

Every observation agrees on the complete canonical payout-law SHA-256, exact
RTP, second moment and hit frequency. Both variants use Core binary SHA-256
`5ca28087db16eefebb9f06316f5585b05431778f8ce1d489695ee46d5df536c2`.
The cycle configuration identity is
`d3a638df1cbab6a3cccee7836b807533b2f93ce6835f2658793b867d80dc43e4`;
the numeric-sum identity is
`9bc8c7831e852bc2e46eb305bea47b9d62aa8d0b1dd25a9859acf17e615d6a75`.
Raw reports and logs remain local under `/tmp/principal-benchmarks`, including
`summary.json`; they are not source-controlled generated artifacts.

Reproduction commands, remaining benchmark dimensions and implementation scope
are in the [math engine roadmap](MATH_ENGINE_ROADMAP.md).
The source-level acceptance at this point is 79 selected mathematical tests,
including the prior matrix regressions and new Auto, NTT/CRT and complete-law
risk cases. After a formatting-only root-search rewrite, the 16 affected
convolution cases were rerun successfully. The Release solution build, both
JetBrains cleanup profiles on touched sources and `dotnet format` verification
also passed. Full, API integration and browser suites were not required for
these core-only additions.

### Scope and earlier structured measurements

A round with too many outcomes to enumerate can still have an exact RTP.
`StructuredExactEvaluator` computes the exact rational RTP, and where they fit
its allowances the exact hit frequency, variance and payout distribution, from
the structure of the graph
([method](architecture.md#structured-exact-evaluator-structuredexactevaluator)).
It answers only what it can model: Nine Lives, whose cluster search and round
cap need the whole law of a thirty-cell board, is declined, and its RTP stays
Sampled ([LAB_DEMO.md](LAB_DEMO.md)).

The measurements this section carried were taken on the retired demo graph and
were removed with it, rather than attributed to another graph. On this
workstation (i5-13600KF, one thread, default options) the games that are
evaluated exactly today took 12 to 21 s for the full Ancient Dragon round and
20 to 25 s for the full Woodland Whisper round, both by expectation only and
both on a machine that was busy with other work; their cut-down models, with
the whole distribution, take under a second. A
request through the API is given twelve seconds, so the two full rounds are
reported there as `BudgetExceeded`, never as a number.

What the profile of the evaluator showed and what changed:

- Law values were sorted by a recursive comparison of tuples for every law built
  (19% of samples). A law is now listed by the hash of its values, and the total
  order is consulted only among values whose hashes collide.
- `ExprValue.GetHashCode` combines five fields for every element of a tuple (15%).
  The evaluator has its own hash, a function of the value alone, so the listing
  order, and with it the work counted, is the same in every process.
- Every new table allocated a `HashSet` to collect its distinct values (12% in
  resizing). They are collected in a 65-entry buffer by hash, in first-seen order.
- Masses were accumulated in a `Dictionary` per law, hashing every value again
  at every mixture. An open-addressing accumulator takes the hash kept by the
  source law; a value is hashed once however many mixtures it passes through.
- Arrays of tables that turn out to exist already are reused for the next table.

EventPipe thread-time samples land on GC safepoints, so allocation sites are
over-represented; `--allocations` and the collector's own pause counters
(reported by `--exact`) were used to tell allocation from computation.

Limits, stated plainly: for the full Ancient Dragon and Woodland Whisper rounds
the payout distribution, and with it maximum win, the hit frequency and the
variance do not fit their allowances; those metrics come from Sampled. Plugins,
all opaque map transforms (including legacy built-ins), and wins stored as state
data are declined.

```sh
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/Games/ancient-dragon.json \
  --warmup 1 --exact --repeats 4
# --exact --allocations prints allocated bytes by type for one evaluation.
```

### Correctness gates of the structured evaluator

`StructuredEquivalenceTests` generates 10,000 graphs (CsCheck, pinned seed,
shrinking) with draws, dependent state, loops, conditionals, folds, failing
expressions and win caps, and compares the structured result with the classic
exact interpreter: the whole payout distribution by rational equality, and the
hit frequency derived without the distribution against the distribution's. A
case the classic interpreter cannot finish, or where the reference fails, must
be declined, never answered. Seven deliberately seeded defects (a merge keeping
the wrong arm, dependent sums convolved, pseudo-variables shared between
dependent parts, errors ignored, the cap ignored, hit frequency taken from the
first part, the second moment of dependent factors taken as a product) each fail
the gate. Recorded
counts: 7,157 graphs equal distribution for distribution, 6,733 of them with the
structural hit frequency checked as well; 2,338 declined where the reference
fails; 504 not compared because the reference exceeds its 10,000-branch limit;
one declined on its work budget. No case answered differently from the reference,
or answered where the reference fails. The seeded defects fail 19 to 85 of
their 2,000 cases each.

The D9 fixtures, authored as graphs: REF-A gives RTP 3/4, variance 15/16 and hit
frequency 1/2 by both derivations; REF-C equals the classic interpreter and the
closed form 94/1000 · (1 − (35/1000)^10) / (1 − 35/1000), within 10⁻¹² of 94/965,
and the same closed form at a cap of 4,000. REF-B, whose retrigger loop is solved
as a Markov reward chain, equals the classic distribution at caps 1, 3 and 6 and
at its cap of 1,000 is `Exact` in 2 to 3 s: the RTP equals an independent
recurrence over the number of spins left, rational for rational, and lies in
(15/17 − 10⁻⁹, 15/17]. At the largest cap the schema allows, 100,000, both
fixtures end as `ExactInterval` with `boundSource = DeclaredWinCap`, enclosed
within 10⁻²⁴: REF-B in 4 to 6 s with 2.4·10⁻²¹⁸ of pruned mass, REF-C in 0.1 s
without pruning. With pruning forced early, the pruned mass reported equals the
mass an independent recurrence leaves running at that iteration, and the exact
value lies inside the interval.

Loops solved as chains have their own gates on generated feature games (a
trigger, then a loop of free spins with retriggers, counters, sticky state and
multipliers), each run once with the loop's full law and once with its moments
only. Of 10,000 games, the law run equals the reference distribution for
distribution in 9,815 and declines 7; the moments run equals it in 8,023 (1,496
of them with every metric, 6,527 on the mean, where the reference has no cheaper
second moment to compare), with 178 not compared and 1,799 declined where the
reference answers. The gates kept in the suite run 5,000 games per mode. Five
seeded chain defects (rewards taken unconditionally, the cross term of the
second moment dropped, the zero-payout mass taken unconditionally, the cap off
by one, products of dependent factors taken as independent) fail 84 to 576 of
1,000 games each. With pruning forced on 3,000 games, every result that was
pruned must enclose the reference's value in the interval it reports, and at
least 300 must be pruned for the gate to count; reporting the interval without
its pruned mass fails the gate.

The reel gate generates 5,000 configurations containing short reels, multiplier ports,
loops, branches and opaque maps. The review rerun compared 831 unnamed-map configurations
by rational distribution equality, explicitly declined 3,885 opaque-map configurations and
left 284 outside the classic reference's branch budget. The structural hit frequency and
second moment matched in all 831 comparisons. Separate deterministic fixtures verify that
the classic interpreter still pays legacy named maps after structured evaluation declines.

Line and ways expectations are tested using payouts authored from ordinary ModifyState
nodes and typed expressions. The five-reel, fifteen-stop case compares with the classic
interpreter over all 759,375 boards. Fifteen independent cells with ten faces each represent
10¹⁵ boards; their exact expectations are checked against independent formulas (explicit
line combinations and per-reel match-count moments). The earlier timings for specialized
native evaluator splitting do not apply to this implementation and have been removed.

The review rerun used Release, .NET 10.0.12 on the shared workstation. The authored
five-reel cases took 10.803 s (line expressions) and 0.032 s (ways expressions), against
5.853 s and 5.846 s for the independent classic reference using legacy evaluators.
The 10¹⁵-outcome expectations took 20.054 s and 1.123 s. These are shared-host
observations, not a D7 runner certificate; their independent rational checks are retained
in `StructuredEquivalenceTests`.

The games ported from TGSlots hold the evaluator against games it did not grow
up on (`SlotMath.Core.Tests/Math/Games`). For the full Ancient Dragon and
Woodland Whisper rounds the structured RTP has to agree with the original game,
and the same value shifted is rejected. For the cut-down model of every game
the structured result, the classic interpreter and the hybrid evaluator have to
give the same rationals. Where the full round is beyond the evaluator (X7 Club,
Nine Lives, Le Militare) it has to decline within its budget and return no
number. `StructuredLabDemoTests` holds the same on the graphs of the lab demo.

## Reproduce

```sh
dotnet build backend/SlotMath.Benchmarks -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo.json \
  --warmup 2000000 --rounds 10000000 --repeats 3 --workers 8 --live \
  --report /tmp/nine-lives-performance-eight-workers.json
```

For the shared graph port, select **Ready subgraphs → Open Nine Lives model** in
Build, then Simulate with eight workers. The corresponding standalone command is:

```sh
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/Games/nine-lives.json \
  --warmup 2000000 --rounds 100000000 --repeats 3 --workers 8 --live \
  --report /tmp/tgslots-nine-lives-performance.json
```

Its exported model hash is
`32d8a2b32e09c55e97cb14d2bd613c619f45d1331822d5553eb0c484bc471b6c`.
The comparison between old and table-driven settling in
[TGSLOTS_OPTIMIZATION.md](verification/TGSLOTS_OPTIMIZATION.md) uses five million
rounds per observation. The standalone timing excludes HTTP, queueing and SignalR;
use a Release API run to verify the full application path.

For the per-worker observation, use `--workers 1 --rounds 1000000`.
Record the model hash, runtime, warmup, worker count and host load with the
result. The canonical interpreter is available through `--reference`.
