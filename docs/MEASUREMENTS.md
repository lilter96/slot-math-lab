# Configurable simulation measurements

Simulate is a measurement workspace. Game rules and state remain authored in Build. The collection plan selects a numeric constructor expression, a Boolean filter and an execution point. It is independent of the dashboard's presentation settings.

## Configure and display

Choose **Track metric** in Simulate. Give it a name, select its observation point, choose the value and filter, label its units, then select the statistics and optional chart. Numeric and scope field selectors are derived from the compiled, flattened graph, including nested catalog and custom mechanics. Search nodes by name or path. Schema discovery does not navigate away from an open constructor subgraph.

* **Completed paid round**: one eligible observation after a whole round finishes, including its bonus. The built-in settled payout source uses the sampler's actual scale and cap. An authored state expression uses its own units and may describe an uncapped amount.
* **Each visit to a graph node**: reads state immediately before that node executes. Choose the node following the operation that writes the value. Loop-body nodes can yield multiple observations in one paid round; loop entry is observed once each time that loop is entered. Paths distinguish separate instances of the same mechanic.

For example, if the graph writes `fsType` and `spinWin`, select a point after the FS payout is written, use `state.spinWin`, and filter `state.fsType == "sticky"`. Select only Minimum, Maximum and Average. If payouts are in coins, normalize by the graph's stake expression or label the unit `coins`; display units do not convert values. A graph without a feature-type field needs that field authored in Build, not guessed by the dashboard.

The provided Dog House model has one Sticky Wild FS mode. Its per-FS payout can be measured before `free-spin/snapshot-winHistory` using `state.spinCoins / 20` in stake multiples. A filter such as `state.fsCount >= 18` limits it to bonuses awarding at least 18 spins; this is a filter on awarded length, not a different FS type.

State expression mode supports arithmetic, comparisons, Boolean combinations, conditionals and indexed fields. Advanced AST mode accepts the constructor's typed expression grammar, including bounded folds and explicit conversion functions. The backend validates numeric values, Boolean filters, known fields, stable flattened node IDs, unique measurement IDs and expression cost before collection. Runtime state can still be absent at a selected point: those observations are reported as invalid.

**Customize dashboard** selects built-in cards and charts. Each tracked card's **Display** control selects statistics and its average/range chart without changing collection or restarting the job. Collection definitions can be edited while running, but the changes apply only to the next run; the active and saved run keep the original plan. Display settings and the next-run plan survive browser reload. **Use this run's collection plan** restores saved definitions to the next-run draft.

## Denominators and errors

Each metric carries eligible observations, matching valid count, exclusions and errors. These obey `eligible = matching + excluded + errors`. Filters run before numeric values. Minimum, maximum, average and sum are null when there are no matching valid observations; standard deviation additionally requires two matches. A real numeric zero remains zero.

Average is `sum / matching valid count`. Matching share is `matching / eligible observations`, including invalid observations in the denominator. A node metric's denominator is visits, not paid rounds. Filters referencing missing state count as invalid, not excluded; the first error is retained. Average trend uses its own scale so a rare large payout does not flatten the mean line. Observed range is a separate chart mode and represents observed extremes, not an interval bound or theoretical maximum. Events from the same round may be correlated, so event standard deviation does not imply an independent-event confidence interval.

## Execution and durability

Plans are opt-in, capped at 32 definitions and 1,000 static expression operations per value/filter. Expressions are pure and cannot consume RNG or mutate game state. Compiled sampling uses precompiled expression delegates and indexed state; the reference interpreter observes transparent annotation points with the same semantics. Runs without extra measurements retain the existing compiled path.

Each worker owns fixed-size accumulators. Observations are staged per round and committed only when that round finishes. Cancellation discards all visits from an unfinished round. Full and partial terminal metrics use ascending logical-chunk reduction, preserving deterministic replay across worker counts for the same completed stream chunks. A cancellation changes the completed subset; independently cancelled runs need not match.

Live snapshots are cumulative and carry the plan SHA-256, stable IDs and complete statistics. The existing bounded SignalR delivery, HTTP recovery, checkpoints and terminal persistence include these fields. Clients reject malformed observations and mismatched plans. Crash recovery retains only the durable prefix. History is bounded by the metric count, avoiding unbounded event storage or localStorage growth.

Results shows saved scoped measurements and their original value/filter AST. Pinned replay carries the same collection plan and checks its fingerprint. JSON evidence contains the plan, statistics and pinned graph; CSV and HTML reports include the descriptive measurements.

## API

