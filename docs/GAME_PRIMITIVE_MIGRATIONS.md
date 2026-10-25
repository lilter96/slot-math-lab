# Real-game admission of generic primitives

The user requires game migrations and complete-round measurements as each
capability becomes available. Backend availability and synthetic speedups do
not establish a game-wide gain. Final acceptance requires at least 1,000,000
complete rounds/second with one worker on all 15 full configurations, smaller
editable graphs, and usable game creation in the service. This target remains
outstanding. Complete rounds include their cascades and awarded/bought features.

## First migration: ordered board, prize and respin draws

The shared authoring helpers now emit ordinary `DrawMany` and categorical
assets. They replace repeated scalar Draws and balanced selection trees;
the kernel has no game/mechanic-name dispatch. Legacy String payloads, filtered
positive outcome order and the separate zero payout channel are retained.
The X7 respin schedule stays land, prize, land, prize in cell order, including
held cells and discarded prizes. No random choice is skipped.

Assets are attached at graph level by the generic Flow/Mechanics authoring
helpers. Equal independent source arrays do not become linked merely because
their numeric rows match. Input rows are snapshotted; source copies and edited
asset copies remain independent. Editor integration is saved separately for the later UI phase; the current
priority is the backend and complete-round performance.

`board` remains the same public String array. Obsolete `cellN`, `prizeN` and
`landedN` scratch fields retire; ordered prize/respin arrays replace them.
The original server answers, math goldens and recorded random choices are
preserved. Derived client-state caches change with the graph's private fields.
Older saved project/configuration JSON remains its own source for replay;
this template migration does not rewrite stored user projects.

Before the small exact-evaluation follow-up, canonical size changed as follows:

| Full configuration | Stored nodes before/after | Compact UTF-8 bytes before/after |
| --- | ---: | ---: |
| Nine Lives | 225 → 136 | 194,601 → 114,412 |
| Nine Lives buy | 184 → 125 | 159,573 → 107,401 |
| X7 Club | 148 → 90 | 114,453 → 66,127 |
| X7 Club buy | 78 → 49 | 61,214 → 39,252 |

Explicit release of dead prize/respin buffers adds two nodes to each X7
configuration: their final counts are 92 and 51. This avoids retaining unused
random history in the exact recurrence state. File indentation is excluded:
node count and canonical source size are separate from formatted line counts.

The first full candidate passed 1,024 old/new comparisons: 128 seeds with and
without game tracing for each configuration. Payout, every state field except
explicitly retired/new scratch storage, complete RNG audit and the following
word agreed. Maintained-source checks also cover the default new scratch array
in X7 buy. Public game traces retain their original semantics.

Preliminary full-candidate timings used one frozen kernel, CPU 2, one worker,
ABBA process order, 32,768 warmup rounds, three measurements and first-sample
discard. Paid cases used 131,072 measured rounds, bought cases 16,384.
All counts, RTP, variance, hit frequency, maximum payout, histograms and caps
matched before and after. These are preliminary observations, not final
acceptance or dedicated-host capacity measurements.

| Configuration | Before median rounds/s | Candidate median rounds/s | Ratio |
| --- | ---: | ---: | ---: |
| Nine Lives | 309,005 | 337,996 | 1.094 |
| Nine Lives buy | 15,578 | 18,127 | 1.164 |
| X7 Club | 272,622 | 276,651 | 1.015 |
| X7 Club buy | 45,546 | 46,370 | 1.018 |

The X7 ranges overlap, so no throughput gain is established there. Nine buy
allocation rose from 2,817 to 3,441 B/round in this candidate despite improved
throughput. These tradeoffs must remain visible; this is not zero-allocation
simulation. Final checks include original-server replay, client adapter
answers and independent complete exact laws for the small models.

## Rejected first Reachable substitution

Replacing the authored grid component growth with generic Reachable passed
2,560 old/new comparisons on the ten cluster configurations, including
traced/untraced state, payout, RNG audit and following word. Only the new
adjacency data and precisely enumerated retired loop fields differed.
It reduced stored definitions by five nodes, but complete rounds showed no
speedup against the existing bit-mask growth and seed pruning:

| Full configuration | Before rounds/s | Candidate rounds/s | Ratio |
| --- | ---: | ---: | ---: |
| Nine Lives | 349,227 | 347,415 | 0.995 |
| Le Militare spin | 92,078 | 90,793 | 0.986 |
| Le Militare standard bonus | 2,049 | 2,042 | 0.997 |

