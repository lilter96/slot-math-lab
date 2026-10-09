# Measurement implementation ledger

Active objective: implement the full researched metric catalogue in the production application, with independently checked math, authored semantics, durable live/saved evidence and native configuration. This ledger records engineering progress, **not a claim of completion or certification**.

## Implemented and pushed in `111756a`

- Optional versioned measurement options preserve the legacy plan/hash when absent.
- Explicit observation, zero-filled paid-round and nested feature-episode populations; ten within-subject reductions; Boolean event/count sources without numeric dummy expressions.
- Worker-private transactional collection; discarded unfinished rounds; bounded group/support/bin/tail/lag/award storage; compiler expression/type/lifecycle/storage-budget validation.
- Stable pairwise central moments through order four, paired covariance/correlation/ratio, empirical exact support and complete fixed bins with sums/squares, inverse-CDF quantiles with honest bin enclosures, threshold tails, tie-aware upper-tail means/concentration and empirical absolute-deviation enclosures.
- Declared independent-subject fixed-count inference, exact binomial limits, conservative bounded time-uniform mean intervals, family error allocation, equivalence requiring the interval inside tolerance, zero-variance insufficiency, paid-round cluster ratio inference.
- Ordinary importance-weighted means/event estimates, effective sample size/weight ranges, weighted mean intervals; weighted support comparison avoids an invalid multinomial chi-square verdict.
- Ordered chunk-boundary gap/streak/autocorrelation summaries; unordered live data and cancelled streams containing holes cannot claim ordered inference.
- Raw/settled payout capture in both compiled and reference samplers; separate cap-deduction source; global cap reach includes payout equal to cap.
- API transport, pinned hashes/replay, cumulative WS/checkpoints/final evidence carry the richer snapshots. Browser validates rich snapshot consistency before accepting it.
- Native editor population, lifecycle, grouping, pairing, award, distribution, uncertainty/reference and weighting controls. Shared live/results analysis displays distributions, quantile/tail bounds, accounting, paired/ordered statistics and typed verification outcomes.
- Independent exact rational finite-law and absorbing-state reference calculators; native authoring workbench, bounded APIs and evidence export. Unnormalized pruned first/second/event/variance bounds; reachable closed classes produce explicit nontermination.
- Prior CI browser failure identified as quota handling in the one-round fixture; safe GET retry and bounded launch wait added without changing production limits.

## Additional implemented workflows

- Independent paid rounds, one persistent trajectory and independent fixed-horizon sessions, with declared retained state, explicit wagers/bankroll, deterministic per-session streams and complete/interrupted exposure accounting.
- Session return/profit/profitability, ending bankroll, drawdown, ruin and conditional first-passage time, drought, duration, maximum and authored feature waiting with censoring and invalid-subject accounting.
- Searchable native catalogue: 159 definitions in 15 families. Recipes explain the population and prerequisites and open measurement, reference, execution, planning or saved-comparison tools. Metadata is not proof of implementation; the subsequent review below records the actual scope of every definition.
- Native visual AST authoring for value/filter/cohort/pair/weight/award/lifecycle expressions, and native reference PMF rows.
- Bounded canonical graph enumeration with exact branch probabilities, unresolved mass, payout bounds, reachable maximum and measurement occurrence laws. Measurement numerical semantics remain explicitly IEEE754; this is implementation evidence, not an independent oracle.
- Rational partial absorption, absorption-duration variance and stationary occupancy/reward/cost references. Nontermination and multiple recurrent classes withhold unjustified scalar answers.
- Rare-event and mean-precision sample planning; sparse discrete CDF/Pearson null calibration through small-law enumeration or separately seeded Monte Carlo with its own uncertainty and resolution limits.
- Bounded deterministic witness coordinates and saved-prefix reconstruction in both sampling engines, with pinned graph/plan/seed and required Core artifact identity.
- Bounded paired categorical joint support/independence diagnostics, variance decomposition and difference inference. Group budget overflow retains complete overall evidence while explicitly withholding incomplete cohorts.
- Runtime binary SHA-256, numerical algorithm contracts and execution regime in evidence/exports. Cross-engine seeded replay and custom-metric saved-run comparison distinguish altered populations, dependent streams and descriptive distribution differences.
- Browser checkpoints retain identity, exact plan, scalar trends and bounded history; rich evidence is rehydrated from authoritative server snapshots. Charts avoid retaining a full distribution per point.

