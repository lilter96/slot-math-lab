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
Exact results for games too large to enumerate come from a separate evaluator,
described under [Structured exact evaluation](#structured-exact-evaluation).

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

## Structured exact evaluation

Enumeration of the full Dog House round exceeds any budget: five reels of base
stops, then up to 27 free spins whose sticky Wilds make every spin depend on the
ones before. `StructuredExactEvaluator` computes the exact rational RTP and the
exact hit frequency of that graph from its structure
([method](architecture.md#structured-exact-evaluator-structuredexactevaluator)).

Measured on the workstation above (i5-13600KF), one thread, full `dog-house-ui.json`,
default options:

| | Before tuning | Now |
| --- | --- | --- |
| Whole evaluation, warm | 3,985 ms | 1,596 ms |
| Whole evaluation, first run in the process | 5,768 ms | 3,204 ms |
| Allocated per evaluation | 2,262 MiB | 1,778 MiB |

The evaluation covers 233 random variables and builds 102,525 terms and 49,269
laws in 27,991,118 units of deterministic work; the counts are identical before
and after, because the tuning changed no decision of the algorithm. The phases
are the expectation (4.5M work), an attempt at the full payout distribution that
is abandoned at its 4M allowance, and the hit frequency (19.3M work).

Since then the evaluation also attempts the variance by second moments, which is
abandoned at its own 4M allowance on this game. With that attempt the whole
evaluation is 32,073,846 units of work, 112,125 terms and 58,131 laws: 1.9 to
2.0 s warm, 3.9 s on the first run in the process, 2,272 MiB allocated.

What the profile showed and what changed:

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

Limits, stated plainly: the payout distribution of the full round, and with it
maximum win, is not computed (the distribution attempt does not finish within
400M work), and the variance by second moments does not fit its 4M allowance
either; those metrics come from Sampled. Plugins, evaluators and transforms
outside the built-in `lines`, `ways` and `cluster`, and wins stored as state
data are declined.

```sh
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --graph backend/SlotMath.Core.Tests/TestData/DogHouse/dog-house-ui.json \
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

Reel strips and the built-in evaluators have a gate of 5,000 generated reel
games: two to four reels of two to five stops, one to three rows, payouts that
are and are not whole win units, paylines that are short or leave the board,
`lines`, `ways` and `cluster`, multiplier ports, respin loops, two spins in a
round, a branch between two evaluators. 4,716 equal the reference distribution
for distribution, 2,586 of them with the structural hit frequency and second
moment checked as well; 284 are not compared because the reference exceeds its
branch limit; none is declined and none differs. Dropping one payline or one
paytable symbol from the split fails 333 of 1,000 games.

At the usual size, five reels of fifteen stops with three rows and twenty
paylines, the structured RTP equals the reference's over all 759,375 boards:
0.75 s against 3.3 s for `lines`, 0.1 s against 2.9 s for `ways`. Fifteen cells
drawn one by one from ten faces of unequal weight make 10¹⁵ boards, which nothing
lists; there the result equals formulas written out from the rules (a sum over
the 10⁵ face combinations of each payline; a closed form in each reel's expected
match count for `ways`), in 21 s and 11 s.

`StructuredDogHouseTests` holds the full game against the independent
Fraction-based rational, the reduced model's whole distribution against the
classic interpreter, and the exact hit frequency 9743/35280 against two million
pinned sampled rounds within four standard errors; a single weight unit moved in
the graph, and the hit frequency moved by one percent, are each rejected.

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
