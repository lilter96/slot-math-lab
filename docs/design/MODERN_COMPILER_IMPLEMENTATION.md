# Generic primitives and modern compiler implementation

The active objective is the complete implementation of
[the primitive research](PRIMITIVE_RESEARCH.md), including the compiler work,
designer workflows, game migrations, regressions and measured admission rules.
The user explicitly authorized this scope on 2026-10-23. A completed row is an
individually verified milestone; it does not complete the overall objective.

| ID | Required result | Completion evidence | Current state |
| --- | --- | --- | --- |
| R1 | Immutable distribution assets, typed outcomes, finite families and bounded dynamic weight vectors | JSON/reference/compiled/exact agreement, identity and override regressions | Backend verified; public contract in docs/DISTRIBUTIONS.md; UI/migrations remain R18/R17 |
| R2 | Ordered DrawMany, including explicit mixed-table schedules | Scalar RNG transcript, following Draw, cancellation, snapshots and observations | Backend verified; public contract docs/DRAW_MANY.md; dependency-aware migrations remain R17 |
| R3 | Let, typed record/tuple results, expression-valued collections and bounded composition | Lexical scope, exact arithmetic, shape and error-order regressions across execution paths | Backend verified; contracts in docs/LEXICAL_EXPRESSIONS.md and docs/COLLECTION_EXPRESSIONS.md; designer integration remains R18 |
| R4 | Typed IR with state versions, effects and guarded analyses | IR inspection plus observation/exception/override/cancellation controls | Pending |
| R5 | SCCP, dominance-aware CSE, safe LICM and dead intermediate elimination | Independent equivalence controls and cost measurements; no mechanic dispatch | Pending |
| R6 | Lifetime/alias analysis, buffer reuse and safe collection fusion | Old snapshots, persistent sessions and measurement boundaries remain correct | Pending |
| R7 | Bounded Reachable on arbitrary adjacency, including cyclic data graphs | Independent BFS oracle and independent JSON mechanics | Pending |
| R8 | Scan and stable segmented compaction | Exact prefix oracle, exhaustive small segments and carried attributes | Pending |
| R9 | Designer repeat over collections and runtime subgraph bindings where needed | Editable JSON, apply/undo, nested scopes and responsive visual acceptance | Pending |
| R10 | Bounded foldWhile, any/all | Empty, early stop, short-circuit errors and resource bounds | Pending |
| R11 | Indexed reduction/partition and deterministic scatter | Duplicate-index contract, typed values, snapshot and ownership controls | Pending |
| R12 | Cost-admitted finite-domain pretabulation | Full domain oracle, errors, cache identity, memory/work bounds and negative controls | Pending |
| R13 | Additively constrained independent-product sampling via bounded DP | Exact joint-law oracle and explicit RNG/source identity | Pending |
| R14 | SIMD execution across existing logical streams | Scalar transcript, rejection, chunk schedule and ordered aggregation parity | Pending |
| R15 | Exact authoring of 64-bit masks and bounded wider finite sets | 53/54/63/64-bit literals and multiword independent set oracle | Pending |
| R16 | Bounded equality saturation and measured extraction | Guarded total pure regions, saturation budgets, equivalence and cost controls | Pending |
| R17 | Integrate approved capabilities into all 15 full game configurations | JSON remains editable and atomic; seeded outputs, observations and complete-round benchmarks | Pending |
| R18 | Designer forms, shared-data impact previews, schema generation and release | Ownership/staleness/undo/browser coverage, scoped Rider cleanup and required CI | Pending |

The baseline is merge commit `c325ae74aaca46771f2a0abee69d2a4b92f623c8`.
Its PR checks passed 1,572 Core tests, 187 API tests and 37 critical browser
scenarios. The earlier normalization experiment reduced one expression from
106 AST elements to 24 plus 11 setup elements but regressed throughput 5.43%.
Therefore source size never suffices as a performance admission rule.

Generic semantics remain independent of game names, IDs and authored mechanic
patterns. New primitives are implemented and tested independently before JSON
migrations use them. Existing PRNG constants, stream schedule, exact amounts,
observation boundaries and private project identity remain part of the contract.

