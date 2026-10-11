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

### Structured exact evaluator (`StructuredExactEvaluator`)

Enumeration visits every outcome, so its cost is the product of the draws' sizes. A five-reel game with a
feature of dependent free spins has more outcomes than can be visited, yet its exact RTP is a small
computation once the structure of the graph is used. The structured evaluator reads the same compiled
graph and returns the same exact rationals without enumerating:

1. **Symbolic execution.** The graph is run once over terms instead of values. A draw becomes a random
   variable, a state field becomes a term over the variables it depends on, a short `Loop` is unrolled
   up to its cap and a long one is solved as a chain (below). Terms are hash-consed into a DAG, and a
   function of a single variable is kept as a table.
2. **Independence.** A sum whose parts share no variable is the convolution of their laws
   (decomposability); an expectation is taken by linearity over the parts and never needs the law of
   the payout itself.
3. **Lumping.** A sub-term that carries everything random about its parent is replaced by one
   pseudo-variable with that sub-term's law, so only the law's distinct values are pushed further.
4. **Symmetry.** Pseudo-variables are named by their law, so sub-models with one law — twenty paylines
   over the same reels, every free spin with the same sticky pattern — are computed once.
5. **Conditioning.** What is left is expanded on the first variable it reads, and the residual terms,
   again hash-consed, are solved recursively and mixed.

Hit frequency is derived the same way without the payout distribution: "the amount is positive" is
rewritten structurally (a sum of non-negative parts is positive when one part is, a product when both
factors are) into a Boolean term, whose probability is then inferred. The second moment, and with it
the variance, is taken by bilinearity: E[(Σaᵢ)(Σbⱼ)] = ΣΣ E[aᵢbⱼ], where a pair of parts that share no
variable is the product of two expectations.

**Loops as Markov reward chains.** Unrolling a loop costs its cap times its body, and a retrigger loop
with a cap of 1,000 (D6) is out of reach that way. A loop whose cap is above `ChainLoopsAbove` (32) is
executed once, symbolically, on a state of *parameters*, which gives the next state, the stop condition
and what an iteration adds to each amount as terms over the parameters and the iteration's own draws.
From that one pass:

- every field the body changes is classified: an **amount** it only adds to and nothing in the loop
  reads (the win), part of the **chain state** (whatever the stop condition, the next state or an
  amount's increment reads, to a fixpoint), or dead. Liveness after the loop is read off the graph, so
  a spin counter that only steers the loop does not tell its exits apart;
- everything random about the next state is the joint law of the largest parameter-free sub-terms, the
  **transition classes**; given the state, the kernel p(z), and the increments' E[δ; z], E[δδ′; z] and
  P(δ = 0; z), are computed by the inference above, once per state the chain reaches;
- the capped loop is then exactly `cap` steps of the transfer recurrences over the reachable states, in
  integers over one running denominator: m₀′ = m₀·p, m₁′ = m₁·p + m₀·r₁, m₂′ = m₂·p + m₁r₁ + r₁m₁ + m₀·r₂,
  and the probability of having added nothing. Mass that stops leaves through an exit class (state at
  exit, exit reason); what is still running after the last step leaves as `modelLimit`.

Downstream the loop is one lumped variable over its exit classes per entry state. An amount is a term
with a mean, second moments and a zero-probability per exit class and no law: its expectation is the
mean (tower property, which is Wald's identity for a random number of iid spins), its square goes
through the second-moment table. With the amounts moved into the chain state instead the loop keeps
its full law; that is tried first on a small allowance, and it is what gives the payout distribution.
A loop that is not such a chain (too many transition classes, a state that does not settle into one
shape) is unrolled as before. The exact expected number of cap hits is reported per loop.

A chain is solved exactly while it is cheap. Once its numbers pass `PruneLoopsAboveBits` (16,384 bits)
or a run has spent `PruneLoopsAboveWork` (64M units; the D9 fixtures at their caps stay under both), the
run is ended as soon as the probability that the loop is still running is below `PruneLoopMassBelow`
(10⁻³⁰), as if the cap were there. Only mass that would go on is cut: states that stop at that step
leave through their exits as usual, and the pruned mass of a loop entered with probability p counts p
times. The result is then `ExactInterval` with `boundSource = DeclaredWinCap` (D5): a pruned round pays
between nothing and the win cap in the game and in the shortened game alike, so the RTP lies within
pruned mass × cap of the value computed, and a probability within the pruned mass. Statements in
iterations past the cut are not checked for failing. Setting `PruneLoopMassBelow = null` never prunes.

**Reel strips and authored payouts.** A reel-strip draw is one uniform variable per reel,
and every cell is a table over its reel's stop. Payouts are composed in editable graphs from
atoms and typed expressions. Generic sums, products, conditionals and dependency sets
provide factoring; the structured evaluator does not recognize evaluator classes, mechanic
names, or authored graph patterns. The equivalence fixtures compose line and ways payouts
in test-owned graphs and compare them with independent probability formulas.

A named opaque map has no expression semantics for this evaluator to model. It declines
before execution, including legacy catalog maps. The caller retains the classic exact/hybrid
path and its budget/provenance behavior. There is no `Op.Native` or special line/ways split.

The evaluator never approximates silently. Plugins (D5: a result with a plugin is never `Exact`),
opaque transforms, wins stored as state data, a
statement that can fail on a reachable outcome, or an exhausted work budget make it **decline**, and
the caller keeps what it had. A result is `Exact`, or `ExactInterval` with its pruned
mass when a loop was cut short. When the payout distribution is too large to build, RTP is still
exact, hit frequency and variance are exact when their own derivations fit their allowances, and the
maximum win is reported as absent.

What it does not do: a win cap that is active (the proven maximum exceeds it) needs the payout
distribution, because E[min(W, cap)] is not a function of moments; a loop whose state is large and does
not factor (a board of sticky symbols carried as one chain state) is neither a small chain nor cheap
to unroll; a chain over a counter that grows with the cap costs cap² states·steps and its exact
numbers cap·log₂(denominator) bits, which is why the largest caps end in an enclosure; `cluster` reads
the whole board at once, so it costs the product of the reels like enumeration does.

The approach follows weighted model counting for discrete probabilistic programs (Holtzen, Van den
Broeck, Millstein, *Scaling Exact Inference for Discrete Probabilistic Programs*, OOPSLA 2020),
module detection in fault trees (Dutuit, Rauzy, IEEE Trans. Reliability 45(3), 1996) and state-space
lumping (Derisavi, Hermanns, Sanders, IPL 87(6), 2003). Loops follow exact inference for loopy
programs by generating functions (Klinkenberg et al., OOPSLA 2024; Zaiser et al., NeurIPS 2023), the
moment recurrences of probabilistic loops (Moosbrugger, TU Wien 2024, and the Polar analyzer) and the
transient analysis of Markov reward models in exact rational arithmetic as in Storm's exact engines.

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
| Branch count ≤ budget | `Exact` by enumeration |
| Branch count > budget, graph modelled exactly | `Exact` by structured evaluation |
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
