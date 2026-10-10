# Configurable simulation measurements

Simulate is a measurement workspace. Game rules and state remain authored in Build. The collection plan selects a numeric constructor expression, a Boolean filter and an execution point. It is independent of the dashboard's presentation settings.

## Numeric input and display precision

Initial-state JSON numbers support decimal and scientific literals, including numbers inside arrays and records. The compiler preserves their exact authored rational value: `1e-9` is `1/1000000000`, and `0.1` is `1/10`. Compiled templates parse once and supply private mutable collections to each round. State literals have a 4096-character / ±4096 decimal-power budget; exceeding it rejects compilation. Every API launch compiles its graph before creating a queued run, including launches without tracked metrics.

Direct CLR `double` and `float` inputs retain their finite binary value exactly; `decimal` inputs retain their exact decimal value. Binary floating-point `0.1` and authored JSON `0.1` therefore have different exact fractions. Nonfinite floating inputs produce `EVAL_NONFINITE_NUMBER`. Expression residual assertions run before reporting conversion. The collector and measurement enumeration retain their documented binary64 observation contract; enumeration's conditional metric mean describes those reported values, rather than claiming to preserve the source decimal fraction after conversion.

State decimal tokens retain numeric type and exact literal content in canonical config hashes, including nested mechanics. Strings with the same spelling have a different hash and type. Typed mathematical fields continue to require their declared integer/rational representation. Archived integer-only config hashes remain unchanged.

Nonzero values below display precision use scientific notation in metric cards, analysis, trend axes and Results. Constant tiny trends use a range relative to their magnitude. Presentation does not round stored evidence or alter the payout scale: payouts still require the precision declared by the game's paytable, and extra fractional units produce `EVAL_PAYOUT_PRECISION`.

## Configure and display

Choose **Track metric** in Simulate. Give it a name, select its observation point, choose the value and filter, label its units, then select the statistics and optional chart. Numeric and scope field selectors are derived from the compiled, flattened graph, including nested catalog and custom mechanics. Search nodes by name or path. Schema discovery does not navigate away from an open constructor subgraph.

* **Completed paid round**: one eligible observation after a whole round finishes, including its bonus. The built-in settled payout source uses the sampler's actual scale and cap. An authored state expression uses its own units and may describe an uncapped amount.
* **Each visit to a graph node**: reads state immediately before that node executes. Choose the node following the operation that writes the value. Loop-body nodes can yield multiple observations in one paid round; loop entry is observed once each time that loop is entered. Paths distinguish separate instances of the same mechanic.

For example, if the graph writes `fsType` and `spinWin`, select a point after the FS payout is written, use `state.spinWin`, and filter `state.fsType == "sticky"`. Select only Minimum, Maximum and Average. If payouts are in coins, normalize by the graph's stake expression or label the unit `coins`; display units do not convert values. A graph without a feature-type field needs that field authored in Build, not guessed by the dashboard.

The provided Dog House model has one Sticky Wild FS mode. Its per-FS payout can be measured before `free-spin/snapshot-winHistory` using `state.spinCoins / 20` in stake multiples. A filter such as `state.fsCount >= 18` limits it to bonuses awarding at least 18 spins; this is a filter on awarded length, not a different FS type.

State expression mode supports arithmetic, comparisons, Boolean combinations, conditionals, indexed fields and the closed function set `abs`, `min`, `max`, `floor`, `ceil`, `round`, `length`, `contains`, `index`, `append`, `tonumber`, `tostring`. Advanced AST mode accepts the constructor's typed expression grammar, including bounded folds. The backend validates numeric values, Boolean filters, known fields, stable flattened node IDs, unique measurement IDs and expression cost before collection. Runtime state can still be absent at a selected point: those observations are reported as invalid.

Collection calls enforce their declared arity and types. `length` accepts arrays or text; `contains` checks exact typed array membership or an ordinal text substring. `append` creates a new array. `index` requires a whole-number position and returns the derived homogeneous element type, including Number and Boolean; mixed/unknown elements withhold an invented scalar type. Empty arrays are neutral when deriving homogeneous append/fold results, while conflicting writers remain unknown. Fractional indexes produce an error rather than truncating to a different cell.

Explicit `tonumber` accepts invariant integer, decimal, exponent or rational text with a nonzero denominator. Its input and exponent remain within the existing 4096-character / ±4096 decimal-power budget. Invalid numeric text is an invalid observation, never an implicit zero. `tostring` preserves numeric values as an integer or reduced `numerator/denominator`, so exact fractional round trips work in both engines. The separately documented implicit symbol-scoring aggregate convention still maps nonnumeric symbols to zero; it is not the explicit text-conversion contract.

**Customize dashboard** selects built-in cards and charts. Each tracked card's **Display** control selects statistics and its average/range chart without changing collection or restarting the job. Collection definitions can be edited while running, but the changes apply only to the next run; the active and saved run keep the original plan. Display settings and the next-run plan survive browser reload. **Use this run's collection plan** restores saved definitions to the next-run draft.

## Denominators and errors

