# Bounded collection expression composition

`map`, `filter`, `fold` and `aggregate` accept exactly one input:

- `stateKey`: the existing state-array input;
- `source`: an array-valued expression, evaluated once outside the iterator's bindings.

`source` requires an explicit integer `maxItems` in 0–4096. A state-key input
can declare the same bound. Nullable new properties are omitted from legacy
JSON, preserving its serialized shape. Legacy state-key forms retain their
existing iteration behavior and nesting restrictions.

An explicitly bounded form can compose other iterators in its source,
initializer, body, predicate or selector. Every composed iterator must declare
its own `maxItems`, including state-key forms. Within a bounded iterator,
item/index/accumulator names must be distinct nonblank strings of at most
128 characters. Nested scopes can shadow outer names. Source shape inference
preserves arrays of records, positional records and nested arrays.

```json
{
  "exprType": "fold",
  "source": {
    "exprType": "map",
    "source": { "exprType": "fieldAccess", "target": "state", "path": ["awards"] },
    "maxItems": 30,
    "itemName": "item",
    "itemType": "Number",
    "body": {
      "exprType": "binary",
      "op": "Mul",
      "left": { "exprType": "fieldAccess", "target": "state", "path": ["item"] },
      "right": { "exprType": "constant", "kind": "Integer", "value": "2" }
    }
  },
  "maxItems": 30,
  "itemName": "item",
  "itemType": "Number",
  "accName": "total",
  "init": { "exprType": "constant", "kind": "Integer", "value": "0" },
  "body": {
    "exprType": "binary",
    "op": "Add",
    "left": { "exprType": "fieldAccess", "target": "state", "path": ["total"] },
    "right": { "exprType": "fieldAccess", "target": "state", "path": ["item"] }
  }
}
```

This doubles and sums the awards without authoring an intermediate state
field. The same `source` and `maxItems` properties apply to `filter` and all
five aggregate functions. Sources can also read a `let` binding or project
an array from a typed record. A tuple itself is a record and cannot serve as
an array source.

## Evaluation and resource bounds

A source is evaluated completely before its consumer starts. Producer errors
therefore precede consumer-body errors. `fold` evaluates `init` before its
source, including for an empty source. Empty map/filter return an empty array;
empty fold returns the initializer; aggregate identities and undefined empty
min/max retain their existing contracts.

The actual source length is checked before the first body evaluation. An
oversized input raises `EVAL_COLLECTION_BUDGET` at `map.source`,
`filter.source`, `fold.source` or `aggregate.source`. Reading or computing the
source can fail before that check. State-array type and missing-field errors
retain their field locations. Temporary iterator bindings restore on success,
errors and cancellation; expression sources introduce no state writes.

A streaming state enumerable already present at iterator entry is read at most
`maxItems + 1` elements when checking the bound. The accepted prefix is retained
for that execution. This does not impose a new budget on state ingestion or
arbitrary expression builtins; public JSON inputs are finite arrays.

Explicit bounded iteration is charged conservatively as:

```text
1 + source cost + initializer cost + maxItems × (1 + body/selector/predicate cost)
```

Nested costs multiply, while sequential source/consumer passes add. The
existing 10,000-operation admission limit applies. Cost addition and
multiplication saturate at `long.MaxValue`, and excessive AST depth is rejected
by the cost guard. Legacy estimates keep their existing 64-element factor.
These are expression-operation bounds, not a universal wall-clock guarantee.

## Execution and exact analysis

Canonical evaluation, interpreted sampling, generated IL and hot-tier sampling
use the same source order and bound contract. Generated loops consume the
source directly. Proven immutable sources can use existing constant-map
lowering; runner overrides invalidate those facts and retain bound checks.
No RNG algorithm or stream schedule changes.

The structured evaluator keeps numeric aggregates as arithmetic terms when
array elements are visible. This retains independent variables and shared
random dependencies through nested maps and folds. Count selectors remain
eager on selected elements, and predicate-false elements skip their selectors.
Empty selected min/max remain errors. Unsupported symbolic shapes retain the
canonical fallback.

A regression covers 64 independent biased binary choices: all 65 probabilities
match the binomial formula, with mean 320 and variance 192. The earlier opaque
aggregate path exceeded a one-million-work budget on that same control; the
structured path completes within it. Other cases cover correlations across
loop iterations in both unrolled and Markov-chain execution.

Collection fusion and automatic removal of authored state fields still need
the effect, observation and lifetime proofs tracked by R4–R6. Writing a compact
source expression does not by itself prove higher simulation throughput.

## Verification

The initial backend milestone passed 531 selected Core cases and five API
preview cases. After the measured typed-source lowering, all 180 affected Core
cases passed, including 71 new composition cases. Complete-round comparisons cover 128 pinned
seeds for each sampling tier, including the exported state. Other controls
cover source evaluation order, nested shadows, reusable-subgraph references,
runner overrides, exact wide/rational values, cancellation and repeated error
restoration. Both mandatory Rider profiles covered the 14 changed source files;
scoped formatting and the Release solution build passed.

Run the new cases without services or browser dependencies:

```sh
dotnet test backend/SlotMath.Core.Tests/SlotMath.Core.Tests.csproj -c Release \
  --filter FullyQualifiedName~CollectionCompositionTests
```

## Simulation control

A full-round primitive control compares three authored Modify nodes
(map → filter → fold) with one Modify containing the same source expressions.
Both variants declare 30-item bounds. The input is 0–29; one biased Draw
returns 1 or 3 with weights 1 and 3. Map adds that value, filter keeps values
at least 15, and fold sums them. Payouts are 360 and 423, with theoretical mean
407.25 and variance 744.1875. The mutable-input control adds the same preceding
Modify to both variants, copying an immutable input into the source field.

Measured with one frozen Release binary, CPU 2, one worker, ABBA processes,
1,048,576 warmup rounds and 1,048,576 measured rounds per repeat. Each process
has three repeats; the first is discarded. All payout, histogram and RNG-driven
statistics matched across variants and repeats.

| Input | Three Modify nodes | Source expression | Source / nodes |
| --- | ---: | ---: | ---: |
| Immutable array | 1,516,377 rounds/s (1,494,408–1,541,800) | 1,574,660 (1,553,250–1,579,124) | 1.038 |
| Array written during the round | 1,068,261 (1,040,666–1,099,364) | 1,175,135 (1,173,688–1,175,842) | 1.100 |

Retained ranges do not overlap in these runs. Allocations are approximately
6.63 bytes/round for all variants; the existing arena already reuses arrays.
The relevant Modify fragment shrinks 3 → 1; complete controls shrink 5 → 3
nodes and 6 → 4 nodes respectively. The node-based control authors two extra
unobserved state fields, so this comparison does not certify an automatic
rewrite that preserves observable state. No full-game speedup is claimed.

The first lowering wrapped expression results in a state cell and returned
a large cell/length pair before entering the loop. Its warmed immutable-array
control had a 0.929 median ratio, with overlapping ranges. The final generic
lowering validates the typed ExprValue directly, retaining length in an IL
local. State-cell fallback remains available for non-array-backed lists;
symbol normalization and located bounds are preserved. Readonly-list, nested
symbol, runtime override, cancellation and code-sharing controls passed after
this change. The initial shorter-warmup results were noisier and are retained
as local evidence rather than used for a speedup claim.

Frozen final Core SHA-256:
`b4f340638335f30ba951616fea862d0ef3a744877851310baadb84788fc6f3d7`.
Raw reports and generated audit output are local artifacts; maintained
contracts and regression inputs remain in the repository.
