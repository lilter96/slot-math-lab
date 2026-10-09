# Slot mathematics verification: metric requirements and collection design

Research date: 2026-10-09. Code reviewed: `217f8ca`. This document specifies the next measurement system; it does **not** claim that the missing capabilities below are implemented. The preceding implementation was committed and pushed before this investigation.

## Finding

Minimum, maximum and average are useful descriptive measurements. They cannot establish that a slot implements its mathematical model correctly. Even the complete marginal payout distribution cannot, by itself, establish correct feature state transitions or rule compliance. Verification requires an independent rules specification, independent reference calculations, accounting invariants, appropriate distributions and uncertainty, and executable cross-checks.

The catalogue in [slot-math-metrics.catalog.json](slot-math-metrics.catalog.json) contains **159 parameterized definitions across 15 families**, including advanced analysis. Each entry identifies its formula, units, priority and coverage in the reviewed code. Parameters such as threshold, percentile, symbol, reel, feature type, state and mode generate additional instances; there is no finite universal list of every possible game-specific metric. Required evidence depends on the mechanics actually present. The independent toy-model calculations are executable with `python3 scripts/verification/metrics_reference.py --check`; they validate the counterexamples, not implementation of the planned capabilities. [Research checks and source list](verification/metrics-research.json).

The recommended release profile is **Accounting + Return + Probabilities + Distribution + Features + Reference checks**. State/sequence/rare-event profiles become required when the corresponding mechanics exist. Session experience measurements are optional design analysis, not a substitute for correctness evidence.

## What the sources establish

