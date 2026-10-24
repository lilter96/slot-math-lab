# Indexed updates and stable index groups

R11 implements a minimal family of two generic array capabilities. The contracts
below are separate from game migrations; no game name, mechanic ID or authored
graph pattern selects an implementation. Admission requires comparison against
existing typed compositions and the packed bit-plane representation already
used by the current board reader.

## Contracts

```json
{
  "exprType": "scatter",
  "target": {"exprType":"fieldAccess","target":"state","path":["target"]},
  "indices": {"exprType":"fieldAccess","target":"state","path":["destinations"]},
  "values": {"exprType":"fieldAccess","target":"state","path":["updates"]},
  "mode": "LastWriteWins", "maxItems": 64, "maxUpdates": 8
}
```

Scatter evaluates Target, Indices and Values once, eagerly in that order. An
explicit Mode chooses RejectDuplicates, LastWriteWins or Sum. The destination
array and update count each have required literal bounds in 0..4096. Indices
and values have identical lengths; indices are exact integers in the target
range. No out-of-range update is ignored. LastWriteWins applies updates in
source order; Sum adds exact Numbers to each destination in that same order.
It is an indexed reduction over an explicitly supplied initial accumulator.

Reject/LastWrite support one readable element kind across target and values:
Number, Boolean, String/Symbol, Record or Array. Record/array field and element
shapes remain conservatively inferred. Sum requires Numbers, including exact
rationals and wide integers. All target elements, then all indices/duplicates,
then all values are validated. Values and destinations are snapshotted before
applying updates, preserving input aliases and getter/error order. Source
state arrays remain borrowed. Only a separately proven unique private target
may transfer its buffer; no field name or inferred type proves ownership.

```json
{
  "exprType": "partitionIndices",
  "labels": {"exprType":"fieldAccess","target":"state","path":["labels"]},
  "bucketCount": 8, "maxItems": 64
}
```

Stable PartitionIndices accepts exact numeric labels, a literal BucketCount
and explicit MaxItems, each bounded through 4096. Labels belong to 0..B-1.
It returns B arrays of original global source indices, one array per bucket.
Indices within a bucket retain source order. Empty buckets remain empty arrays;
zero buckets requires an empty source. All labels are validated, including
those whose result is unused. The output can be applied to any aligned arrays
through ordinary bounded Map and index. Counts are ordinary group lengths.

Both operations check cancellation at entry and bounded validation/application
steps, consume no RNG words and return new logical immutable values. Their
shared kernels take O(N+M) work for scatter and O(N+B) for partition. Validating
and copying a borrowed target remains O(N), including a single update. Buffer
transfer removes a copy; the initial implementation still validates the full
target and does not claim O(M) execution. Partition allocates one outer array
and buffers only for nonempty groups; empty groups share immutable empty data.

## Type, effects and resource checks

Static cost conservatively charges operand costs plus `1 + maxItems +
3*maxUpdates` for scatter and `1 + 2*maxItems + bucketCount` for partition.
The existing 10,000-operation budget can reject combined declarations even
when each bound separately fits 4096. Nested collection inputs must declare
explicit bounds too. Required JSON fields prevent silently choosing a duplicate
policy or unbounded operation.

Errors distinguish `EVAL_SCATTER_MODE`, `EVAL_SCATTER_LENGTH`,
`EVAL_SCATTER_DUPLICATE`, `EVAL_INDEXED_DOMAIN`, existing type errors and the
collection budget, with operand/element locations. Input validation is strict;
there is no integer truncation or Boolean/Number coercion. Empty targets accept
only empty updates; empty label inputs retain every declared empty bucket.

Shapes retain common Record fields and conservative nested array element types.
IR records ordered operands, bounds, possible errors/allocation and cancellation.
Canonical, closure and native paths call the same checked kernels. Structured
exact execution uses the existing deterministic opaque term with every operand's
state and lexical dependencies. A returned array is a fresh logical container;
SSA use counts across *all* scatter operands guard target storage transfer.
Nested groups remain borrowed even when a private partition outer array can
transfer to a consuming Map. Escaping source aliases block destructive reuse.

## Composition and prototype admission

The reference Scatter/Reduce composition uses a bounded Fold of update indices,
with an index-aware Map over the destination for each update. Partition uses
Map over bucket IDs and Filter over original indices. These are valid editable
AST compositions with no named array helper. Prototype measurements compare
these expressions against standalone generic checked kernels before extending
the public grammar. A separate small-domain count control uses a packed bit
plane Fold and bitset queries, following the current board reader's representation.
Expression microbenchmarks are distinct from whole-round or production-game
throughput. A new primitive must still pass final compiled whole-round controls.

