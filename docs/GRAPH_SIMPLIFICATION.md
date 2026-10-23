# Automatic graph simplification

The compiler specializes the sampled execution plan automatically. **Build →
Simplify graph** previews permanent simplifications of the editable JSON, including
custom subgraph definitions. Applying the preview uses the existing whole-project
validation/transaction boundary; **Undo simplification** restores the preceding
source and editor context. A stale or cancelled preview cannot commit to a newer
draft or another project.

The system optimizes the declared program. It does not infer missing game rules,
invent a payout, remove random draws, change a cap, or guess how to repair an
invalid graph. Validation errors must be repaired first.

## Transformation contracts

All rules operate on generic expressions, independently of game names, node IDs,
mechanic identifiers or authored graph patterns. No new atom or expression function
is introduced. Reusable definitions remain reusable JSON; there is no replacement
game runtime or baked evaluator.

| Rule | Justification | Required guard |
| --- | --- | --- |
| Exact constant evaluation | Evaluate a closed scalar operation with the existing rational evaluator | Successful evaluation, bounded operands and result size |
| Constant condition | Only the selected arm is evaluated | The condition is an actual Boolean constant |
| Boolean short circuit | The skipped operand was never evaluated | The left constant determines the result |
| Dominated predicate | The same pure predicate has already succeeded on this path | Structural equality and the same lexical bindings |
| Numeric/Boolean identity | Remove an identity around an already checked result | The retained operand checks its type itself, or a guarded numeric field invariant proves it |
| Double negation | Reuse an already checked Boolean | The retained operand enforces Boolean output |
| Immutable-state specialization | A read has the same value throughout the runner | Existing write/binding analysis and the runner's override fallback |

Arithmetic stays exact, including rational denominators. Operand order is retained.
Failed constant evaluation leaves the operation in its original position. For
example, `missing * 0`, `x / x`, and a raw field's `x + 0` are not reduced: doing so
could erase a missing-field, division or type error. Predicate facts are discarded
at collection binding boundaries, and locally bound names cannot use outer state
constants. Collection loops are not executed by the optimizer.

Sampling specialization uses the existing set of unwritten initial-state fields.
It never replaces initial-state reads in the persisted source. A runner supplying
an overriding field uses a fresh plan compiled from the **unspecialized** source.
Worker replicas also start from that source. Exact/reference execution continues
to interpret the original program, providing an independent comparison path.

Numeric identities can also use the sampler's existing finite-mask proofs: initial
values and every writer must preserve a numeric value, with local bindings and
measurement aliases excluded. Supplying an overriding field selects the existing
plan without mask assumptions, which also disables these numeric facts. Merely
declaring a field's schema as `number` does not authorize removing runtime type
checks. Fold/map/filter binders cannot inherit a same-named outer field's proof.
Reduced conditions feed the existing constant-branch compiler, which avoids
building the unchosen execution chain while retaining the source topology.

Nodes, edges, execution ordering, loop limits, writes and measurement point IDs are
retained. The current pass simplifies the expression graph; it deliberately does
not erase control-flow nodes or state writes. Static expression cost is an AST
estimate, not a prediction of rounds per second.

## Preview API and resource bounds

`POST /api/validate/simplify` accepts `{ "config": <complete draft> }`. It validates
the source, returns the candidate, source/candidate hashes and per-expression JSON
pointers with rule counts and before/after static costs, then validates the
candidate. It never saves a configuration. The API retains unknown project data;
expressions carrying unsupported extension properties are left intact and listed
under `retained`. Draft hashes include opaque authoring data and exact JSON number
tokens; they are source fingerprints, separate from compiled math evidence hashes.

The graph pass visits at most 250,000 expression positions, with at most 50,000
visits per root and depth 128. Constant evaluation has a separate work allowance;
literal/result length is limited to 4,096 characters and immutable collection
operands to 4,096 items. Reaching a traversal limit returns the completed safe
rewrites with `budgetReached`, leaving remaining expressions intact. Cancellation
is observed between roots and preview patches. These bounds are additional to
the compiler's existing validation limits.

