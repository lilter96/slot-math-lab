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