* `POST /api/runs/measurements/schema` accepts `{ config, measurements? }`, returns flattened points and derived state fields, and optionally validates a proposed plan without starting a run.
* `POST /api/runs` accepts `measurements: [{ id, name, nodeId?, value?, filter?, unit, options? }]`. Null `nodeId` means completed round; null `value` means settled payout for the legacy plan, or a declared advanced source such as event count. Expressions are structured ASTs, not text.
* Run snapshots, WebSocket progress and `/api/runs/{id}/evidence` carry `measurements` and `measurementHash`. Progress entries contain `id`, `observations`, `count`, `excluded`, `errors`, nullable `min/max/mean/sum/stdDev`, and `firstError`.

Optional advanced plans add explicit observation/round/episode/transition populations, entry-selected cohorts, paired values, bounded distributions, quantile enclosures, tail summaries, calibrated uncertainty and independent reference checks. Users select storage and inference assumptions before a run. These statistics are sufficient summaries; custom instrumentation runs through the validated AST. The [implementation ledger](verification/METRICS_IMPLEMENTATION.md) records current scope and evidence for the 159-definition catalogue.

## Feature lifecycle exposure

Global entry/exit/open counts describe all authored lifecycle points before entry filtering. Cohort counts belong to the selected entry-time key. A later group change, excluded exit or absence of included numeric children cannot move or hide that instance. An empty cohort retains its lifecycle counts and complete paid-parent normalization, while its mean, sum, conditional tail probabilities and per-subject second moments remain undefined. Counted tail amounts can still contribute zero to known paid turnover.

For settled paid rounds, reconcile `entries = exits + unclosed`. An unmatched exit or exhausted nesting/group budget remains an explicit error or incomplete cohort report. Entry-excluded features contribute only to global lifecycle exposure. Cancellation discards the unfinished paid round; these counts do not claim to classify its interruption cause. The shared Simulate/Results accounting view explains these scopes and shows the reconciliation residual.

## Distinct matching parents

`analysis.parentExposure` distinguishes `paidRoundsWithMatchingChildren` from nullable `episodesWithMatchingChildren`. Each accepted child counts its paid round once and, for an episode population, its innermost owning episode once. Repeated visits do not multiply either parent; nested ancestors do not inherit an inner episode's children. A zero or false value is an accepted child unless filtered out. Counts are recorded before exit filtering and subject reduction, so an excluded exit or an empty sum's numeric identity cannot change exposure. Open instances in a completed paid round remain visible; unfinished paid rounds are discarded.

Saving a metric validates the complete proposed next-run plan, including the individual support and shared 32,768-cell collection limits. Quota rejection can be retried explicitly; closing validation aborts the request and prevents a late save. Choose cohort/support budgets for the intended populations.

The live statistic picker exposes both counters. Episode exposure requires declared episode boundaries and remains unavailable in legacy saved evidence. The old `distinctParents` field is retained for compatibility; current views use the explicit exposure contract. Rational graph enumeration publishes known matching parents per paid round, marks incomplete traversal, and never renormalizes a cut population.

The Dog House template has a bonus-only `bonus-completed` point after its FS loop. Choose `free-spins` as entry, that completion point as exit, and `free-spin/snapshot-winHistory` as the child point to track a complete bonus. This authored Boolean marker changes no payouts or random draws; earlier standard graphs still pass their own expectation-proof structure check.

## Independent verification

The [verification record](verification/measurements-checks.json) contains the executed check totals and populated preview links. All 750 Core, 75 API, 27 frontend unit and 49 browser checks passed, along with the frontend build and lint. The [production report](verification/measurements-production.json) records a real authenticated 100,000-round run, scoped WebSocket observations, unchanged game statistics, and bit-identical measurement replay with two workers versus one. These are locally executed checks, not a claim that remote CI has run.

The core oracle defines three FS iterations: sticky payouts 2 and 4, other payout 3. Five paid rounds must produce 15 eligible visits, 10 matches, 5 exclusions, minimum 2, maximum 4, sum 30, average 3 and sample variance 10/9. Both sampling paths are checked against this manual specification. Additional checks cover worker-count replay, unchanged game payouts, interrupted rounds, empty scopes, missing state, invalid plans, capped round payouts, API snapshots/evidence and real-browser configuration/export/replay.

Run launches honor `Retry-After` once for an explicitly rejected HTTP 429, with a visible wait message and Cancel launch action. Accepted requests, network failures and ambiguous timeouts are not retried automatically. Existing observations remain available if a new launch fails. Saved Simulate permalinks (`/simulate?run=…`) restore the pinned snapshot and plan in an empty browser without displacing another active run. A new run uses the current constructor draft; pinned replay is available in Results.