## Validation and further optimization

The regression corpus includes all 15 configurations of the current five game families, exact rational
examples, reachable and unreachable errors, lambda shadowing, initial-state
overrides, idempotence and immutable source handling. Browser validation exercises
preview, nested definitions, atomic apply, undo and cancellation. Performance
comparisons must use complete rounds, fixed seeds/streams and matching statistics,
with compilation and warmup separated from measured execution.

`GraphCompiler(simplifyExpressions: false)` disables the new expression pass while
retaining the existing compiled sampler. The benchmark harness accepts
`--no-simplification` for paired comparisons in the same binary. `--reference`
instead selects the general interpreter and is a different performance control.

The next useful extensions require evidence from actual workloads:

- General control-flow effect/dependency analysis, then loop-invariant motion
  where observation timing and state visibility can be preserved.
- Bounded interval/range proofs for redundant guards, including empty collections,
  exact rational bounds and exceptional operations.
- Explicit authoring transformations for repeated fragments and source dependency
  closure, with previewed changes to identifiers and observation references.
- A cost model calibrated against measured generic atom costs. A smaller AST must
  not automatically be assumed to be a faster sampled plan.

These are separate capabilities, not hidden game-specific fast paths. Existing
game configs serve as a regression and performance corpus, not as patterns that
the runtime recognizes.

## Measured current game corpus

The comparison below uses the same frozen Release binary with the new pass off/on/on/off. Measurements ran on .NET 10.0.13 and a 13th Gen Intel Core i5-13600KF shared Linux host. Sampled payout totals, variance, hit frequency, maxima, caps, histograms and loop evidence matched for every measured repetition. Nine Lives also matched between one and eight workers. This maintained acceptance summary covers the current corpus; generated reports and screenshots stay outside Git. Full local evidence, input hashes and allocation/compilation measurements are retained under `/tmp/graph-refactor-evidence/`. Source-only rewriting simplified 12 expressions across all 15 full configurations without reaching a budget limit.

| Configuration | Workers | Before rounds/s | After rounds/s | Observed change |
| --- | ---: | ---: | ---: | ---: |
| nine-lives | 1 | 391,111 | 392,853 | +0.45% |
| ancient-dragon | 1 | 291,276 | 303,822 | +4.31% |
| woodland-whisper | 1 | 259,839 | 273,113 | +5.11% |
| x7-club | 1 | 345,222 | 342,870 | -0.68% |
| le-militare.spin | 1 | 144,286 | 143,841 | -0.31% |
| le-militare.standard (bonus entry) | 1 | 6,150 | 6,096 | -0.88% |
| nine-lives | 8 | 2,068,845 | 2,092,596 | +1.15% |

Rounds include triggered features. `le-militare.standard` is an entire configured bonus entry, so its throughput is not directly comparable to a single paid spin. Single-worker processes were pinned to logical CPU 2; the eight-worker case used the shared host. Each process warmed up and ran three repeats; its first timed repeat was excluded from the reported median. Compilation, worker initialization and warmup are outside throughput.

These local observations do not establish a universal speedup. The existing generated sampler already folds many expressions, so source simplification and steady-state throughput are separate results. Compilation cost increased by roughly 14–27 ms in the single-worker cases; allocated bytes per round were unchanged. The small negative results are retained, and these runs are insufficient to establish their cause. The raw report retains the per-case ranges.

Reproduce each side with the same Release build, substituting a fixture name and worker count:

```sh
dotnet run -c Release --project backend/SlotMath.Benchmarks -- \
  --graph backend/SlotMath.Core.Tests/TestData/Games/nine-lives.json \
  --workers 1 --rounds 1048576 --warmup 262144 --repeats 3 --live \
  --report /tmp/graph-simplification-on.json
```

Add `--no-simplification` for the control. Alternate off/on/on/off processes and discard the first timed repetition from each. The bonus-entry case used 16,384 rounds and 2,048 warmup rounds. Compare the complete statistical output before interpreting the timing difference.