Each metric carries eligible observations, matching valid count, exclusions and errors. These obey `eligible = matching + excluded + errors`. Filters run before numeric values. Minimum, maximum, average and sum are null when there are no matching valid observations; standard deviation additionally requires two matches. A real numeric zero remains zero.

Average is `sum / matching valid count`. Matching share is `matching / eligible observations`, including invalid observations in the denominator. A node metric's denominator is visits, not paid rounds. Filters referencing missing state count as invalid, not excluded; the first error is retained. Average trend uses its own scale so a rare large payout does not flatten the mean line. Observed range is a separate chart mode and represents observed extremes, not an interval bound or theoretical maximum. Events from the same round may be correlated, so event standard deviation does not imply an independent-event confidence interval.

## Execution and durability

Plans are opt-in, capped at 32 definitions and 1,000 static expression operations per value/filter. Expressions are pure and cannot consume RNG or mutate game state. Compiled sampling uses precompiled expression delegates and indexed state; the reference interpreter observes transparent annotation points with the same semantics. Runs without extra measurements retain the existing compiled path.

Each worker owns bounded accumulators. Numeric observations are staged per round and committed only when that round finishes. Cancellation or runtime failure discards all numeric visits, even a closed feature's value, from an unfinished paid round. Full and partial terminal metrics use ascending logical-chunk reduction, preserving deterministic replay across worker counts for the same completed stream chunks. A cancellation changes the completed subset; independently cancelled runs need not match.

Feature and transition plans also retain **separate interrupted lifecycle evidence**: cancelled and failed paid-round counts, feature entries, reached exits, and instances still open at interruption. Entry-selected cohorts retain these boundaries even without a matching numeric child. The accounting view separates them from settled-round lifecycle counts and exposes a reconciliation/completeness flag. Nesting faults withhold completeness; cohort overflow withholds the cohort breakdown. Closed boundaries in an unfinished round are evidence of execution, not settled feature awards. They contribute to no means, distributions, turnover, completed-parent exposure or verification verdicts.

Choose “Feature entries · unfinished rounds”, “Feature exits · unfinished rounds” or “Open features at interruption” to track these counters on the live dashboard. Incomplete tracking and older archives without this contract show an undefined scalar instead of an invented zero. The API publishes the same `analysis.interruptedLifecycle` in progress, pinned snapshots and retained evidence; cumulative frames cannot regress those counters. A calculation time limit currently uses the cancelled category. Distinguishing per-feature resource expiry, payout caps and authored stop reasons remains a separate workflow.

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

## Configurable statistic trends

Choose **Chart statistic** when authoring a metric or directly on its live/saved Simulate card. Minimum, maximum, sum, count, spread, higher moments, paired statistics, accounting and matching-parent counters share the same scalar selector as the card. Only statistics supported by the authored population and paired/event/assertion contract are offered. Card statistics and chart choice are independent presentation settings; changing either preserves the measurement fingerprint and applies immediately. To isolate one FS type, author its child/entry filter in the collection plan. A chart summarizes that metric’s full filtered population.

Every available snapshot stores a bounded map of scalar values. Undefined statistics remain `null`, including an empty-scope average; known zero counts still plot zero. Missing values break the line and observed-range band. The horizontal axis is completed paid rounds, even when the measured population is node visits or owning feature episodes. The optional average band is an observed min/max range, never a confidence interval. Pointer selection and Arrow/Home/End navigation inspect retained snapshots. Custom and RTP/throughput/precision charts observe their actual card width, so SVG labels retain their physical font size; narrow cards use fewer ticks.

Reload preserves the selected statistic and compact history. Checkpoints omit distributions, cohorts and witnesses per point, enforce a 1.5 MB serialization limit and downsample older history while preserving the last point. Corrupt or oversized local histories are rejected. Earlier checkpoints offer their retained basic statistics; unavailable advanced history is shown as gaps, and an authoritative same-revision server snapshot enriches the last point without inventing earlier values. Rich final evidence continues to come from the server.

## Independent verification

The initial [verification record](verification/measurements-checks.json) contains the scoped implementation’s 750 Core, 75 API, 27 frontend unit and 49 browser checks, all passed. Subsequent changes and remote CI results are recorded in the [implementation ledger](verification/METRICS_IMPLEMENTATION.md). The [production report](verification/measurements-production.json) records a real authenticated 100,000-round run, scoped WebSocket observations, unchanged game statistics, and bit-identical measurement replay with two workers versus one. That report describes the initial local deployment; the ledger and advanced production report identify later verified builds.

The core oracle defines three FS iterations: sticky payouts 2 and 4, other payout 3. Five paid rounds must produce 15 eligible visits, 10 matches, 5 exclusions, minimum 2, maximum 4, sum 30, average 3 and sample variance 10/9. Both sampling paths are checked against this manual specification. Additional checks cover worker-count replay, unchanged game payouts, interrupted rounds, empty scopes, missing state, invalid plans, capped round payouts, API snapshots/evidence and real-browser configuration/export/replay.

