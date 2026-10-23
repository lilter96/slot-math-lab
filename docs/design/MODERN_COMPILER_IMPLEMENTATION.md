# Generic primitives and modern compiler implementation

The active objective is the complete implementation of
[the primitive research](PRIMITIVE_RESEARCH.md), including the compiler work,
designer workflows, game migrations, regressions and measured admission rules.
The user explicitly authorized this scope on 2026-10-23. A completed row is an
individually verified milestone; it does not complete the overall objective.

| ID | Required result | Completion evidence | Current state |
| --- | --- | --- | --- |
| R1 | Immutable distribution assets, typed outcomes and finite family selection | JSON/reference/compiled/exact agreement, identity and override regressions | Static references and pooling verified; typed outcomes and families pending |
| R2 | Ordered DrawMany, including explicit mixed-table schedules | Scalar RNG transcript, following Draw, cancellation, snapshots and observations | Pending |
| R3 | Let, typed record/tuple results, expression-valued collections and bounded composition | Lexical scope, exact arithmetic, shape and error-order regressions across execution paths | Pending |
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
