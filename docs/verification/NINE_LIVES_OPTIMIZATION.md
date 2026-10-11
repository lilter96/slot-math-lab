# Nine Lives optimization and verification

The current user target is **2,000,000 complete paid rounds/s on eight workers
on this machine**. Triggered bonuses, cascades and live statistics are included.
The final current measurement exceeds the target in all three 10M-round repeats
(2,061,865 / 2,059,413 / 2,065,394 rounds/s). Earlier observations below
are historical intermediate evidence. No new capabilities beyond the six explicitly
approved finite-set operations have been implemented.

## Profiling evidence

The initial eight-worker median was 197,327 rounds/s, allocating 21,438 bytes per
round. Array-backed traversal was the largest allocation source. Bit masks,
worker-private reusable loop evidence and generic expression lowering reduced
allocation substantially. The current graph builds three symbol bit planes in
two folds, then composes component discovery from finite-set expressions.

Linux `perf` is unavailable with the host's `perf_event_paranoid=4`; host settings
were not changed. EventPipe attributed 97.4% of the first managed profile to
`PollGC`, so it was not used to estimate CPU shares. This is a documented
[limitation of safe-point sampling](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/debug-highcpu).

An alternative profile launches only the benchmark under GDB, enables the .NET
JIT perf map, excludes five seconds of startup and samples native instruction
addresses at random intervals between 5 and 15 ms. The initial 1,200-sample pass
and a 2,400-sample follow-up agreed: the chain/loop executor and mask-building
Fold were dominant sampled methods. Native stack unwinding was incomplete, so
only leaf addresses were analyzed; inclusive stack percentages are not claimed.
[Machine-readable sample counts](lab-demo-native-profile.json) retain both
2,400-sample passes. Counts are not precise exclusive CPU-time measurements.

The follow-up before compact values sampled the chain/loop executor in 29.2%
of observations and the mask-building Fold in 12.5%. JIT disassembly identified
24-byte expression results copied through stack temporaries. A separate
`Unsafe.SizeOf` probe confirms the current state cell occupies 32 bytes and the
expression value 16 bytes. Compact values and in-place typed
state writes preserve exact large integers/rationals. The subsequent profile
sampled those executor methods in 23.8% and native/unresolved code in 9.2%, down
from 13.7%. The mask-building Fold was then the largest individual method at
15.2%. This led to generic range propagation that keeps finite-set Fold
accumulators as native unsigned words.

The paired eight-worker medians before/after compact values plus in-place state
writes were 492,612 and 569,920 rounds/s. Allocation fell from 4,501 to 3,258 bytes
per round. Native mask Fold plus the two-pass graph measured 640,597 rounds/s.
All repetitions retained identical one-million-round payout statistics. These
are intermediate measurements on a shared host, not acceptance evidence for
the two-million target.

The subsequent gravity profile sampled the chain/loop executor in 29.1% of
observations. The authored gravity graph now uses a 32-entry stable survivor-row
table and five conditional refill draws per column, followed by one board map.
Its one-million-round median was 787,164 rounds/s versus 678,061 for the preceding
graph on the same executable. Allocation fell to 2,527 bytes/round. Replay
fixtures confirmed the same survivor order, draw order, cascade boards and wins.

Straight runs of existing state updates and draws now compile together after
the existing hot threshold. Bound slots and scalar literals are embedded in the
run's generated method; expression methods continue sharing code across
different operands. Scalar Draw execution remains shared so the PRNG body is
not duplicated for every authored Draw. The two-million-round median was
827,749 rounds/s, with a best repetition of 908,264. This is below the target.

For untraced components without Wilds, the graph now selects seeds with degree
at least three or two neighbors among internal vertices. Every connected
component with at least five cells has such a seed: otherwise its maximum
degree is two and it is a path or cycle. Wild-containing boards keep their
original seed order. A branch skips an empty candidate loop. Across two million
rounds this reduced discovered components from 2.163 to 1.291 per round and
frontier expansions from 6.695 to 4.307. The median increased to 881,021 rounds/s;
the payout mean, variance, hit frequency, maximum and histogram remained
bit-identical. Allocation was 2,483 bytes/round.

The latest native profile sampled the combined mask-building block in 11.0%
of observations and the symbol-selection block in 7.6%. An experiment
keeping published values and masks in locals passed 327 focused tests but
measured 875,775 rounds/s, versus 881,021 beforehand. A narrower mask-only
experiment passed 16 focused tests but measured 802,614 rounds/s. Neither
showed a gain, so both were removed. These measurements include substantial
shared-host GC variation; neither is treated as a precise isolated regression.

