# Finite-domain precomputation

R12 adds a general compiler strategy for an ordinary editable expression with
explicit finite input domains. The source expression and reference evaluation
remain authoritative. A lookup table is a private compiled artifact; domain
metadata is an optimization hint and does not restrict mathematical inputs.
Game names, mechanic identifiers and authored graph patterns never select it.

## Annotation contract

An optional `precompute` annotation belongs to any `Expression`. Its ordered domains
identify a direct state field or lexical local name and an ordered finite list
of typed scalar constants. `expectedUses` estimates lifetime invocations. Its default of zero declines optimization. Entry,
work and memory budgets bound compilation and storage. Absent annotations
preserve existing JSON. Missing/invalid hints decline optimization and execute
the ordinary body; inputs outside the listed domain also use that body.

This annotation introduces no new call semantics. Lazy branches remain lazy.
Runtime lookup probes must read only existing inert scalar values, without
converting an unsupported value, invoking a getter or throwing on a missing
binding. A failed probe falls back to the unchanged expression. Array iterator
and lexical scopes need their own safe probes rather than reading stale frame
slots. Results may be immutable shared data and must not acquire a false fresh
buffer ownership proof.

The following is the annotation property of an ordinary expression, not a
separate mathematical operator:

```json
{
  "precompute": {
    "domains": [
      {
        "name": "x",
        "scope": "State",
        "values": [
          { "kind": "Integer", "value": "0" },
          { "kind": "Integer", "value": "1" }
        ]
      }
    ],
    "expectedUses": 1000000,
    "maxEntries": 65536,
    "maxWork": 1000000,
    "maxBytes": 8388608
  }
}
```

`Local` selects a lexical name instead of a direct state field. Each domain
uses one scalar type: exact Number, Boolean or String. Values must be unique;
the declared ordering is retained. This example specifies the hint only;
a small or cheap surrounding expression still declines admission. Canonical
and exact evaluators continue to evaluate that surrounding source normally.

## Admission and safety

Admission considers the product of domain sizes, original computation cost,
lookup cost, construction work, result footprint and expected use count.
Short scalar decision trees are holdouts: their source size does not justify
building a table. The complete declared domain must succeed exactly. An error
at any entry declines the table; errors are never cached as zero payouts.
Unknown effects, observations, RNG and cancellation-bearing bodies decline.
Readonly captured parameters require compiler snapshot guards.

A bounded abstract analysis checks numeric bit growth, string/container sizes
and total work before the canonical evaluator enumerates rows. Checking only the final table
size would not bound temporary BigInteger growth. Source/reference evaluation
retains its ordinary semantics when optimization declines.

Table identity uses compact JSON without formatting whitespace and includes
the body, captured parameter values, input types,
ordered domain values and their order. Per-program cache limits cover entries
and retained bytes and prevent concurrent duplicate construction. Worker
replicas can share the same immutable admitted table. Different domains,
parameters, result types or source edits cannot reuse stale table data.

## Resource limits and lowering

The hard bounds are eight inputs, 256 values per input and 65,536 rows.
The hint may lower the row limit. Work is limited to eight million charged
operations and storage to eight MiB per table; the defaults are one million
work units and eight MiB. A program cache retains at most 64 plans and 16 MiB
of charged table/identity data. These are conservative accounting bounds,
not measurements of every CLR object or compilation peak allocation.

Before enumeration, analysis limits numeric numerator/denominator growth to
4,096 bits, text to 4,096 characters, arrays to 4,096 elements, records to
64 fields, and AST traversal to 10,000 visits and depth 128. Captured/result
snapshots have a 65,536-node and depth-32 bound. Captured arrays require inert
owned array backing: an opaque indexable container is declined without
invoking its getter. Structural bounds use reference-memoized joins and walk
costs so a compact record DAG cannot trigger exponential analysis expansion.

Admission requires estimated body cost at least twice lookup cost, then
expected saved work at least twice construction work. Lookup uses ordered
mixed-radix indices, dense integer ordinals where suitable and typed scalar
maps otherwise. Native emission uses scalar locals rather than an argument
array. Cold sampling probes the same table and retains its ordinary fallback.
The cache serializes construction; worker owners share immutable table data.
Readonly captured parameters still use the existing initial-state guards.

A native `Let` now publishes a frame binding only when a nested fallback or
frame-based read requires it. Native-only lexical children use IL locals;
other bodies retain frame publication and restoration in `finally`. Cached
array/record results remain borrowed, so collection consumers cannot overwrite
them through a fresh-buffer assumption.

## Independent experiment