- Typed model-limit vs condition loop completion counters, transactional cancellation behavior and bounded loop-point coverage, in live/saved reports and exports. Both engines and worker counts agree on independent loop oracles.
- Server-calculated diagnostic artifacts (enumeration, discrete calibration, witnesses and independently authored finite/state references) retain input/output/binary hashes with encrypted run evidence. Deduplication and storage budgets preserve existing results; diagnostics are excluded from WS payloads.
- Shared cancellable calculation requests honor explicit 429 rejection and Retry-After; ambiguous accepted requests are never automatically repeated.

## Verification so far (local, 2026-10-10)

- Full Core suite: **818 passed** (`/tmp/advanced-core-full.log`).
- Full API suite: **84 passed** with the CI database (`/tmp/advanced-api-full.log`).
- Frontend build/lint and 37 unit tests passed. Zero-variance interval and recipe refinements passed the final unit/build/lint checks.
- Three new native browser workflows passed in isolation: episode/reference/witness/calibration/enumeration; rational absorbing/stationary reference and rare-event planning; visual event recipe/session/reload.
- The earlier full browser run exposed stale export/checkpoint expectations, an invalid quota-preflight route and diagnostic quota handling. These are fixed. **52/52 browser tests passed**, including retained artifacts and loop UI. Four targeted browser checks passed after the zero-variance display and recipe refinements.
- Independent Python reference and catalogue publication checks passed. OpenAPI/schema regeneration is complete; final remote CI remains required.
- Authenticated deployment: five UI-authored Dog House metrics, 100,000 rounds, correct paid-round/reveal/bonus accounting, 192 rich WS frames, identical worker-count and canonical reference-engine replay, 98% authored expectation and rendered desktop/mobile inspection. The optimized run took 3.848 seconds (25,988 rounds/second); the reference run took 25.279 seconds. Observed RTP was 99.6719%, distinct from the reference. Cohort-parent inference includes absent parents before/after cohort discovery and across worker chunks; the full Core/API suites passed its sparse manual oracle. Deployment and cross-engine production verification passed with the corrected implementation.
- That first deployment's populated dashboard remains `https://localhost:8543/simulate?run=86f71f4b5b4e43dc961f78f5436c33ce`. The current reproducible production report below replaces the earlier report file with evidence from the subsequent build.
- This implementation was committed and pushed as `111756a9003de3d70058848b3de173554ea7c8ab`. Existing `main` history is preserved. Remote CI `38001834290` passed backend/frontend checks but exposed browser recovery deadlines that ignored the shared quota and failed-test job ownership; its browser job failed, so Docker/smoke jobs were skipped.

## Subsequent refinements under verification