The interface family is informed by the authors' [Futhark SOAC documentation](https://futhark-lang.org/docs/prelude/doc/prelude/soacs.html),
which distinguishes indexed reduction, scatter, scan and partition and cautions
that asymptotic complexity hides constant costs. Our contract makes update
order, duplicate behavior and invalid-index errors explicit. The [Futhark
scan/scatter fusion discussion](https://futhark-lang.org/blog/2026-03-24-scan-scatter-fusion.html)
provides compiler background; this project preserves eager error order and
cancellation before any buffer reuse rather than importing parallel semantics.

The prototype compared both heap-backed evaluation and the engine's bounded
array arena before adding either public expression. With the arena enabled,
median expression-evaluation ratios were 3.470 for 64 items/eight updates,
2.382 for 64 reductions into eight cells, and 8.611 for grouping 64 labels into
eight index lists. A single update of 15 items regressed to 0.678. The separate
15-cell packed-plane count query measured 1.765, but that control is only a
count projection and does not certify replacing a game's complete reader.

These figures are expression evaluations, not spins. Both variants used the
same frozen R10 Core, CPU 2 and ABBA process order; 8,192 warmup and three
65,536-evaluation repeats discarded the first timing sample. Optimized-JIT
settings were a diagnostic, with application defaults unchanged. Every process
first matched independent update/group oracles for both input variants. Original
unpooled reports are retained separately. Full-round results and final selected regressions follow.

## Final validation

Release build and scoped formatting passed after all 20 touched C# sources
completed Rider Reformat Code and Full Cleanup. The selected regression passed
656 Core cases and five API preview cases without skips, including 41 new
indexed-array cases and all 15 current-game seeded state comparisons. No full
integration, browser, schema export or large generative suite ran for this
backend milestone.

Independent regressions enumerate every small dense label assignment through
six items/four buckets, compare randomized updates against grouped integer
oracles, pin exact wide/rational sums and all duplicate policies, and check
Boolean/String/Record/nested-array payloads. Errors, required JSON settings,
getter counts/order, cancellation, runtime overrides, structural result shapes
and preservation of borrowed/escaping inputs are covered. SSA tests count all
scatter operands and exercise private target/partition-outer transfer.

One shared random flag drives both indexed reduction and partition in a
joint-law control: payout seven has probability 1/4, payout 41 has probability
3/4, with mean 65/2. Classic and structured exact results agree. Three sampling
tiers match independent scalar alias audits, the following word and complete
seeded state. Two persistent sessions retain target and nested-group exports
through 100 rounds and trajectory resets. Editable Library JSON verifies
reference rewriting in every operand without kernel mechanic dispatch.

## Complete-round admission controls

The final frozen Release binary executes both ordinary JSON compositions and
the new expressions. Each control contains five physical nodes: one categorical
flag Draw, a selected input write, the operation result write, payout projection
and settlement. The packed-plane case compares a complete count-vector query
with PartitionIndices plus ordinary Map/length, not an entire production board
reader. Numeric labels are common inputs to both variants.

CPU 2, one worker and ABBA process order are fixed. Each process has 65,536
warmup rounds and three 262,144-round measurements, discarding the first timing
sample. `DOTNET_TieredCompilation=0` is a diagnostic on both sides; application
defaults are unchanged. Every paired count, payout statistic, histogram, cap
and loop result agrees. A separate runner comparison covers 128 seeds per pair
for complete state, RNG audit and following word, plus the complete classic
exact payout law. These 640 controls use the same final frozen Core.

| Query | Existing composition rounds/s [range] | Indexed rounds/s [range] | Median ratio |
| --- | ---: | ---: | ---: |
| One update, 15 items | 5,854,055 [5,756,422–5,859,590] | 4,045,479 [3,977,833–4,073,716] | 0.691 |
| Eight updates, 64 items | 591,497 [580,543–601,469] | 1,481,967 [1,478,822–1,489,843] | 2.505 |
| 64 sums into eight cells | 404,359 [398,932–410,612] | 585,665 [582,411–595,748] | 1.448 |
| Group 64 labels into eight lists | 544,355 [541,989–548,315] | 762,528 [746,070–763,673] | 1.401 |
| 15-cell packed-plane count query | 3,895,218 [3,838,323–3,943,978] | 2,019,137 [2,008,363–2,044,955] | 0.518 |

All retained ranges are disjoint. Bulk updates/reduction and full index groups
improve here. The single update regresses about 30.9%; the compact packed-plane
count composition remains substantially faster. Its apparent expression-only
prototype advantage reverses in the complete compiler/pipeline. Consequently
neither single-cell Map nor the existing bit-plane representation is replaced
on a primitive microbenchmark claim. Read-only facts and specialized lowering
available to full graphs are part of the comparison, not disabled baselines.

Allocation is near 6.64–6.82 B/round for both variants of the first four cases.
The packed-plane composition uses 54.65 versus 6.66 B/round, yet remains faster.
Less allocation therefore does not establish a throughput gain. Cold preparation
ranges 295–341 ms across only two observations per variant. The indexed
operation can also simplify source representation, but speed admission still
requires complete-round evidence and preserved observable semantics.

This completes the admitted R11 backend capability. Current-game migrations
remain R17 and designer/schema integration remains R18. No production-game
multiplier is inferred. Source/DLL manifests, prototype/full-round inputs,
negative controls and raw reports are retained outside Git under
`/tmp/modern-primitives-evidence/` (r11-prototype*, r11-compare and indexed-benchmark).
