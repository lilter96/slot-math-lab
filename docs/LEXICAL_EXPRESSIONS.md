# Lexical expressions and structured results

`let`, `local`, `record`, `tuple` and `project` are generic expression forms.
They introduce no game-specific execution and do not write recurrence state.
They compose with ordinary arithmetic, conditions and collection expressions.

## Binding and evaluation order

A `let` eagerly evaluates `value` once in the surrounding scope, binds its
result under `name`, then evaluates `body`. Names must be nonblank strings
of at most 128 characters. The binding is nonrecursive: an initializer can read an outer
binding of the same name, but cannot read the binding it is creating.
An unused initializer still evaluates and can fail.

`local` reads the nearest binding with the exact, case-sensitive name. Its
optional `path` projects nested record fields or array indices. Empty `path`
returns the bound value. A missing binding raises `EVAL_MISSING_LOCAL` at
`local.<name>`; path failures use that same location.

Local and recurrence-state names are independent. A local named `item` does
not change `state.item`; legacy iterator variables still bind state names.
Locals remain visible within iterator bodies and interpreted fallbacks, and
are restored on success or failure. They never appear in saved round state,
state hashes or measurement fields.

```json
{
  "exprType": "let",
  "name": "base",
  "value": {
    "exprType": "binary",
    "op": "Add",
    "left": { "exprType": "fieldAccess", "target": "state", "path": ["award"] },
    "right": { "exprType": "constant", "kind": "Integer", "value": "3" }
  },
  "body": {
    "exprType": "binary",
    "op": "Mul",
    "left": { "exprType": "local", "name": "base" },
    "right": { "exprType": "local", "name": "base" }
  }
}
```

## Records, tuples and projection

A `record` has an ordered `fields` list of `{name, value}` entries. Names must
be nonblank and unique, and at most 64 fields are allowed. Each value is a
fully typed expression, including exact rationals, strings, booleans, arrays
and nested records. Fields evaluate eagerly from left to right. The list
preserves authored error order when JSON object properties are canonicalized.

A `tuple` has an ordered `items` list of at most 64 expressions. It produces
a positional **record** with fields `"0"`, `"1"`, and so on. Its runtime and
inferred kind is `Record`; it is not a homogeneous array. Empty records and
empty tuples are valid.

`project` evaluates `value` eagerly, then follows its static `path`. For
example, the following expression returns the exact rational `1/3`:

```json
{
  "exprType": "project",
  "value": {
    "exprType": "record",
    "fields": [
      {
        "name": "amount",
        "value": { "exprType": "constant", "kind": "Rational", "value": "1/3" }
      },
      {
        "name": "enabled",
        "value": { "exprType": "constant", "kind": "Boolean", "value": "true" }
      }
    ]
  },
  "path": ["amount"]
}
```

Projection does not make other record fields lazy. Invalid or missing path
segments produce the usual coded traversal errors, located at `project`.
An empty path returns the readable value. The type checker requires a known,
readable shape and rejects scalar traversal and unknown record fields.
Exceeding the 64-field budget fails before child evaluation, with
`EVAL_RECORD_BUDGET` located at `record` or `tuple`. Duplicate/blank names fail
with `EVAL_RECORD_FIELD` at `record`; validation rejects them before execution.

## Execution and optimization contract

The canonical evaluator, interpreted sampling, generated IL and hot-tier
sampling share these semantics. Native local references use IL locals; an
internal lexical environment supplies values to separately compiled fallback
closures and is restored in `finally`. This first implementation still
allocates lexical environment entries and record storage. Allocation removal
requires the lifetime and escape proofs tracked by R6.

The structured exact evaluator retains shared random-variable dependencies
between fields. Projection can discard other fields only when they cannot
fail. A strict sequencing term retains potentially failing unused `let`
initializers. Opaque expression fallbacks receive both state and lexical
inputs; locals are never converted into public state fields.

Simplification visits every new child and resets predicate assumptions at a
lexical binding boundary. Its state-constant environment remains separate
from lexical names. Expression cost includes initializer, body, field and
projection work; lexical binding itself does not authorize dropping failures.

This contract covers the lexical/record part of implementation row R3.
Expression-valued collection inputs and bounded nested composition remain
separate required R3 work. Designer forms and schema/type generation remain
R18 work. No game-wide throughput gain has been measured for these forms.

## Schema generation

The exported expression schema contains one variant per registered serializer
subtype. Every nested expression uses the shared `#/$defs/expression` reference;
nullable optional children retain a null alternative. Required discriminators
and child values remain enforced. This uses the serializer's
[metadata-based export API](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.schema.jsonschemaexporter?view=net-10.0).

Expanding the whole polymorphic union at every recursive property exceeded
the exporter's default depth once record fields were added. Exporting each
variant separately avoids that expansion and leaves authored JSON depth
limits unchanged. Regression tests cover existing finite-set expressions,
new forms, nullability, unknown nested discriminators and missing children.

## Verification

The lexical/record milestone passed 462 selected Core cases and five API
preview cases. Forty new cases cover lexical execution and the exported
schema, with 64 pinned generated arithmetic programs and 32 pinned whole-round
state comparisons for each of two exact-law forms. Both mandatory Rider
profiles, scoped formatting and the Release solution build passed.

Run the new cases without services or browser dependencies:

```sh
dotnet test backend/SlotMath.Core.Tests/SlotMath.Core.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~LexicalExpressionTests|FullyQualifiedName~LexicalSchemaTests'
```
