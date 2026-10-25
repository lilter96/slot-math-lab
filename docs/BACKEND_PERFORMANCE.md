# Backend performance

The acceptance target remains **1,000,000 complete configured rounds/second on
one worker for every full game**. Awarded features, bought bonuses and cascades
belong to their triggering round. Internal steps are never counted as additional
rounds. The target is not achieved.

## Exact integer floor division

A managed CPU/allocation profile of the complete `le-militare.super` graph
identified numeric dispatch and rational division/reduction as major costs.
The compiler now lowers the ordinary expression `floor(a / b)` to one operation.
When both operands are inline integers, it calculates the signed quotient and
corrects negative nonintegral results toward minus infinity. It avoids creating
and reducing a rational only to discard its fractional part immediately.

Zero denominators, `Int64.MinValue / -1`, wide integers, proper rationals and
invalid types use the original exact operations. Both operands retain eager
left-to-right evaluation. Lazy parents and located errors retain their behavior.
The native compiler and cold closures share this lowering; the independent
canonical evaluator is unchanged. This adds no DSL function, mechanic dispatch,
authored-graph recognition or game-specific code.

### Verification

The final Release build, scoped formatting and both Rider cleanup profiles
passed. The affected selection passed 38 tests, including 12 new cases covering
an independent `BigInteger.DivRem` floor oracle, signed/extreme integers, exact
fallbacks, error order, lazy parents, iterator indices and shared code shapes.

Separate frozen old/new kernels replayed the same 15 full configurations, each
with 65 seeds (`0..63` and `UInt64.MaxValue`), both traced and untraced. All 1,950
comparisons preserve payouts, complete exported state, the RNG audit and the
following random word. State comparison uses an explicit typed canonical
encoding and SHA-256. It does not use process-local `ExprValue.GetHashCode`:
the existing `StateHasher` fallback for that type is unsuitable for comparing
independent processes. No state fields or random draws are excluded.

### Paired full-round measurements

Both kernels use identical current JSON and benchmark code. Each case runs in
old/new/new/old process order, with one worker pinned to CPU 2, compiled sampling,
live progress and full statistics. Each process measures three repetitions after
warmup; its first repetition is discarded, leaving four observations per kernel.
The diagnostic uses `DOTNET_TieredCompilation=0`. These are shared-host
observations, not dedicated-host or API capacity guarantees.

Feature cases measure 4,096 complete rounds after 2,048 warmup rounds. Other cases
measure 131,072 after 32,768 warmup rounds. Counts, RTP, variance, hit frequency,
maximum payout, cap hits, histograms and loop termination evidence match exactly
across every old/new measurement.

| Full configuration | Before rounds/s | After rounds/s | Ratio | Before B/round | After B/round |
| --- | ---: | ---: | ---: | ---: | ---: |
| le-militare.super | 717 | 2,524 | 3.523 | 177,377.6 | 83,073.3 |
| le-militare.elite | 931 | 3,528 | 3.790 | 119,240.4 | 44,338.9 |
| le-militare.standard | 1,961 | 9,032 | 4.606 | 42,233.2 | 4,122.5 |
| le-militare.spin | 86,910 | 111,471 | 1.283 | 296.3 | 49.2 |
| nine-lives | 359,225 | 367,685 | 1.024 | 47.1 | 47.1 |
| woodland-whisper | 225,416 | 228,014 | 1.012 | 24.8 | 24.8 |

Nine Lives ranges overlap (356,089–362,213 before and 360,765–373,264 after),
as do Woodland ranges (224,090–227,042 and 224,356–229,689). No reliable
throughput gain is established for these holdouts. All three heavy feature
ranges are disjoint. The earlier [all-game baseline](GAME_PRIMITIVE_MIGRATIONS.md)
uses its own paired measurements; gains above use adjacent old/new runs rather
than dividing timings collected at different times.

This compiler change leaves authored graph sizes unchanged. Existing game graph
reductions remain documented separately. Continue profiling the resulting
backend, reducing array work and admitting further generic optimizations and
editable game migrations before resuming UI work.

## Retaining more small arrays within the existing element budget

On 1,024 seeded complete rounds per configuration, the old arena retained 256
buffers but only 5,691 of its permitted 16,384 elements for super/elite. The
buffer-count cap prevented it from using the remaining payload capacity. The
count cap is now 1,024, while the element cap remains 16,384. This permits more
small buffers; it also permits more array headers and bucket metadata. Nothing
preallocates the full capacity, and buffers remain distinct inside each round.

