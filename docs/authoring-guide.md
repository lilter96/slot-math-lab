# Slot Math Lab — Authoring Guide

This guide covers the three levels of authoring flexibility:

- **Level (a)** — Declarative subgraph config
- **Level (b)** — Typed expressions (fold/map/filter)
- **Level (c)** — Plugin escape hatch

---

## Level (a) — Subgraph Config

The simplest path: drop catalog subgraphs from the palette and wire them together
with Draw/State/Loop primitives. Configure them with the table editors.

### Building a basic lines game

1. Open the **Build** tab.
2. From the palette, drag a **Draw** node onto the canvas. This draws the reel
   symbols. Add draw weights in the Inspector (e.g. A:5, B:3, W:1).
3. Drag a **Lines** catalog subgraph from the Standard Catalog palette section.
4. Wire the Draw output → Lines input.
5. Drag a **Metrics Sink** and wire Lines output → Sink.
6. Open **Tables → Symbols** and add your symbols (A, B, W, Scatter).
7. Open **Tables → Paytable** and enter payouts for each symbol × count combination.
8. Watch the live RTP badge update as you edit weights.

### Adding a scatter trigger → free spins (loop)

1. Drag a **Scatter** catalog subgraph. Wire the same Draw output → Scatter.
2. Drag a **Loop** node. Set the termination condition: `state["freeSpins"] <= 0`.
3. Wire the Scatter trigger output → Loop input.
4. Inside the Loop body, add another Draw + Lines subgraph.
5. The Loop tracks free-spins count in user state. Use a **State (Modify)** node to
   decrement `state["freeSpins"]` on each iteration.

---

## Level (b) — Typed Expressions

Expressions go on **expression-valued ports**: draw weights, payout multipliers,
trigger predicates, and state-array transforms.

### Syntax reference

```
# Arithmetic
state["multiplier"] * 2 + 1

# Conditional
state["bonusActive"] ? state["mult"] * 2 : state["mult"]

# State-array aggregations
sum(state["cells"], c => c > 0)          # sum of positive cells
count(state["cells"], c => c == 3)       # count of symbol 3
product(state["multipliers"], m => m)    # product of all multipliers

# fold (general left-fold over array)
fold(state["paylines"], 0, (acc, line) =>
  acc + (state["cells"][line[0]] == state["cells"][line[1]] ? 10 : 0))

# map (transform each element)
map(state["cells"], c => c == 0 ? 1 : c)

# filter (keep elements matching predicate)
filter(state["cells"], c => c > 0)
```

### Writing a fold-driven line scan

Instead of using the `Lines` catalog subgraph, you can implement line-scanning
yourself with a fold expression on a `Map` node:

```
# Expression on Map node "win" port:
fold(paylines, 0, (acc, line) =>
  acc + (cells[line[0]] == cells[line[1]] && cells[line[1]] == cells[line[2]]
         ? paytable[cells[line[0]]] : 0))
```

where `paylines` and `cells` are arrays in the game state.

### Restrictions

- No unbounded loops or recursion.
- `fold`/`map`/`filter` are bounded by the array size (fixed at authoring time).
- Maximum one level of nesting — no `fold` inside a `fold`.

These restrictions ensure the expression is analysable by the exact interpreter in
finite time.

### Expression editor features

The CodeMirror 6 expression editor (on expression ports in the Inspector panel)
provides:
- Autocomplete for `state["key"]` field names
- Autocomplete for `fold`, `map`, `filter`, `sum`, `count`, `product`
- Live type-checking (errors match the backend type-checker exactly)
- Errors linked to the offending node

---

## Building a custom mechanic as a subgraph

A **custom mechanic** is a named, reusable subgraph composed from primitives +
catalog subgraphs + expressions — **zero C# code**.

### Example: Rising Multiplier

This mechanic increments a multiplier each cascade tumble and applies it to wins.

1. On the **AI** tab in the right panel, click **"Compose subgraph"**.
2. Add a **Loop** node with condition `no_new_wins(state["board"])`.
3. Inside the loop: **Draw** (refill) → **State (Modify)** to increment
   `state["multiplier"]` → **Map** node with expression:
   `win * state["multiplier"]`.
4. Click **"Save as mechanic"** and name it `rising-multiplier`.
5. It now appears in your **Custom** palette section and can be dragged onto any
   graph like any catalog entry.

---

## Level (c) — Plugin Escape Hatch

For spatial evaluators or exotic logic that can't be expressed with fold/map:

### Creating a plugin

Implement `IEvaluator` or `ITransform`:

```csharp
using SlotMath.Core.Plugins;

[SlotPlugin("my-evaluator")]
public class MyEvaluator : IEvaluator
{
    public IReadOnlyList<Win> Evaluate(
        IReadOnlyDictionary<string, object?> state,
        PluginCallContext ctx)
    {
        // Pure function — no I/O, no shared mutable state
        var cells = (IReadOnlyList<int>)state["cells"]!;
        // ... compute wins
        return wins;
    }
}
```

Compile to a .NET 10 class library (no executable).

### Registering a plugin

1. Go to **Build → Mechanics → Plugins**.
2. Click **Register plugin** and upload the .dll.
3. The conformance harness runs automatically:
   - Purity check (determinism: same inputs → same outputs on 10 runs)
   - No-I/O check (file access, network, env vars all blocked)
   - Time/memory cap (100 ms / 64 MB per call)
4. If conformance passes, the plugin appears in the Evaluator node dropdown.

### Plugin governance

A non-conformant plugin is **blocked** with a specific failure reason.
Any graph using a plugin is **permanently sampled-regime** — the engine
never claims `Exact` for it.

### Sandbox

Plugins execute in an isolated `AssemblyLoadContext` with:
- No file I/O
- No network
- No environment variable access
- 100 ms CPU time cap
- 64 MB memory cap

---

## Auto-Tune (G27)

Use the **AI → Auto-Tune** panel to converge draw weights to a target RTP:

1. Build your graph.
2. Set **Target RTP** (e.g. 0.96).
3. Set **Max iterations** (e.g. 200).
4. Click **Tune**.

The optimizer runs a coordinate random search over DrawNode weights, evaluating
each candidate with a fast sampled run (5,000 spins). It converges when
|achieved − target| < 0.1pp.

---

## Compliance Lint (G28)

The **Results → Compliance Lint** panel runs rule-based checks:

| Rule | Fires when |
|------|-----------|
| `rtp-out-of-band` | RTP < 80% or > 100% |
| `missing-sink` | No MetricsSink node |
| `dead-symbol` | Symbol in paytable but never in any draw weight |
| `paytable-anomaly` | All payouts for a symbol are zero |
| `max-win-too-low` | Multiplier nodes present but max draw value ≤ 100 |

Each issue links to the offending node id when available.

---

## AI Explain (G28)

The **Results → AI Explain** button sends the current RTP, hit frequency, and
volatility to Claude and returns a plain-language analysis of the game's player
experience. It only cites numbers you provide — no invented figures.
