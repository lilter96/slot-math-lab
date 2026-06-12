# Slot Math Lab — Architecture

## Overview

Slot Math Lab is a no-code, node-based constructor for iGaming slot math. It lets
designers and mathematicians build arbitrary game mechanics on a visual graph and
receive **exact RTP / hit-frequency / volatility / max-win** computed analytically —
plus a Monte Carlo sampling fallback — live, per edge.

---

## Substrate: the free monad

Every game is a **free monad** over a small effect algebra:

```
Draw(weights)          — weighted choice; weights may depend on state
GetState / PutState    — read/write arbitrary user-defined state
Modify                 — atomic state transformation
Loop                   — fixpoint with a state-dependent stop condition
Pure                   — lift a value
```

The substrate is **generic over the user's state type `S`**. The engine hardcodes
no "board", "multiplier", "free-spins counter", or any other game concept. Those
live in user-defined state.

```csharp
// A simple 3-outcome draw (the whole game)
var program =
    from idx in Slot.Draw<MyState>(_ =>
        WeightSet.FromIntegers([5, 3, 2]))
    select idx switch {
        0 => BigInteger.Zero,
        1 => new BigInteger(10),
        2 => new BigInteger(50),
        _ => BigInteger.Zero,
    };
```

---

## Two interpreters, one program

### Exact interpreter (`ExactInterpreter`)

`Exact : Slot<S,T> → Dist<T>`

- Folds every branch into a rational probability distribution
- Probabilities are `BigInteger` numerator / denominator — no floats
- Memoised on a canonical hash of the **recurrence state** (win accumulator excluded)
- ε-pruned with a reported `[lo, hi]` RTP interval
- Stack-safe (trampolined)

### Sampled interpreter (`SampledInterpreter`)

Monte Carlo with:
- Welford streaming mean/variance
- Running stdErr + CI95
- Win histogram and tail tracking
- Cancellation token support
- Deterministic (fixed seed → identical stats)

---

## Regime detection and hybrid evaluation (`HybridEvaluator`)

A budget layer (`RegimeConfig`) picks the cheapest strategy per subgraph:

| Condition | Strategy |
|-----------|----------|
| Branch count ≤ budget | `Exact` |
| Contains a plugin | `Sampled` (forced) |
| Too expensive | `Sampled` |
| Mixed graph | Hybrid (exact base + sampled feature) |

Every metric value carries **provenance** (`Exact`, `ExactWithinEpsilon`, `Sampled`).

---

## Mechanic layer

### Level (a) — subgraph config

Named subgraphs stored as JSON. A subgraph is composed from substrate atoms
(`Draw`, `State`, `Loop`, `Branch`, `Map`) and bounded expressions. The compiler
inlines subgraph references at compile time. The catalog ships 6 subgraphs:
**lines, ways, scatter, cascade, sticky-wild, hold-and-win**.

### Level (b) — expression language

A small DSL on expression-valued ports:
```
arithmetic    +  -  *  /
boolean       &&  ||  !
comparison    ==  !=  <  >  <=  >=
conditional   expr ? then : else
aggregations  sum(arr, pred)  count(arr, pred)  product(arr, pred)
fold          fold(arr, init, (acc, x) => expr)
map           map(arr, x => expr)
filter        filter(arr, x => pred)
field access  state["key"]  state["key"][idx]
```

No unbounded loops. Evaluates in exact rationals on the exact path.

### Level (c) — plugin escape hatch

Compiled .NET assemblies implementing `IEvaluator` / `ITransform`. Loaded in an
isolated `AssemblyLoadContext`, no I/O, time + memory caps. A graph using a plugin
is **always sampled-regime**.

---

## API surface (`SlotMath.Api`)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/validate` | Validate a graph config |
| POST | `/api/evaluate/light` | Fast metrics (exact or sampled estimate) |
| POST | `/api/runs` | Start a heavy Hangfire run |
| GET | `/hubs/runs` | SignalR streaming hub |
| GET/POST | `/api/configs` | In-memory config store |
| POST | `/api/persisted/{id}/configs` | Persistent save (auth required) |
| GET | `/api/persisted/{id}/configs/latest` | Load latest (auth required) |
| POST | `/api/ai/generate-graph` | NL → graph (G26) |
| POST | `/api/ai/auto-tune` | Inverse design (G27) |
| POST | `/api/ai/lint` | Compliance lint (G28) |
| POST | `/api/ai/explain` | AI explain (G28) |
| POST | `/api/auth/token` | Dev JWT token |

---

## Data flow

```
User edits graph (canvas)
  → store changes
  → useLiveMetrics (debounced 400ms)
  → POST /api/evaluate/light
  → HybridEvaluator
  → MetricReport { rtp, hitFrequency, volatility, provenance }
  → MetricStrip badges
```

Heavy run:
```
POST /api/runs
  → Hangfire job (RunJobService)
  → SampledInterpreter (cancellable)
  → SignalR streaming (sampleCount, runningRtp, stdErr)
  → result persisted + cached
```

---

## Persistence

EF Core 10 + PostgreSQL for `Projects`, `ConfigVersions`, `Runs`, `Users`, `Plugins`.
Redis result cache keyed by canonical config hash (SHA-256 of normalized JSON).

---

## Frontend

React 19 + TypeScript strict. Tabs: **Build** (canvas + palette + inspector + tables
+ mechanics + AI) | **Simulate** | **Results** | **Export**.

Canvas: `@xyflow/react` with drag-from-palette, typed edge validation, inline
compiler messages.

Expression editor: CodeMirror 6 with autocomplete for state fields and fold/map
syntax, live type-checking mirroring the backend.
