# Numeric and collection expression contracts

The constructor's structured AST is the authored math. Canonical and compiled execution preserve exact rational numeric values until payout/report conversion. A constant with `kind: "Rational"` may contain `"3/2"`; a numeric map declares `itemType: "Number"`.

| Operation | Contract | Independent example |
|---|---|---|
| `abs` | Preserve magnitude and denominator | `abs(-1/2) = 1/2` |
| `min`, `max` | Select the exact value by rational order | `min(1/2,3/4) = 1/2` |
| `floor` | Greatest integer no larger than the argument | `floor(-1/2) = -1` |
| `ceil` | Least integer no smaller than the argument | `ceil(-1/2) = 0` |
| `round` | Nearest integer, ties away from zero | `round(-3/2) = -2` |
| Array sum/product/extrema | Preserve fractional selected values | `sum([1/2,1]) = 3/2` |

Numeric built-ins reject wrong operand types and arities. An explicit aggregate selector for sum/product/min/max must produce a number. The existing implicit symbol-scoring contract remains: without a selector, integer strings contribute their integer value and other strings contribute zero. Use an explicit numeric selector when that convention is unsuitable.

Collection operations require an existing array. Missing state produces `EVAL_MISSING_STATE`; null, scalar, string and dictionary sources produce `EVAL_TYPE_ERROR`. A deliberately empty array has sum/count zero, product one and an unchanged fold seed. Empty min/max are undefined and raise an evaluation error. Aggregate/filter predicates require Booleans.

Homogeneous initial scalar arrays and map/filter/copy outputs carry derived element types. A numeric array index can therefore feed a numeric expression. Empty, mixed or conflicting writer types do not invent a scalar type; unsupported indexing is rejected during validation. Array element typing is conservative and does not infer arbitrary object schemas or array-producing folds.

The native fixture [fractional-model.json](../frontend/e2e/fixtures/fractional-model.json) maps `[-6,8,15]` to `[-1/2,2/3,5/4]`, then settles:

```text
sum(fractions)*12 + min(3/2,7/4)*4 + abs(fractions[0])*2 + floor(-1/2)+1
= 17 + 6 + 1 + 0
= 24
```

API tests select both exact and sampled evaluation regimes against that manual result. The browser workflow imports the graph through Build, authors a fractional tracked metric, runs both sampling engines, checks saved evidence, and enumerates the pinned payout law. The engine identity includes these numerical contracts; light-evaluation cache keys include the loaded Core/API identity and execution policy so a corrected engine cannot inherit a prior build's calculation.
