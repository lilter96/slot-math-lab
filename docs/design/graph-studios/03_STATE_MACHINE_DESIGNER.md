# State Machine Designer

Status: proposed feature/semantics specification. Depends on `01_GRAPH_EDITING_CONTRACT.md`.

## 1. Product contract

Create, inspect and edit gameplay lifecycles using states, actions and guarded transitions. Apply the result as existing graph primitives, expressions and reusable library nodes. Existing Logic Graph remains fully available and edits the same source.

The first release must support existing game lifecycles AND creating/reusing a new machine. It is not merely a read-only diagram, a renamed React Flow canvas, or a fixed set of five hardcoded bonus forms.

Scope is a synchronous mathematical round/feature with explicit state and bounded graph execution. External asynchronous event queues, network events, wall-clock timers, parallel statechart runtimes, a full game animation timeline and time-travel debugger are not part of this delivery.

## 2. Existing graphs versus new machines

### Existing graphs: project their authored control flow

Recognize verified sequence/branch/loop/library structures and present meaningful regions as states/actions. Preserve their existing execution order, variable bindings, boundaries and conditions. Do not replace every imported game with a central program counter merely to display it.

A complex region such as cluster detection, column combat or custom evaluation can remain a `Custom graph operation` inside a state. It exposes its original source and a clear boundary; it is not discarded, reimplemented in the UI, or mislabeled as understood. Users can open that subgraph in Logic Graph and return.

Bundled game factories may emit non-executable source descriptors to make this mapping robust. Existing graph-only exports must be supported through structural matching/explicit binding. The graph is authoritative. No-op projection does not add nodes or rename state variables.

### New user-created machines: compile their declared semantics

A temporary typed Machine IR may represent state IDs, action sequences, ordered guarded transitions, selected starting/terminal states, variable bindings and an explicit state-step budget. After apply, its executable meaning is the generated ordinary graph. Store view layout/source mapping separately and reconstruct editable values from that graph.

An imported graph outside recognized patterns still executes and remains fully editable in Logic Graph. Safe portions can be edited in the State Machine Designer; unknown portions are retained as operations. Never claim general lossless recovery of the author's original intent from arbitrary graph JSON.

## 3. What a state and a transition mean

State fields: stable ID, display name, ordered entry actions, outgoing transition list and source region. Terminal states have no outgoing transitions. Entry actions execute once per visit, including a deliberate self-transition revisit. An action is an existing graph operation, library invocation, typed expression assignment or weighted Draw—not a frontend callback that calculates gameplay.

For newly authored machines, define one logical step as:

1. Execute the current state's entry actions sequentially.
2. Evaluate transition guards against the state after those actions.
3. Select the FIRST true guard in the explicit priority order; evaluate only the guards needed to select it. A single unconditional fallback may be last. Reordering priority is a behavioral edit and must be visible.
4. Execute the selected transition's effects sequentially, each reading the state left by the preceding effect.
5. Set the next state. Its entry actions execute at the next logical step, not again during the current dispatcher pass.

A nonterminal state without a match/fallback is a validation/runtime diagnostic, not an implicit payout or hidden restart. Validate structural reachability and missing destinations. Do not claim that structural reachability proves feasible guards or eventual termination.

Probabilistic choices are explicit Draw actions followed by deterministic guards on the selected outcome. Do not infer a transition probability from its label or number of outgoing arrows. Preserve existing games' random draw order and their purposeful unconditional prize draws.

When adapting an existing feature, preserve its actual ordering even if it differs from a convenient generic bonus template. For example, Woodland processes a pending pick award before consuming the next free spin; Nine Lives does not retrigger free spins.

## 4. Lowering to current GraphConfig

| Designer element | Existing graph output |
|---|---|
| Assignment/action | `modifyState` and an existing typed expression |
| Weighted choice | `draw` with declared outcomes and the existing state write contract |
| Reusable operation/nested machine | `library` pointing to editable generic-atom subgraphs |
| Conditional transition | Nested `branch` nodes on true/false ports |
| Structured repetition | Existing `loop`, body/exit ports, stop expression and current limits |
| Machine state storage, when required | Explicit namespaced fields in ordinary user state |
| Feature completion | Return through the feature boundary; existing outer payout/settlement path remains responsible |

Do not add `StateMachineNode`, a named FreeSpins/HoldAndSpin dispatch, XState execution, or an alternate RNG/payout loop. A UI graph library may render diagrams, but it may not become the mathematical runtime.

### Preserve structured loops where they already exist

Editing a recognized free-spin or respin region normally updates the corresponding expressions, shared actions, loop limit and dependencies in place. The designer may draw a return arrow visually, but the persisted executable graph uses the existing Loop body/exit representation. Do not write that arrow as an arbitrary cycle into `edges`.

### General finite control flow created in the designer

For new state machines that need arbitrary state transitions, use an ordinary bounded Loop with a dispatch tree on an explicit per-instance program-counter state field. Generate mutually exclusive nested branches. The current-state selection is fixed for the step: updating the program counter must not cause another independent `if` branch to run a second state in the same pass.

Create deterministic private variable names from a stable machine-instance identity. Use a reserved `Done` sentinel distinct from every user state ID. A transition to a terminal state selects that state for the next step; its entry actions run once and then set `Done`. An initially terminal machine also runs those actions once. The outer loop checks `pc == Done` before the next pass. The step budget counts state visits, including the terminal visit; label it as such, not as a number of free spins. It is a guardrail, not a substitute for feature rules. Respect the current compiler's limit precedence: reaching MaxIterations is classified as `modelLimit`, even at a boundary where a completion condition has also become true. Do not relabel that outcome as natural completion; choose justified slack for a proven bounded feature and test the exact boundary. Respect existing validator ranges and pre-pass checks.