The graph now builds its three planes with two 15-cell folds, reading thirty
cells instead of sixty. Small immutable Maps are expanded at compilation, and
successful writes supply conservative readability and normalization facts only
within their straight run. Pure expressions with immutable operands use the
existing expression semantics at compilation; invalid expressions stay on the
runtime path. This removes immutable geometry lookups and dependent branches
without changing error order. The eight-worker median was 993,110 rounds/s.

Loop evidence binds an ordinal hash once per compiled loop and keeps the two
standard exit-reason counts as integers. Custom reasons, bounded coverage,
transactional round commits and independent snapshots retain their semantics.
The authoring helper connects both branches directly to the next node instead
of emitting an intermediate GetState. That graph measured 1,094,785 rounds/s.
Compiled run bindings now publish code and operands together and include
terminal update/draw nodes with their original return policy. Their median was
1,085,457 rounds/s. These small measurements do not establish a gain from that
last change alone. [Sequential experiment reports](lab-demo-optimization-experiments.json)
retain individual repeats, CPU time, GC pauses and full payout statistics.

Two further experiments were rejected. Keeping cascade boards in bit planes
reduced allocation from 2,483 to 1,804 bytes/round but measured only 795,924
rounds/s. Its 2,400-sample native profile located the regression in symbol
selection (12.7% of observations), packed-column updates (8.0%) and final-board
decoding (6.8%). The model and its independent 1,024-case permutation check
passed before the experiment was removed. A cached unsigned-integer eligibility
flag kept state cells at 32 bytes and passed 56 focused tests, but measured
1,042,330 rounds/s; it too was removed. Lower allocation and fewer checks alone
are not treated as evidence of higher throughput.

Analysis of unresolved native addresses accounts for about 4.2% of the earlier
2,400 observations in a 32-byte memory-copy loop. File offsets were translated
through the ELF load segments before disassembly. This is supporting profiling
evidence, not a claim that removing state reset would be correct or sufficient.

Raw native samples, JIT maps, disassembly, scripts and a dotTrace sampling
snapshot are retained under
`backend/SlotMath.Core.Tests/TestResults/native-profiling/` (ignored artifacts).
The dotTrace snapshot was captured with the official standalone CLI without
changing the owner's running Rider instance.

## Validation status

325 focused tests passed after compact storage, native Fold lowering, compiled
straight runs and direct immutable-array operands. They cover exact expression
semantics, randomized tier/plan equivalence, independent Python game fixtures,
large masks, empty/nested folds, error order, state overrides and scoped bindings.
Seven game tests then passed after the stronger seed bound, including all 295
connected four-cell and 766 connected five-cell layouts on the complete board.
This exhaustive boundary check uses independent row/column set growth, without
the product's seed formula or traversal. Subsequent focused checks covered pure
constant evaluation, immutable lookup branches, terminal return policies,
evidence-key equality and eager errors after state changes. Final backend/API
and browser validation is being rerun after the rejected experiments were removed.

Mandatory scoped Rider cleanup has been attempted after each source change.
Both backend profiles fail to load `System.Composition.AttributedModel,
Version=10.0.0.12` and stall; timed-out task-owned tool processes were stopped.
Frontend reformat reports that another Rider instance is running; frontend Full
Cleanup is unavailable through the installed CLI. Neither required pass is
claimed successful. Final file inventory and validation remain pending.

## Subsequent profile-driven changes

The scalar mask-state proof now establishes nonnegative 64-bit values from
all initial values and all writers. A second finite fixed point grows the
maximum occupied bit until every writer preserves it. Only values proved to
fit 63 bits use inline integer reads and writes. Scoped aliases override
state facts; initial-state overrides select a plan without those assumptions.
The independent tests cover cyclic widening, the sign bit, full-width masks,
shadowed bindings, shared generated shapes and invalid caller values.

The authored seed expression excludes the two four-cell false candidates:
a degree-three star and an isolated square. All 295 connected four-cell
placements produce no seed, and all 766 connected five-cell placements
retain a seed, verified through independent row/column set growth. The
no-Wild traversal therefore no longer repeats a remaining-size check.
The same-process ABBA/BAAB comparison reduced median CPU time from 5.172
to 4.754 microseconds per complete round, with identical payout statistics.
Wild-containing boards retain their original exclusive claiming order.

A profile located the next cost in repeated Loop control and indexed masks.
Generic integer comparisons and bounded constant-Fold indices now lower to
native integers only when proved safe. The JSON component subgraph explicitly
performs its first two required frontier expansions before a remaining
28-step loop; its total bound remains thirty. The same-process graph
comparison reduced median CPU time from 5.672 to 5.277 microseconds per round.