Those candidates remain outside the maintained game configs. The generic
primitive remains available; its synthetic advantage over repeated generic
relaxation does not justify replacing a cheaper authored grid representation.
Reports and failed-case evidence stay under `/tmp/modern-primitives-evidence/`,
outside Git.

## Backend validation and current baseline

The final backend selection passed 54 cases without skips after scoped Rider
Reformat and Full Cleanup. It covers the generic ordered-draw contracts,
original-server replay, client-state caches, independent small exact laws and
a new bounded-cycle regression. The latter first reproduced a 5,000-branch
budget failure when a fixed nonempty batch followed a state read. Binding its
input-independent program and continuation once restores exact memoization;
zero-length and dynamic-count semantics remain unchanged. The real small X7
model again completes exact evaluation in its original branch budget.

The final four migrated configurations passed another 1,024 old/new
traced/untraced payout, state, RNG-audit and following-word controls on the
same frozen tested kernel. Only enumerated retired scratch fields and their
new array replacements differ. All independent original-server goldens are
unchanged. Two derived client-state cache inputs were refreshed for the new
private fields.

A complete current baseline uses one worker, CPU 2, compiled sampling tier,
two processes and three measurements per process, discarding each first
sample. Payout statistics and loop evidence agree between repeated executions.
No configuration currently meets the one-million-round acceptance target.
Rates are shared-host observations, not a capacity guarantee.

| Current full configuration | Median rounds/s | Allocated B/round |
| --- | ---: | ---: |
| ancient-dragon | 233,069 | 32.0 |
| nine-lives | 305,489 | 47.1 |
| nine-lives.buy | 17,275 | 3,440.6 |
| woodland-whisper | 198,663 | 24.8 |
| woodland-whisper.buy | 15,071 | 61.3 |
| x7-club | 263,864 | 67.5 |
| x7-club.buy | 43,055 | 1,213.8 |
| le-militare.spin | 78,145 | 296.3 |
| le-militare.standard | 1,839 | 42,236.5 |
| le-militare.super | 654 | 177,377.6 |
| le-militare.elite | 845 | 119,240.3 |
| le-militare.chance | 28,329 | 2,233.8 |
| le-militare.air-raid | 65,978 | 341.4 |
| le-militare.siege.spin | 80,473 | 300.1 |
| le-militare.recon.spin | 72,078 | 323.4 |

The heavy bonus cases remain the largest gap. Managed hot-stack and allocation
profiles identify exact-number temporaries and array work as major costs;
subsequent generic backend changes must be measured against this baseline.
[Exact integer floor division](BACKEND_PERFORMANCE.md) subsequently measured
3.52–4.61x on the three heavy Le Militare features and 1.28x on its ordinary
spin, with identical full-round statistics and 1,950 state/RNG replay controls.
Nine Lives and Woodland holdout timing ranges overlap. This is a generic
compiler improvement; graph sizes are unchanged and the final target remains open.
UI/editor work and mobile browser follow-up are preserved separately at the
user's request to prioritize backend performance.

## Remaining work

The binary contribution migration uses a binary contribution mask in all eight Le
Militare variants and their six small fixtures. Group sizing uses existing
finite-set expressions; original numeric weights remain supported by the shared
authoring helper. Final old/new complete-round measurements improve 1.21–1.41x
in the normal runtime, with all 2,048 replay/RNG controls, selected regressions
and the small exact control passing. [Backend performance](BACKEND_PERFORMANCE.md)
records the hardware profile, rejected control-flow experiments, geometry oracle,
source changes and measurement conditions. This does not achieve the final target.

The prepared-adjacency follow-up now admits the existing Reachable atom into
all ten full cluster configurations, eight small models and two authoring
starters. It replaces the bounded growth subgraph, preserving first-seed order,
wild claims and RNG draws. Each stored graph loses five nodes and each full
configuration shrinks by 2,906 canonical bytes. Nine Lives becomes 136 → 131
nodes (buy 125 → 120); Le Militare Super/Elite/Standard become 236 → 231 and
its ordinary spin becomes 254 → 249. The two starter graphs shrink as well.