Do not add a sink to every state. A nested machine returns through its boundary; the outer graph retains its single valid payout/settlement contract. No newly disconnected entry nodes may execute accidentally.

This compilation changes only authored graph data. Generic compiler/interpreters and atom semantics remain unchanged. Keep generated graphs proportional to the authored structure; do not expand every path combination or every possible runtime state. Measure graph size/compile time on the largest shipped variants and do not silently force previously supported models onto an incompatible execution path.

## 5. Scopes, reusable definitions and state isolation

The compiler namespaces node/edge/expression IDs during library expansion, but arbitrary state fields are not automatically local. A machine instance must explicitly declare which existing fields it reads/writes and which scratch fields are private. Two copies of the same machine must not share a program counter or counters by accident.

The user can choose Edit shared definition or Make this use independent. Show all affected callers before a definition edit. Copy-on-write includes required expression/dependency closure, rewritten parameter references, and intentional state binding—not just cloned canvas positions.

A game/entry variant may use a different initialization or price. Do not silently turn a bought bonus into a paid-spin path or use the base game's denominator for its RTP.

## 6. Concrete existing-game projections

These are source-grounded targets for import support, not a fixed engine enum:

| Family | Expected high-level readable structure | Required writable example |
|---|---|---|
| Ancient Dragon | Paid spin → scatter decision → award → free-spin loop with retrigger → settlement | Edit trigger/award/retrigger action consistently, including valid bounds. |
| Woodland Whisper | Paid or conditional bought entry → bonus-due decision → pick award → free-spin step → repeat/finish | Edit an award/guard while preserving pick-before-spin order and multiplier/price semantics. |
| X7 Club | Paid coins or bought initial coins → Hold & Spin → respin + column boost → reset/decrement → full/exhausted finish | Edit respin initial/reset count together and recompute its proven loop bound. |
| Nine Lives TGSlots | Paid board/cascades → initial scatter decision → fixed free-spin loop → cap/natural finish | Edit award/stop/multiplier action without introducing a retrigger that was not authored. |
| Le Militare | Selected entry → paid combat/cascade → award → persistent free-spin combat/cascade → retrigger/cap/limit finish | Edit a transition/action while retaining armed reels, held cells, weighted group counts, mode and entry. |

The reference Nine Lives and cluster/cascade starters are additional compatibility cases. Custom imported definitions may keep opaque actions while exposing verifiable control structure.

## 7. Complete page composition

Use the same two-tone visual system and controls as the Studio, but a diagram-first composition. Do not reuse a dashboard with state cards stacked vertically.

```text
Project / variant     Feature / nested path        Validate & apply   Open graph
State Machine Designer                         Saved / pending changes

STATE EXPLORER        | MACHINE CANVAS                         | SELECTED STATE
Current round         | Start / paid entry                     | Name
Feature definitions   |      ↓                                 | Ordered actions
Reusable machines     | [Game action]                          | Variable bindings
Search                |      ↓                                 | Outgoing transitions
                      | <condition> → [Feature state]           | Guard + effects
Add state             |                  ↺                     | Priority / destination
Add action            |             Finish boundary            | Source / uses

                      | Validation + proposed graph changes drawer
```

States must show a meaningful action summary and termination/entry role at a readable default zoom. Transition labels show actual guards, or a human-readable expression with source details. Nested features have breadcrumbs. Low-level evaluator atoms stay inside expandable operations.

Selecting an edge opens its guard/effect editor; selecting a state opens ordered actions. Adding a transition offers compatible targets and explicit fallback/priority. Keyboard users can select states, edit actions and reorder transitions in an equivalent table without dragging. Expose a transition table as an alternative view of the SAME graph-backed machine, not another independent model.

Auto-layout runs on first import or explicit request; it must not fight manual placement on every keystroke. Preserve diagram pan/zoom and layout as non-executable metadata. Deleting a referenced state requires rerouting or removing its transitions in one validated change.

No live animation is required. A backend test execution may highlight retained source evidence when available, but do not invent a full trace debugger as a dependency for this release.

## 8. Exact semantic proofs and behavior tests

Before rendering the full UI, test the pure lowering/projection:

- Deterministic two-state sequence: action A once, action B once, terminal action once, one final payout.
- Priority guards: two true guards select only the first; two normal outgoing graph edges are never substituted for this choice.
- Self-transition: one visit/action per step, correct counter and limit semantics.
- Guard/effect ordering: a guard observes post-entry state; effects update sequentially; next state does not execute in the same dispatcher pass.
- Weighted choice: a small independently computed distribution matches exact execution; one chance selection is not redrawn on each guard.
- Nested/reused machine instances: private control variables do not collide; explicitly shared game state stays shared.
- Natural stop and model-limit exhaustion remain distinct, including off-by-one boundaries.
- Imported no-op projections preserve graph identity, seeded result, round state, cap behavior and diagnostic fields.
- Graph edits while the designer is open invalidate stale bindings; returning to the designer cannot undo those edits.
- Custom/opaque action survives export/import and remains part of actual execution.

Then demonstrate an actual edit-and-run journey for each family in section 6, as well as a newly created reusable machine that runs from exported raw GraphConfig. A rendered arrow or a saved UI metadata document is not acceptance evidence.