The most recent 2,400 native leaf samples found Draw and Loop execution in
10.2% of observations each, the combined mask-building block in 9.0%, and
initialization including scatter counting in 4.5%. Scalar Draw's existing
alias operations now keep Xoshiro state in registers across index rejection
and fractional selection. Audited streams use the original per-word path.
Independent comparisons across table sizes, seeds, audit settings and
interleaved subsequent RNG calls preserve every word. The paired measurement
was modest: median CPU time 5.319 to 5.249 microseconds per round.

The initial scatter count is authored from the three existing bit planes;
a flag captures it once before any paying cascade. Traced rounds keep their
original board count. The same-process comparison measured 5.477 versus
5.393 microseconds per round, with identical complete payout statistics and
histograms. Shared-host load varies substantially, so small gains are not
reported as precise throughput guarantees.

Rejected experiments are retained as evidence, with their product changes
removed. Expanding constant Folds tripled the mask-building machine code
and regressed throughput. Keeping unobserved loop counters in locals did
not produce a stable gain. Six separately staged gravity columns reduced
allocation by 15% but did not improve paired throughput. Reducing server GC
heap count to four or eight did not improve throughput either; runtime GC
configuration is unchanged. Independent shadowing and raw-loop-counter
regressions written during these experiments remain useful tests.

The three ten-million-round repeats immediately before the scatter rewrite
measured 1,461,476, 1,465,502 and 1,440,684 complete paid rounds/s on eight
workers with live callbacks. They are intermediate observations for hash
`d86a3fef79f124bcc6be6cb5cc4c06cbf5a372d55c5c6afecfb55ed53bfd671c`.
At this intermediate stage the two-million target remained unmet. Final measurements and
full-suite validation remain pending.


## Array lifetime optimization

Generic compiled Map, Filter, immutable Map expansion and fused Append now
request worker-private arrays from a bounded round arena. Each requested array
is distinct for the entire current round. State export recursively materializes
independent CLR arrays. The arena resets only between paid rounds, and is
conservatively disabled for measurement definitions, measurement collectors or
persistent state. A retention request also disables it before recycling.
Buffers larger than 64 elements and requests past 256 cached buffers allocate
normally; these bounds limit retained memory rather than changing graph results.

The same-process eight-worker ABBA/BAAB comparison measured medians of
1,309,162 versus 1,442,051 complete paid rounds/s. Median CPU time fell from
5.935 to 5.349 microseconds per round, allocation from 2,479.7 to 453.8
bytes/round, and the four GC-pause observations from 92–149 to 12–26 ms.
Payout statistics, histograms and loop-termination evidence were identical
across all eight five-million-round repetitions. The graph hash is unchanged.

Independent regressions check saved exports after subsequent rounds, nested
aliases, separate runners, empty and 65-element arrays, a 300-iteration round
past the cache budget, persistent trajectories, errors after partial array
construction and cancellation. Six focused tests passed. The expanded full suite passed all 1,157 Core
and 134 API tests. All 92 frontend unit tests and the production frontend build passed.

Preparing scalar Draw outcome cells once removed runtime normalization/type
checks; its native body shrank from 506 to 413 bytes. The paired median CPU
reduction was 2.7%. A further direct-write prototype measured only a small,
unstable difference and remains outside the product.


## Remaining allocation profile and scalar export

An EventPipe allocation trace on the arena-enabled Release binary retained
12,452 GCAllocationTick samples after excluding the first five seconds.
Estimated allocated bytes were 48.6% expression arrays, 40.1% boxed
BigInteger values and 3.8% exact rational objects. These are sampling estimates,
not exact allocations per type. [Allocation profile](lab-demo-allocation-profile.json)
retains the sample counts and estimated amounts.

A separate counter-instrumented, one-worker prototype recorded 3,374,009 zero
numeric exports over 600,000 complete rounds (warmup included), and no other
numeric exports at that cell boundary. Those counters exist only in the
profiling copy, never in the product or the performance comparison. The cell
export now caches one immutable boxed BigInteger zero. Raw Int32/Int64 values,
fractions, booleans, null and deferred opaque values retain their original types.
The same-process comparison measured median CPU time 4.818 versus 4.670
microseconds, allocation 453.8 versus 273.9 bytes/round, and throughput
1,604,322 versus 1,636,921 complete paid rounds/s. All payout statistics,
histograms and loop evidence remained identical.


The full suite after cached zero export passed 1,158 Core tests and 134 API
tests. Frontend production build, scoped ESLint, all 92 frontend unit tests,
fixture-export check, independent Python Fraction check and branding scan
passed. Full browser validation and final throughput runs are in progress.

### Intermediate standalone measurement and browser verification (2026-10-11)

