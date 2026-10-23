# Bounded directed reachability

`ReachableExpr` computes the least allowed vertex set closed under authored
directed adjacency. It is a generic deterministic expression; geometry,
symbol matching, Wild ownership, group ordering and payouts stay in JSON
subgraphs. It traverses a data graph, not the authored control graph or the
stochastic transition graph used by Markov reward analysis.

```json
{
  "exprType": "reachable",
  "vertexCount": 4,
  "adjacency": { "exprType": "fieldAccess", "target": "state", "path": ["adjacency"] },
  "allowed": { "exprType": "fieldAccess", "target": "state", "path": ["allowed"] },
  "seeds": { "exprType": "fieldAccess", "target": "state", "path": ["seeds"] }
}
```

## Contract

`vertexCount` is a required literal integer in 0..64. Adjacency is an array of
exact Number masks with exactly that length. Bit j of row i denotes the directed
edge i→j. Allowed and seeds are exact nonnegative integer masks. Every mask must
fit both UInt64 and the declared vertex domain. Bit 63 remains exact; neither
input masks nor results pass through floating-point conversion.

The initial set is `seeds & allowed`: disallowed seeds are omitted. Repeated
edges, self loops, cycles and disconnected components are valid. Expansion
adds only allowed vertices. A zero-vertex domain accepts an empty adjacency
array and zero masks, returning zero. The result is an exact numeric mask,
which composes with existing Count, First, Union, Intersect and Except.

Operands evaluate eagerly once in adjacency/allowed/seeds order. After operand
evaluation, runtime validation checks vertexCount, adjacency type and length,
every row in index order, allowed, then seeds. All rows are validated even when
seeds or allowed are zero. Bad rows in unreachable components are not silently
ignored. The enclosing expression's ordinary lazy If/And/Or rules still apply.

Invalid static types or bounds fail compilation. Runtime diagnostics distinguish
`EVAL_REACHABLE_BUDGET`, `EVAL_REACHABLE_LENGTH` and `EVAL_REACHABLE_DOMAIN` with
precise operand/row locations. Existing `EVAL_TYPE_ERROR` and `EVAL_BITSET_RANGE`
handle nonnumeric, fractional, negative and wider-than-64-bit masks.

## Bounded execution and effects

The kernel validates rows into a fixed stack buffer and keeps pending/reached
sets in machine words. Each iteration removes one pending vertex. New pending
bits exclude every previously reached vertex, so a vertex can enter only once.
There are at most V validation steps and V expansion steps, without a heuristic
iteration cap. Cancellation is checked at entry, each validation row and each
expansion. Static expression cost charges `1 + 2*vertexCount` plus operands.

