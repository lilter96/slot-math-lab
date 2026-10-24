# Bounded short-circuit folds

The existing `fold` expression accepts an optional `whileCondition`. Presence
of that Boolean expression selects a bounded pre-item guard. Omission or null
retains ordinary Fold behavior and legacy JSON serialization. There is no
named `any`/`all` evaluator or mechanic-specific control path.

```json
{
  "exprType": "fold",
  "stateKey": "values",
  "maxItems": 64,
  "accName": "total",
  "itemName": "value",
  "indexName": "nextIndex",
  "itemType": "Number",
  "init": { "exprType": "constant", "kind": "Integer", "value": "0" },
  "whileCondition": {
    "exprType": "compare", "op": "Lt",
    "left": { "exprType": "fieldAccess", "target": "state", "path": ["total"] },
    "right": { "exprType": "constant", "kind": "Integer", "value": "50" }
  },
  "body": {
    "exprType": "binary", "op": "Add",
    "left": { "exprType": "fieldAccess", "target": "state", "path": ["total"] },
    "right": { "exprType": "fieldAccess", "target": "state", "path": ["value"] }
  }
}
```

This returns the first running total reaching 50, or the total of the whole
source if it never reaches 50. Negative integers and values wider than 64 bits
remain exact. The guard can use the next zero-based index and typed accumulator
fields. It cannot read the current item: the guard precedes that item's getter.
A nested bounded iterator inside the guard can introduce its own item binding.
Lexical Local expressions remain a separate namespace.

## Evaluation order and domains

The initialization and source expressions evaluate eagerly, in that order.
Exactly one StateKey or Source is required. The explicit maxItems must be in
0..4096, even for a state-key source. The complete outer length must fit before
any guard/body execution. An overlong source fails rather than being truncated
or bypassed by an initially false guard. Composed iterators require their own
explicit bounds. The existing global operation budget still applies.

For a nonempty validated source, each candidate step checks cancellation, binds
the previous accumulator and next index, evaluates whileCondition, and loads
the current item only when the condition is true. Then Body computes the next
accumulator. False returns the previous accumulator immediately. An empty
source returns Init without evaluating the guard. There is no extra predicate
call after reaching the source length. Entry cancellation is still checked
for empty and initially stopped folds, after source and length validation.

The source StateKey accepts indexed arrays with a known count: ordinary JSON
arrays/IList or a typed ExprValue array. A streaming IEnumerable is rejected
without enumeration. Counting or snapshotting an unknown-length stream would
itself read the tail. An explicit Source expression, including a producer Map,
is evaluated completely before the fold; errors in that producer precede the
guard. External state import/conversion also precedes this operator boundary.
The short-circuit guarantee concerns item getters during the fold.

Init and Body have the same inferred accumulator type. Successful Body results
are also checked at runtime, including the final visited item. This prevents a
malformed external value from returning a Number through a Boolean quantifier.
Number arithmetic remains exact, including rationals. Arrays and records retain
their container kind; the existing structural inference remains conservative
about their element/field shapes. Symbol/String normalization follows existing
expression-source semantics and is performed only on selected items here.

Scoped accumulator, item and index bindings restore their previous values on
success, a failing guard/getter/body or cancellation. A stopped fold does not
load, validate or normalize an unvisited item. The enclosing expression's
ordinary lazy If/And/Or boundaries remain unchanged.

## Any and All as editable compositions

For Boolean flags, Any starts with false, continues while the accumulator is
false, and uses the current flag as its body:

```json
{
  "exprType": "fold", "stateKey": "flags", "maxItems": 64,
  "accName": "found", "itemName": "flag", "itemType": "Boolean",
  "init": { "exprType": "constant", "kind": "Boolean", "value": "false" },
  "whileCondition": {
    "exprType": "not",
    "expr": { "exprType": "fieldAccess", "target": "state", "path": ["found"] }
  },
  "body": { "exprType": "fieldAccess", "target": "state", "path": ["flag"] }
}
```

All starts with true, uses the accumulator itself as whileCondition, and uses
the same body. Empty Any is false; empty All is true. Replace Body with any
ordinary Boolean predicate to quantify over other element types. These are
JSON compositions of one generic capability, not dedicated kernel functions.
A previously executed DrawMany still consumes its full ordered random schedule;
stopping the query does not erase draws or move the following random word.

## Compiler representation

IR distinguishes FoldWhile from ordinary Fold and stores a `while-before-item`
region before its body region. Bounds, scoped bindings, reads, possible errors
and cancellation remain explicit. Native generation keeps the source and
accumulator local where possible and places the guard before item access.
Separate generated shapes prevent an ordinary Fold and a guarded Fold from
sharing incompatible machine code. The closure tier uses the same ordering.

