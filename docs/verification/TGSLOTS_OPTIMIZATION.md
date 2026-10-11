# TGSlots shared graph optimization evidence

2026-10-11. The final exported Nine Lives model measured **2.49–2.53 million
complete paid rounds/s through the Release UI on eight workers**. All three
100-million-round observations exceeded the two-million target. They include
cascades, triggered bonuses, live API/WebSocket statistics and loop evidence.
The identical seeded samples have equal payout statistics and histograms.
The earlier LabDemo measurements in PERFORMANCE.md use a different authored graph.

The Claude handoff was copied into the isolated `task/tgslots-finish` worktree
from checkpoint `a0c486c`; the original checkout and owner services were left
running. The independent original-game round fixtures were not regenerated.

## Contract and paired measurements

Every measurement includes triggered bonuses and cascades, payout statistics,
histograms, loop evidence and live progress. Each process warms two million
complete rounds and measures five million rounds three times, on eight workers,
seed 42, `splitmix64-chunk-65536`, .NET 10.0.12 Release. Four processes alternate
new/old/old/new or old/new/new/old. Medians below summarize six observations per
version within each pair, not a cumulative gain across unrelated host conditions.
Own benchmark services use CPUs 0–19; the terminal is restricted to CPUs 12–19.
Existing workstation applications remained running, so these are not an idle D7
runner certificate. HTTP, queueing, SignalR and rendering are outside the timing.

| Experiment | Before rounds/s | After rounds/s | Before CPU µs/round | After CPU µs/round | Decision |
| --- | ---: | ---: | ---: | ---: | --- |
| Immutable state reset | 1,548,274 | 1,564,847 | 5.027 | 4.989 | Rejected |
| Reuse initial scatter set | 1,570,715 | 1,633,117 | 4.953 | 4.779 | Retained |
| Lazy common subexpressions | 1,741,601 | 1,797,277 | 4.468 | 4.316 | Retained |
| Skip unused map item | 1,742,642 | 1,726,977 | 4.503 | 4.509 | Rejected |
| Lazy loop-invariant values | 1,650,915 | 1,560,178 | 4.746 | 4.934 | Rejected |
| Source-row table in JSON | 1,923,474 | 2,065,746 | 4.050 | 3.712 | Retained |
| Bounded integer lowering | 1,935,299 | 1,896,334 | 3.974 | 4.122 | Rejected |

Raw repeat measurements, graph hashes, compile times, allocation and GC counters
are retained in [TGSLOTS_PERFORMANCE.json](TGSLOTS_PERFORMANCE.json).
Every pair preserves all payout statistics and its entire payout histogram.
Scatter reuse changes the authored graph hash; the compiler comparison runs the
same graph hash, `0a5f86fddd83fee2f4358ea9ae4eb8252972ccc35708e4723ceba116d95b8d75`.

## Final application measurement

Open **Ready subgraphs → Open Nine Lives model** in Build, then Simulate with
100,000,000 rounds, seed 42 and eight workers. The normal UI chooses 65,536-round
progress batches at this size; workers also publish after 250 ms. No custom
observations were requested. A two-million-round warmup precedes the measured runs.
The Release API runs with CPUs 0–19 and a separate local test database. Existing
owner applications remained running; this is a shared-workstation observation.

| Repeat | Complete rounds | Elapsed | Rounds/s | Live observations |
| --- | ---: | ---: | ---: | ---: |
| 1 | 100,000,000 | 39.567 s | 2,527,359 | 490 |
| 2 | 100,000,000 | 40.088 s | 2,494,512 | 496 |
| 3 | 100,000,000 | 40.104 s | 2,493,517 | 497 |

Median: **2,494,512 complete rounds/s**. Model hash:
`32d8a2b32e09c55e97cb14d2bd613c619f45d1331822d5553eb0c484bc471b6c`.
Raw persisted result payloads are in [TGSLOTS_RELEASE_UI.json](TGSLOTS_RELEASE_UI.json).
The same 100-million-round seeded sample was repeated three times for throughput;
it is not 300 million independent observations. Timing uses the API's sampled
execution elapsed time, including live publication work, and excludes admission,
queueing, graph compilation and browser rendering. Screenshots show the
[authoring library](tgslots-authoring.png), [completed run](tgslots-simulation-release.png)
and [throughput card](tgslots-throughput-release.png).

## Profile and retained changes

Native GDB sampling attaches only to an owned diagnostic child. Reflection in
that external diagnostic maps generated methods back to compiled graph chains.
One thousand leaf samples, after startup, use deterministic 5–15 ms jitter.
These counts are not inclusive CPU percentages. Perf profiling is restricted by
the host; no system security setting was changed.

Before CSE, 201/1000 leaf samples landed in the settling map. A separate profile
with CSE still finds settling the largest generated method, at 139/1000, followed
by scalar draw (101), generic loop control (89) and initial board assembly (83).
Sample sets were collected separately; their difference is not a speedup estimate.

