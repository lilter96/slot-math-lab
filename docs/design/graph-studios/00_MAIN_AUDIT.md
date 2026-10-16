# Main-branch audit: graph-native authoring studios

Reviewed: 2026-10-12. Repository: `lilter96/slot-math-lab`.
Source snapshot: `b81e71c82cb0314d066d7134278751b85676f356` (`main`, merge of PR #17).

This is a source-based design/engineering audit, not a report of tests executed by the author of this handoff. The repository tree, all five migrated game factories, shared graph-building primitives and key mechanics, model schema, compiler/inliner, editor load/export path, authoring tests and verification guidance were inspected. Historical verification reports describe earlier runs; they are not fresh evidence for a future implementation. Local unpublished redesign code is not part of this snapshot. Codex must inspect its actual checkout and preserve useful existing work.

## 1. Correct product scope

This is a multi-game graph-authoring product, not a Nine Lives dashboard. The requested delivery consists of exactly two workspaces:

1. Reel & Paytable Studio, including independent weighted-cell sources where a game has no physical reels.
2. State Machine Designer, as an editable projection and compiler front end for the existing graph format.

Both must work with current projects, existing imported graphs, the five game families, their relevant variants, and user-created reusable graph mechanics. The reference Nine Lives is an additional compatibility case, not the architecture of the product. Overview redesign, new analytics products, a full time-travel debugger, auto-tuning and a new runtime are outside this delivery.

## 2. Game inventory verified in main

| Model/factory | Draw and payment model | Lifecycle that the designers must preserve |
|---|---|---|
| `ancient-dragon/graph.ts` | 5×3 physical reel strips; 25 paylines; mystery-symbol draw; scatter payouts | Paid spin, scatter trigger, free-spin award, decrement, retrigger, finish. Same underlying spin mechanic is reused. |
| `woodland-whisper/graph.ts` | 5×3 physical reel strips; 30 paylines; mystery/scatter; ordinary and bought-entry variants | Pick until two cards match, award free spins, boosted free-spin payout, repeat pick when bonus is due. Bought entry conditions the ordinary reel distribution on the scatter event. |
| `x7-club/graph.ts` | 5×3 independent cell draws, NOT reel strips; 20 paylines; weighted coin prizes | Paid spin or bought initial coins; Hold & Spin; restore respins on new coins; column boosts; finish on no respins/full board; collect once. |
| `nine-lives-tgslots/graph.ts` | 6×5 independent cells; distinct paid/free/refill weights; cluster paytable; coin prizes | Cascades; initial-paid-board scatter trigger; fixed free-spin award without retriggers; collection to wilds; carried multiplier; payout cap; bought entry. |
| `le-militare/graph.ts` | 6×5 physical paid/free strips; weighted cluster counting; cascade refill; multiplier/prize weights | Combat, armed reels, planes, air raid, persistent free-spin state, retriggers, limits. Three model modes and six entry kinds: spin, chance, air-raid, standard, elite, super. |

`frontend/src/lib/starterLibrary.ts` registers all five migrated factories plus cluster and cascading-cluster starters. `frontend/src/games/nine-lives/` contains a separately authored reference/lab model. Do not confuse it with `nine-lives-tgslots` or silently merge their identities.

`frontend/src/games/fixtures.ts` exports ordinary/bought variants, small mathematical fixtures, Le Militare mode/entry cases and gravity fixtures. Not every exported file is an executable game: `le-militare.prices.json` is a price table. Discover the corpus by shape and source, not by assuming every JSON file is a graph. Exercise the complete mode/entry cross-product where a factory supports it, even where only representative full graphs are checked into the fixture directory.

## 3. Existing authoring compiler: reuse, do not replace

`frontend/src/games/shared/graph/flow.ts` already builds ordinary graph JSON:

| Authoring operation | Existing executable representation |
|---|---|
| `Flow.write` | `modifyState` with `outputKey` and an expression reference |
| `Flow.draw` | `draw`, inline `drawWeights`, `stateWriteKey` |
| `Flow.use` | `library` plus parameters |
| `Flow.onlyIf` / `Flow.either` | `branch` with `true`/`false` ports and joins |
| `Flow.repeat` | `loop` with `body`/`exit`, optional stop expression and a limit |
| `Flow.sink` | The round's `metricsSink` |

`gameGraph` assembles nodes, edges, expressions, mechanics, state and tables. The new State Machine Designer is another authoring language targeting this same representation. It is NOT a request for a new `stateMachine` kernel node, named engine behavior, or another JavaScript payout engine.

Backend path: GraphConfig → catalog/default merge → subgraph-reference validation → SubgraphInliner → GraphValidator/ExpressionResolver → ProgramBuilder → existing reference/optimized sampled and exact-capable evaluation paths.

Important semantics observed in `GraphCompiler.cs`:

- A normal node with multiple outgoing paths executes all paths and sums outputs. It is not a state-machine choice.
- Branch chooses its true or false path. Exclusive transitions must lower to this control flow.
- Loop checks its condition before a pass. Its body has a terminal boundary and its exit continues the graph. Arbitrary back edges are rejected.
- Loop limit exhaustion is classified as `modelLimit`, not natural feature completion.
- The sink may read the final payout from a state key, normalized using `WinScale`.
- `SubgraphInliner` namespaces node and expression identifiers. Arbitrary user state keys remain shared unless authored otherwise.

## 4. The data-binding traps a generic CRUD screen misses

### Reels are not always `GraphConfig.reelStrips`

Migrated physical reels are expanded by `reelState()` into `initialState` arrays. Each array contains the physical strip PLUS a wrap tail repeating the first `rows - 1` symbols. Reel stops are separate weighted Draw nodes. Some stop definitions are reused by cascade refill. Woodland bought entry also contains derived `showPatterns`, conditional pattern weights, branches and permitted stops.

A reel edit must update the actual execution dependencies. Editing a table-shaped array without knowing its padding, stops and consumers is unsafe. Show the physical strip once; keep expansion and conditional-derived structures behind the adapter.

### Cell weights are not physical strips

Nine Lives and X7 Club draw cells independently. Their screen must expose source distributions and their usage, not invented reel lanes. Paid/free/refill/prize draws may look similar but have different semantics. Group them only when their bindings prove that they represent one editable source or when the user explicitly chooses linked editing.

`Flow.draw` writes `outcomeId` to state and currently emits numeric `value: 0` for many choices. This does NOT mean those choices have zero probability or zero payout. Preserve IDs, payloads and random draw order.

### There are executable payout tables and normalized declared tables

`gameTables()` creates symbol/paytable/payline metadata and rational payout denominations. The atom-composed games actually look up values in state/expressions:

- `linePays`: 2D symbol-by-count array.
- `clusterPays`: flat symbol-by-size array, stride `cellCount + 1`.
- `scatterPays`: count lookup, with its own units.
- Prize draws: outcome IDs/values, weights and the stake conversion.
- Payline positions: executable Library parameters `p0`, `p1`, …, not only the top-level payline list.

The declared paytables also determine supported payout precision in the compiler. They are not disposable display-only fields. Update executable values and declared denominations consistently.

Derived dependencies include an inferred all-wild row, cluster minimum size, eligible symbols, generated cluster-pruning expressions and traversal bounds, conditional reel-stop tables, and some computed payout bounds. Changing a new minimum paying group or adding a paying symbol is not merely changing one cell of a matrix.

### Board encoding must be explicit

Shared migrated graph helpers use column-major indices: `column * rows + row`. The native board contract in `Graph.cs` describes row-major payloads. Do not globally transpose all games or assume one encoding based on screen geometry. Bind the actual producer/consumer pair.

### Do not overwrite user edits by rerunning a factory

`createXGraph(originalModel)` is a useful template/oracle, not permission to replace a user's current graph with the bundled default. Existing imported graphs may have renamed nodes, edited AST, new mechanics and custom payloads. Patch proven source locations and dependent fragments only. Where a relationship is ambiguous, retain the custom operation and explain the local editing limitation instead of guessing.

## 5. Main frontend integration points

- `frontend/src/lib/projectFiles.ts`: `loadProject`, `exportProject`, `peekRootProject`, nested `graphTrail`, library reuse. `rootProject()` navigates/mutates; do not call it merely to display a projection.
- `frontend/src/lib/configPayload.ts`: boundary between UI nodes and the canonical graph. Inspect both preserved `backendNode` and legacy authored node paths before extending it.
- `frontend/src/lib/subgraphComposition.ts`: extraction with dependency closure and boundary validation. Reuse its principles and tests instead of ad-hoc copying nodes.
- `frontend/src/components/editor/ProjectDataEditor.tsx`: current generic data/expression editor. It is a compatibility fallback, not the proposed specialized Studio.
- `backend/SlotMath.Core/Model/GraphConfig.cs`: no current dedicated authoring-view metadata field. Prove persistence for any new project envelope/sidecar; never assume unknown JSON survives server deserialization.
- `backend/SlotMath.Api/Features/Play/PlayEndpoints.cs`: generic `/api/play/round`; returns exact payout numerator/denominator, state, seed/index and config hash. Preview must use the edited canonical graph, not a bundled game default.

## 6. Tests and operational constraints already present

`frontend/e2e/subgraph-library.spec.ts` explicitly opens all five games and verifies retaining the previous draft. It also checks extraction/reuse and a transitive-expression graph whose played payout remains 22. Extend existing evidence instead of replacing it with screen-render assertions.

`frontend/scripts/export-game-fixtures.mjs --check` checks generated fixture drift. Independent original-game replay data must not be regenerated to make a failing adapter look correct. Historical counts in TESTING and verification logs differ; enumerate the current tests rather than copying an old number into a completion report.

`AGENTS.md` prohibits game-specific kernel/compiler/API dispatch and unauthorized new atoms. It also requires scoped Rider Reformat/Full Cleanup; an already documented frontend environment blocker is `solution can't be null`. Report the real blocker once per changed environment, retain the uncleaned-file list, and do not claim ESLint substitutes for the required pass or disable the hook.

## 7. Source index (pinned)

All links below point at the audited snapshot. New design requirements in the other documents are proposals, not claims that these capabilities already exist.
- [`AGENTS.md`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/AGENTS.md)
- [`frontend/src/lib/starterLibrary.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/lib/starterLibrary.ts)
- [`frontend/src/games/ancient-dragon/graph.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/ancient-dragon/graph.ts)
- [`frontend/src/games/woodland-whisper/graph.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/woodland-whisper/graph.ts)
- [`frontend/src/games/x7-club/graph.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/x7-club/graph.ts)
- [`frontend/src/games/x7-club/model.json`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/x7-club/model.json)
- [`frontend/src/games/nine-lives-tgslots/graph.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/nine-lives-tgslots/graph.ts)
- [`frontend/src/games/le-militare/graph.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/le-militare/graph.ts)
- [`frontend/src/games/fixtures.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/fixtures.ts)
- [`frontend/src/games/shared/graph/flow.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/shared/graph/flow.ts)
- [`frontend/src/games/shared/graph/tables.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/shared/graph/tables.ts)
- [`frontend/src/games/shared/graph/mechanics/reels.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/shared/graph/mechanics/reels.ts)
- [`frontend/src/games/shared/graph/mechanics/paylines.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/shared/graph/mechanics/paylines.ts)
- [`frontend/src/games/shared/graph/mechanics/clusters.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/shared/graph/mechanics/clusters.ts)
- [`frontend/src/games/shared/graph/mechanics/hold-and-spin.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/games/shared/graph/mechanics/hold-and-spin.ts)
- [`backend/SlotMath.Core/Model/GraphConfig.cs`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/backend/SlotMath.Core/Model/GraphConfig.cs)
- [`backend/SlotMath.Core/Model/Graph.cs`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/backend/SlotMath.Core/Model/Graph.cs)
- [`backend/SlotMath.Core/Compiler/GraphCompiler.cs`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/backend/SlotMath.Core/Compiler/GraphCompiler.cs)
- [`backend/SlotMath.Core/Compiler/SubgraphInliner.cs`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/backend/SlotMath.Core/Compiler/SubgraphInliner.cs)
- [`backend/SlotMath.Api/Features/Play/PlayEndpoints.cs`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/backend/SlotMath.Api/Features/Play/PlayEndpoints.cs)
- [`frontend/src/lib/projectFiles.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/lib/projectFiles.ts)
- [`frontend/src/lib/subgraphComposition.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/lib/subgraphComposition.ts)
- [`frontend/src/components/editor/ProjectDataEditor.tsx`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/src/components/editor/ProjectDataEditor.tsx)
- [`frontend/e2e/subgraph-library.spec.ts`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/e2e/subgraph-library.spec.ts)
- [`frontend/scripts/export-game-fixtures.mjs`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/frontend/scripts/export-game-fixtures.mjs)
- [`docs/TESTING.md`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/docs/TESTING.md)
- [`docs/verification/TGSLOTS_OPTIMIZATION.md`](https://github.com/lilter96/slot-math-lab/blob/b81e71c82cb0314d066d7134278751b85676f356/docs/verification/TGSLOTS_OPTIMIZATION.md)