The result is the least fixed point: seeds are included, expansion is monotone,
and every permitted outgoing edge of a processed vertex is added. The bounded
word-frontier algorithm does not require matrix inversion or a stationary
probability calculation. The [GraphBLAS specification and BFS examples](https://graphblas.org/docs/GraphBLAS_API_C_v2.0.0.pdf)
provide a broader graph-algebra reference; this implementation imports no
matrix library and assumes no slot-specific neighborhood.

Exact evaluation, canonical delegates, all sampling tiers and structured exact
translation support the operator. Structured translation captures state and
lexical dependencies through the existing bounded opaque deterministic term,
preserving correlations. `EvalContext.CancellationToken` propagates through
lexical and collection contexts; compiled sampling uses its frame token.

IR represents the operator explicitly with ordered operands, a vertex bound,
known reads, possible errors/allocation and cancellation. State constant
propagation and speculative folding do not execute the traversal at compile
time. Dominated-predicate simplification now consults IR effects before reusing
a successful predicate: a repeated traversal cannot disappear along with its
cancellation checks. Iterator cache and buffer passes remain conservative.

Successful ordinary-domain traversal allocates no heap objects. Big masks use
the existing exact numeric representation; a large result may require its
BigInteger storage. Valid high-bit adjacency rows do not allocate location
strings, and generic mask validation avoids constructing a temporary UInt64
maximum BigInteger. Error paths construct detailed locations when needed.

## Editable composition and verification

Two maintained JSON fixtures compose Reachable with Count inside a reusable
Library subgraph, with independent adjacency and parameter values. Inlining
rewrites expression references in all three operands. The kernel does not
inspect the subgraph name or graph ID. Production game migrations remain R17.

An exhaustive small mathematical control covers all 512 directed three-vertex
graphs, eight allowed sets and eight seed sets. Its oracle uses Floyd-Warshall
transitive closure rather than the production frontier algorithm. An additional pinned queue/HashSet BFS oracle covers 384 directed graphs
through 64 vertices. Other
controls cover direction, blocked vertices, cycles, empty domains, bit 63,
invalid unreachable rows, precise failures, cancellation, lexical contexts,
JSON bounds and per-use inlining. Existing Fold/bitset composition agrees on
4-, 15- and 25-vertex cyclic graphs.

A pinned categorical mixture has independent payout probabilities 1/8 at zero,
1/8 at two and 3/4 at four, with mean 13/4. Canonical and structured exact laws
agree; sampled tiers preserve seeded full state, complete RNG audits and the
following word. A regression first reproduced and then prevented elimination
of the second cancellation-bearing predicate.

## Final validation and performance control

The backend stage passed 441 selected Core cases and five API preview cases,
then one supplementary queue-BFS regression passed after its own cleanup,
format and build: 442 unique Core cases, including 33 new controls. The existing
15-game seeded full-state corpus is included. All 18 changed C# files passed
both mandatory Rider profiles; the original 17 were not edited after their
final validation, and the additional test did not change the frozen runtime. The two JSON fixtures passed
both profiles through an isolated Rider frontend after the CLI explicitly
skipped them. No required file remained uncleaned. Full integration, browser
and large generative suites were not run for this stage.

The paired complete-round control used one frozen Release binary, two ordinary
Draws, closure, Count and settlement. The reference expression composes the
existing bounded Fold, If and bitset atoms into N monotone relaxation passes
over N adjacency rows. The new configuration changes only that expression to
Reachable. A directed cycle exercises propagation through the whole domain;
random allowed sets and seed choices retain the same ordered Draw schedule.
Both configurations satisfy the existing 10,000-operation admission budget.

CPU 2, one worker and ABBA process order were fixed. Each process used
262,144 warmup rounds and three 1,048,576-round measurements, discarding the
first timing sample. `DOTNET_TieredCompilation=0` was set for both processes
as an optimized-JIT diagnostic; project defaults were unchanged. Shorter
initial measurements were retained, then lengthened because the faster
variant completed them in only tens of milliseconds.

| Directed domain | Fold million rounds/s range | Reachable million rounds/s range | Median ratio |
| --- | --- | --- | ---: |
| 15 vertices | 1.142–1.150 | 6.249–6.371 | 5.499 |
| 25 vertices | 0.4846–0.4861 | 4.527–4.542 | 9.331 |

Counts, RTP, variance, hit frequency, maximum payout, histograms, cap counts
and loop termination evidence agreed in all matched measurements. Allocations
were near parity, 6.642 versus 6.637 B/round. Two cold compile observations
were 302.8/306.5 versus 275.3/278.3 ms on the 15-vertex control and
303.1/306.3 versus 274.1/274.6 ms on 25 vertices. Cold compile alone excludes
subsequent hot native generation.

These gains compare a generic relaxation composition with the new bounded
operator. They are not a measured multiplier for the current game configs or
for their geometry-specific authored growth algorithms. Game migrations,
author-defined symbol/group order and complete-round admission remain R17.
The first variant is explicitly limited to 64-vertex masks; wider finite-set
authoring remains R15.

Frozen Core SHA-256:
`009fa7ee552706a86bc1afca3d8d6db9106d49d9ca31e290aeb0101a4dd5e9c8`.