The final 5,120 controls compare complete remaining state, payout and RNG,
allowing only new immutable adjacency and precisely retired growth-loop fields.
Original goldens are unchanged. Final normal-runtime paired measurements improve
1.027–1.060x across all ten migrated full configurations, with disjoint ranges.
The [performance report](BACKEND_PERFORMANCE.md#preparing-immutable-adjacency-and-reducing-component-graphs)
records actual maintained inputs, longer paid measurements and the repeat profile.
Payout editing supports the new graphs, binary or numeric weights and saved grid
composition. Broader asset editor integration remains in the deferred UI work.

The stationary-carried follow-up removes one further node from all eight full
and six small Le Militare definitions, keeping every exported field. Free-spin
held flags cover complete protected columns and remain unchanged by gravity;
the source composer omits their redundant settlement. Existing finite-set
expressions replace free-spin held-cell exclusion. Paid and one-row fallback
behavior is preserved. All 3,584 complete-state/RNG controls match without
exceptions, and original client/server goldens stay unchanged. Actual maintained
heavy modes improve 1.13–1.19x on the accepted integer-lowering kernel, with
allocation reductions. The [performance report](BACKEND_PERFORMANCE.md#stationary-carried-flags-in-authored-games)
records all eight results, neutral ordinary/air-raid timing ranges, geometry
regressions, cleanup and exact-model validation.

Continue the backend toward the one-million target across all 15 cases, with
real-game migrations and paired measurements per admitted capability. Retain
exact arithmetic, source/replay identity and the complete-round denominator.
Return to editor integration after backend performance work.


## Full game-graph rebuild: first line-evaluation stage

The active rebuild covers all maintained game families and their small models,
followed by profiling and correction of weak execution paths. Product UI work
is deferred until speed and graph reduction are achieved. The game-authoring
TypeScript emits mathematical JSON; these changes do not redesign the UI.
Reference models, all 37 graph inputs and original client contracts were frozen
outside Git before the rebuild. Independent original-server goldens remain unchanged.

The new line evaluator uses ordinary Let/Local, conditional, array-index and
bounded collection expressions. Each needed line cell is read once. The first
nonwild symbol and stopped prefix length are packed into a scalar descriptor.
Analysis sums payouts directly without per-line result arrays; played rounds
materialize their line details. The previous payline template stays available
for existing studio-created models; the three maintained payline families now
compose the new evaluator. No kernel primitive or mechanic dispatch was added.

Ancient Dragon, Woodland Whisper and the paid line section of X7 were migrated
immediately, including their eight affected full/small JSON fixtures. Baseline
source-node totals change 72->51, 89->63 and 92->76 respectively. Canonical JSON
currently grows (59,337->95,536; 68,508->102,912; 67,172->105,166 bytes), because
scalar expressions replace repeated subgraph placements. Source node count alone
does not establish compactness; reducing this expression expansion remains work.

A single frozen accepted kernel runs old and new full graph data on CPU 2 with
one worker, normal .NET tiering/PGO and Tiered sampling. ABBA processes measure
three repetitions, discard the first and compare all outcome/loop statistics.

| Complete configuration | Old rounds/s | Rebuilt lines rounds/s | Ratio | Timing ranges |
| --- | ---: | ---: | ---: | --- |
| Ancient Dragon | 346,848 | 385,585 | 1.112 | disjoint gain |
| Woodland Whisper buy | 24,292 | 28,804 | 1.186 | disjoint gain |
| X7 Club paid | 473,394 | 480,365 | 1.015 | overlap |

These are preliminary frozen-data timings, not final acceptance of the complete
rebuild. Allocation per round is unchanged. Original-round goldens replay unchanged; played-round checks cover both
sampling tiers. Five small exact-model laws retain their original rational RTPs
and budgets. The new prefix evaluator also has an independent exhaustive
four-symbol-line oracle and a stopped-prefix lazy-read case. A separate 1,032
old/new replay comparison retains payout, every original state field, audit and
the following RNG word exactly. Added private fields are lineDescriptor,
lineDescriptors and immutable paylineCells. Derived client-trace fixtures add
only the first two: every existing trace field and win matches all 36 original
trace rows, without changing original-server expected inputs/results.

Remaining rebuild work includes full reel/field formation, pick/hold/boost
mechanics, both cluster-game families, the independent lab demo and dependent
starters. The next data layout experiments target packed finite vectors and
elimination of intermediate field arrays, with generic atomic capabilities only
where profiling and a standalone mathematical contract justify them.

Final validation of this first stage passes 29 selected Core cases: the six prefix
cases, original rounds, played-round rules in both sampling tiers, three regenerated
trace snapshots and five small exact laws. The first trace check failed solely on
new private keys; regeneration was accepted only after an independent comparison
proved every original key/value and win unchanged in all 36 rows. All 16 touched
source/fixture files received Reformat Code then Full Cleanup, with CLI evidence
for C# and exact-file solution scope evidence for authoring/JSON. Scoped ESLint,
TypeScript checking and the 38-fixture source check pass. Pre/post-cleanup full
JSON values match the timed snapshots. No product UI files were changed.


## Full game-graph rebuild: packed fields and pick state

Ancient Dragon and both Woodland configurations now compose a finite-vector
representation from existing exact integer, Let/Local and bitset expressions.
No kernel primitive, named mechanic evaluator or product UI change is involved.
Each reel stop indexes immutable local-window data. The same mystery draw and
scalar reel stops run in their original order, then shifts assemble the field.
Symbol replacement and scatter counts operate on its bits. The line program
reads cells at precomputed bit offsets, avoiding coordinate arithmetic at runtime.
All existing expression budgets remain enforced; none was raised.

Analyses materialize the final field once per complete round; played traces
lazily decode every recorded field. Pick bonuses retain a set of previously
turned card kinds, materializing the original boolean vector once after the
bonus. They draw the same cards in the same order and stop by the same rule.
Exact large constants are decimal strings derived from BigInt; floating-point
serialization must not round a 60-bit field mask.

A fresh comparison uses one identical accepted kernel and the preceding
line-rewrite JSON versus packed JSON. CPU 2, one worker, Tiered sampling and
normal .NET tiering/PGO; ABBA four processes with three measurements each,
discarding the first (four retained per version). Paid configurations use
262,144 warm-up and 1,048,576 measured complete rounds; bought uses 32,768 and
16,384. Every outcome statistic and every loop-termination row matches without
filtering. No change to the definition of a complete round is made.

| Complete configuration | Previous rounds/s | Packed rounds/s | Ratio | Retained ranges, previous / packed |
| --- | ---: | ---: | ---: | --- |
| Ancient Dragon | 394,954 | 434,421 | 1.100 | 390,093–400,873 / 422,089–443,753 |
| Woodland Whisper paid | 364,456 | 405,621 | 1.113 | 352,884–376,659 / 396,497–414,806 |
| Woodland Whisper buy | 27,816 | 35,464 | 1.275 | 27,584–28,187 / 34,950–36,206 |

All three ranges are disjoint in this shared-host experiment. These measurements
do not establish dedicated server capacity or completion of the all-game target.
Measured allocation changes only slightly: 33.36->33.07, 27.48->26.96 and
61.29->58.06 bytes/round respectively. Most removed buffers were already pooled.

A separate work diagnostic replays 1,024 independent streams after 512 warm-ups
and counts existing arena requests of lengths 1..64, including retained buffers.
Paid Woodland requests/elements fall 2.303/34.355 -> 1.009/15.088 per round;
bought falls 34.641/499.316 -> 2.123/26.230 (93.9% fewer requests, 94.7% fewer
requested elements). Payout and actual RNG words match. These counters exclude
arrays outside that arena and do not measure physical instructions or throughput.
The smaller speed gain is consistent with removing pooled array work while
line evaluation and the remaining feature execution still cost time.

The authored node totals are 51 for Ancient and 64/96 for Woodland paid/buy,
versus original 72 and 89/120. Canonical UTF-8 JSON is currently 99,444 and
107,807/142,526 bytes, larger than original 59,337 and 68,508/102,750.
Repeated expression bodies and added immutable data still need compacting.
Neither fewer nodes nor fewer buffers satisfies the entire graph-size target.

Validation passes 23 selected game/oracle/trace cases and three small exact laws,
retaining their original rational RTPs and budgets. The generic vector oracle
exhausts all 4,096 four-cell base-eight words in three tiers, and probes each
position/value plus mixed and maximum 63-bit words independently. A separate
774 old/new round comparison retains payout, every original state field, RNG
audit and following RNG word, with trace off and on. All 24 original client
trace rows retain every existing field and win; only boardPacked and, where
applicable, pickSeenBits are added. Original-server goldens and game models are
unchanged. Both cleanup profiles cover all 19 changed source/fixture files;
scoped ESLint, TypeScript, C# format and all 39 exported fixtures pass. No full
suite or integration run was required. A TypeScript constructor-syntax failure
was corrected without relaxing compiler settings or changing generated graphs.

Next work remains backend execution and mathematical authoring: compact the
repeated expression syntax, then rebuild cluster/cascade/combat and hold/boost
compositions, including their dependent models and starters. Product UI and
editor bindings remain deferred until speed and graph reduction are achieved.