The same audit now retains 752–753 buffers and 16,383 elements for super/elite.
Their mean small-buffer misses fall from 223.90 to 72.59 and from 117.55 to
29.27 per round. Nine Lives Buy falls from 20.76 misses to zero. Woodland Buy
and X7 Buy already fit the old limits and remain zero-miss controls. These are
observed samples, not allocation-free guarantees for every possible round.

The Release build and both cleanup profiles passed for the two touched files.
The affected selection passed 80 cases. After resolving a formatter disagreement
in an existing test fixture, both profiles and the final scoped format check
passed again for that file, followed by its nine tests. The kernel binary stayed
identical through that test-only adjustment. All 1,950 complete-state/RNG replay
comparisons against the previous floor-division kernel match.

The following adjacent old/new measurements isolate the cache change, using the
same ABBA method above. Buy cases use 16,384 measured and 8,192 warmup rounds;
feature and ordinary-spin schedules retain the previous counts. Every full
statistical result is identical across repetitions and variants.

| Full configuration | Before rounds/s | After rounds/s | Before B/round | After B/round |
| --- | ---: | ---: | ---: | ---: |
| le-militare.super | 2,512 | 2,556 | 83,073.3 | 26,606.9 |
| le-militare.elite | 3,558 | 3,604 | 44,338.9 | 9,990.5 |
| le-militare.standard | 9,074 | 9,178 | 4,122.5 | 499.5 |
| le-militare.spin | 112,788 | 112,214 | 49.2 | 25.0 |
| nine-lives.buy | 19,165 | 19,786 | 3,440.6 | 71.8 |
| woodland-whisper.buy | 16,381 | 16,379 | 61.3 | 61.3 |
| x7-club.buy | 152,295 | 152,070 | 60.8 | 60.8 |

Super improves by 1.75%, with disjoint ranges of 2,506–2,531 and 2,538–2,570.
The other ranges overlap; their medians do not establish a throughput gain.
This change chiefly reduces allocation and is retained for that result. It does
not materially close the remaining gap to one million complete rounds/second.

## Mapping profiles to authored operations

Generated sampling methods now have stable `sml_*` names derived from their code
shape. Code cache keys are unchanged. The benchmark's optional
`--profile-symbols /tmp/profile-symbols.json` writes a bounded map from method
names to source node IDs or expression JSON. All source bindings of shared code
remain represented. The file includes the configuration hash, kernel binary
SHA-256, sampling tier and an explicit truncation flag; the limit is 4,096 sites.
Names and metadata are produced during code generation, without instrumenting
individual rounds. Capture is disabled unless explicitly requested.

Both Rider profiles, the Release solution build and scoped formatting passed for
the four source files. The affected selection passed 36 tests, including two
new controls for actual method names, shared code with distinct bindings and
located errors, node provenance, and 64 independent draw/RNG audit controls.

For a normal-runtime profile, leave .NET tiering/PGO overrides unset, use the
service's `Tiered` sampling mode, and wait until the symbol file is written after
warmup before attaching. Capture the same complete configuration on one worker.
Retain the binary and map with the trace. Profiling and intentionally interrupted
long runs are not throughput measurements.

