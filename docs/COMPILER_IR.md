# Typed expression regions and compiler effects

The expression stage of row R4 builds an
internal IR for every resolved named expression, node port default, loop exit
reason and measurement expression. `CompileResult.Ir` exposes it internally for
inspection and verification. Compilation uses its write/binding summaries to
choose immutable initial-state facts for the sampling plan.

[Graph memory SSA](MEMORY_SSA.md) adds verified state versions, branch/loop
phis and guarded read queries. It supplies the write footprint used for initial
facts. CSE, LICM and elimination of observable state remain R5/R6 work.

## Values, scopes and execution order

Every expression result and region parameter has a unique SSA definition in
its expression program. An `IrValue` carries its inferred scalar/record/array
shape and, where proved, a captured exact value. Shapes describe the authored
contract; they do not establish that arbitrary external state is present or
has the declared type.

A region contains operations in execution order. `if` owns separate then/else
regions; logical and/or retain the right operand in a conditional region.
Values defined in one sibling region cannot be used by another. `let` eagerly
defines its initializer in the outer region and makes that definition visible
in its body. Local names and state names are distinct.

Map/filter/fold/aggregate regions bind element/index/accumulator parameters.
Reads of those bindings reference SSA values rather than recurrence-state
resources. Their names are nevertheless retained as scoped bindings because
the current sampling emitter uses temporary frame cells for fallback code.
They must not inherit global constant-state facts.

Fold initialization precedes source evaluation. A source finishes before its
consumer region begins. Aggregate iteration has separate predicate and
selector-if-selected regions, preserving conditional selector errors.
Projection retains the full nested shape. Unknown or over-deep forms produce
opaque barrier effects rather than guessed types or implicit purity.

IR root keys are structured `(kind, owner, slot)` values. User-authored
separators in names cannot collide across expressions, ports or measurements.
Collections and operation/region lists are immutable snapshots.

## Effects and guards

Effects identify recurrence reads/writes, possible semantic errors, allocation,
RNG, observations, cancellation and unknown behavior. Each operation has its
own effects plus a recursive summary for its nested regions. A region also
summarizes its ordered operations. Allocation is conservative, including
potential exact-number payload allocation; out-of-memory behavior is not a
new authored game effect.

An inferred numeric field read remains potentially throwing: it may be absent,
null or overridden. Successful evaluation against a captured immutable fact
can establish a narrower guarantee. That guarantee records a guard on its
source field; an override of that field invalidates it. Captured arrays and
records own a snapshot, so caller mutations cannot alter the evidence.
Constant scalar analysis uses its SSA operands, including lexical values,
rather than accidentally reading an identically named outer field.

`CanSpeculate` is only one condition for a future transformation. Memory
conflicts, SSA dominance and execution/observation boundaries must also be
proved. A known empty write set is distinct from unknown writes. Unknown
operations conservatively include RNG, writes, observations, errors and
cancellation, and disable initial-state specialization.

Node summaries use generic node kinds. Transform/evaluator IDs never select
behavior, inferred types or effects. A resolved categorical Draw records RNG
and its declared state destination; DrawMany also records allocation and its
count expression. Loop counters, accumulated wins and exit reasons are
writes. Settlement evidence and measurement scopes are accounted for.

Ordered outgoing edges are retained. The current runtime can execute multiple
continuations sequentially against shared state. They must not be mistaken
for mutually exclusive CFG predecessors when graph memory SSA is added.

The design separates resources, speculation and SSA reasoning, following
[MLIR's effect model](https://mlir.llvm.org/docs/Rationale/SideEffectsAndSpeculation/).
The state-version analysis follows the distinction between memory
definitions, uses and merges described by
[LLVM MemorySSA](https://llvm.org/docs/MemorySSA.html), while preserving this
engine's defined exceptions and sequential continuation semantics.

## Verification and limits

`IrVerifier` checks unique definitions, dominance across region boundaries,
region ownership and consistency of recursive effect summaries. Negative
controls intentionally leak a sibling definition or remove an error summary;
both are rejected. Semantic regressions separately compare execution with the
canonical program, including external overrides and complete exported state.
The verifier does not itself prove the correctness of an opcode's effect rule.

All 15 current game configurations have their expression regions inspected by
the regression corpus. Integration into immutable-fact selection is exercised
by the existing seeded full-state comparisons. This foundation has compile-time
cost and does not claim a simulation-throughput improvement on its own.

The first R4 stage passed 272 selected Core cases and five API preview cases.
Thirty-six new cases include all 15 game fixtures, negative verifier controls,
scope/type/effect rules and runtime override equivalence. Both mandatory Rider
profiles and scoped format verification covered all eight changed C# files;
the Release solution build passed. Full integration and browser suites were
not run for this compiler-analysis stage.

Cold preparation was measured with ABBA processes on CPU 2, one worker, the
same .NET 10.0.13 runtime, and the same input JSON. Only 128 rounds follow each
compile, to check functional output rather than steady-state throughput.

| Model | Before IR, compile ms | With IR, compile ms |
| --- | --- | --- |
| Small collection composition | 120.1, 127.4 | 121.5, 146.9 |
| Current Nine Lives config | 238.6, 257.3 | 271.7, 258.7 |

All reported outcome statistics matched. Two cold observations per version
are not a precise cost model; these runs show a preparation cost that later
passes must justify, rather than a claimed speedup. The stage adds no IR
interpretation to the spin loop. The subsequent memory stage is recorded in
[the memory SSA contract](MEMORY_SSA.md). The first transformation is
[conditional state constant propagation](STATE_PROPAGATION.md), followed by
[scalar value reuse, lazy invariants and intermediate-store elimination](VALUE_REUSE.md).
[Collection ownership and guarded fusion](COLLECTION_BUFFERS.md) follow in R6;
state-backed arrays and unproven aliases retain copying.

Frozen Core SHA-256:
`281e21003ad93169ad8075bdc2083523dc76bf07ed1d4c17f281f1c9dc1bde08`.

The [Reachable primitive](REACHABLE.md) adds an explicit ordered numeric
operation with a vertex bound and cancellation/error/allocation effects.
Dominated-predicate admission now checks recursive IR effects before adding
assumptions; repeated cancellation-bearing predicates remain at their authored
execution points. The effect check is structural and independent of mechanics.