The compiler design follows three established mechanisms rather than importing
an alternative game engine:

- [MLIR effect and speculation interfaces](https://mlir.llvm.org/docs/Rationale/SideEffectsAndSpeculation/)
  motivate explicit reads, writes, exceptions, RNG, observations and termination.
- [MLIR bufferization](https://mlir.llvm.org/docs/Bufferization/) motivates
  ownership/use-def proofs before in-place storage reuse.
- [LLVM analysis and transformation passes](https://llvm.org/docs/Passes.html)
  motivate distinct analyses, guarded SCCP/CSE/LICM and independent validation
  of correctness and profitability.

R1 static asset contract: a nullable `distributions` dictionary contains ordered
`outcomes` with the existing `outcomeId`, nonnegative integer `weight` and
numeric payout-channel `value`. The sum must be positive. `Draw.distributionId`
binds one declared asset; it cannot be combined with inline `drawWeights` or
the legacy scalar `weightExpressionId`. Binding snapshots each asset once,
shares that private snapshot between uses, retains zero-weight positions and
never reorders or merges outcomes. Resolution is compile-time data binding;
the immutable reference program and sampled plan execute ordinary Draw
semantics. No asset name selects an implementation. Legacy JSON omits both
new nullable properties, preserving existing serialization and hashes.

Resource limits are 1,024 assets, 65,536 outcomes per asset and 262,144 total
asset outcomes. Invalid and missing assets produce coded compiler errors.
Typed outcome values, finite-family selection and arbitrary dynamic weight
vectors extend this contract in subsequent R1 work; static references alone
must not be reported as completion of R1.

The static R1 milestone is implemented in `DistributionResolver`, nullable
model properties and program-scoped bound operand interning. References bind
before inlining; shared private arrays also reuse the canonical program's
WeightSet. Generated code and interpreted sampling use the same interned
categorical operands, keyed by the entire ordered payload and payout scale.
No per-spin asset lookup or interning occurs.

Verification on 2026-10-23: Release solution build, scoped format verification,
17 new `DistributionAssetTests` cases and all 15 current-game seeded full-state
comparisons passed. Another 21 sampling-plan/CSE cases passed before the
test-stream correction and asset-only total-budget preflight change. The first
three new tier comparisons failed because their test mixed a raw seed with
`RunSingle`'s derived stream; both sides now use `ForStream`, and the final
32-case run passed without skips. Both mandatory Rider profiles completed on
all nine touched C# files, repeated on the final two edited files; known
third-party Roslyn provider warnings are retained in the local cleanup logs.

Generated logs, test results and the final source hash manifest are retained
under `/tmp/modern-primitives-evidence/`, outside Git. No performance gain is
claimed from the static milestone; typed outcomes, finite families, batching,
designer integration and later compiler passes remain open in the table above.

R1 typed/family contract: an outcome's optional `stateValue` is a scalar
constant (`kind` and `value`), independent of its numeric payout-channel
`value`. Missing `stateValue` retains the legacy string `outcomeId`. Integer
and rational literals remain exact Numbers; Boolean and String are retained
as such. Every outcome, including zero-weight positions, must have the same
scalar type across a table and all members of a selected family. Invalid
literals are compile errors, and literals are bounded to 4,096 characters.

`Draw.distributionIds` explicitly lists 1..256 asset IDs in family order;
`distributionIndexExpressionId` references a Number expression selecting an
exact integer index. References, inline rows and legacy computed weights are
mutually exclusive. Missing, negative, fractional, out-of-range and nonnumeric
selectors fail before the Draw consumes random words or writes its result.
Existing initial-state override guards apply to selector specialization too.
Row lengths and zero positions are retained, with no union-domain padding.

Canonical execution chooses a prebuilt Draw program; sampled execution chooses
prebound operands with the compiled selector. Structured exact execution uses
the existing `Sel` IR and one independent random variable per candidate row.
For selector S, row variables Y_i independent of S and Z=Y_S give the required
joint law P(S=s,Z=z)=P(S=s)P(Y_s=z). The selected state and payout use the same
row variable, preserving their correlation. Constant selectors instantiate
only the selected row. The original editable source contains references, not
the private bound operand arrays. Full dynamic weight vectors remain a separate
unfinished part of R1.

The typed/family milestone passed the Release solution build, scoped formatting,
84 selected Core cases and five preview API unit cases. The Core selection
contains 26 new categorical cases, 17 asset cases, all 15 full game fixtures
and 26 affected schema-derivation cases. Independent exact laws pin mean 363/4
for a state-dependent family and mean 2,380 for the joint state/payout control
7*D². Every sampling tier matches scalar alias transcripts, the following word
and complete state. Invalid external selector overrides consume zero RNG words.
Typed integer, wide integer, rational, Boolean and String data retain their
values when settlement forces WinScale 10. Parameterized reusable bodies,
source mutation and JSON round-trips are also covered.

Both mandatory Rider profiles completed for all 11 touched C# files, repeated
for the final test additions. Cleanup diffs were reviewed; existing third-party
Roslyn provider warnings remain in local logs. No frontend or integration suite
was needed for this kernel/model milestone. It adds capability; no new
rounds/second claim is made before full-round performance comparisons.

R1 weight-vector contract: `Draw.weightVectorExpressionId` names an expression
returning a homogeneous Number array with exactly one entry per fixed outcome.
It may use inline outcomes or `distributionId`; it is mutually exclusive with
the legacy scalar weight expression and family selection. The vector replaces
the table's static weights, so inline zero placeholders are allowed. Its
length is at most 4,096, weights are nonnegative and at least one is positive.
Position order and zeros are retained.

The bounded converter retains exact rational masses by taking a common
denominator without proportional normalization. Limits: 8,192 combined bits
per input number, 65,536 total input bits, 16,384 denominator bits and 262,144
projected scaled coefficient bits. Validation and resource failure occur before
the Draw consumes RNG or writes state. The numeric cache is program-scoped,
thread-safe and bounded by 64 entries, 8,192 retained scalar positions and
1,048,576 charged numeric bits. Keys own snapshots; lookups compare values,
including rational numerators and denominators. Valid oversized cache entries
are evaluated without retention. These are charged numeric bounds, not a
claim about exact total CLR heap size.

The reference interpreter evaluates the current vector; compiled sampling
uses bound output data and the numeric alias cache. Proven immutable default
vectors are partially evaluated, with the existing initial-override fallback
restoring the general path. Failed partial evaluation retains runtime errors.
Prepared vectors participate in the same straight-run lowering and generated
code shape as ordinary static Draw; there is no retained dynamic getter on
that path. Binding maps are private compilation data and never source syntax.
Structured exact evaluation supports constant vectors and up to 64 finite
vector values through the existing `Index`/`Sel` IR, preserving the selector,
state and payout joint law. Nonfinite or over-budget support explicitly
declines. The legacy scalar expression behavior remains unchanged.

The final vector milestone passed a 142-case affected Core selection, followed
by 13 sampling-tier equivalence cases after review of the required cleanup
diffs, plus five preview API cases. The Core cases include 37 new numeric and
compiler vector regressions. Initial failures in three tests came from assuming
an existing runner re-imports externally mutated initial arrays every round;
the corrected tests import the changed input into a new runner and separately
verify a graph changing its weights between Draws. Pure cache-key mutation
controls passed throughout. Failed evidence remains local. Final Release build,
scoped format and both Rider profiles completed for all 13 touched C# files,
with final-file passes repeated after edits. No broad integration suite ran.

A same-binary 128-outcome control measured static Draw, immutable vector and
runtime-rebound vector in mirrored order. All 18 complete statistical outputs
matched, including histograms and loop evidence. Each process used CPU 2,
one worker, 1,048,576 rounds, 131,072 warmup rounds and three timed repetitions,
discarding the first. The shared host used .NET 10.0.13.

| Path | Median rounds/s | Retained range | Bytes/round |
| --- | ---: | ---: | ---: |
| Static Draw | 4,843,118 | 4,822,272–4,867,704 | 12.79 |
| Prepared immutable vector | 4,877,239 | 4,843,692–4,918,179 | 12.79 |
| Runtime vector plus one equivalent state write | 642,013 | 615,846–664,904 | 44.79 |

The prepared/static ratio is 1.007 and their ranges overlap. The prepared/runtime
ratio is 7.60, including the additional runtime state write, so it does not
isolate cache performance or imply a game-wide speedup. An earlier lowering
kept a dynamic chain around already prepared operands and measured 10.7%
below the matched static control. That negative control prompted reuse of the
ordinary Draw code shape; both sets of reports remain under
`/tmp/modern-primitives-evidence/vector-benchmark{,-v2}/`. Remaining runtime
allocation and its numeric-cache traversal are candidates for R6, not evidence
of a zero-allocation runtime-vector path.

R2 contract: a `drawMany` node declares a required `outputKey` and exactly one
`distributionId` (repeat one asset) or `distributionIds` (explicit ordered
schedule). A nullable fixed `count` defaults to one for a repeated distribution
and to the schedule length otherwise. Alternatively `countExpressionId` requires
an explicit `maxCount`, with no simultaneous fixed count. All counts and maxima
are in [0,4096]; a scheduled count selects a prefix and cannot exceed the
schedule length. An empty schedule and a zero count are valid.

Each element performs exactly the ordinary scalar alias call, retaining zero
positions, rejection sampling and order. The homogeneous typed data array is
committed once after the batch completes. The value-channel is the last scalar
payout in WinScale units; zero draws preserve the incoming value-channel.
Count validation occurs before randomness or the output write. Cancellation
cannot publish a partial array; consumed words still belong to the interrupted
attempt under the existing lifecycle contract. Snapshot exports copy arrays,
and persistent/observed execution retains the existing arena restrictions.

Canonical execution composes ordinary Draw slots using immutable accumulator
links; the compiled batch uses private array buffers. Structured execution
builds independent element RVs and shared prefix arrays, selecting among at
most 256 proven finite counts. It retains the joint dependence of count, array
and final payout. A batch has one observation point. Migrating old per-draw
fields or measurement points requires explicit dependency checks or preserves
the scalar graph; no kernel pass recognizes authored mechanic patterns.

R2 verification: the first 162-case affected Core selection passed with no
failures, followed by a 30-case DrawMany-only run after four boundary/type cases
were added. This covers 166 unique passing Core cases and five preview API unit
cases. New controls include scalar alias transcripts and the following Draw in
all tiers; zero/fixed/dynamic counts; schedule prefixes; 64/65 and 4,096 elements;
wide/rational/Boolean/String arrays with WinScale 10; snapshots across runners
and persistence; post-batch measurements; cancellation; reusable count bindings
and editable JSON. Loop liveness now includes all stochastic operand reads;
independent loop means 69/4, 169/6 and 59/4 cover batch, family and vector reads.
The random-count joint law has mean 1269/8 and four independently pinned masses.

Both mandatory Rider profiles completed for all 13 touched C# files and again
for final test edits. Release build, scoped format and cleanup-diff review
passed. Local logs and the frozen source manifest are under
`/tmp/modern-primitives-evidence/`; generated outputs stay outside Git.

A frozen Release control compared 30 scalar draws plus fused append assembly
against one batch, on CPU 2 with one worker, mirrored scalar/batch/batch/scalar
processes, 524,288 full rounds and 131,072 warmup rounds per process. Three timed
repeats were collected, discarding the first. All statistical outputs matched.

| Schedule | Scalar rounds/s [range] | Batch rounds/s [range] | Observed ratio |
| --- | ---: | ---: | ---: |
| Repeat one table | 1,160,800 [928,493–1,211,833] | 1,406,107 [1,402,757–1,417,477] | 1.211 |
| Explicit mixed tables | 1,154,587 [994,308–1,278,203] | 1,197,758 [1,145,492–1,362,672] | 1.037 |

Mixed ranges overlap substantially; this run does not establish a reliable
mixed-table gain. Allocation stayed close: 7.25→7.22 and 8.34→8.31 bytes/round
in the respective cases because the scalar baseline already reuses arrays.
The relevant authored fragment shrinks from 30 Draw nodes plus one collection
write to one DrawMany. These are local primitive controls, not whole-game
performance estimates. Raw inputs and reports are retained in
`/tmp/modern-primitives-evidence/batch-benchmark/`.

R3 lexical/record milestone: `let` has eager, nonrecursive bindings in a
namespace separate from state; ordered records and positional record tuples
retain typed nested shapes. Canonical evaluation, closure sampling, generated
IL and hot-tier sampling agree. The structured exact evaluator carries shared
random-variable dependencies through records and passes lexical operands to
opaque expressions without changing the identity used to intern those terms.
Strict sequencing preserves potentially failing unused initializers.

The serializer-derived expression schema now exports registered variants once
and uses a shared reference for nested expressions, preserving nullability and
required discriminators without raising authored JSON depth limits. Scope and
schema regressions passed 462 selected Core cases and five API preview cases,
including 40 new lexical/schema cases and 64 generated arithmetic controls.
Both mandatory Rider profiles covered all 20 changed source files; final
scoped formatting and the Release solution build passed. Large generative
sweeps, convergence runs, integration and browser suites were not run for this
milestone. No game-wide throughput claim is made. Records and fallback lexical
environments still allocate; lifetime-based removal remains required R6 work.
Expression-valued collection inputs and bounded nested composition are verified
in the following R3 backend milestone. Designer integration remains R18 work.

R3 bounded composition adds nullable `source` and `maxItems` to map, filter,
fold and all five aggregate functions. Exactly one source/state-key input is
required. Explicit bounds are 0–4096; all iterators composed inside a bounded
form must declare their own bounds. Cardinality is checked before body entry,
fold initializes before reading its source, and completed producer passes
precede consumer bodies. Legacy JSON omits nullable properties. Cost estimates
multiply nested bounds, add sequential work and saturate safely instead of
wrapping. Shapes, lexical scopes, expression references and loop liveness
include the source expression.

Numeric aggregates retain symbolic arithmetic when their elements are visible.
The same 64-choice binomial control that exceeded a one-million-work budget
with opaque aggregation now returns all 65 exact masses within that budget,
with independently checked mean 320, second moment 102592 and variance 192.
Strict Count selectors and empty min/max errors remain observable. Conditional
sources retain shared random variables across loop iterations, verified against
complete classic distributions in unrolled and forced-chain execution.

Initial verification passed 531 selected Core cases and five API preview
cases. The final typed-source lowering passed all 180 affected Core cases,
including 71 new source/composition cases. Tests include 128 complete-round
seed comparisons per sampling tier, runtime overrides of proven constants,
wide/rational values, cancellation and repeated failure restoration. Both
mandatory Rider profiles covered all 14 touched source files; scoped formatting
and the Release solution build passed. Large generative, convergence,
integration and browser suites were not run for this milestone. Automatic
state-field removal and collection fusion remain R4–R6 work and require
observation/error-order proofs.

The R3 primitive simulation control initially showed no gain; a warmed
immutable-array comparison had a 0.929 median ratio with overlapping ranges.
Inspection identified an unnecessary state-cell conversion of expression
results. The final generic typed-value lowering preserves validation and
normalization while avoiding that large intermediate. Final matched ABBA
controls measured 1.038× for immutable inputs and 1.100× for inputs written
during the round, with nonoverlapping retained ranges and identical live
statistics. Allocation rates stayed near 6.63 bytes/round. The Modify fragment
shrinks 3→1, while full controls shrink 5→3 and 6→4. This is not a full-game
performance estimate or proof for removing observable authored fields.
[The collection contract](../COLLECTION_EXPRESSIONS.md) records method, ranges,
limits and the retained negative control. R4–R6 and all game migrations remain
required.