The external prototype compares a pure arithmetic expression and a short
five-input decision tree on their complete declared domains before timing.
It uses the frozen R11 kernel and independent body/reference evaluation;
prototype lookup measurements are expression evaluations rather than spin
throughput. Original and extended timing reports remain outside Git.

The compiler model follows the separation of effects and legal speculation in
[MLIR's rationale](https://mlir.llvm.org/docs/Rationale/SideEffectsAndSpeculation/)
and the separation of analyses/transformations in
[LLVM's pass documentation](https://llvm.org/docs/Passes.html). This project
must preserve exceptions and cancellation even where an abstract pure
mathematical function has a finite domain.

Full-round measurements and final validation are recorded below. Designer forms and current-game
admission remain R18 and R17; the annotation alone establishes no current-game
speedup.

## Regression evidence

The initial affected selection passed 702 Core cases and five API preview
unit cases, without skips. A later full-graph admission check found that
pretty-printed cache identity could reject a large eligible body. Compact
identity fixed that conservative rejection. The new regression first failed
with `identity-size`; after the fix, all 47 affected cases passed following scoped cleanup
(38 new R12 cases plus nine existing lexical/schema controls). Together the
two selections verify 703 distinct Core cases and five API preview cases;
the unchanged broader selection was not repeated. The Release solution build
and scoped formatting passed. Both mandatory Rider profiles completed for all
13 touched C# files, with final-file passes repeated after subsequent edits.

Controls cover every declared rational-domain row, independent denominator
27,720 results, out-of-domain and missing/deferred inputs, lazy branches,
whole-table decline on a failing row, type/order/body/parameter identity,
initial overrides, lexical and iterator shadowing, fallback frame restoration,
concurrent sharing, bounded retention, huge numeric growth, compact structural
DAGs and captured containers whose getters must never execute. Cached container
results remain unchanged after collection consumption. Pipeline controls check
complete classic/structured exact laws and full state plus RNG audits and the
following word in all three sampling tiers. The existing selection also
contains all 15 current-game seeded full-state regressions.

No full integration or browser suite is required for this backend milestone.
Designer controls, generated public schema and current-game migrations remain
R18/R17; neither expression nor synthetic full-round measurements establish
a speedup for an unmodified production game.

## Full-round measurements

One frozen Release binary compared the ordinary body with the same body plus
an annotation, on CPU 2, one worker and ABBA process order. Each process used
65,536 warmup rounds and three measurements, discarding the first. Heavy and
low-use controls used 262,144 rounds per measurement; the short control used
1,048,576. `DOTNET_TieredCompilation=0` was a diagnostic benchmark setting;
application defaults are unchanged. Retained fast heavy measurements lasted
roughly 71–73 ms, so the ranges describe this controlled experiment rather
than a production capacity guarantee.

| Complete-round control | Body rounds/s range | Hint rounds/s range | Median ratio | Table |
| --- | ---: | ---: | ---: | --- |
| 24-term rational body, five four-value inputs | 534,114–547,939 | 3,593,805–3,685,815 | 6.681 | Admitted |
| Short five-input decision tree | 6,721,697–6,971,984 | 6,757,377–7,020,014 | 1.011 | Declined |
| Heavy body, one expected use | 547,596–555,804 | 550,714–558,209 | 1.009 | Declined |

Heavy median throughput rose from 545,009 to 3,641,002 rounds/s. Allocation
fell from 484.30 to 15.96 B/round. Cold whole-graph preparation was 307.6/309.5
ms without the hint and 400.1/410.4 ms with it; these are two observations per
variant, not isolated table-build costs. The admitted table contains 1,024
rows, 958,464 charged work units and 245,352 charged bytes. Short and low-use
allocations stayed at 6.60 and 484.30 B/round respectively. Their timing ranges
overlap, so no throughput gain is established for either decline control.

All counts, RTP, variance, hit frequencies, maximum payouts, histograms, cap
counts and loop evidence matched within every pair. On the frozen binary,
128 seeds per pair also matched payout, complete exported state, RNG audit
and following word: 384 controls in total. Every one of the heavy table's
1,024 entries matched both ordinary native evaluation and the independent
canonical evaluator. Admission counters confirmed one table for the heavy
graph and zero for both holdouts.

The preliminary expression-only experiment measured 134.2× for its heavy
lookup and overlapping short-tree ranges. That number excludes the Draw,
state updates and settlement in the complete-round graph and is not a
spin-throughput claim. Complete-round admission uses the 6.681× result above.
No claim is made for an unchanged current-game configuration.

Frozen Core SHA-256:
`8e495b69ecd0744354a4ea96f6e422d3d3802c7f4d0b0883254e643e3b9313a3`.
Generated logs, full reports, failed regression evidence and source/binary
manifests remain outside Git under `/tmp/modern-primitives-evidence/`.