The UK Gambling Commission defines operational RTP from wins and turnover, relates acceptable variation to volatility and volume, and discusses separate measurements by stake, channel, base game, bonuses and jackpots. This supports explicit populations and decomposition rather than one aggregate RTP card. It does not prescribe the full catalogue below. [UKGC live RTP guidance](https://www.gamblingcommission.gov.uk/licensees-and-businesses/guide/live-return-to-player-performance-monitoring-of-games-of-chance).

GLI-19 v3.0 describes testing final RNG outputs after mapping, distribution and independence checks, wagering configurations, bonus information and highest-award odds. This supports checking the generating process and rule-dependent events as well as payouts. It is a reference standard, not a claim that these local checks confer certification. [GLI-19, sections 3.2, 4.7 and 4.8](https://gaminglabs.com/wp-content/uploads/2024/06/GLI-19-Interactive-Gaming-Systems-v3.0.pdf).

Stake Engine's public Math SDK documentation describes analysis by game type, win range, event frequency and average win, using weighted published outcomes. This is a useful primary example of a mathematical report, not a requirement to integrate that platform. [Math utilities](https://stake-engine.com/docs/math/utilities), [quick start](https://stake-engine.com/docs/math/quick-start), [math file format](https://stake-engine.com/docs/math/math-file-format).

The metric definitions, counterexamples, priorities, UI and engineering design below are our proposed design, informed by those sources and the statistical references cited where methods matter. They are not a transcription of a regulator's checklist.

## Define what is being measured

Let paid round `r` include its initial paid action and all resulting free spins, respins, cascades and awards up to settlement. Let `B_r > 0` be its total external wager, `W_r` its final settled payout and `G_r = W_r - B_r` its net player result. For a fixed stake `b`, define `X_r = W_r / b`.

| Observation unit | Meaning | Example measurement |
|---|---|---|
| Paid round | One complete independent paid game, including its bonus | RTP, probability of a bonus, settled payout distribution |
| Feature episode | One explicitly identified bonus instance from entry to exit | Bonus total, duration, retriggers, zero-paying bonus |
| Reveal | One base spin, FS, respin or cascade evaluation | Payout of a Sticky FS, winning lines, scatter count |
| Award | One unique payable award | Symbol/line payout contribution, duplicate award checks |
| Node visit | One execution of a flattened constructor point | State value before the selected operation |
| Transition | A defined before/after state pair | Sticky occupancy growth, refill and loop counter changes |
| RNG draw | One mapped selection from a named distribution | Reel-stop frequency, symbol-selection probability |
| Session or regeneration cycle | An ordered sequence retaining required state | Drought lengths, persistent-state long-run return |

These units are not interchangeable. A loop body can be visited many times in a round. A round can contain multiple feature instances. A free spin has no new external wager, so dividing its payout by its own zero wager is invalid; label its yield in parent-stake multiples instead.

Production labels must also distinguish sample statistics, exact model values, approximate sketches, conservative bounds and authored targets. A constructed target of 98% is not an observation or a proof.

## Accounting and return

The basic counters are attempted, completed and interrupted rounds; eligible, valid, excluded and invalid observations; feature entries/exits; reveals; awards and visits. A feature probability counts rounds containing the feature, while a feature rate can count multiple entries per round. A collector needs both, not a single generic count.

For mixed stakes, observed RTP is `sum(W_r) / sum(B_r)`. It generally differs from `mean(W_r / B_r)`. Constant-stake RTP is `mean(X_r)`. Track both turnover and payout in currency or exact credit units; normalize each paid mode by its actual cost, including ante bets, buys and extra charges. Report each mode/stake/paytable configuration separately before any aggregate.

Required invariants include:

* Completed round count equals the settled-payout distribution count.
* Eligible observations equal valid matches plus exclusions plus invalid observations.
* Each expected paid wager is charged once, and each payable award is credited once.
* Opening balance plus funding minus wagers plus credited payouts minus withdrawals equals closing balance in a modeled wallet scenario.
* Feature entries and exits reconcile with explicitly open or interrupted episodes.
* Raw award sum, component totals, cap deduction and settled payout reconcile under a declared settlement order.
* Integer/rational credit calculation and displayed decimal rounding follow the same authored policy.

For a true additive payout component `A_{r,f}` in parent-stake multiples, its contribution is `mean_r(A_{r,f})`, including zero for rounds without that component. Its share of return is `E[A_f] / E[X]` when the denominator is positive. A conditional bonus average is a different quantity. Overlapping cohorts cannot be added as if they were disjoint payout components.

If a component is zero unless feature `F` occurs, then `E[A_F] = P(F) E[A_F | F]`. Therefore correct conditional feature payouts with an incorrect trigger rate still give incorrect RTP. When a round can contain several feature instances, event counts and conditional episode values need an explicit per-round reduction before making this comparison.

Cap allocation must be defined. Suppose raw components sum to 120 and the round cap is 100. Their raw totals cannot sum to a final 100 without an explicit cap-loss term or component allocation rule. A generic payout ledger should record unique awards, component tags, raw values, applied deductions and settled values. The collector must observe settlement, not invent game accounting.

## Counts, frequencies and event probability

A probability definition needs an event predicate, an eligible population, a unit, and treatment of invalid observations. With zero invalid observations, a once-per-round Boolean event estimates `k/N`. A node-visit matching share describes visits; it does not automatically estimate the probability that a paid round contains that event.

Track no payout (`X=0`), below-stake payout (`0<X<1`), break-even (`X=1`), profit (`X>1`), win-band membership, threshold exceedance, feature activation, feature-type selection, retrigger, termination reason and rule-relevant outcomes. Exact equality must use exact authored payout units, not a floating-point tolerance that silently changes the event.

Counts should include both events and distinct parent rounds/episodes. Conditional probabilities should label their eligibility, for example `P(retrigger | eligible FS in Sticky mode)` and `P(at least one retrigger | Sticky episode)`. These are different metrics with different dependence.

Use a suitable binomial interval for independent Bernoulli subjects. Normal approximations become unreliable near zero/one or with scarce events; exact binomial limits are an available alternative. Invalid data must not be silently converted into Bernoulli failures. [NIST exact binomial confidence limits](https://itl.nist.gov/div898/software/dataplot/refman2/auxillar/exacbici.htm).

If an event is never observed in `N` independent fixed-count trials, its one-sided 95% upper bound is `1 - 0.05^(1/N)`, not zero. At `N=100,000` that is approximately `0.002996%`. This formula is not a guarantee for adaptively stopped or correlated observations.

## Distribution, moments and tails

Two different models can both have exactly 98% RTP:

| Model | Payout law in stake multiples | Hit probability | Variance | P(payout >= 5x) |
|---|---|---:|---:|---:|
| A | 0 with probability 1/2; 1.96 with probability 1/2 | 50% | 0.9604 | 0% |
| B | 0 with probability 9/10; 9.8 with probability 1/10 | 10% | 8.6436 | 10% |

An RTP-only comparison cannot distinguish them. A complete small-model reference must compare exact payout probability masses, not only the mean or agreement between two implementations sharing an oracle.

For each relevant numeric population, provide count, sum, min/max, mean, second moment, variance, standard deviation and explicitly named coefficient of variation. Sample variance uses a different denominator from population variance. When used, skewness and kurtosis must declare whether they are moment or bias-adjusted estimators and whether kurtosis is excess or raw. Undefined cases stay undefined. [NIST skewness and kurtosis](https://www.itl.nist.gov/div898/handbook/eda/section3/eda35b.htm).

Distributions require configurable exact-value frequencies where feasible, fixed semantic payout bands, within-band payout sums and moments, empirical CDF, survival curve and configurable quantiles. For instance, store both `count(100<=X<500)` and `sum(X for 100<=X<500)`; counts alone cannot reconstruct the band's contribution to RTP.

Define quantiles by `Q(q) = inf{x : F(x) >= q}` for a discrete payout law. Report zero mass separately; a median of zero is often meaningful. A coarse histogram yields a quantile range, not an exact quantile. Bounded-memory quantile sketches add algorithmic rank error, which is separate from sampling uncertainty and is not generally a monetary-value error guarantee. [Karnin, Lang and Liberty, streaming quantile approximation](https://arxiv.org/abs/1603.05346).

For threshold `t`, useful tail metrics are `P(X>=t)`, `E[X 1{X>=t}]`, conditional tail mean, tail share of RTP and tail contribution to the second moment. The last measurement identifies rare payouts dominating variance. For an upper-tail mean at quantile `q`, allocate fractional mass at tied boundary payouts so the selected tail has exactly probability `1-q`; this is not always the same as conditioning on `X>=Q(q)`.

Maximum observed, maximum reachable payout under the rules, declared WinCap, probability of reaching it, probability of exceeding it before settlement, and mean clipped amount are separate metrics. The same separation applies to operational loop limits and authored game termination rules.

## Feature lifecycle and mechanic-specific evidence

For every feature type, collect activation frequency per paid round, entry count, initial awards, added awards, actual reveals played, removed/unplayed awards, duration, total payout, payout per episode, payout per reveal, conditional win probabilities, retrigger count and terminal reason. Configure both entry-time cohorts and dynamic reveal-time filters. A bonus type chosen at entry should not silently change cohort because later state changes.

For free spins, use the conservation equation `initial awarded + additional awarded - played - explicitly removed = remaining`. At ordinary completion, remaining should be zero. A payout-cap termination can leave positive remaining only if the game specification permits cancellation of future spins and the removed balance is recorded. Retrigger opportunity counts must follow the authored eligibility rules.

For other mechanics, the same collection system needs typed events and before/after state observations:

| Mechanic | Diagnostic populations and invariants |
|---|---|
| Lines | Symbol and match-length frequencies by line, leading/all Wild behavior, substituted symbol, payable-line count, line awards and overlap rules |
| Ways | Match lengths, ways multiplicity, combination values, Wild treatment and product-of-counts arithmetic |
| Scatter | Scatter-count distribution, award per count, trigger threshold and reel/location restrictions |
| Cluster | Component sizes, payable clusters, adjacency, Wild membership/ownership policy and award deduplication |
| Sticky Wild | New/retained/cleared positions, occupancy by FS index, multiplier evolution, entry/reset conditions and preservation within the correct episode |
| Cascade | Step count, payout at each depth, removal set, survivors, refill selections, multiplier state and stopping conditions |
| Hold & Win | New collection events, values, repeated visits to held cells, respin resets, terminal occupancy and exactly-once settlement |
| Multiplier/pick feature | Outcome probabilities, conditional awards, multiplication order, selection policy and any allowed strategy |
| Progressive/persistent feature | Contribution accounting, jackpot triggers/resets, state transition probabilities, initial-state conditions and separate long-run analysis |

A symbol appearing in a draw, appearing in the visible grid, matching a line and receiving an award are four different events. Weighted reel stops can make visual symbol frequencies unequal by design. Compare against the declared generating process rather than assuming uniform symbols.

## Complex metrics require a pipeline

The user-facing metric is more than `expression + filter + average`. It needs a subject, within-subject reduction, population reducer, denominator and inference method.

Consider two Sticky episodes: one contains one FS paying 10; another contains nine FS paying zero. The pooled per-FS mean is 1, the mean episode total is 5, and the average of the two episode-level per-FS averages is also 5. All are valid questions. They must have different labels and independent collection definitions.

| Requested question | Subject and inner reduction | Population result |
|---|---|---|
| Min/max/average payout of Sticky FS | One matching FS reveal; value = its payout | Event-level min/max/mean, count and distribution |
| Min/max/average Sticky bonus payout | One Sticky episode; sum its payable awards | Episode-level min/max/mean and distribution |
| Contribution of Sticky FS to RTP | One paid round; sum allocated Sticky payouts, zero if absent | Mean across all paid rounds or payout/turnover ratio |
| Probability of any Sticky retrigger | One Sticky episode; Boolean `any(retrigger)` | Proportion of eligible episodes, episode-level interval |
| Average FS count for large bonuses | One episode; count FS, then filter episode total | Conditional episode mean; requires end-of-episode scope |
| Sticky occupancy versus FS index | One FS; count sticky positions, group by FS ordinal | Per-index distribution/mean with episode-clustered uncertainty |
| Payout covariance between base and FS | One paid round; paired base and FS totals | Covariance/correlation and variance decomposition |
| Longest bonus drought | Ordered paid-round trigger sequence | Gap/run-length distribution, boundary censoring |

An episode or round collector therefore needs inner reducers `sum`, `count`, `any`, `all`, `first`, `last`, `min`, `max` and declared deltas. Non-additive observations must not be repeatedly counted as new winnings. Episode identity and lifecycle boundaries must be explicit and versioned with the graph.

## State, dependence and termination

Positive correlation within a sticky bonus is expected behavior, not automatically an RNG error. Statistical uncertainty must use the independent parent subject or a justified dependent-process method. Preserve ordered logical round indexes for sequential measurements; parallel workers completing in a different order must not change the measured sequence.

Track component covariance. For additive components, `Var(sum A_f) = sum Var(A_f) + 2 sum_{f<g} Cov(A_f,A_g)`. Treating base and bonus variance as additive without covariance can give incorrect tolerances. Cross-feature covariance requires paired values from the same paid round, including zeros for absent components.

Autocorrelation, transition counts, runs and gap distributions can reveal dependence and mapping problems. An insignificant autocorrelation is not proof of randomness, and `s/sqrt(N)` is not automatically valid for dependent data. [NIST autocorrelation guidance](https://www.itl.nist.gov/div898/handbook/eda/section3/autocopl.htm).

Persistent state across paid rounds needs its own simulation regime. Resetting to the initial state every round evaluates a reset model, not long-run persistent gameplay. Record state occupancy, transitions, initial-state policy, warm-up/regeneration policy, exposure and long-run payout/turnover. Appropriate recurrent-state assumptions matter. [University of Washington stochastic modeling notes](https://faculty.washington.edu/yenchic/26A_stat516/Lec4_DTMC_p2.html).

For a finite transient-state feature with substochastic matrix `Q`, the fundamental matrix `(I-Q)^-1`, when valid, gives expected transient visits; multiplying by a vector of ones gives expected duration. A state reward vector gives expected reward by the same visit accounting. Validate reachability and termination before applying this calculation. [University of Washington notes on fundamental matrices](https://faculty.washington.edu/yenchic/26A_stat516/Lec4_DTMC_p2.html#fundamental-matrices).

For the special branching model where each FS independently produces an average `m<1` additional FS with unchanged offspring rules, expected total FS from `L0` initial spins is `L0/(1-m)`. This shortcut is not a general formula for stateful, capped or mode-dependent retriggers. Nor can `E[number of spins] * E[payout per spin]` be used without establishing the required stopping/independence conditions.

Measure authored termination, payout-cap termination, loop-budget termination, cancelled work and non-termination risk separately. A resource limit should not silently become an undocumented game rule.

## Reference comparisons, uncertainty and rare events

The comparison set should include exact rational outcome masses on small models, selected event probabilities, component return, moments, threshold tails, feature duration/retrigger laws and state transitions. Include negative controls that deliberately produce the same RTP with a different distribution, incorrect trigger frequency, duplicate awards and broken state resets.

For observed discrete payouts versus a known distribution, use a suitable multinomial/exact or calibrated test. Pearson chi-square depends on binning and sufficient expected counts; sparse bins need a justified procedure. Standard continuous-distribution Kolmogorov–Smirnov critical values must not be applied unmodified to tied discrete slot payouts. A CDF distance remains a useful effect-size measurement independently of its test calibration. [NIST chi-square guidance](https://www.itl.nist.gov/div898/handbook/eda/section3/eda35f.htm), [NIST K–S restrictions](https://www.itl.nist.gov/div898/handbook/prc/section2/prc212.htm).

A reference falling inside an observed confidence interval indicates compatibility at that resolution. It does not establish agreement within a requested tolerance. To show adequate precision relative to tolerance `delta`, require the appropriate uncertainty interval to fit within the allowed reference band, with the estimator's assumptions satisfied. Exact rule invariants have no statistical tolerance.

For an independent fixed-count mean, the normal planning approximation is `N ~= (z*sigma/delta)^2`. This is a planning estimate, not a guaranteed sample count. The reported volatility and tail coverage determine whether the approximation is informative. A fixed number such as 100,000 or one million rounds cannot serve as a universal confirmation criterion.

Testing many metrics creates multiple opportunities for a false alarm. Define critical metric families and an error budget before the run. Bonferroni allocation is one conservative available approach; correlated or hierarchical tests need a documented treatment. [NIST simultaneous comparisons](https://www.itl.nist.gov/div898/handbook/prc/section4/prc463.htm).

Repeatedly watching a fixed-count 95% CI and stopping when it turns green changes the inference procedure. Live descriptive charts can keep pointwise intervals if clearly labeled. Automatic acceptance, stopping or repeated rejection needs a validated sequential design, such as an appropriate confidence sequence. [Howard et al., time-uniform confidence sequences](https://arxiv.org/abs/1810.08240).

At event probability `1e-7`, approximately 30 million independent trials are needed for a 95% chance of observing even one occurrence. That is not sufficient to estimate its probability precisely. Enumerate tractable rare-event submodels or use analytically justified stratification/importance sampling. Forced rare outcomes must carry correct likelihood weights; naïvely averaging them estimates the modified generator. Track weight coverage and instability diagnostics as well as the estimate. [Chan, Glynn and Kroese, importance sampling](https://web.stanford.edu/~glynn/papers/2011/ChanGKroese11.pdf).

For exact pruning with unknown probability mass `p` and a proven payout bound `0<=X<=C`, the full mean is contained in `[known first moment, known first moment + p*C]`. The second moment is contained in `[known second moment, known second moment + p*C^2]`. Derive variance bounds from compatible moment constraints; do not report a truncated variance as a full exact value. Known event probability has upper bound `min(1, known probability+p)`. Do not renormalize surviving mass without labeling it a conditional retained distribution. An unbounded or invalidly capped model cannot use this finite bound.

## Reproducibility and evidence

Seed replay proves reproducibility, not correctness. Preserve graph and expanded mechanics, tables, state initialization, expression semantics, measurement plan, currency/normalization, caps/loop policies, RNG and stream scheme, software version and calculation budgets. Graph hashes and seed alone omit some of these conditions.

Compare exact versus sampled engines on the same authored model, scalar versus optimized execution, worker counts with fixed logical streams, save/load serialization, and interrupted/recovered evidence. Same-seed paired comparisons can be informative only when the random coupling is defined; a changed graph can consume draws differently.

For any failed assertion, retain a bounded witness with subject ID, state/event context, graph point and replay coordinates. Live snapshots and terminal evidence should include estimator method, subject count, invalid count, denominator, provenance, reference and precision, not just a displayed number.

## Coverage of the current application

These are source-code findings at `217f8ca`, separate from the internet-derived requirements.

| Capability | Current coverage | Gap |
|---|---|---|
| Completed counts, RTP, hit frequency, mean uncertainty, observed maximum and payout SD | Live API/Simulate/Results | Fixed parent-stake semantics; no general wager ledger or multi-mode measurement matrix |
| Scoped count, sum, min/max, mean, SD and matching share | Configurable numeric expression and filter at round/node | No typed event-only measure, subject reduction, automatic grouping or lifecycle identity |
| Payout histogram/CDF and P50/P90/P99 | Saved settled payout bins and quantile bounds in Results | No custom-value histograms, configurable quantiles, within-bin payout sums or sketch provenance |
| Zero-event tail bound | Results has one fixed 100x threshold analysis | Not a generic threshold/probability interval engine |
| Exact return and per-feature reducers | Mathematical core has report types/reducers; Dog House has a separate mean proof | These do not imply generic live feature collection or full-distribution proof |
| Feature statistics through authored state | Manual point/value/filter definitions can observe suitable fields | No generic feature ledger, zero-filled per-round components, covariance or component reconciliation |
| Cap metrics | UI accurately calls the existing counter sampler clippings | `SampledMetrics.ComputeMaxWin` assigns that strictly-above-cap clipping count to `PCapReached`; equality at cap is not counted there |
| State/sequence and persistent models | Constructor can encode within-round state; sampling resets each paid round | No generic transition/gap collector or persistent paid-round regime |
| Exact pruning | Bounded RTP interval implemented | General custom metrics, event bounds, variance bounds and references need their own guarantee contracts |
| Saved plan, evidence, WS recovery and cross-worker replay | Implemented and tested for current reducers | New accumulator types must extend all of these paths |
| General equivalence/distribution/feature acceptance | Results offers uncertainty and comparison diagnostics | No executable typed assertion suite covering the full catalogue |

Relevant implementation paths: [measurements](../backend/SlotMath.Core/Measurements/Measurements.cs), [sampled reducers](../backend/SlotMath.Core/Math/SampledMetrics.cs), [streaming statistics](../backend/SlotMath.Core/Math/StreamingStats.cs), [exact reducers](../backend/SlotMath.Core/Math/ExactMetrics.cs), [run service](../backend/SlotMath.Api/Features/Runs/RunJobService.cs), [Results distribution](../frontend/src/components/results/Distribution.tsx).

The existing 100,000-round Dog House demonstration has 11,275 FS visits across 630 bonuses. Its pooled FS average is about 3.692 parent stakes. The captured FS sum divided by all paid rounds is about 0.416271, a different quantity. Its "Bonus round payout" metric includes the triggering base payout, so that cohort total is not a standalone FS component. A pre-settlement FS sum also needs cap allocation before it is generally an additive settled RTP component. These distinctions should be visible in the product.

## Product design

**Build defines game semantics and measurement boundaries.** Feature entry/exit, reveal type, unique awards, state transitions, parent stake and persistence belong to the authored model. Inspector controls should bind observations to those semantics; users should not have to guess a private compiler path to identify an FS boundary. Keep advanced expressions and explicit graph points available.

**Simulate defines collection and the live workspace.** Add metric templates for Counts, Event probability, Numeric distribution, Feature summary, RTP component, Paired values, State transition, Sequence and Invariant. A guided editor specifies subject, scope, value/event, grouping, inner reduction, denominator and population statistics. It previews a plain-language definition: "For each completed Sticky bonus, sum its settled awards; show min/max/average across bonuses."

**Results evaluates saved evidence.** Keep estimates, references, precision and assertions together. Required status states are exact agreement, within declared tolerance with sufficient precision, discrepancy, insufficient evidence, invalid collection, and unsupported analysis. Each status needs a concrete reason and source population. One global green badge must not hide an unresolved critical metric.

Display configuration remains separate: cards, tables, distributions, threshold curves, contribution breakdown, covariance matrix, state transitions or sequence plots. Hide a chart without changing what is collected. Collection edits apply to the next run; attaching a new metric to an ongoing run must never imply earlier events were observed.

The editor should expose available typed scope fields and feature types, a visual filter builder, units and named denominators. No-match examples, invalid-field locations, overlap warnings, grouping cardinality and method limits should appear while configuring the metric. Unavailable reference data should remain unknown rather than becoming a default zero.

## Engineering design

Use a versioned typed measurement plan with independent layers: observation binding, subject/lifecycle binding, filter/grouping, within-subject reduction, population accumulator, reference/assertion specification and presentation. Compile observation expressions and reducers once. Native counters should not require a dummy numeric expression.

Use exact monetary ticks/rationals for accounting and discrete event equality; stable mergeable floating accumulators for sampled moments. Preserve `n`, central moment state and invalid counts rather than deriving variance from rounded display values. Covariance is collected from explicitly requested paired subject values, not from aggregate means.

For small bounded payout supports, exact frequency maps are preferable. For larger supports, choose an explicit fixed histogram or a validated bounded sketch and expose its approximation contract. Histogram bin edges are pinned; groups have declared domains/cardinality limits. Never silently drop overflow groups or clamp arbitrary custom values. Keep invalid or unsupported observations explicit.

Resource cost is per plan: selected points, expression work, subject buffers, groups, bins, sketch size, requested covariance pairs, maximum lag and witness budget. Do not allocate every statistic for every field. Sampled workers own private accumulators; deterministic logical chunk merges preserve final results. Ordered summaries must carry boundary state for runs/gaps so chunk boundaries do not invent droughts or streaks. Persistent sessions are independent simulation subjects, not parallelized paid rounds with a shared mutable state.

Snapshots should be cumulative, versioned and coalesced. WS delivery, HTTP reconciliation, protocol validation, durable checkpoints, terminal serialization, CSV/HTML/JSON export and pinned replay all need each new accumulator's sufficient state and metadata. Charts may be downsampled; underlying counts and final aggregates must not be. Sketch randomness must use its own pinned stream, never consume the game's RNG.

The API should reject unsupported combinations before queueing: for example, independent-event CI on correlated FS visits, episode reduction without boundaries, quantile exactness from coarse bins, summing overlapping components, persistent-model independent-round execution, or a finite pruning bound with no proven payout limit.

## Implementation order and acceptance

| Phase | Deliverable | Independent acceptance evidence |
|---|---|---|
| P0 | Subject/denominator contracts; typed events; exact accounting; separate cap reach/exceed/deduction; per-round feature components | Small integer/rational games, multiple bonuses per round, equal-to-cap case, invalid observations and feature/cap reconciliation |
| P0 | Event probability/counts, explicit cohorts, episode reductions and generic binomial limits | Known Bernoulli laws, zero/all successes, FS conservation and two-episode weighting example |
| P0 | Configurable distributions, per-bin return/moments and small-support exact comparison | Same-RTP/different-distribution negative control; hist counts and sums reconcile; no fabricated quantiles |
| P1 | Covariance, retriggers, state transitions, ordered gaps and authored truncation diagnostics | Correlated component reference, simple branching calculation, small Markov reference, partition-invariant chunk merging |
| P1 | Typed assertions, precision planning, critical-family error budget and repeat-look policy | Deliberate faults, insufficient rare events, same-seed versus independent comparison, deterministic gate behavior |
| P2 when needed | Persistent-state simulation, rare-event methods, sketches and sensitivity/session analysis | Independent finite-state solver; weighted rare-event reference; rank error checks; session path oracle |

Completion requires the visual constructor to author required semantics, Simulate to configure and collect the metric, Results to retain and verify it, evidence exports to reproduce it, and tests to cross the entire pipeline. For every critical rule family, provide a manually specified small model and a reference that does not share the production algorithm. The catalogue tracks requirements; it is not an implementation-completeness score or a certification claim.