Shared JSON subgraphs prune wild-free components below the payout threshold,
apply necessary edge/cell bounds, use two explicit frontier steps before a loop,
and bind immutable cell positions for initial assembly. Candidate geometry is
checked independently against all 295 connected four-cell placements and all
766 connected five-cell placements, plus mixed boards. Ordered traces and wild
claiming retain their original traversal. Weighted-cell evaluation remains general.
The initial scatter trigger now reuses the board's existing symbol sets once;
subsequent cascades do not overwrite it and free spins do not retrigger.

A small-column source-row table now replaces the survivor filter in Nine Lives.
Five rows require only 160 integer entries. Shift/Intersect recover each column's
removal pattern; normal index and conditional expressions read survivors directly
from the old board or pick replacements in the original draw order. The same lookup
moves carried values, and reel refills retain their stop semantics. The generic
subgraph retains survivor arrays by default for consumers such as Le Militare's
new-scatter count; Nine Lives disables that unused output. Columns above eight rows
keep the general filter composition. All patterns of 1, 3, 5, 8 and 9 rows are
checked against independent geometry in all sampling tiers and exact expressions.

The compiler caches repeated pure selector expressions lazily within one map,
filter or fold item. Flags reset for every item. Untaken branches do not evaluate
cached expressions, failing expressions retain their error location, and scoped
bindings are restored. This optimization recognizes no game, mechanic name or
authored graph pattern.
No new capability beyond the previously approved six finite-set operations was added.

## Authoring

Ready subgraphs includes all five full game models, plus complete editable cluster
and cascade examples with uncalibrated example RTP. Users can inspect definitions, retain and restore their
current draft, extract selected atoms and add references to shared definitions.
The Mechanics panel uses those same definitions rather than copying canvas nodes
without their expressions. Expressions, connections and project data survive
extraction; state keys remain shared and boundary ambiguities are reported before
mutation. See [OPTIMIZED_SUBGRAPHS.md](../OPTIMIZED_SUBGRAPHS.md).

## Validation status

- 101 frontend unit cases, frontend build, lint and all 38 fixture export checks passed.
- 22 original-game replay and gravity cases passed on the final graph. Original
  scripts' draw sequences reproduce the boards, features and payouts of all five games.
  Independent golden data was never regenerated; working API traces were refreshed
  only after these comparisons passed.
- 19 CSE/gravity regressions passed after backend cleanup. Gravity covers every
  column pattern, mixed neighboring columns, full/empty columns, carried values,
  cell and reel refill, and the table/general-composition boundary. CSE covers
  lazy branches, located errors, per-item reset and nested binding restoration.
- The full browser suite passed all 82 cases without retries, including all five
  played/replayed games and four authoring workflows. After adding the complete
  game model chooser and empty-draft fix, all six authoring cases passed again,
  including opening each game, restoring the prior draft and returning from an
  emptied subgraph without locking the editor. Earlier failure logs were retained.
- All 204 affected backend math/sampling cases passed with no failures or skips.
  Whole-solution formatting verification and the Release build passed. All three
  long Release UI measurements exceeded the target and retained live observations. Rejected integer range lowering was removed.

The first remote gitleaks scan matched `apiBinarySha256` in the persisted UI
report. Its value equals the SHA-256 of the Release API binary. The exception
requires that exact reviewed value, field and report path together. A local scan
with CI's gitleaks 8.24.3 reports no findings over the PR history; negative
controls still detect a changed hash, another file and another field.

## Cleanup evidence