- Exact decimal session bankroll/wager ledger (`decimal-roundtrip-v1`). The three 0.1 wagers from a 0.3 bankroll reach ruin at round three, and small net losses remain measurable against a large bankroll. Report conversion is separate from accounting; no currency rounding is invented.
- Independent complete rational payout-law comparison: normalized mass is mandatory, equivalent fractions/order/zero-mass atoms do not create discrepancies, and exact TV/CDF differences expose equal-98%-mean models with different payout laws. Standalone and authenticated retained-run API/native workflows preserve input, result and binary identities.
- Canonical enumeration now carries bounded exact occurrence laws for cohorts and paired values, including covariance and variance of sums/differences. Incomplete mass or support withholds unsupported conditional guarantees; authored likelihood weights are distinguished from the graph's unweighted law.
- Exact zero/false assertions count failures before binary64 report conversion, retain a violation witness and invalidate erroring observations. Rule residuals are distinct from exposure reconciliation, and per-observation checks cannot cancel opposite failures through an aggregate.
- Arithmetic and Boolean/conditional operators require their declared runtime operand types in both engines. Unknown operators fail explicitly, while Boolean short-circuiting and unevaluated conditional branches remain valid.
- Band/tail/cohort contribution displays use all settled paid-parent external turnover, including parents without a matching child. A paired wager retains its separately declared conditional turnover scope. Coefficient of variation is withheld for nonpositive means.
- Reviewed implementation traceability for all 159 definitions is published in [metrics-coverage.json](metrics-coverage.json) and checked against the native catalogue in CI: **92 ready, 51 require an authored game contract, 16 partial**. Family fixtures establish the generic primitive, not every game's rules. Partial scopes remain explicit in the product.
- Browser tests own accepted jobs and reconcile terminal cancellation conflicts. Explicit quota rejections honor Retry-After; failed tests cannot leave long simulations occupying subsequent test workers. Production quotas are unchanged.
- Legacy metric drafts normalize only the optional assertion's semantic default before pinning; an actual assertion or stake change still fails plan equality. The authenticated production check reproduced this contract defect before the regression fix.
- OpenAPI regeneration from an explicitly requested backend now fails when that source is unavailable, preserves old output on fetch failure, and stores the successfully generated authoritative schema.

Current local evidence: **848 Core tests, 85 API integration tests, 39 frontend unit tests**, frontend build/lint and independent Python reference/catalogue checks passed. The full browser run passed **52/53**; the remaining test parsed an empty quota rejection during its authoritative replay read. Its read now follows explicit Retry-After, and the complete failed workflow passed a targeted rerun. All browser behavior assertions remain intact. Remote full-suite CI is still required for these refinements.

The updated authenticated production check passed with **100,000 rounds**, **113 primary-run rich WS frames** (388 across the checked primary/replay/reference workflows), identical worker-count and canonical-engine evidence, reconciled complete paid-parent/cohort turnover and a retained exact equal-mean/different-law comparison. Desktop/mobile rendering passed with no browser errors. The current report is [advanced-measurements-production.json](advanced-measurements-production.json); the populated dashboard is `https://localhost:8543/simulate?run=a4c3f12543654fc3b68b8b9d3bd563fa`. Loaded Core SHA-256: `01101ccf68166efa026f64b8fb0bd4f8f01ba112dfab94cda2f3dbfd09ac5db3`. Observed elapsed time was 11.224 seconds during concurrent full browser verification; this is deployment evidence, not an isolated performance comparison with the earlier run. The authored reference is 98%, and the sampled observation is 99.6719%.

## Required remaining work

1. Commit/push the subsequent verified refinements and confirm remote CI; repeat relevant checks for subsequent changes.
2. Close the explicitly recorded partial workflows in the definition-by-definition review, with independent scenario fixtures and correct game-author prerequisites.
3. Multi-component accounting; parameter-sweep/common-stream experiments and sensitivity evidence; proposal/stratified design and verified likelihood support where required by the researched catalogue.
4. Refine sequence/state-conditioned diagnostic workflows and any remaining session-policy prerequisites; extend reviewable diagnostic coverage beyond the retained reference/calibration/witness workflows.
5. Deploy/repopulate the authenticated production application with advanced authored metrics, inspect desktop/mobile, verify live/saved/replayed evidence and final remote CI.

An additional exact-expression probe reproduced truncation in existing built-ins: `abs(-1/2)`, `min(1/2,3/4)` and `max(1/2,3/4)` return zero, `floor(-1/2)` returns zero, and array `sum([1/2,1])` returns one. These are concrete mathematical defects beyond the completed operator-type checks. Correct rational built-in/aggregate semantics and independent constructor fixtures are the next priority; the catalogue must not imply that authoring any numeric expression already guarantees correct rules.

This work remains active. A complete catalogue claim requires evidence for every definition, not just a searchable entry or a generic expression field.
