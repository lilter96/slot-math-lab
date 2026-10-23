# Graph state versions and continuation memory SSA

The compiler builds and verifies a sparse memory SSA module after resolving
expression roots. It uses the module's write footprint when selecting
immutable initial-state facts. Expression regions retain their typed SSA;
this module describes versions of the recurrence store around graph operations.
No game names, transform IDs or authored mechanic patterns select an analysis.

## Representation

A version denotes the complete store. A known write names its affected fields;
a read's reaching-definition query skips writes to disjoint fields. A phi joins
possible predecessor stores. Conditional writes and call summaries conservatively
retain the incoming store as an alternative. Unknown writes clobber every field.
Queries walk a worklist with a visited set, so loop-header cycles terminate.

Continuation functions are keyed by `(node ID, stop-sink ID)`. Each has one
memory input and one result. Shared graph tails remain calls; they are not
expanded into every possible path. The two possible sink contexts (round
continuation and loop body) remain distinct. Call sites record their actual
incoming store and their conservative result footprint. A callee's input is a
function parameter; it is not implicitly substituted with every caller's state.

This follows the separation of memory definitions, uses and phis in
[LLVM MemorySSA](https://llvm.org/docs/MemorySSA.html), adapted to the engine's
continuation semantics. It does not turn the graph into an ordinary CFG by
assuming that every incoming edge represents an exclusive branch.

## Control and observation semantics

- Ordinary fan-out calls every downstream continuation in authored edge order.
  Each call receives the store returned by the preceding call. Their data
  channel is the same original/node-output value, not the preceding result.
- A true/false branch invokes one continuation. Its returned stores join in a
  phi; a missing branch retains the incoming store.
- A loop first initializes its counter and accumulated wins. Its header phi
  merges that entry with the backedge. The cap test precedes the optional stop
  expression. Body calls use a null data input. Counter and accumulated-win
  reads occur after the body, because the body may overwrite them. The counter
  advance is definite; the win update depends on the body's numeric/Win[]
  channel. Exit classification is conditional on leaving below the cap, followed
  by reason storage, termination evidence and the loop-total read.
- `IterationCap` records the counter comparison, not a separate termination
  theorem. A body that resets the service counter can prevent reaching the cap;
  the IR preserves that clobber rather than inventing a fixed trip count.
- Measurement expressions live under a collector-present region at continuation
  entry. They are observation/error boundaries. Unknown future expression writes
  merge back conservatively instead of being discarded.
- Top-level entries execute sequentially. Reaching the designated sink inside a
  continuation returns the data channel; final settlement reads state and commits
  evidence once after the entry calls. The final store remains exportable.

## Guarded queries and compiler use

`SameValueSource` answers only a memory question. It is not permission to remove,
move or share an expression. `CanReuseRead` is restricted to a single-field,
owned expression root in one ordered region. It also requires totality, valid
captured-fact guards, matching root identity and no intervening RNG, error,
observation, cancellation or unknown-effect boundary. Writes to that field
invalidate it. A declared field shape does not establish presence or runtime
kind after an external override.

The compiler verifies the memory module before consuming it. Initial-state
specialization excludes every field the module may write, every scoped iterator
binding and reserved settlement/service fields. Unknown writes disable facts.
Existing runner overrides still select a plan without those assumptions.

This stage does not perform SCCP, CSE, LICM, dead-store elimination or buffer
fusion. Those R5/R6 passes must rebuild/invalidate the memory analysis after
changing the graph or its expression effects. Full exported state and user
observation points remain part of their equivalence contract.

## Verification

The memory verifier checks definition inputs, ordered region consumption,
call links/effects, conditional merges and loop entry/backedge/exit versions.
Only recognized loop-header phis may have forward backedge inputs. Negative
controls intentionally discard a conditional write, return stale memory or
introduce an unstructured self-cycle. Structural checks supplement semantic
comparisons with the canonical interpreter; they do not independently prove
every builder rule.

Regression controls include disjoint and overlapping writes, sequential fan-out,
branch alternatives, post-body counter reads, cap/exit-reason short circuit,
unknown clobbers, measurement order, settlement, shared tails and guarded read
reuse. The full 15-game expression-IR corpus also verifies its memory modules.
A counter-reset example is inspected without executing a potentially
nonterminating graph.

The completed R4 backend passed 290 selected Core cases and five API preview
cases. Eighteen new memory-specific cases supplement the 36 expression-IR
cases and existing 15-game seeded full-state controls. Both mandatory Rider
profiles, scoped format verification and the Release solution build passed for
all six changed C# files. No full integration, browser or large generative suite
was run for this stage.

Cold preparation control used the same .NET 10.0.13 runtime, CPU 2, one worker,
ABBA processes and identical JSON. Only 128 rounds followed compilation to
verify outputs. All measured outcome statistics matched.

| Model | Expression IR only, compile ms | With memory SSA, compile ms |
| --- | --- | --- |
| Small collection composition | 120.0, 111.6 | 137.7, 132.5 |
| Current Nine Lives config | 238.1, 294.9 | 448.7, 475.3 |

The preparation increase is material on the larger model. Two cold observations
per version are not a precise cost model. This stage changes analysis and
fact selection, and claims no steady-state throughput gain. Subsequent passes
must justify their preparation and retained-analysis cost against measured
execution savings; shared continuation functions alone do not remove the
cost of transitive effect footprints.

Frozen Core SHA-256:
`a5a27b14e49cb157894919951bedcb5f7aeba4b275768694309c46580b539964`.
