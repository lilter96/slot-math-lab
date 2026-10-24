# Categorical distributions and exact weight vectors

Categorical data belongs to the editable graph. Draws can use inline outcomes,
share an asset, select an asset from a finite family, or compute a bounded
numeric weight vector. These contracts are generic and do not select behavior
by a game or mechanic name.

An ordered asset is declared in `GraphConfig.distributions`. This fragment
shows Number data written independently of the numeric payout channel:

```json
{
  "distributions": {
    "choices": {
      "outcomes": [
        { "outcomeId": "low", "weight": 1, "value": 0,
          "stateValue": { "kind": "Integer", "value": "2" } },
        { "outcomeId": "high", "weight": 3, "value": 0,
          "stateValue": { "kind": "Integer", "value": "10" } }
      ]
    }
  },
  "nodes": [
    { "id": "choice", "nodeType": "draw", "distributionId": "choices",
      "stateWriteKey": "choice" }
  ]
}
```

The fragment needs ordinary ports, edges and a payout sink when used as a full
program. Existing Draw fields retain their behavior. Without `stateValue`,
`stateWriteKey` receives the string `outcomeId`. With it, Integer/Rational,
Boolean or String data is stored exactly. All outcomes in a table and across
a family must use one scalar type, including positions whose weight is zero.
Data is never multiplied by WinScale. The separate `value` channel retains
its existing payout-unit scaling.

`distributionIds` and `distributionIndexExpressionId` select a finite family.
For example, after declaring assets `a` and `b`, a Draw can use:

```json
{
  "id": "choice", "nodeType": "draw", "stateWriteKey": "choice",
  "distributionIds": ["a", "b"],
  "distributionIndexExpressionId": "selection"
}
```

The named expression returns a zero-based exact integer. Selection reads the
current state before RNG is consumed. Rows retain their own outcome counts,
order and zero positions; there is no padded union of their domains. Invalid
selectors fail before writing the Draw's data or consuming its random words.
Reusable subgraphs namespace the expression reference, while asset IDs remain
graph-level data references.

`weightVectorExpressionId` uses the outcomes of an inline Draw or static asset
but computes their current weights. For example:

```json
{
  "initialState": { "weights": [1, 3] },
  "expressions": {
    "current-weights": {
      "exprType": "fieldAccess", "target": "state", "path": ["weights"]
    }
  },
  "nodes": [
    { "id": "choice", "nodeType": "draw", "distributionId": "choices",
      "weightVectorExpressionId": "current-weights", "stateWriteKey": "choice" }
  ]
}
```

The expression must have a known homogeneous Number-array type, and its result
must contain exactly one weight per outcome. Values can be exact rational
expression results. Every weight is nonnegative and at least one is positive.
The vector replaces the static weights; inline zero placeholders are allowed.
It cannot be combined with family selection or the legacy scalar
`weightExpressionId`. The legacy field has not been reinterpreted as a vector.

Rational entries use an exact common denominator without deleting zero
positions or applying proportional normalization. For `[1/2, 1/3, 0, 5/6]`,
the integer numerators are `[3, 2, 0, 5]` with denominator 6. The alias table
therefore follows the same ordered conversion as an explicit scalar control.
The full exact law and sampled RNG replay are different checks, and both are
covered by independent controls.

Invalid type, length, negative or all-zero mass and arithmetic-budget failures
occur before the failed Draw consumes RNG or writes its result. Earlier Draws
in the program may already have completed. There is no replacement of invalid
weights by a uniform or zero-paying distribution.

Limits for dynamic vectors are 4,096 entries; 8,192 combined numerator and
denominator bits per entry; 65,536 input bits in total; 16,384 bits for the
common denominator; and 262,144 projected scaled coefficient bits. The
program-owned LRU retains at most 64 entries, 8,192 scalar positions and
1,048,576 charged numeric bits. These bounds limit stored values and exact
arithmetic; they are not a precise total CLR heap-size formula. Valid entries
too large for retention still execute without being cached.

Cache keys own immutable scalar snapshots and compare numeric contents.
Changing a producer's reused buffer cannot mutate a retained key. Runner
construction imports readable initial values; use a new runner to import
changed external inputs. Ordinary state changes inside a graph are observed
by each subsequent Draw. Initial-state overrides disable conflicting constant
specialization, and persistent/custom-measurement behavior keeps its existing
boundaries.

The reference program evaluates the vector using the exact expression
evaluator. The compiled sampler evaluates its selector using bound expressions
and shared immutable output data. A proven immutable initial vector can be
prepared once and lowered through the ordinary static Draw chain; a failing preparation retains the original runtime error
timing. Structured exact supports constant vectors and finite support of at
most 64 different vectors through its general Index/Sel IR. It retains the
joint dependence between selector, selected data and payout channel. Other
supports explicitly decline under the evaluator's existing fallback/budget
contract.

Static catalogs allow 1,024 assets, 65,536 outcomes per asset and 262,144 total
asset outcomes; families contain 1..256 IDs. Assets are snapshotted once during
binding, shared only inside the compiled program, and stay editable in the
original JSON. Public serialization contains asset references and declarations,
not private bound arrays. Source identity includes the authored data.

The broader implementation and remaining primitives are tracked in
[the compiler implementation matrix](design/MODERN_COMPILER_IMPLEMENTATION.md).

The low-level 128-outcome control measured 4.84M rounds/s for static Draw,
4.88M for a prepared immutable vector and 0.64M for a runtime-rebound vector.
Static and prepared ranges overlap. Runtime includes an extra equivalent state
write; this comparison does not isolate alias-cache cost or estimate whole-game
speedups. Full method, negative control and allocation figures are retained in
the implementation matrix.


[Conditional products](CONDITIONAL_PRODUCTS.md) can prepare bounded independent
variables subject to an additive integer range, then emit ordinary families and
editable JSON atoms. They add no runtime dispatch. Their new conditional draw
schedule has a distinct source identity; exact law equivalence does not imply
seed replay equivalence with a former flat or pattern-based graph. Small flat
tables can be faster, as the complete-round negative control documents.
