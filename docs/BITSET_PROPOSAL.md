# Proposal: finite-set operations on integer masks

Status: explicitly approved and implemented. Independent mathematical and
backend/frontend regression checks pass. The overall throughput target remains unmet.

## Why this capability

The current eight-worker Nine Lives baseline is 197,327 complete paid rounds/s
(median of three one-million-round repetitions, seed 42, live statistics).
The original requested 1,800,000 rounds/s requires 9.12 times that throughput;
the subsequent active goal is 2,000,000 complete rounds/s on eight workers.
An allocation sample attributes 75.9% of allocated bytes to `ExprValue[]`:
about 16.4 KB out of 21.6 KB per complete round. Allocation volume is not CPU
share: these numbers do not establish a ninefold speedup from any proposal.

The graph represents finite sets of positions as immutable arrays. Traversal
repeatedly searches and copies those arrays. A finite-set primitive would
replace that representation while leaving traversal, adjacency, symbol rules,
Wild claiming and payout evaluation in editable JSON subgraphs.

## Approved contract

One generally useful capability: unsigned 64-bit finite-set expressions, with
positions 0 through 63 represented by bits. Masks remain ordinary exact Integer
values; no game-specific type, node, dispatcher or optimizer is introduced.
The proposed expression family has six operations:

| Operation | Exact result |
| --- | --- |
| `union(a, b)` | Bits present in either mask. |
| `intersect(a, b)` | Bits present in both masks. |
| `except(a, b)` | Bits present in `a` and absent from `b`. |
| `shift(a, distance)` | Move bits by a signed distance; discard bits leaving positions 0–63. |
| `count(a)` | Number of set bits, from 0 to 64. |
| `first(a)` | Lowest set position; -1 for the empty mask. |

The AST uses `{"exprType":"bitset","op":"Union","args":[...]}`. Operation
names are `Union`, `Intersect`, `Except`, `Shift`, `Count`, `First`. The text
editor accepts `setUnion`, `setIntersect`, `setExcept`, `setShift`, `setCount`
and `setFirst`, which parse into that AST, not named function calls.
Existing array/collection functions are unchanged. A mask operand must
be an exact integer in [0, 2^64 - 1]. A shift distance must be an exact integer
in [-63, 63]. Invalid kinds, fractions and out-of-range operands produce located
errors. Arguments are evaluated left to right. Existing lazy If/And/Or semantics
and expression budgets remain in force. An operation is pure and has constant
bounded cost. Exact and sampled evaluators share the same mathematical contract;
the compiled tier may use ordinary unsigned-integer CPU instructions.

## Authored composition

A graph supplies its own cell-to-bit table, board geometry and boundary masks.
For a rectangular board it composes shifted frontiers, boundary intersections,
allowed positions and already-visited positions using these operations. A
bounded existing Loop repeats the expansion until its frontier is empty. The
engine knows neither the board dimensions nor what a component means.

For Nine Lives, the JSON graph would build symbol/Wild masks from the drawn
board, sweep symbols in the original first-occurrence order, discover each
component and claim participating Wilds only after a payout. Stable gravity,
RNG order, cap settlement and bonus rules remain authored graph operations.
Ordered trace positions require explicit JSON authoring: either retain the
existing ordered traversal for trace requests, or reconstruct precisely its
breadth-first order. A mask alone does not preserve discovery order.

Other uses include permissions, occupancy, scheduling availability and bounded
feature sets. No operation selects behavior by game, mechanic or graph shape.

## Validation and decision gate

Before game integration, independent tests must cover all 64 single-bit masks,
empty/full masks, bits 31/32/63, union/intersection/difference identities, shifts
at both boundaries, counts, first-element behavior, invalid inputs, operand
error order and lazy branches. Compare Exact, reference and all sampling tiers
against a separate BigInteger/set oracle. Update schema and editor support.

Afterward compare the authored graph against the independent Python full-round
and component corpus, including shared Wilds, collectors, cascades and cap
settlement. Re-run deterministic paired samples, allocation profiling and the
same eight-worker benchmark. Complete paid rounds include all triggered bonus
spins and live statistics; inner cascade steps do not count as paid rounds.
The current acceptance target is a median of at least 2,000,000 complete rounds/s
on eight workers after warmup, with no semantic mismatch. Current acceptance measurements
use three ten-million-round repetitions and include CPU time and GC pauses;
see [optimization evidence](verification/NINE_LIVES_OPTIMIZATION.md).

This is a candidate for a substantial improvement, not a throughput promise.
If the measured result misses the target, report the gap and profile again.
The separate Fold-bound proposal is not a prerequisite and remains unapproved.

Approval was obtained under AGENTS.md, Atomic Graph Composition: “Implement
it only after explicit user authorization, with a documented contract and tests
independent of any game.” The user approved these six operations. This approval
does not extend to additional atoms or the separate Fold-bound proposal.
