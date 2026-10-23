# Prefix sums and stable segmented compaction

These two generic deterministic expressions operate on bounded arrays. No
geometry, game identity, symbol policy or payout rule selects their behavior.
Editable JSON subgraphs compose them with ordinary Map, If and index expressions.

## Scan

```json
{
  "exprType": "scan",
  "source": { "exprType": "fieldAccess", "target": "state", "path": ["flags"] },
  "mode": "Exclusive",
  "maxItems": 64
}
```

`mode` and `maxItems` are required. The source is a homogeneous array of exact
integer Numbers or Boolean flags. A Boolean contributes zero or one; mixed
Boolean/Number arrays and fractional Numbers are errors. Both modes start at
zero and return numeric arrays of the input length. Inclusive sums include
the current item, while exclusive sums precede it. Negative integers and sums
wider than 64 bits retain exact arithmetic. Even the final input of an
exclusive scan is read and validated. Empty sources return empty arrays.

The explicit bound is 0..4096. Source evaluation completes first, followed by
entry cancellation, bound/mode checks and source type/length checks. Item
validation and addition proceed in source order, with a cancellation check per
item. Static work charges source cost plus `1 + maxItems`. No floating-point
reassociation or parallel reduction is used.

## Segmented compaction

```json
{
  "exprType": "segmentedCompact",
  "keep": { "exprType": "fieldAccess", "target": "state", "path": ["keep"] },
  "boundaries": { "exprType": "fieldAccess", "target": "state", "path": ["boundaries"] },
  "packing": "End",
  "maxItems": 64,
  "maxSegments": 8
}
```

All five operands/settings are required. Keep contains Boolean flags. Numeric
boundary offsets begin at zero, end at the input length and never decrease.
There is one more offset than segments. Repeated boundaries denote empty
segments. The two bounds are each 0..4096; source and segment counts must fit.
A zero-segment plan requires an empty source and boundaries `[0]`.

The result is a typed record with `sources` and `freeCounts`, both Number
arrays. Sources has the input length. Each retained element contributes its
original global index; unused positions contain -1. Start packing places
survivors at the beginning of each segment; End places them at the end. In
both cases the original survivor order is stable. FreeCounts has one entry per
segment, including zero for an empty segment.

For keep `[true,false,true,false,true,true]`, boundaries `[0,3,6]` and End
packing, sources is `[-1,0,2,-1,4,5]` and freeCounts is `[1,1]`. Apply the same
sources to symbols, multipliers and other arrays with bounded Map and index;
an ordinary If handles -1. Fill draws remain separate atoms in authored order.
The maintained fixture
`backend/SlotMath.Core.Tests/TestData/Primitives/segmented-attribute-transfer.json`
transfers three differently typed attributes through a lexical plan binding.
No new named array helper or mechanic-specific kernel branch is involved.

Keep and boundaries expressions evaluate eagerly, once, left to right. After
entry cancellation, validation checks bounds/packing, outer array types and
lengths, all keep items, then every boundary in order. Invalid unreachable or
empty-segment boundaries are still errors. Flags and offsets are snapshotted
into bounded stack buffers before constructing the plan. Work is O(N + S);
static cost conservatively charges operand costs plus `2 + 3*maxItems +
2*maxSegments`. The existing global expression budget can therefore reject
large combined declarations even when each individual bound is legal.

Diagnostics retain operand/item locations: generic type and collection-budget
errors, `EVAL_SCAN_MODE`, `EVAL_SCAN_INTEGER`, `EVAL_COMPACT_PACKING` and
`EVAL_COMPACT_BOUNDARIES`. Lazy enclosing branches retain their usual behavior.

## Compiler and ownership

Canonical evaluation, interpreted sampling and native lowering call the same
validated kernels. Ordered operands, declared bounds, possible errors,
allocation and cancellation are explicit in IR. Structured exact evaluation
uses the existing deterministic opaque term, capturing all state and lexical
dependencies to preserve joint laws. These operators consume no random words.