The intermediate `09e4fca19d064284290c6d0e869acc5872684ff4e92e1e039720876a3fdc3ae7` graph, array arena and boxed-zero cache measured 1,518,347 / 1,490,896 / 1,530,711 complete paid rounds/s: median **1,518,347**, eight workers, three 10M runs, seed 42, 2M warmup, live statistics. Allocation: 274 B/round. The 2M target remains unmet. The independent concurrent test process on this shared host was left untouched.

Full browser suite: 69 passed, one test timed out before schema loading. The retained retry trace proves HTTP 429 with `Retry-After: 60` exceeded its default 30-second budget. Its timeout is now 120 seconds, with cancellation assertions unchanged. Rerunning all three verification-profile tests passed (17.8 seconds). Original failure evidence is retained at `/tmp/nine-lives-e2e-quota-failure`. This is a full-suite failure followed by a successful focused rerun, not a clean full-suite pass.

### Exact-length array reuse and branchless alias selection

The native leaf profile after array reuse and boxed-zero caching put scalar Draw at 13.5%, generic loop control at 10.2% and mask construction at 9.8% of 2,400 samples. Lazy caching of loop-invariant field reads did not improve wall throughput (1,413,489 → 1,403,965 rounds/s), and preventing loop delegate inlining was neutral (1,657,644 → 1,659,897); both experiments were rejected and remain outside product code.

The arena now retains buffers in exact-length buckets rather than their request ordinal. Different request orders in subsequent rounds no longer replace buffers of another length. Arrays remain distinct throughout each round, total retention is still bounded at 256 buffers of length at most 64, and overflow uses ordinary fresh allocation. Independent tests pin reference identity, changing request order, overflow, exact lengths, snapshots and scoped state behavior. Its paired median was 1,764,854 → 1,812,251 rounds/s, CPU 4.367 → 4.259 µs/round, allocation 273.9 → 64.7 B/round.

Alias sampling uses a full-width integer acceptance mask to select the existing bucket or alias, avoiding an unpredictable control-flow decision. PRNG calls, rejection ordering, thresholds and aliases are unchanged. Its paired median was 1,629,934 → 1,799,076 rounds/s, CPU 4.712 → 4.295 µs/round. The independent ordinary `Next(count)`/`NextDouble()` oracle covers non-power-of-two rejection, uniform/nonuniform and zero weights, 1–1,025 outcomes, negative seeds, interleaved subsequent RNG calls and audited streams. Payout statistics and loop evidence are identical in every paired run for both changes.

Focused verification passed 70 RNG/storage tests; the earlier focused run passed all game regressions but the new overflow assertion initially used xUnit structural array equality instead of reference identity. The assertion was corrected; product array lifetime behavior did not fail. Required Rider passes were attempted again and remain blocked by the documented assembly-loading failure.

The combined exact-length arena and alias mask version measured **1,914,400 / 1,871,719 / 1,888,555** complete paid rounds/s (median **1,888,555**) in three 10M-round runs with 2M warmup, eight workers and live statistics. Allocation: 65 B/round; GC pauses: 0 / 7 / 7 ms. RTP, variance, hit frequency, maximum and histogram remain identical to the pinned 10M sample. **The 2M target remains unmet.**

The RNG fraction conversion now casts **after** the 11-bit unsigned shift: the remaining 53-bit integer is always nonnegative and exactly representable in both Int64 and double. This removes the unsigned-to-double adjustment instructions seen in the native Draw profile. Paired medians were 1,725,790 → 1,781,085 rounds/s, CPU 4.457 → 4.372 µs/round; all payout statistics and loop evidence remained identical. Full current backend verification passed **1,160 Core tests and 134 API tests**. An initial API test invocation omitted the isolated database environment and failed authentication; the corrected isolated invocation passed all API tests.

### Current target measurement

The signed-fraction conversion combined with exact-length array reuse and alias selection measured **2,061,865 / 2,059,413 / 2,065,394 complete paid rounds/s** (median **2,061,865**) on eight workers, three 10M-round repeats, 2M warmup and live statistics. All three repeats exceed 2M. Counts, RTP, variance, hit frequency, maximum, complete histogram and loop evidence are identical to the earlier current-model report. Progress callbacks ran in every repeat. Allocation: 65 B/round; GC pauses: 0 / 2 / 1 ms. This supersedes the earlier unmet intermediate measurements; it is a local machine measurement, not a D7 hardware certificate.

Final current full E2E: **70 passed in 17.9 minutes**, including the corrected cancellation timeout in the complete suite. Full backend: 1,160 Core + 134 API passed. Independent Fraction reference, fixture exporter, branding scan, scoped ESLint and absolute-scoped backend format verification passed. The mandatory Rider Reformat/Full Cleanup passes were attempted and remain blocked as listed in `lab-demo-cleanup-status.json`; these are not claimed successful. Machine-readable final validation is retained in `lab-demo-validation.json`.
