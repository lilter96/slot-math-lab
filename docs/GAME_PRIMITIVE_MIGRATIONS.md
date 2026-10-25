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

The next admitted migration uses a binary contribution mask in all eight Le
Militare variants and their six small fixtures. Group sizing uses existing
finite-set expressions; original numeric weights remain supported by the shared
authoring helper. Final old/new complete-round measurements improve 1.21–1.41x
in the normal runtime, with all 2,048 replay/RNG controls, selected regressions
and the small exact control passing. [Backend performance](BACKEND_PERFORMANCE.md)
records the hardware profile, rejected control-flow experiments, geometry oracle,
source changes and measurement conditions. This does not achieve the final target.

Continue the backend toward the one-million target across all 15 cases, with
real-game migrations and paired measurements per admitted capability. Retain
exact arithmetic, source/replay identity and the complete-round denominator.
Return to editor integration after backend performance work.
