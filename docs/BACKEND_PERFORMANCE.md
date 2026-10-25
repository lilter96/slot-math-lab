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