The installed `dotnet-trace` 10 supports managed thread-stack sampling and runtime
GC/JIT events. Its managed samples describe thread stacks over time; kernel CPU
sampling is a separate `collect-linux` mode. The host's unprivileged perf policy
currently prevents the latter; no host policy was changed. See the
[Microsoft tracing documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).
JetBrains dotTrace 2026.2.3.1 also successfully captured a 20-second Sampling
snapshot with `CpuInstruction` measurement after actual warmup. The CLI procedure
follows the [JetBrains profiling documentation](https://www.jetbrains.com/help/profiler/Performance_Profiling__Profiling_Using_the_Command_Line.html).
The snapshot is retained for independent inspection; collecting it alone does
not establish agreement between profilers.

Aggregate trace summaries can mix the simulation and tracing-control threads.
Analyze the time-weighted execution-thread stack intervals, remove synthetic
time-marker frames when assigning self time, and preserve source-map ambiguity
when several bindings share one method. A short manifest-control thread must
not become the apparent dominant game cost.

Likewise, `PollGC` stack samples are not a measurement of GC pause duration.
The unprofiled super cache-control run measured a median 1.254 ms of GC pauses
in 1,602.719 ms, versus 0.515 ms in 1,136.579 ms for elite and zero pauses for
standard. The cache's allocation reduction does not justify assigning those
stack samples to actual collector pauses.

The named profile of the normal .NET/Tiered super round still places about
14–15% of execution-thread stack time in the weighted group-size block. That
block scans all cell weights for each group. Branch/loop control also remains
prominent, while JIT inlining changes attribution relative to the earlier
optimized-JIT diagnostic. Further fixes must be selected and re-profiled in the
normal runtime mode, with separate unprofiled full-round comparisons.

## Hardware samples and binary cell contributions

Privileged, process-scoped kernel sampling subsequently became available through
the host account's existing permissions. The host perf policy stayed unchanged.
The successful Linux perf capture uses a monotonic clock (`record -k 1`), .NET
JIT maps, `inject -j` and an explicitly selected owned process. Its 19,956 CPU
samples had no recorded loss. Weighted group sizing accounted for 15.11% of
CPU samples, tumble settlement for 8.52%, loop control for 7.75%, and the branch
wrapper for 2.69%. Managed stack-residency attribution had overstated the latter.
The .NET kernel trace collector also succeeded, but its bundled reader rejected
a label kind; that undecoded trace is not used as CPU attribution evidence.

Terminal-branch fusion passed 108 affected tests and 1,950 full-state/RNG controls,
but paired measurements across all 15 games established no throughput benefit.
A cached frame-array reference and invariant-mask-load prototypes also failed
their real-game performance controls. These experiments are not shipped.

The admitted change removes repeated algorithmic work in authored JSON. Le
Militare's cell contributions are binary: a cell counts zero or one. A giant
wild counts at its top cell. A 30-bit `countedCells` mask replaces the private
30-element `cellCounts` array, and group size becomes
`popcount(group & countedCells)`. A six-element fold over armed columns prepares
the mask using ordinary If, Fold, index and finite-set expressions. Numeric
weights remain available through the generic cluster authoring helper; an
optional `weightsMask` selects the editable binary-mask composition. Weighted
traversal and pruning rules retain their previous behavior.

The two game generators and all eight full/six small Le configurations were
updated immediately. No mechanic-specific kernel, compiler or API behavior was
added. The original server and independent golden answers are unchanged. One
derived client-state cache was refreshed for the private representation change.
All 2,048 old/new seeded traced/untraced controls preserve payout, remaining
state, RNG audit and the following word. The only state differences are retired
`cellCounts`, replacement `countedCells` and its constant column-mask table.
Each replacement mask is also checked against the old cell vector.

The maintained regression checks every armed-column state and individual cell
basis vector against an independent geometry-based weight oracle, including
empty/full/mixed groups and the small board. Final validation passed 32 selected
Core cases and one small-model exact comparison in its original branch budget.
The Release build, scoped formatting, TypeScript check, two-file ESLint and
38-fixture export check passed. Both Rider profiles completed for all 18 touched
source files, including the TS/JSON solution-item scope. JSON whitespace grew
under mandatory formatting; canonical full-graph JSON shrank by 1,256 bytes per
configuration. Stored graph node counts are unchanged by this representation.

### Actual maintained configurations, normal runtime

One frozen kernel runs both JSON versions on CPU 2 with one worker, normal .NET
tiering/PGO and the service's Tiered sampling mode. ABBA processes each retain
two of three measurements. All cases warm 32,768 complete rounds; feature cases
measure 16,384 per repetition and ordinary entries measure 131,072. Full outcome
statistics and loop evidence match exactly. Timing ranges are disjoint for all
eight admitted cases. These remain shared-host kernel measurements.

| Complete configuration | Before rounds/s | After rounds/s | Ratio |
| --- | ---: | ---: | ---: |
| le-militare.spin | 154,469 | 214,471 | 1.388 |
| le-militare.standard | 11,072 | 14,196 | 1.282 |
| le-militare.super | 3,183 | 3,838 | 1.206 |
| le-militare.elite | 4,441 | 5,549 | 1.250 |
| le-militare.chance | 96,455 | 128,145 | 1.329 |
| le-militare.air-raid | 129,225 | 182,012 | 1.409 |
| le-militare.siege.spin | 154,436 | 210,367 | 1.362 |
| le-militare.recon.spin | 148,767 | 205,265 | 1.380 |

A preliminary short Standard run appeared slower. Increasing warmup and measured
rounds removed that result; the longer repeated control and final maintained
measurements both show a gain. The short run does not establish a steady-state
regression or isolate a specific JIT cause. The one-million complete-round target
remains open for every configuration.

## Preparing immutable adjacency and reducing component graphs

The first direct Reachable substitution repeated validation of every adjacency
row on each call and was slower on the current games. The compiler now validates
and snapshots a proven immutable adjacency field once. Each evaluation still
checks its dynamic allowed/seed masks and performs the same directed traversal.
Mutable fields and invalid constants retain the original checked execution,
including eager operand errors and lazy parents. Cancellation checkpoints and
the complete unsigned 64-vertex domain remain supported. Shared generated code
keeps each configuration's adjacency in its own operands. This extends no DSL
and recognizes no game, mechanic or authored graph pattern.

The shared component authoring helper immediately uses this existing atom in
all ten full cluster configurations, their eight small models and two authoring
starters. A completion expression and frontier reset replace repeated growth;
seed order, wild claims and payouts are unchanged. Each stored graph loses five
nodes. Every full graph is 2,906 canonical UTF-8 bytes smaller, including its new
constant adjacency data. Numeric JSON masks stay exact through 53 cells; larger
JavaScript-authored boards retain grid expressions. The kernel still supports
all 64 vertices. Explicit grid composition also preserves older saved contracts.

All 5,120 old/new traced/untraced controls across the 20 maintained graphs match
payout, remaining exported state, RNG audit and the following random word.
Differences are limited to the constant adjacency table and the exact retired
growth-loop iteration/wins/exit-reason fields. Original independent and server
goldens remain unchanged; two derived client-state caches were refreshed.

Final verification passed 81 affected Core cases and three small exact models
with their original expected rationals and budgets. Source/payout authoring
checks cover numeric and binary weights, renamed closures, old grid graphs,
adjacency tampering, batch-output writes and expression dependencies. All 16
focused unit cases pass. Two additional lifecycle controls pass; three asset
editing lifecycle cases fail identically on the baseline commit and this branch.
Their evidence is retained separately for the deferred editor work. The Release
solution build, scoped formatting, scoped ESLint, app TypeScript and 38-fixture
export check pass. Both required Rider profiles completed for all 34 touched
sources; compatibility edits received both profiles again before final checks.

### Final complete-round comparison

Frozen original and prepared kernels run their respective actual maintained
JSON on CPU 2, one worker, normal .NET tiering/PGO and Tiered sampling. Each ABBA
process measures three repetitions and discards its first. Heavy features and
Nine Lives buy warm 32,768 and measure 16,384 rounds; paid entries warm 262,144
and measure 1,048,576. The longer paid schedule avoids treating short transient
runs as steady-state estimates; it does not isolate a particular JIT cause.
All outcome statistics and remaining loop evidence match. Every retired growth
loop has zero model-limit completions. All ten retained timing ranges are disjoint.
These are shared-host kernel observations, not API capacity guarantees.

| Complete configuration | Before rounds/s | After rounds/s | Ratio |
| --- | ---: | ---: | ---: |
| le-militare.super | 3,937 | 4,057 | 1.030 |
| le-militare.elite | 5,510 | 5,752 | 1.044 |
| le-militare.standard | 14,069 | 14,715 | 1.046 |
| le-militare.spin | 211,588 | 221,286 | 1.046 |
| nine-lives | 435,969 | 448,769 | 1.029 |
| nine-lives.buy | 24,537 | 25,212 | 1.028 |
| le-militare.chance | 129,416 | 133,471 | 1.031 |
| le-militare.air-raid | 180,855 | 189,438 | 1.047 |
| le-militare.siege.spin | 209,681 | 222,194 | 1.060 |
| le-militare.recon.spin | 206,315 | 213,766 | 1.036 |

Allocation falls slightly, rather than becoming zero: Super changes from
22,090 to 22,084 B/round and Nine Lives from 42.9 to 41.5 B/round. The complete
round denominator is unchanged, including awarded or bought features.

A fresh process-scoped hardware profile of the final Super configuration has no
lost samples. The mapped settlement block accounts for 10.85%, loop control
6.86%, pay/tumble preparation 5.90% and prepared reachability 3.03% of CPU samples.
Several generated methods serve multiple source bindings; the symbol map retains
all of them. Profiling runs are not used as throughput measurements. Board and
cascade expression work remains the main area for subsequent backend changes.

A separate flat-strip refill prototype preserved all state/RNG controls but
slowed four full games by roughly 2–7%; it is not shipped. Forced inlining of
the integer value constructor also failed its real-game controls. Neither a
smaller graph nor an isolated helper speedup establishes a complete-round gain.
The one-million complete-round target remains open for all 15 configurations.

## Bounded integer lowering in sampled expressions

A native instruction profile of the maintained Super configuration shows
repeated value construction and 16-byte temporary copies in settlement/index
arithmetic. The sampling compiler now proves signed Int64 bounds through
literals, iterator indices, established mask fields, set cardinality/first,
addition, subtraction, multiplication, floor division by a nonzero singleton
divisor, and conditionals. Every arithmetic intermediate must fit, not merely
the final result. Compile-time bound calculations use BigInteger.

Admitted expressions use machine integers until a value boundary. Index helpers
accept proven integer positions and retain the original checked fallback.
Cardinality and first-set-bit operations produce a machine integer directly in
integer contexts. Lazy scalar reuse stores these results separately from boxed
values, with distinct cache keys/code shapes and the same per-iteration and
per-invocation resets. Zero literals lower to the exact default integer value;
other literals retain their parameter bindings. Precomputation annotations keep
the existing finite-table path. Unknown ranges, overflow, proper rationals,
wide integers and malformed operands retain the exact implementation and its
located errors. No public atom, expression name or mechanic dispatch is added.

The source game JSON is unchanged: existing compositions receive this generic
lowering automatically. Original server replays and played traces remain
unchanged. Final verification passed 166 affected Core tests, Release solution
build, scoped formatting and both required cleanup profiles for all four
sources. Two subsequently edited files received both profiles again. Independent
signed-floor, overflow, UInt64 high-bit/empty-set, lazy/error-order, alias/reset
and shared-code tests accompany the change.

Separate frozen kernels replayed all 15 full configurations, 65 seeds including
UInt64.MaxValue, traced and untraced. All 1,950 payout, complete exported-state,
RNG-audit and following-word transcripts match without state exceptions. The
comparison uses a typed canonical SHA-256 transcript across processes.

### All-game complete-round comparison

Both kernels use identical maintained JSON, one worker pinned to CPU 2, normal
.NET tiering/PGO, Tiered sampling, live updates and full statistics. Each ABBA
process runs three repetitions and discards its first. Paid entries and X7 buy
warm 262,144 rounds and measure 1,048,576; other buys warm/measure 65,536.
Le Super/Elite warm 32,768 and measure 16,384; Standard measures 32,768.
Every outcome statistic and every loop-termination statistic match exactly.
Nothing is filtered from these comparisons.

The first Nine Lives buy series showed a 1.1% loss. A longer affected-case
control warms 131,072 and measures 262,144 rounds per repetition. Its retained
ranges overlap: 25,039–25,112 before and 24,976–25,069 after. No stable gain or
regression is established there; the table uses this longer control. This
experiment does not isolate a particular JIT or scheduling cause. The initial
candidate with boxed set results was superseded by direct integer set lowering.

| Complete configuration | Before rounds/s | After rounds/s | Ratio |
| --- | ---: | ---: | ---: |
| ancient-dragon | 310,981 | 336,948 | 1.084 |
| nine-lives | 447,419 | 451,286 | 1.009 |
| nine-lives.buy | 25,075 | 25,043 | 0.999 |
| woodland-whisper | 277,284 | 312,353 | 1.126 |
| woodland-whisper.buy | 21,204 | 24,101 | 1.137 |
| x7-club | 393,559 | 450,159 | 1.144 |
| x7-club.buy | 186,818 | 189,486 | 1.014 |
| le-militare.spin | 218,512 | 232,289 | 1.063 |
| le-militare.standard | 14,658 | 15,646 | 1.067 |
| le-militare.super | 4,056 | 4,327 | 1.067 |
| le-militare.elite | 5,686 | 6,067 | 1.067 |
| le-militare.chance | 133,397 | 144,919 | 1.086 |
| le-militare.air-raid | 187,532 | 196,387 | 1.047 |
| le-militare.siege.spin | 221,486 | 232,613 | 1.050 |
| le-militare.recon.spin | 213,506 | 226,766 | 1.062 |

Twelve configurations have disjoint improved timing ranges. Nine Lives, its buy
and X7 buy are neutral holdouts with overlapping ranges. Allocation is largely
unchanged; this removes instruction/value movement overhead, not all allocation.
These are shared-host observations rather than API capacity guarantees. Bought
and awarded features remain part of their complete round.

A separate attempt to cache every numeric literal as a prepared ExprValue slowed
Nine Lives by 3.2% with disjoint ranges and is excluded. Another authored
weighted-count upper-bound prototype improved heavy Le cases by 3–5% but changed
private untraced group scratch state; it remains outside maintained graphs.
The one-million complete-round goal for all 15 configurations remains open.

## Stationary carried flags in authored games

The next migration removes repeated work in the actual Le Militare graphs.
During free spins, held flags cover complete armed columns. Paid cells exclude
held cells, so those columns have no removals. Other columns contain only zero
flags and refill with zero. Ordinary gravity therefore leaves the entire held
vector unchanged. The generic Carried authoring option `stationary` lets a caller
state that invariant: the composer omits the settlement write while retaining
survivor bookkeeping. The kernel receives ordinary editable JSON, without a new
atom, evaluator or mechanic-specific branch. Paid-spin carried values still move.

The existing binary contribution mask also identifies held giants: below-top
cells excluded from counting, union their one-row upward shift, cover exactly
the armed columns. In free spins with at least two rows, held-cell exclusion is
one mask subtraction instead of a per-cell fold. Paid rounds and one-row boards
retain the original fold. Arming updates the contribution mask before payout.

All eight full and six small configurations were regenerated immediately. Each
loses one stored node; full Super shrinks from 231 to 230 nodes and 287,868 to
286,185 canonical UTF-8 bytes. No state fields are retired or added. Original
server/math goldens and derived client traces remain unchanged. All 3,584
traced/untraced seeded comparisons preserve payout, complete exported state,
RNG audit and the following word, without exceptions. Every outcome and loop
statistic is also identical in the paired performance runs.

The maintained regressions evaluate the authored protection against independent
cell flags for every armed pattern, removal basis/mixed masks and bonus mode,
then exhaust every permitted column-removal pattern through the ordinary carried
operation. Full and small boards are covered. Final validation passed 30 affected
Core cases, the small Standard exact law in its original budget, nine payout
editing cases, Release solution build, scoped formatting, two-file ESLint, app
TypeScript and 38-fixture export check. Both required cleanup profiles completed
for all 17 sources; the TS/JSON scope used the isolated solution-item workflow.

### Actual maintained full configurations

One frozen accepted integer-lowering kernel runs both versions of the actual
maintained JSON. CPU 2, one worker, normal .NET tiering/PGO and Tiered sampling
remain in force. Each ABBA process measures three repetitions and discards its
first. Super/Elite warm 32,768 and measure 16,384 complete rounds; Standard
warms/measures 32,768. Other entries warm 262,144 and measure 1,048,576.

| Complete configuration | Before rounds/s | After rounds/s | Ratio | Before B/round | After B/round |
| --- | ---: | ---: | ---: | ---: | ---: |
| le-militare.super | 4,313 | 5,113 | 1.185 | 22,082.3 | 14,552.5 |
| le-militare.elite | 6,102 | 7,189 | 1.178 | 7,949.2 | 4,657.7 |
| le-militare.standard | 15,697 | 17,677 | 1.126 | 473.0 | 304.8 |
| le-militare.spin | 233,595 | 235,750 | 1.009 | 31.1 | 29.5 |
| le-militare.chance | 143,955 | 150,290 | 1.044 | 123.0 | 89.7 |
| le-militare.air-raid | 199,801 | 200,709 | 1.005 | 41.7 | 39.9 |
| le-militare.siege.spin | 230,918 | 234,786 | 1.017 | 32.1 | 30.3 |
| le-militare.recon.spin | 225,474 | 228,943 | 1.015 | 32.2 | 30.1 |

Six configurations have disjoint improved timing ranges. Ordinary spin and air
raid ranges overlap; their small median increases establish no throughput gain.
Their allocations still fall. These remain shared-host kernel observations.
The complete-round denominator includes all awarded or bought features.

A separate work-budget diagnostic audits 1,024 complete rounds per selected
configuration. Nine Lives buy averages about 1,520 64-bit PRNG words per round;
Super averages about 579. A million such bought rounds would require about
1.52 billion words/second. A separate unaudited tight scalar-generator control
measures about 1.26 billion words/second on this host. This is a diagnostic of
the current scalar implementation, not a game throughput measurement or a
universal hardware bound. Preserving each logical stream while batching/vectorizing
execution is a necessary area to investigate toward the remaining target.
The one-million complete-round goal for every configuration is still unmet.

## Native reads from immutable integer tables

The compiler prepares a private Int64 snapshot and signed result bounds for
already proven immutable numeric arrays, up to 65,536 entries. Integer consumers
read the machine value directly, allowing surrounding arithmetic, comparisons
and indices to retain their bounded integer representation. Consumers requiring
an ExprValue keep the existing array read, avoiding an unbox/rebox cycle.
Data belongs to operands rather than shared generated code. Mutable, mixed,
wide or fractional arrays decline preparation; lexical aliases invalidate facts
or use the general plan. No DSL function, atom or mechanic dispatch is added.

Prepared reads share the existing integer-position and bounds validation.
Review caught an opaque IReadOnlyList ordering edge: invalid/fractional positions
must fail before its Count getter is evaluated. The original order is preserved
and independently tested with a throwing Count implementation. Empty arrays,
signed extremes, snapshot ownership, lazy parents, operand errors, scalar-cache
resets, mutable inputs and shared code with different data also have regressions.

Final validation passed 248 affected Core cases, three small exact-model laws,
Release solution build and scoped formatting. All five files received both
required Rider profiles; two revised files received both again. Separate frozen
kernels match all 1,950 complete payout/state/RNG/next-word transcripts across
all 15 full configurations, traced/untraced, including UInt64.MaxValue. State
and loop evidence have no comparison exceptions. Original game JSON is unchanged.

### Complete maintained games on one worker

Both kernels run identical current JSON on CPU 2, normal .NET tiering/PGO and
Tiered sampling, in ABBA process order. Each process measures three repetitions
and discards its first. Paid entries and X7 buy warm 262,144 and measure
1,048,576 rounds; other buys warm/measure 65,536. Super/Elite warm 32,768 and
measure 16,384; Standard warms/measures 32,768. All outcome statistics and every
loop-termination statistic match exactly without filtering.

| Complete configuration | Before rounds/s | After rounds/s | Ratio |
| --- | ---: | ---: | ---: |
| ancient-dragon | 340,045 | 341,543 | 1.004 |
| nine-lives | 448,455 | 456,308 | 1.018 |
| nine-lives.buy | 25,100 | 25,670 | 1.023 |
| woodland-whisper | 311,234 | 314,250 | 1.010 |
| woodland-whisper.buy | 24,054 | 24,280 | 1.009 |
| x7-club | 447,668 | 444,428 | 0.993 |
| x7-club.buy | 186,926 | 188,463 | 1.008 |
| le-militare.spin | 234,235 | 237,067 | 1.012 |
| le-militare.standard | 17,707 | 18,117 | 1.023 |
| le-militare.super | 5,121 | 5,276 | 1.030 |
| le-militare.elite | 7,178 | 7,341 | 1.023 |
| le-militare.chance | 151,867 | 154,746 | 1.019 |
| le-militare.air-raid | 200,991 | 207,508 | 1.032 |
| le-militare.siege.spin | 236,017 | 239,746 | 1.016 |
| le-militare.recon.spin | 228,494 | 231,767 | 1.014 |

Eight timing ranges are disjoint improvements. The other seven overlap and
establish no gain or regression. Allocation per round is unchanged; preparation
adds bounded model-level storage. These are shared-host kernel measurements,
including complete awarded/bought features, not API capacity guarantees.

The SIMD RNG draft remains unshipped. An isolated four-stream raw-word probe
preserves active-lane words, audit hashes, committed state and the following word.
It measures roughly 1.48 billion words/s versus 1.12 billion for four scalar
streams; a local-state bulk-output probe measures about 1.44 billion and provides
no additional gain. These primitive results do not establish game throughput or
justify changing logical chunking/stream order. A first overlapping build/timing
run and an incomplete table comparison against an obsolete baseline are excluded
from performance evidence. Corrected isolated/current-baseline results are above.
The one-million complete-round target remains outstanding for all configurations.