Guard evaluation does not reuse body scalar caches. Dominated-predicate reuse
cannot erase a repeated cancellation-bearing fold. Static cost charges the
worst-case condition and body for every declared item, plus initialization,
source and iteration overhead. Saturating arithmetic rejects missing bounds
and oversized work; it does not assume that a runtime early exit will occur.
Library inlining rewrites expression references inside the guard as well.

The exact translator uses its existing bounded deterministic opaque term for
a guarded fold, capturing source, state and lexical dependencies. It does not
unroll Body while ignoring the condition. Correlated Any/All queries over one
shared random array retain their joint law. This stage does not introduce a
specialized symbolic product-quantifier algorithm or claim its scalability.

## Regression controls

Independent truth tables enumerate all Boolean arrays through nine elements.
Prefix controls use pinned negative/wide integer arrays and an independent
loop. Indexed probes throw on unwanted head/tail reads and on enumeration.
Further controls pin empty/bound/stream/error order, strict guard scope,
nested local scopes, record accumulators, symbol normalization, shared native
shapes, repeated guards, cancellation and binding restoration.

A negative regression reproduced a missing-bound bypass in native execution
when called directly without graph validation. The fixed adapter rejects it
for mutable and compiler-known immutable sources. Final-item Body type checks
also reject non-Boolean results from malformed quantifier inputs.

For six independent Boolean draws with P(true)=1/4, the joint payout
`10*Any + All` has masses 729/4096 at zero, 3366/4096 at ten and 1/4096 at eleven,
with mean 33671/4096. Classic and structured exact laws agree. Sampling tiers
match an independent scalar alias oracle, the complete RNG audit, following
word and full seeded state. Two persistent array-accumulator sessions retain
old exports through 100 rounds and trajectory resets. A default guard override
and an editable parameterized Library body are also covered.

Final validation passed 615 selected Core cases and five API preview cases,
including all 36 new controls and the complete seeded state comparisons for
15 current games. All 17 touched C# files completed both required Rider
profiles, followed by scoped format verification and the Release solution
build. Later bound/type/scope edits repeated both profiles on their exact
changed files. Full integration, browser and large generative suites were not
run for this kernel milestone.

## Paired complete-round controls

The frozen Release control compares ordinary bounded Fold with Boolean OR
against a guarded fold with `!acc` and a predicate body. A preceding ordinary
Draw chooses between two 256-item arrays; ModifyState writes the result and
payout before settlement. Early controls place true at index 0 or 1; middle
controls use 127 or 128; the negative full-scan control has no true element.
Both variants preserve the same Draw, final result and settlement statistics.

Processes used CPU 2, one worker and ABBA order. Each had 65,536 warmup rounds
and three 1,048,576-round measurements, discarding the first timing sample.
`DOTNET_TieredCompilation=0` is an optimized-JIT diagnostic for both variants,
not a change to application defaults. All paired counts, payout statistics,
histograms, cap and loop evidence agreed; payout is independently one for the
first two cases and zero for the full scan.

| 256-item query | Ordinary Fold rounds/s [range] | Guarded Fold rounds/s [range] | Median ratio |
| --- | ---: | ---: | ---: |
| Match at 0/1 | 1,643,064 [1,603,098–1,668,013] | 6,678,496 [5,781,961–7,640,689] | 4.065 |
| Match at 127/128 | 1,462,653 [1,419,774–1,491,118] | 1,507,562 [1,484,956–1,543,473] | 1.031 |
| No match | 1,367,904 [1,330,525–1,430,369] | 869,050 [822,012–914,143] | 0.635 |

Early-match ranges are disjoint and demonstrate a substantial gain on this
control. Middle ranges overlap, so that median does not establish a reliable
improvement. The complete scan is about 36.5% slower: guard and runtime type
checks add work without avoiding elements. Allocation stayed approximately
6.56 B/round in every variant, including amortized run infrastructure. Cold
compilation ranged 333–377 ms across two observations per variant.

Both variants contain four physical graph nodes and the same number of
expression AST elements. This gain comes from avoiding traversal, not shrinking
the saved graph. These controls do not authorize unconditional Fold-to-While
rewrites or claim a current-game spin-rate improvement. R17 must admit each
migration using its actual access distribution and observable semantics.
Inputs, logs, raw paired reports and source/DLL manifests remain outside Git
under `/tmp/modern-primitives-evidence/while-benchmark/` and its parent. Designer/schema integration remains R18 and game
migrations/admission remain R17. No current-game throughput multiplier is
inferred from this primitive.