Run launches honor `Retry-After` once for an explicitly rejected HTTP 429, with a visible wait message and Cancel launch action. Accepted requests, network failures and ambiguous timeouts are not retried automatically. Existing observations remain available if a new launch fails. Saved Simulate permalinks (`/simulate?run=…`) restore the pinned snapshot and plan in an empty browser without displacing another active run. A new run uses the current constructor draft; pinned replay is available in Results.

## Component accounting

Choose **Create component accounting plan**, or the variance-decomposition catalogue workflow. Author a total and 2–6 named components, a common observation point, units, fixed external cost, optional Boolean filter and cohort expression. The native visual expression builder uses the same bounded AST grammar as Build. A completed feature must first write all its component totals in state before its observation boundary; means from different node-visit populations cannot be combined.

Saving adds the total, each component, every pair and an exact residual `total - (component1 + … + componentN)` atomically after validating the full proposed plan. Two components add five measurements; six add 23. The existing 32-definition, serialized-size and shared storage budgets apply. Pair cards start hidden as a dashboard preference, while their evidence remains collected. Small explicit support/cohort budgets avoid multiplying unnecessary storage across all pairs.

Once the run completes, open **Component accounting** in Simulate or Results. Select the saved plan or compatible metrics manually, and choose the overall matching population or a retained cohort. The server verifies common scope/units/cost, the residual tree, all pair covariances, matching exposures and pair marginals. Weighted observations and within-episode averages need a different population contract and are rejected. Node values are read before that node executes. Invalid observations, duplicate awards, incomplete selected cohort coverage and inconsistent summaries cannot produce a pass.

The report shows component means, sums and contributions to **all paid turnover**, each covariance, and `sample variance(total) = sum(component variances) + 2 × sum(all pair covariances)`. Mean and variance residuals use explicitly reported floating-point tolerance; severe cancellation withholds a meaningful numeric verdict. The independent zero assertion counts mismatches in exact constructor arithmetic before conversion to report numbers. Empty populations and single observations retain undefined sample variances. Zero observed violations describes this sample, with no population-law or independence guarantee.

`POST /api/runs/{id}/measurements/accounting` accepts `{ name, totalMeasurementId, componentMeasurementIds, residualMeasurementId, groupKey? }`. It uses completed pinned evidence, never dashboard drafts, and the existing authenticated compute quota. The retained diagnostic stores request/result hashes, config/plan hashes, producer artifact, completed paid-round count and snapshot revision separately from the calculator artifact. It is deduplicated in the bounded encrypted archive and available after browser storage is cleared. Reports can also be exported when the archive's diagnostic budget is full. Diagnostic reports stay outside live socket frames; live component scalar statistics continue through the cumulative measurement stream.

## Predeclared verification profiles

Configure a verification profile in the measurement workspace before launching. Select required measurements/checks and minimum matching counts. Author their filters, references, tolerances and independence guarantees in the measurement editor. A requirement for one FS type should reference its explicitly filtered definition. Choose references from the model specification; a viewed-seed replay establishes reproducibility.

The editor can allocate the shared family error budget by raising selected interval confidence/family settings. It validates and saves the full updated collection/profile atomically. The run pins a separate profile hash. Editing/removing the workspace profile affects the next launch; saved Results and replay retain the original declaration. The inference catalogue's sufficiency action opens this editor.

Evaluate the pinned profile after completion in Simulate or Results. Interrupted prefixes remain visible as measurements but cannot satisfy a final profile. Logical checks cover observed cases; precision checks require the whole supported uncertainty interval inside the authored tolerance. Wide compatible intervals are insufficient. Non-rejection by a distribution test does not establish equivalence. Every marginal method retains its assumptions; a family allocation cannot upgrade approximate intervals into exact coverage. Numeric nonzero-event probability uses event counts, rather than the numeric value mean.

`POST /api/runs/{id}/verification` evaluates only the predeclared profile. The retained report binds model, measurement and profile hashes, producer binary, snapshot revision and completed population. It uses the existing authenticated calculation quota, bounded encrypted retention and deduplication. Native profiles support 1–32 global filtered-measurement criteria, with unweighted precision under declared independent-subject or supported numeric paid-parent cluster assumptions. Weighted proposal certification and post hoc cohort selection require separate contracts.

## Authoritative simulation exports

**Export evidence** reads and validates one authoritative run snapshot with its verified pinned graph. The `slotmath.simulation.v2` JSON contains that snapshot's complete measurements, execution population, predeclared profile, producer identity and retained diagnostics. The top-level progress is the same population as `run.progress`. Browser convergence history is bounded to that sample count and labelled separately; dashboard preferences describe the current presentation, while collection definitions remain pinned in the run.

Explicit quota rejections honor `Retry-After`. Cancel export to stop the request or quota wait; switching runs or leaving Simulate also aborts it. A network failure, rejected read, wrong run, inconsistent snapshot, changed producer or missing verified input shows a recoverable error without downloading a reduced file. Direct saved-run links use the same reader. Profile and accounting verdicts additionally compare their retained source's full producer identity with the original run; a newer calculator is identified separately from the original sample producer.