Scan returns a fresh logical numeric array. Native code can transfer a unique
private producer buffer through the existing SSA ownership proof. The input
item kind is captured before a Boolean source slot becomes a numeric output.
Borrowed state inputs and escaping aliases retain copies. A later Map can
consume a private scan result. Compaction record projections conservatively
remain borrowed. Frame arrays use the bounded arena; canonical evaluation
allocates its result arrays, and record construction still allocates. This is
not an allocation-free contract for every evaluation path.

## Verification

Independent BigInteger prefix calculations cover both modes, negative and wide
integers. Exhaustive small compaction checks enumerate Boolean keep sets and
segment cuts through six items, with both packing directions and duplicate or
empty segments. Further controls pin malformed inputs, errors and lazy order,
required JSON fields, ownership, cancellation and lexical scope.

A shared random flag drives both operations in an independent exact-law control:
payout 11 has probability 1/4 and payout 22 has probability 3/4, with mean
77/4. Canonical and structured exact evaluation agree. Sampling checks all
three tiers against complete state, RNG audit and the following word. Separate
persistent runners retain prior exports over 100 rounds and reset correctly.

The final Release solution build and scoped formatting passed, followed by
479 selected Core cases and five API preview cases, including all 37 new
collection-plan cases and all 15 current-game seeded full-state comparisons.
Both required Rider profiles completed on all 20 C# sources and the JSON
fixture; JSON used the isolated frontend workflow. No full integration,
browser or large generative suite was run for this backend milestone.

## Complete-round performance controls

One frozen Release binary executes both ordinary JSON compositions and the new
expressions. Prefix controls compare Scan with a bounded Fold carrying a total
and an appended prefix array in a record. Compaction controls compare the new
plan with Filter plus Map/index placement. A separate five-item finite lookup
table is the negative control. Each graph includes an ordered categorical
choice, result writes, a payout projection and settlement. All paired counts,
payout statistics, histogram, caps and loop evidence matched.

Processes use CPU 2, one worker and ABBA order, three measured repetitions,
discarding the first timing sample. `DOTNET_TieredCompilation=0` is an explicit
optimized-JIT diagnostic for both variants; application defaults are unchanged.
Scan64 and compaction use 65,536 warmup and 262,144 measured rounds per repeat.
After the first Scan64 case showed an expensive Fold baseline, the incomplete
initial Scan128 attempt was stopped and only that case used 4,096 warmup and
16,384 measured rounds. Completed controls were retained, not rerun.

| Control | Existing composition rounds/s [range] | New expression rounds/s [range] | Median ratio |
| --- | ---: | ---: | ---: |
| Prefix 64 | 6,424 [6,301–6,505] | 1,437,345 [1,432,043–1,449,940] | 223.73 |
| Prefix 128 | 2,249 [2,217–2,261] | 788,734 [786,056–792,133] | 350.76 |
| Compact 32 | 533,398 [530,896–538,017] | 798,580 [792,574–803,142] | 1.497 |
| Compact 128 | 258,736 [257,915–264,529] | 271,660 [226,332–316,802] | 1.050 |
| Five-item lookup | 1,657,795 [1,644,673–1,661,358] | 1,440,311 [1,428,927–1,451,150] | 0.869 |

The large prefix ratios reflect removal of repeated growing-array append and
record reconstruction, not a comparison with an already optimized hand-written
prefix loop. Heap allocation fell from 308,807 to 6.65 B/round for Prefix64 and
1,010,264 to 8.13 for Prefix128. Compact32/128 fell from about 1,015 to 919
B/round; these records still allocate. The five-item variants both allocate
about 919 B/round. Finite-run infrastructure overhead contributes to these
amortized rates.

The compact128 timing ranges overlap substantially: the median does not
establish a speedup. The five-item lookup is demonstrably faster here and
must not be replaced on a claimed performance benefit. Cold compilation ranged
304–327 ms across these controls; each variant has only two cold observations.
The relevant expression fragments shrink 24→11 AST elements for Prefix64,
36→12 for Compact32 and 22→12 for the lookup control, which is slower despite
the smaller expression. Physical graph node count stays four in these controls.

These are local primitive controls, not measured speedups of current games.
R17 still requires editable game migrations and full-round admission. Inputs,
raw reports, retained incomplete-attempt logs and source/DLL hash manifests are
outside Git under `/tmp/modern-primitives-evidence/plan-benchmark/` and its parent.
