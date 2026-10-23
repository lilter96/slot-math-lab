# Scalar value reuse and intermediate storage

These R5 passes consume the verified expression/effect IR while lowering generic
atoms. They do not recognize games, mechanic IDs or authored mechanic patterns.
The authored graph, logical writes and observation points are preserved.

## Dominating successful values

`ScalarReusePlan` numbers available expression results in one straight
continuation. An earlier whole-expression evaluation executes before a later
use on every path through that continuation. A successful scalar result can
therefore live in an IL local while its dependencies remain unchanged.

The plan uses owned named-expression IR and the node's complete write footprint.
A write invalidates available values that read the affected fields. RNG,
observations, unknown effects and temporary iterator bindings prevent reuse
across the boundary. All original cancellation checkpoints remain in place.

This is successful-evaluation availability, not permission to speculate a
possibly throwing operation. The first evaluation still occurs at its original
point and must complete before any reuse. No division, missing read or branch
operand is evaluated earlier. See [LLVM value numbering](https://llvm.org/docs/Passes.html#gvn-global-value-numbering)
and [MLIR effects/speculation](https://mlir.llvm.org/docs/Rationale/SideEffectsAndSpeculation/)
for the distinction between dependency reasoning and legal motion.

Admission currently covers repeated intrinsic scalar roots (arithmetic,
Boolean/comparison and finite-set operators) with static cost at least five.
Their successful results are immutable scalar values. Inferred external shapes
alone do not establish ownership of array or record results. Packed mask
outputs are excluded from scalar capture, preserving their representation.
[Collection ownership and buffer lowering](COLLECTION_BUFFERS.md) follow in R6.

Only groups with multiple valid uses allocate a local. The analysis charges
AST work and invalidation scans, limits serialized signature data, and discards
all partial groups when its budget is exhausted. Structural keys include the
complete ordered expression, literals, paths and annotations. Hash collisions
cannot establish equivalence. Locals belong to one invocation, not to a shared
program or worker cache.

## Lazy loop invariants

Native collection loops can retain an invariant scalar across elements when
its reads do not intersect item, index or accumulator bindings. The enclosing
selector must have known scalar-only syntax; nested collections start separate
scopes rather than lending their bindings to an outer proof.

The cached value is evaluated at its first actual use inside the loop. It is
not evaluated on an empty collection or in an untaken conditional arm. A flag
is set only after success and reset at each collection invocation. A nested
loop therefore refreshes its invariant when an outer item changes. Variant
entries retain the existing per-element cache lifetime. Cancellation checks
and binding restoration remain at their original positions.

This is lazy invariant reuse: it removes repeated heavy work without requiring
an unconditional preheader evaluation. [LLVM LICM](https://llvm.org/docs/Passes.html#licm-loop-invariant-code-motion)
also distinguishes invariant dependencies from the safety of moving loads or
calls. This engine retains defined exception behavior, including zero-entry
and short-circuit cases. Native Map, Filter, Fold and Count lowering can use
this lifetime; other execution forms conservatively retain their current path.

## Repeated stores with observable state

`DeferredStorePlan` admits a field only if it is written multiple times and
never read inside the straight run. All stores to that field must produce
intrinsic scalars or literal values, with known effects and no iterator state.
Unknown operations or observations decline the plan; Draw outputs block
deferral for the keys they write. Complete IR read sets include lazy branches,
so an internal read cannot be hidden by an input-specific execution path.

The generator keeps the latest completed stored value and a presence flag in
locals. A `finally` publishes that value on normal return, evaluation failure
or cancellation. The original eager expressions and cancellation checkpoints
still execute in order. If a later expression fails, the previous completed
write is published. If the first expression fails, the flag stays false and
old raw storage is untouched. Legacy integer truncation and packed mask
representation occur before capture, exactly as in an ordinary store.

Thus the final/exported state remains observable. This removes intermediate
physical cell stores; it does not delete authored state fields, skip eager
errors or hide state updates from measurements. There is no observer inside
an admitted run. The run publishes before its next continuation or observer.
The captured scalar publication itself performs no expression evaluation.

## Dead lexical values

The expression simplifier removes an unused `Let` initializer only when the
IR proves it speculatable, with no state reads or iterator bindings. Local-use
analysis respects nested initializer scope and shadowing, including references
inside collection expressions. Failed literal parsing, division by zero and
possibly missing reads remain eager. An authored state key is never classified
as dead merely because the payout does not depend on it.

## Verification

Regressions compare independent canonical execution and generated code, with
controls that confirm reuse/deferral was actually emitted. They cover state
clobbers, distinct operands, effect/measurement barriers, budget fallback,
per-invocation reset, errors before and after writes, the 256th cancellation
checkpoint, legacy storage, bit 63, lazy empty/untaken collection paths,
iterator shadowing, nested Fold scope, and RNG transcripts plus the following
word. Existing seeded full-state game, collection, mask and lifetime controls
remain part of final validation.

The completed stage passed 381 selected Core cases and five API preview cases,
including 29 new controls and the existing 15-game seeded full-state corpus.
Both mandatory Rider profiles covered all eight touched C# files. Final scoped
format verification and the Release solution build passed. Full integration,
browser and large generative suites were not run for this compiler stage.

## Measured admission controls

Previous and new frozen Release binaries used the same .NET 10.0.13 runtime,
CPU 2, one worker and ABBA process order. Both retained all preexisting
simplification. Each process ran three measurements after warmup; the first
measurement was excluded from timing summaries. All measurements retained
identical counts, RTP, variance, hit frequency, maximum payouts, histograms,
cap counts and loop termination evidence.

| Control | Before, thousand rounds/s range | After, thousand rounds/s range | Median ratio |
| --- | --- | --- | ---: |
| Sixteen repeated nine-op scalar expressions after a Draw | 632–653 | 1,643–1,774 | 2.656 |
| Nine-op invariant in a 64-item Map | 162.5–166.7 | 202.2–206.3 | 1.233 |
| Two writes to one unread field | 2,059–2,080 | 2,011–2,086 | 1.001 |
| Sixteen writes to one unread field | 1,980–2,008 | 1,985–2,014 | 1.005 |
| Current Nine Lives paid round | 136.4–137.3 | 136.5–139.2 | 0.999 |
| Current Le Militare paid round | 42.4–45.8 | 41.8–45.7 | 0.996 |

The scalar control used 262,144 rounds and 65,536 warmup rounds; the Map
control used 131,072 and 65,536. Store controls used 1,048,576 and 262,144.
Game controls used 32,768 of each. Scalar expressions wrote distinct fields,
so their gain measures value reuse rather than store deferral. Store controls
used literal values, so no scalar value-numbering opportunity was present.
The Map control includes the subsequent Sum and complete round settlement.

Allocations were unchanged within every pair: 6.65 B/round for scalar reuse,
6.66 for the Map, 6.55 for store controls, 46.68 for Nine Lives and 277.25 for
Le Militare. Store deferral does not establish a throughput gain in these
controls; it preserves state semantics while removing physical intermediate
stores. The real-game ranges overlap, so the stage establishes no substantial
current-game speedup. It demonstrates gains on admitted scalar/invariant work,
not a multiplier for whole games or multiple workers.

Cold compile samples were 547.5/552.4 ms before versus 538.0/543.9 ms after
for Nine Lives, and 578.3/584.2 versus 588.6/590.4 ms for Le Militare. These
are only two cold observations per version. Tiered native generation occurs
after the hot threshold, so cold compile time alone does not include all
preparation work. R6 collection-buffer lowering is recorded separately; further game migrations
require their own complete-round admission measurements.

Frozen Core SHA-256:
`0db619add9dd8451e4b06d76561c6ad1648275b8863e92085cd1862bfd8cdadd`.