Backend touched files run the pinned Rider Reformat Code and Built-in: Full Cleanup
with solution context. The handoff had a formatter disagreement: nested loop braces,
multiline initializers and switch-case blocks were arranged differently by Rider and
Roslyn. Explicit backend EditorConfig settings align the tools; cleanup and format
verification passed for the retained CSE implementation; the final gravity test
file also completed both scoped passes. Final whole-solution format verification
and the affected mathematical suites passed. The initializer workaround and brace settings
are documented by [JetBrains](https://www.jetbrains.com/help/rider/EditorConfig_CSHARP_CSharpCodeStylePageImplSchema.html)
and the [initializer formatting issue](https://youtrack.jetbrains.com/issue/RSRP-486359).

**Frontend Rider cleanup remains blocked.** Rider 2026.1.2's formatter exits 3 with
`PluginException: solution can't be null`. Standalone CleanupCode 2026.1.5.2 exits
3 with “No items were found to cleanup.” Scoped ESLint/build checks passed, but
neither replaces the required Rider passes. The affected source files are:

- `frontend/e2e/subgraph-library.spec.ts`
- `frontend/e2e/tgslots-games.spec.ts`
- `frontend/src/components/Palette.tsx`
- `frontend/src/components/editor/ModelNavigation.tsx`
- `frontend/src/components/editor/StarterLibrary.tsx`
- `frontend/src/components/mechanics/MechanicManager.tsx`
- `frontend/src/games/fixtures.ts`
- `frontend/src/games/le-militare/graph.ts`
- `frontend/src/games/nine-lives-tgslots/graph.ts`
- `frontend/src/games/server/index.ts`
- `frontend/src/games/server/play.ts`
- `frontend/src/games/shared/graph/expr.ts`
- `frontend/src/games/shared/graph/flow.ts`
- `frontend/src/games/shared/graph/mechanics/cascade.ts`
- `frontend/src/games/shared/graph/mechanics/cells.ts`
- `frontend/src/games/shared/graph/mechanics/cluster-candidates.ts`
- `frontend/src/games/shared/graph/mechanics/clusters.ts`
- `frontend/src/games/shared/graph/mechanics/hold-and-spin.ts`
- `frontend/src/games/shared/graph/mechanics/reels.ts`
- `frontend/src/games/shared/graph/mechanics/symbol-cells.ts`
- `frontend/src/games/x7-club/graph.ts`
- `frontend/src/index.css`
- `frontend/src/lib/projectFiles.ts`
- `frontend/src/lib/starterLibrary.ts`
- `frontend/src/lib/subgraphComposition.ts`
- `frontend/src/store/index.ts`
- `frontend/unit/game-server.spec.ts`
- `frontend/unit/subgraph-composition.spec.ts`

## Review corrections

The structured evaluator's named Lines/Ways dispatch and native evaluator term have been
removed. Large exact fixtures now author the payout through ordinary atoms and expressions;
legacy opaque transforms explicitly decline to the classic interpreter. The sampling graph,
its hash and the earlier raw timing evidence remain unchanged.

Extraction now follows transitive named expression dependencies and inline references,
preserves nested library parameters, and rejects unresolved/cyclic dependencies before
mutating a model. Browser sessions serialize the entire load/validate/play/commit transaction
with Web Locks across tabs. When cross-tab locks are unavailable, the browser server keeps
its sessions in memory instead of sharing a persistent wallet unsafely. Same-realm server
instances also share a cancellable queue. Abort signals reach the play transport and retry
waits, and an aborted action cannot commit later. The simple dispatcher retains an in-memory
session when storage fails. Auto-spin uses a separate internal invocation and cancels queued
continuations. Standalone launchers retain their startup recovery overlay until setup finishes.
Le Militare applies the restored mode to its feature menu when applying authoritative state.

Additional frontend files touched for these corrections are subject to the same tooling blocker:
Rider Reformat exits with `solution can't be null`; standalone JetBrains Full Cleanup reports
`No items found to clean up`. Neither required frontend cleanup pass is claimed as completed:

- `frontend/e2e/tgslots-games.spec.ts`
- `frontend/src/games/client/engine/dispatcher.ts`
- `frontend/src/games/client/engine/spin-orchestrator.ts`
- `frontend/src/games/client/games/le-militare/buy-feature-modal.ts`
- `frontend/src/games/client/games/le-militare/runtime.ts`
- `frontend/src/games/client/games/nine-lives/launch.ts`
- `frontend/src/games/client/games/x7-club/launch.ts`
- `frontend/src/games/server/index.ts`
- `frontend/src/games/server/play.ts`
- `frontend/src/games/server/server.ts`
- `frontend/src/lib/subgraphComposition.ts`
- `frontend/unit/subgraph-composition.spec.ts`
- `frontend/src/games/server/abort.ts`
- `frontend/src/games/server/session-lock.ts`
- `frontend/unit/game-session-reliability.spec.ts`
- `frontend/e2e/subgraph-library.spec.ts`
- `frontend/e2e/realtime-durability.spec.ts`

Review validation: Release build and whole-solution format verification; 28 affected
backend cases (including all 12 sampling CSE cases and independent large-space exact
formulas); 114 frontend unit cases; frontend build/lint; and 38 exported fixture checks.
Backend Reformat and Full Cleanup passed on all five touched sources.

The complete browser run passed 87 of 88 cases. The remaining durability test compared
a legitimate fast-sampler count of 37,814,272 with a fixed corrupt-frame sentinel of
9,999,999. Its sentinel now exceeds the requested population, it waits for authoritative
reconciliation, and it checks the retained sequence as well as displayed values. The
full failure report is retained at `/tmp/tgslots-review-complete-browser-failure` on the
validation workstation. After this correction and restarting the Release API with the
final backend, all 17 WebSocket durability and subgraph scenarios passed without retries
in 2.2 minutes. These include the corrected test and the additional API integration check
that payout remains 22 before/after extraction with transitive expressions and a
parameterized nested library. Across the complete run and targeted rerun, all 89 current
browser scenarios have passed; this is not a claim of one clean complete rerun.
