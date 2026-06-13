# Slot Math Lab — Product & Build Spec (PRD) — v3.1

**This is a build contract for an agentic coding system.** It specifies **goals** and
**definitions of done** only — no implementation code. The agent writes all code, and each
goal's Definition of Done (DoD) is its acceptance gate.

A no-code, node-based constructor for iGaming slot math. Designers and mathematicians build
symbols, reels, paytables and **arbitrary chained mechanics** on a visual graph; the system
returns **exact** RTP / hit-frequency / volatility / max-win (with a sampling fallback), live,
per edge — plus AI for natural-language graph generation and inverse design (auto-tuning to a
target RTP).

**The flexibility thesis (the whole point).** The engine is a *tiny universal substrate*; every
mechanic — including ones nobody has shipped yet — is **composed** on top, and the mechanic
catalog is an **open set of named subgraphs**, never a fixed enum. Lines, ways, cluster, scatter,
Megaways, cascades, free spins, Hold & Win, walking/expanding wilds, collection, jackpots —
all of these are *subgraph definitions built from substrate atoms + expressions*, not engine
features and not C# classes. Designers extend the catalog with **zero C# code** — a new
mechanic is a new subgraph definition.

---

## 0. Agent execution protocol

- Execute goals **in order within a phase**. Phases are sequential.
- Each goal's **DoD is a hard gate.** Do not start the next goal until every DoD item is
  objectively satisfied (tests green, endpoints returning the specified results, tolerances met).
- **One commit per goal**, message `feat: G<N> <title>`. CI must be green at every commit on `main`.
- On completing a goal, append one line to `PROGRESS.md`: goal id, commit hash, and the key
  measured numbers (e.g. computed fixture RTPs, perf figures) in the strict format:
  `| G<n> | <commit-hash-short> | <key numbers> |`
- If a DoD item cannot be met, **stop and report** the blocker — never silently descope.
- The **Non-negotiable invariants** below are always-on; violating one fails the goal even if its
  DoD otherwise passes.
- The **Definitions (D1–D25)** are normative. Constants defined there are the contract; changing
  one requires editing this PRD (stop and report, do not quietly adjust a tolerance to make a
  test pass).
- **All randomized tests use pinned seeds** (checked into the repo) so CI is deterministic, and
  every statistical harness ships a **negative control** — a deliberately corrupted input the
  test must reject (D8). A statistical test that cannot fail proves nothing.
- **"Hand-computed" is defined** as: derived independently of the kernel — either closed-form
  algebra documented in the test, or a standalone brute-force enumerator in its own file that
  imports nothing from `SlotMath.Core`. The kernel must never be its own oracle.
- **Performance DoDs** run in a dedicated benchmark job against the fixture set, with thresholds
  carrying ≥ 2× headroom over the measured dev baseline (record the baseline in `PROGRESS.md`).
  A perf gate is part of the goal's DoD, but lives outside the per-commit unit-test job to keep
  CI fast and non-flaky.

---

## Architecture on one screen

- **Substrate.** A game is a **free monad over a small effect algebra**:
  `Draw(weights)` (weighted choice; weights may depend on state), `Emit(label, amount)`
  (labeled win emission into the accumulator), `GetState / PutState / Modify` (arbitrary,
  user-defined game state), and `Loop` (fixpoint with a state-dependent stop condition **and a
  mandatory iteration cap**, D6). Branch is sugar over bind. The substrate is **generic over the
  user's state type `S`** — the engine hardcodes no "multiplier", "free spins", "board", or any
  other game concept.
- **Wins are labeled, state is not wins.** `Emit` is the only way value enters the accumulator.
  The accumulator never enters the memoization key (invariant 4); the recurrence state never
  holds payout totals. The exact interpreter produces the **full distribution of total win**
  (for RTP, volatility, histogram, max-win) plus an **exact expected value per label** (per-feature
  RTP) — expectations are linear, so per-label contributions are exact and cheap without tracking
  the joint per-label distribution (D13).
- **Two interpreters, one program.** `Exact` is the natural transformation `Slot → Dist`: it
  folds every branch into a distribution with **exact rational** probabilities, memoized into a
  DAG and ε-pruned, with truncation at loop caps accounted into a hybrid provenance model
  (D5, D6, D13). `Sampled` is a stack-safe Monte Carlo interpreter with streaming statistics and
  **deterministic parallelism** (D3). A **regime/budget** layer picks exact, sampled, or hybrid
  per subgraph and tags every metric with **provenance**.
- **Recurrent loops.** A `Loop` may revisit a previous recurrence state (e.g. free-spin
  retriggers). v3.1 policy is bounded unrolling under the iteration cap with the residual
  mass accounted in provenance; an exact fixpoint solve (rational linear system over recurrent
  states) is explicitly **deferred** (D6).
- **Mechanic layer = atoms + expressions + named subgraphs.**
  Mechanics are **composed**, never coded into the engine. The catalog is a set of **named
  subgraphs** stored as data (JSON) — built entirely from substrate atoms (`Draw`, `Emit`,
  `State`, `Loop`, `Branch`, `Map`) and bounded fold/map expressions. There are **no C# molecule
  classes** in the standard library. `IEvaluator` / `ITransform` exist as **plugin contracts
  only** (level c, the escape hatch) — not as a standard library interface to implement.
  Optional C# **fast-paths** (lines, ways, cluster) may exist for performance, paired with
  equivalence tests proving they match the canonical subgraph (D25).
- **Game state is fully user-defined.** The engine has no `Board` type. A "board" (reel grid,
  tile field, etc.) is just a user-defined array in the state `S`. State shape is expressed
  through data tables and subgraph wiring.

## Authoring capability model — the a + b + c flexibility contract

Capability lives on the **leaves**, as a dial, not as three separate products:

- **Level (a) — subgraph config.** A **named subgraph** from the catalog, parameterised by
  finite **tables / constants** (paytable, payline set, reel strips, thresholds, stepped
  multiplier tables). The subgraph is **data** (JSON) — not a C# class. Fully declarative.
  Covers the majority of real games; the simple path stays simple.
- **Level (b) — typed expression.** A small, **pure, deterministic, terminating** expression on
  specific node ports: `Draw` weights, multiplier/payout values, trigger predicates, payout
  adjustments, state-array transforms. Grammar: arithmetic, boolean, comparison, conditional,
  **state-array aggregations** (`sum / product / count / min / max` with a predicate), **bounded
  `fold / map / filter`** over state arrays (one level; no nested fold; bounded by array size →
  stays exact-analysable), and field/index access over state. **No unbounded loops or recursion.**
  Partial operations (division by zero, out-of-range index, `min`/`max` of an empty array) yield
  **deterministic, located evaluation errors** — never undefined behavior, never a silent value
  (D1, D19). Amount-typed expressions evaluate in **exact rationals** on the exact path. Compiles to
  the same internal function type that plugins produce.
- **Level (c) — typed plugin escape hatch.** A compiled, **pure** plugin implementing
  `IEvaluator : State → Wins` / `ITransform : State → State` / a weight source — these are the
  **only** uses of these interfaces; the standard library ships none. **Sandboxed** (no I/O, time
  + memory caps, isolated load context), server-side, trusted, and **test-gated**. Plugin
  registration is an **operator/admin action — there is no public plugin upload.** A game
  that uses a plugin is **flagged sampled-regime** (the engine never claims `Exact` for it). The
  rare path, reserved for brand-new spatial evaluators or exotic logic unrepresentable in
  expressions.
- **Custom mechanic** = a **named, reusable subgraph** composed from primitives + catalog +
  expressions (pure no-code). This is how the catalog grows without touching C# code.

---

## Non-negotiable invariants

1. **Exact path stays exact.** Probabilities and amounts on the exact path are rational
   (`BigInteger` numerator over a positive denominator, **always in lowest terms, canonical
   zero = 0/1**, D1); floats are display-only and never derive the RTP ratio. Level-(b)
   expressions on the exact path evaluate in rationals.
2. **No hardcoded mechanic molecules.** Mechanics are subgraphs (data, not C# classes). `IEvaluator`
   and `ITransform` are plugin contracts (level c) only — the standard library ships zero
   implementations of these interfaces. Adding a catalog mechanic requires zero C# code and zero
   interpreter/compiler changes.
3. **Layered authoring holds.** Level (a) is declarative subgraph config; level (b) expressions
   are pure, deterministic, terminating, and loop-free (except bounded fold/map over fixed-size
   arrays), with partial operations producing deterministic located errors; level (c) plugins are
   pure, sandboxed and test-gated, and force the sampled regime.
4. **Generic state, no engine types.** The recurrence state (everything that affects the future
   distribution) is the memoization key; the win accumulator is held separate and combined by
   convolution. **`Emit` is the syntactic carrier of this split** — value reaches the
   accumulator only through `Emit`, and `Emit` never mutates recurrence state. State is fully
   user-defined — the engine has **no `Board` type**; a "board" is just a user-defined array in `S` (D11).
5. **Every metric carries provenance**: `Exact`, `ExactInterval { lo, hi, prunedMass, boundSource }`,
   `ExactWithMassLoss { prunedMass }` (marked non-regulatory), or `Sampled { n, stdErr, ci95, capHits, loopCapHits }`
   (taxonomy in D5). No silent mixing.
6. **Determinism.** A given seed yields an identical sample sequence and identical stats anywhere
   — **including under parallel sampling and regardless of thread count** (fixed stream
   splitting, fixed chunking, fixed merge order, D3). The PRNG algorithm is pinned (D3).
7. **Tables, not nodes, for data.** Symbols, paytable, reel strips, and state structure config
   are tables, never graph nodes.
8. **Loops only via explicit `Loop`/fixpoint nodes, and every `Loop` carries an iteration cap.**
   The graph is otherwise acyclic. Caps are game semantics (D6); cap hits are accounted in
   provenance. Bounded fold/map/filter in expressions are not graph loops — they operate over a
   fixed-size array in a single evaluation step.
9. **No secrets client-side.** The AI key and plugin execution live only on the backend.
10. **The kernel is pure.** No I/O, no framework, no shared mutation. Everything else is the shell.
11. **Fast-paths are proven equivalent.** Any optional C# performance implementation of a catalog
    subgraph must be accompanied by a passing equivalence test against the canonical subgraph.
    Equivalency must be tested via **Property-Based Testing (PBT)** over at least 1,000 to 10,000+
    cases with automated shrinking (D25).
12. **Statistical gates are deterministic and falsifiable.** Every statistical assertion runs on
    pinned seeds, uses a tolerance with a documented theoretical false-fail probability ≤ 10⁻³
    per suite (had the seeds been random), and is paired with a negative control that must fail
    on corrupted input (D8).

---

## Tech stack

**Backend (.NET 10)** — C# math kernel `SlotMath.Core` (substrate + interpreters + the level-(b)
expression compiler/evaluator including fold/map/filter, all pure); the **standard mechanic
catalog** (curated subgraph definitions as JSON data, no C# molecule classes); a sandboxed plugin
host (isolated `AssemblyLoadContext`, no I/O, time/memory limits) for level-(c) plugins only.
C# host `SlotMath.Api` (Minimal APIs + Vertical Slice). EF Core 10 + Npgsql + PostgreSQL; Redis
(result cache by config hash, job coordination); Hangfire (heavy runs); SignalR (streaming);
built-in OpenAPI. Tests: xUnit + property-based (FsCheck or CsCheck) + **Testcontainers** (real
Postgres/Redis in integration tests) + **BenchmarkDotNet** (perf harness).

**Frontend** — React 19 + TypeScript (strict), Vite; `@xyflow/react` (canvas, confirm latest
major at scaffold); Zustand + TanStack Query; **CodeMirror 6** for the typed expression editor
(lint + autocomplete over state fields + fold/map syntax); uPlot (streaming convergence) + visx
(histograms); Zod + `openapi-typescript` (generated types) + a thin typed client.

**Infra** — Docker (multi-stage), docker-compose (api + postgres + redis); GitHub Actions CI/CD;
deploy backend + Postgres + Redis on Railway, frontend on Vercel (or Railway).

---

## Definitions (normative)

### D1 — Numbers, canonical rational form, expression partiality

- A **rational** is a `BigInteger` numerator over a `BigInteger` denominator, always reduced to
  lowest terms with denominator > 0; canonical zero is `0/1`. All exact-path probabilities and
  amounts are rationals in this form.
- **Display floats** are derived at the edge only (round-half-even, 6 significant digits by
  default) and never re-enter any computation.
- **Sampled path** uses IEEE-754 `double` for amounts. **Integer-valued comparisons stay exact on
  both paths** (integers are exactly representable in `double` up to 2⁵³), so predicates over
  counts, indices, and integer state never diverge between interpreters. Fixture predicates are
  integer-based by construction. Predicates over fractional amounts on the sampled path may, in
  principle, flip near a threshold relative to the exact path; the cross-check tolerance (D8)
  absorbs this, and the authoring guide recommends integer thresholds.
- **Partial operations** in level-(b) expressions — division/modulo by zero, array index out of
  range, `min`/`max` over an empty array — produce a **deterministic evaluation error located to
  the expression node**, on both paths. `sum`, `product`, `count` over an empty array return
  their identities (0, 1, 0). `fold` is total (it has an explicit init) (D19).

### D2 — Canonical serialization & config hash

- **Canonical JSON**: UTF-8; object keys sorted by ordinal; no insignificant whitespace; rationals
  serialized as the canonical string `"n/d"`; arrays are order-significant; no floats appear in
  hashed content.
- `configHash` = SHA-256 over the canonical JSON of the **fully resolved** config:
  - **Subgraph references resolve content-addressed**: each subgraph definition has a
    `subgraphHash` (SHA-256 of its own canonical JSON), and the resolved config embeds
    references by hash (D22). Editing a catalog entry therefore never silently changes the meaning of
    an existing config — the config pins the exact subgraph content it was built with.
  - **Plugin references** resolve as `(pluginId, version, binaryHash)`.
- Identical `configHash` ⇒ cache hit ⇒ byte-identical metrics (G16).
- **Export embeds the resolved subgraph definitions** (G25), so an exported file is
  self-contained and never depends on the catalog state of the importing instance.

### D3 — PRNG & parallel determinism

- The PRNG algorithm is pinned: **xoshiro256\*\*** seeded via SplitMix64. Record the exact
  constants and seeding procedure in the README at G3; pinned thereafter.
- **Stream splitting**: stream *i* is seeded as `SplitMix64(masterSeed, i)`.
- **Parallel Monte Carlo**: work is divided into fixed chunks of `CHUNK = 65,536` rounds; chunk
  *i* runs on stream *i*; chunk results (Welford state, histogram, tail counters) are merged in
  ascending chunk index. Result: bit-identical statistics for a given `(seed, n)` regardless of
  thread count or scheduling.

### D4 — Metric definitions

- A **round** = one top-level program execution (one bet). Bet is normalized to 1 unless stated.
- **RTP** = E[total round net win] / bet — an exact rational on the exact path (D13).
- **Hit frequency** = P(total round net win > 0) (D13).
- **Volatility** = variance and standard deviation of total round net win in bet units (exact from
  the full distribution on the exact path); report variance, std, and std/bet.
- **Max win**: the win cap applies to the round total at the metrics sink; report P(cap reached)
  and, when finite, the pre-cap theoretical maximum.
- **Per-feature RTP** = the expected value of each `Emit` label. By linearity of expectation,
  per-label expected values are exact and **sum exactly to total RTP** on the exact path.

### D5 — Provenance taxonomy

- `Exact` — no pruning, no truncation mass, no plugins; the value is the exact rational for the
  (capped) game.
- `ExactInterval { lo, hi, prunedMass, boundSource }` — ε-pruning and/or loop-cap truncation occurred;
  `[lo, hi]` is a guaranteed enclosure of the exact capped-game value, with width bounded by
  `prunedMass` × `declared win cap` ($W_{cap}$) (D6, D18). `boundSource` declares
  the provenance of the bound (e.g. `ProvenMaxWin`, `DeclaredWinCap`, `UserCap`).
- `ExactWithMassLoss { prunedMass }` — exact calculation with mass loss where the max remaining win
  cannot be proven or bounded. Marked as non-regulatory: *cannot be used as a regulatory certification
  result without additional review*.
- `Sampled { n, mean, stdErr, ci95, capHits, loopCapHits }`.
- **Aggregation rule**: if any contributing component is `Sampled`, the aggregate is `Sampled`;
  else if any component carries un-bounded mass loss, the aggregate is `ExactWithMassLoss`;
  else if any component carries bounded pruned/truncated mass, the aggregate is `ExactInterval`;
  else `Exact`. No silent mixing.

### D6 — Loop policy, caps, and recurrence

- **Every `Loop` has an iteration cap**: author-set, defaulting to `LOOP_CAP_DEFAULT = 1,000`,
  hard system maximum `100,000`. **Every game has a round win cap** (author-set; the compiler
  requires one).
- **Caps are game semantics.** The object being measured is the capped game. With caps, every
  game's outcome tree is finite, so "exact at ε = 0" is well-defined for *all* games — including
  retrigger-style recurrent loops.
- **Sampled path**: hitting a loop cap force-exits the loop, increments `loopCapHits`, and the
  round continues; hitting the win cap increments `capHits`.
- **Exact path**: loops unroll under the cap. If the author requests ε-pruning (ε > 0), pruned
  probability mass accumulates into `prunedMass` and the result is an enclosure `[lo, hi]` or
  mass-loss state per D5; computing `hi` requires the finite win bound that the win cap supplies.
- **Recurrent states** (a loop iteration revisits a previous recurrence state — e.g. "free spins
  remaining" returning to a prior value via retrigger): modeled strictly as a **subcritical Galton-Watson branching process**
  with offspring distribution $Z \in \{0, 3\}$ where $P(Z = 3) = \frac{1}{20} = 0.05$ and $P(Z = 0) = 0.95$.
  Offspring mean $m = 3 \times 0.05 = 0.15 < 1$. The probability of exceeding a loop cap of 1,000 is mathematically
  bounded by $\le 10^{-15}$, ensuring exact-path safety under the cap. An exact fixpoint solve is **deferred** (see Deferred).

### D7 — Budgets, "MVP-class", and latency

All constants live in one strongly-typed config file in the repo and are mirrored here; changing
a value is a PRD change.

- **MVP-class config**: ≤ 200 authored graph nodes (≤ 2,000 after subgraph inlining); exact-path
  explored recurrence states ≤ 1,000,000; ≤ 10,000 expression operations per leaf evaluation;
  subgraph nesting depth ≤ 16.
- **`/evaluate/light` policy**: evaluated on standard **GitHub Actions Standard Runner (2 vCPU, 8 GB RAM, Linux)**.
  Runs exact if the estimated state count (D14) ≤ 250,000 and it completes within 150 ms; else a sampled estimate
  at n = 20,000 with CI; else return `needsFullRun`. Latency target: p95 < 300 ms over the fixture set.
- **Circular references:** Circular subgraph references (e.g. Subgraph A -> B -> A) are strictly prohibited and
  must be detected during the inlining/compilation phase, emitting a specific compiler error (G14).
- **Canvas/editor**: no main-thread block > 50 ms on node/edge changes; live-metric refresh
  debounce 300 ms; **stale in-flight evaluations are aborted** when a newer edit supersedes them.

### D8 — Statistical test policy

- All randomized tests run on **pinned seeds** checked into the repo (CI is deterministic).
- Tolerances are chosen so the theoretical false-fail probability (had seeds been random) is
  ≤ 10⁻³ per suite. Concretely for the G13 cross-check (1,000 comparisons): each comparison must
  satisfy |sampledRTP − exactRTP| ≤ 4·stdErr; **at most 2 of 1,000** comparisons may exceed 4σ,
  and **none** may exceed 6σ. (v1's flat 3·stdErr over 1,000 checks would have expected ≈ 2.7
  false failures per run — a flaky gate by construction.)
- **Negative controls**: every statistical harness includes a corrupted case that must fail —
  e.g. the chi-squared harness must reject samples scored against deliberately perturbed weights;
  the cross-check must fail when the exact value is artificially shifted by 1% (invariant 12).

### D9 — Reference fixtures (normative)

The fixtures below are the shared oracle set. They are expressed in the graph schema (G2 corpus),
used by interpreter, metric, API, frontend, and AI goals, and their closed forms are part of this
contract. Pinned seeds: `SEED_MAIN = 0xC0FFEE`; cross-check seed list `0x5EED0001…0x5EED0014`.

- **REF-A "Coin"** — exactness & volatility baseline. One draw per round from
  {A: weight 1, pay 3; B: weight 3, pay 1; C: weight 4, pay 0}, bet 1, win cap 10 (unreachable).
  Closed forms: **RTP = 3/4**; hit frequency = 1/2; E[X²] = 3/2; **Var = 15/16**;
  std = √15⁄4 ≈ 0.9682; max win 3, P(cap) = 0.
- **REF-B "Retrigger"** — the recurrent-loop fixture. A round = one REF-A pay draw labeled
  `base`, then a trigger draw {trigger: weight 1; none: weight 19}. Trigger awards 3 free spins.
  Each free spin = one REF-A pay draw labeled `freespins` + a retrigger draw
  {retrigger (+3 spins): weight 1; none: weight 19}. Loop cap 1,000 total free spins; win cap
  10,000 (both unreachable in practice).
  Closed forms (uncapped): value per free spin including its retrigger descendants
  c = 3/4 + (3/20)·c ⇒ **c = 15/17**; feature contribution = (1/20)·3·c = **9/68**;
  **total RTP = 3/4 + 9/68 = 15/17 ≈ 0.88235**. (The total coincides with c because a base
  round is structurally one spin with the same trigger odds — both derivations are shown so the
  identity is recognized as consistency, not circularity.)
  Capped-game requirement: the exact (ε = 0) value of the capped game is a rational r with
  **15/17 − 10⁻⁹ < r ≤ 15/17** (D9), with provenance `ExactInterval` (the cap is semantics, not
  approximation). Per-label closed forms: `base` = 3/4, `freespins` = 9/68 (up to the same cap deficit).
- **REF-C "MiniCascade"** — bounded-loop & memoization fixture. A 3-cell row drawn i.i.d. from
  {P: weight 2, pay 5; Q: weight 3, pay 2; R: weight 5, pay 0}; three-of-a-kind in P or Q pays
  and triggers a full redraw of the row (cascade); otherwise the round ends. Cascade cap 10;
  win cap 100.
  Closed form (uncapped): v = (8/1000)(5 + v) + (27/1000)(2 + v) ⇒ **v = 94/965 ≈ 0.09741**.
  Requirements: the exact capped-game value equals the **independent brute-force enumerator**
  exactly (rational equality), and |v_capped − 94/965| ≤ 10⁻¹² with v_capped ≤ 94/965 where the
  truncation probability of exceeding 10 cascades is $P(\ge 10 \text{ cascades}) = (35/1000)^{10} \times 1000/965 \approx 2.85 \cdot 10^{-15}$.
  Every win must be emitted through the strict `cascade_win` label: `Emit("cascade_win", amount)`.
  The continuation state after any win depends only on cascades-remaining, so memoization must collapse the naive tree.
- **REF-D "Volcano"** — high-volatility / tail fixture. One draw: pay 5,000 with weight 1,
  pay 0 with weight 9,999. **RTP = 1/2 exactly**; Var = 2,499.75; σ ≈ 50. Used with win cap
  1,000 for cap testing: P(cap reached) = **1/10,000 exactly** on the exact path.
- **REF-E "PluginPassthrough"** — REF-A plus a trivial level-(c) `ITransform` plugin (e.g.
  doubles an inert state counter). Used for plugin conformance, sandbox, and sampled-regime
  provenance tests (G12).

### D10 — Performance harness

- BenchmarkDotNet suite over the fixture set; runner class documented in the README; thresholds
  set with ≥ 2× headroom over the recorded dev baseline. Initial targets: `/evaluate/light`
  p95 < 300 ms over fixtures (D7); REF-C exact evaluation < 2 s; a 1,000,000-round REF-A sampled
  run < 10 s. Perf assertions run in a dedicated CI job (см. протокол).

### D11 — State Hashing, Identity & Canonicalization

- **CanonicalStateIdentity:** State equality is structural. Objects use canonical ordinal key
  ordering; arrays preserve order; enums serialize as string names, not ordinals. Missing fields
  are distinct from explicit null values (`missing != null`). Rationals are serialized exactly
  as their reduced string `"n/d"`.
- **RuntimeStateHash (Memoization):** For exact-path memoization, a zero-allocation structural
  in-memory hash (e.g. MurmurHash3 or xxHash64 over a flattened sorted-key structure) must be
  used. This hash is strictly in-memory and tuned for O(1) performance.
- **ConfigHash (Persistence):** `configHash` and `subgraphHash` are calculated over the fully
  resolved AST canonical UTF-8 JSON.
- **Invariance:** `RuntimeStateHash != ConfigHash` is a normative contract.

### D12 — Exact Interpreter Memoization & Program Point

- **MemoKey:** To prevent invalid state-merging across different execution paths (DAG corruption),
  the exact interpreter's cache key is defined as:
  $$\text{MemoKey} = (\text{RecurrenceState}, \text{ProgramPoint})$$
  where `RecurrenceState` is the state identity (D11) and `ProgramPoint` is the canonical ID of the
  remaining program cursor (interned execution continuation in the free monad).

### D13 — Win & Payout Semantics

- **Emit-Only:** `Emit(label, amount)` is the sole payout generator. The win accumulator is
  never kept in the recurrence state dictionary.
- **Net Round Win:** Negative payout amounts are fully allowed (modeling costs, feature buys). RTP
  is calculated over `Expected(NetRoundWin)`. Hit frequency is $P(\text{NetRoundWin} > 0)$.
- **Fractional wins:** Fractional payouts (rationals) are natively supported.
- **Win Capping:** The win cap is applied once at the Metrics Sink at the end of all emits.

### D14 — Adaptive Histogram Representation

- **Exact Path:** Exact outcome map stored losslessly (Win Rational -> Probability Rational) (D20).
- **Sampled Path:** To prevent O(OOM) memory blow-up on large win bounds, the sampled path
  utilizes a decade-based adaptive histogram:
  - `0 .. 100x` => `1x` step bins
  - `100x .. 1,000x` => `10x` step bins
  - `1,000x .. 10,000x` => `100x` step bins
  - `≥ 10,000x` => combined tail bucket

### D15 — State-Space Complexity Model & Worst-Case Budgets

- **Complexity Metrics:** The compiler static analyzer computes `EstimatedUniqueStates` (State Space Expansion)
  and `ExpressionCost` (total static operation count of AST trees).
- **Loop Cost:** Loop static worst-case complexity is defined as:
  $$\text{LoopWorstCaseCost} = \min(\text{LoopCap}, \text{StateBudget}) \times \text{BodyCost}$$
  where `StateBudget` is the hard limit of explored states before exact execution aborts (G8).

### D16 — Expression Cost Model

- The compiler computes `ExpressionCost` statically (Draw cost = outcome count; Fold cost = arrayLength * bodyCost).
- Default limit: `10,000` operations per leaf. A compiler error is emitted if exceeded (D19, G14).

### D17 — Immutable State Contract

- All state transitions are strictly immutable. No in-place state mutation allowed anywhere in the
  kernel.

### D18 — Plugin Governance

- PluginManifest is required for all Level (c) plug-ins, tracking `PluginCoverage` of the game.
- System emits a warning when plugin dependency exceeds `30%` of the total graph complexity.

### D19 — Max Win Contract

- Every graph must have a finite `MaxRoundWin` bound. The compiler enforces this strictly by requiring
  a declared win cap on the Metrics Sink (D18, G14).

### D20 — Deterministic Error Contract

- All evaluation/compilation errors are localized and contain: `Code`, `NodeId`, `Message`, and `Location` (D1).

### D21 — Distribution Storage Contract

- Exact distributions must be stored losslessly. Compression is allowed only if RTP, variance, and
  hit frequency remain strictly exact.

### D22 — AI Safety Contract

- AI gateway generates strictly `GraphConfig` data. The compile and validation pipeline can never
  be bypassed by generated code.

### D23 — Catalog Compatibility Contract

- Catalog entries (subgraphs) are immutable. New edits generate new `subgraphHash` versions, preventing
  silent alterations of existing pinned games.

### D24 — Reproducibility Contract

- Given `(ConfigHash, Seed, Version)`, the system must reproduce identical metrics, identical
  sample sequences, and identical provenance across all deployments.

### D25 — Distribution Equivalence Contract

- **Exact Path:** A Fast-Path C# optimization is equivalent to its canonical subgraph if and only if
  they yield identical probability mass functions (PMF):
  $$\forall \text{outcome}, \quad P_{\text{fast}}(\text{outcome}) = P_{\text{canonical}}(\text{outcome})$$
- **Sampled Path:** Fast-path metrics must fall strictly within the confidence interval of the
  canonical subgraph.

---

## Phase overview

| Phase | Theme | Goals |
|------|-------|-------|
| 0 | Foundations & contract | G1–G2 |
| 1 | Universal substrate + interpreters | G3–G8 |
| 2 | Atomic mechanic layer + expressions + plugins | G9–G13 |
| 3 | Compiler, API, persistence, jobs | G14–G17 |
| 4 | Frontend: canvas, authoring, live metrics | G18–G25 |
| 5 | AI: NL→graph, auto-tune, explain | G26–G28 |
| 6 | Production hardening & ship | G29–G32 |

---

# Phase 0 — Foundations & contract

### G1 — Monorepo, tooling, CI skeleton
**Objective.** Reproducible repo with both stacks building and a green CI that compiles and lints.
**Deliverables.** Monorepo (`/backend`, `/frontend`); .NET 10 solution (`SlotMath.Core` C#,
`SlotMath.Api` C#, `SlotMath.Core.Tests`); Vite React-TS-strict app; formatters/linters; CI
(restore → build → format-check → lint); `PROGRESS.md` initialized; the constants file stubbed in.
**Definition of Done.**
- [ ] From a clean clone: backend `dotnet build` and `dotnet format --verify-no-changes` pass.
- [ ] `npm ci && npm run build && tsc --noEmit && npm run lint` pass.
- [ ] CI is green with no manual steps; README documents one-command local bootstrap.

### G2 — Canonical schema + contract generation + canonical hash
**Objective.** One source-of-truth schema, no backend/frontend drift, expressive enough for the
whole authoring model — plus the canonical-serialization and hashing layer everything downstream
(cache, round-trips, share links, catalog pinning) depends on.
**Deliverables.** C# model covering: data tables (Symbol, Paytable, ReelStrip, ReelSet, state
structure config, win cap); **primitive nodes** (Draw, Emit, GetState/PutState/Modify, Loop —
with mandatory iteration cap — Branch, Map); **subgraph references** (catalog subgraphs,
parameterised by tables/constants, resolved content-addressed per D2); **expression-valued
ports** (the level-(b) expression AST stored as data, including fold/map/filter nodes);
**plugin references** (level-(c), by id + version + binaryHash); typed edges. Polymorphic
System.Text.Json; **canonical JSON serializer + SHA-256 `configHash` and `subgraphHash`** per D2;
emitted JSON Schema; TS types + Zod mirror.
**Definition of Done.**
- [ ] A corpus of ≥12 sample graphs — including **schema encodings of REF-A through REF-E**,
      expression-valued ports, a subgraph reference, and a plugin reference — round-trips
      losslessly in .NET, and each round-trip preserves `configHash` bit-for-bit.
- [ ] The canonical serializer is order-insensitive on input (two semantically identical configs
      with different key order and rational representations yield the same hash) and a one-bit
      semantic change changes the hash (tests).
- [ ] TS types regenerate from one command; build fails on drift.
- [ ] **Drift-guard test:** the corpus validates identically under the .NET validator and the Zod
      schema.
- [ ] Schema carries `schemaVersion` with a documented migration hook, and a stored
      previous-version sample loads through the hook (test).

---

# Phase 1 — Universal substrate + interpreters

### G3 — Seeded PRNG + Walker–Vose alias sampler
**Objective.** Reproducible O(1) weighted sampling, statistically validated; rational weights kept.
**Deliverables.** **xoshiro256\*\*** with SplitMix64 seeding and stream splitting (D3), constants
documented in the README; alias build + sample (rational weights normalized to a common
denominator and built on exact integers); chi-squared harness with pinned seeds and a negative
control; weights accepted as integers **or rationals** (exact path reads them directly).
**Definition of Done.**
- [ ] Fixed seed → identical sequence (test), and stream *i* is reproducible from
      `(masterSeed, i)` alone.
- [ ] On pinned seeds, alias output is **not** rejected by chi-squared at α = 0.01 over ≥100k
      samples across ≥10 profiles (incl. a non-integer rational-weight profile).
- [ ] **Negative control:** the same harness **rejects** samples scored against deliberately
      perturbed weights (invariant 12).
- [ ] Degenerate inputs handled (single outcome, zero-weight outcomes, non-normalised weights);
      all-zero weights is a located error, not a crash.

### G4 — Free monad over {Draw, Emit, State, Loop}
**Objective.** The one representation both interpreters consume, generic over user state `S`.
**Deliverables.** Sealed node hierarchy: `Pure`, `Draw` (weights may be a function of state),
`Emit(label, amount)`, `GetState`/`PutState`/`Modify`, `Loop` (fixpoint with a state-dependent
stop **and an iteration cap**), `Branch`, `Map`. LINQ query syntax as do-notation; branch = bind + conditional.
**Stack-safe** interpretation (trampoline / explicit continuation stack).
**Definition of Done.**
- [ ] Monad laws hold (property tests) by comparing exact interpretations of both sides.
- [ ] `Emit` is additive and order-commutative within a round: two `Emit`s to the same label
      equal one combined `Emit` under both interpreters (property test); `Emit` cannot read or
      write recurrence state (API-level impossibility, asserted by a compile/test check).
- [ ] A depth-10,000 synthetic program evaluates without `StackOverflow`.
- [ ] State threads correctly through bind and across `Loop` iterations (test).
- [ ] A `Loop` with a state-dependent stop terminates, a `Loop` that never satisfies its stop
      terminates at its cap, and a `Draw` whose weights are computed from state behaves correctly
      (tests).

### G5 — Exact interpreter → rational `Dist` (generic-state memoised, ε-pruned, cap-aware)
**Objective.** Exact distribution with controlled blow-up, over **arbitrary** user state, with
caps-as-semantics (D6) making every game finite.
**Deliverables.** `Exact : Slot<S,T> → (Dist<TotalWin>, label → ℚ)` with rational probabilities:
the full total-win distribution plus exact per-label expected values (D4). **Memoisation** keyed
on the **full ProgramPoint and structural state identity** (D11, D12; win accumulator excluded),
sub-distributions combined by convolution → DAG. **ε-pruning** and **loop-cap truncation** both
accumulate into `prunedMass` with a guaranteed enclosure `[lo, hi]` or mass-loss state per D5/D6.
Stack-safe.
**Definition of Done.**
- [ ] **REF-A:** exact RTP **equals 3/4** by rational equality (no float anywhere in the
      derivation).
- [ ] **REF-C:** the exact capped-game value equals the independent brute-force enumerator
      **exactly** (rational equality) and satisfies the 94/965 enclosure in D9; an internal
      counter shows memoisation evaluates strictly fewer branches than the naive tree while
      producing the identical distribution.
- [ ] **REF-B (recurrent loop):** the exact (ε = 0) capped value r satisfies
      15/17 − 10⁻⁹ < r ≤ 15/17 (D9), with provenance `ExactInterval` (the cap is semantics, not
      approximation).
- [ ] With ε = 0 on a capped game the result is fully exact and `prunedMass = 0`; as ε decreases,
      `[lo, hi]` narrows monotonically and always contains the ε = 0 value (tested on REF-B and
      REF-C).
- [ ] Memoisation works for a **user-defined hashable state record** (structural-equality
      property test: equal states and program points always memo-hit; unequal states/PC never collide).

### G6 — Sampled interpreter → streaming stats
**Objective.** Monte Carlo for the explosive regime; deterministic (including in parallel),
tail-aware, cancellable.
**Deliverables.** Trampolined sampler; Welford mean/variance; adaptive histogram (D14); tail /
max-win tracking; `capHits` and `loopCapHits` counters; running stdErr + CI95; **deterministic
parallelism** per D3 (fixed chunking, per-chunk streams, ordered merge).
**Definition of Done.**
- [ ] Fixed `(seed, n)` → identical stats; streaming stats match a naive batch on the same
      samples.
- [ ] **Parallel determinism:** the same `(seed, n)` produces bit-identical stats at 1 thread and
      4 threads (test).
- [ ] **REF-D:** observed max win never exceeds the cap; with cap 1,000 the run records cap hits,
      and the pinned-seed hit count is asserted (deterministic).
- [ ] A crafted never-terminating loop exits at its cap and increments `loopCapHits` (test).
- [ ] A cancellation token stops promptly and returns partial stats with the correct n.

### G7 — Metric reducers + provenance
**Objective.** The numbers mathematicians want, from either interpreter, always tagged.
**Deliverables.** RTP (exact rational + display float), hit frequency, volatility (variance/std/
index), max win + P(cap reached), **per-label (per-feature) RTP via `Emit` expectations**, win
histogram. Provenance on every value per D5.
**Definition of Done.**
- [ ] Every metric computable from both the exact distribution and the sampled stats.
- [ ] **REF-A:** exact metrics equal the D9 closed forms by rational equality — RTP 3/4, hit 1/2,
      Var 15/16.
- [ ] **REF-B:** per-label exact expectations equal `base = 3/4` and `freespins = 9/68` (up to
      the D9 cap deficit), and per-label contributions **sum exactly** to total RTP on the exact
      path; exact-path volatility is from the full distribution.
- [ ] **REF-D:** P(cap reached) = 1/10,000 exactly on the exact path.
- [ ] Every metric value carries a correct provenance tag, and the D5 aggregation rule is
      enforced (unit tests over mixed-provenance compositions).

### G8 — Regime detection, budget control & hybrid
**Objective.** Exact-vs-sampled as a managed spectrum, aware of expression/plugin leaves.
**Deliverables.** A **static** state-complexity estimator (EstimatedUniqueStates, D14) **plus
dynamic enforcement**: the exact interpreter aborts mid-run when the state/time budget (D7) is
exceeded and falls back to sampled. A `Budget` (time/state ceiling); a selector running exact when
within budget, sampled otherwise, hybrid where the graph allows. A subgraph that contains a
**plugin** (level c) or a non-discretisable continuous expression is forced sampled. Output records
per-subgraph strategy and aggregate provenance.
**Definition of Done.**
- [ ] REF-A/B/C compute exactly within budget; a documented budget-buster fixture (e.g. REF-C
      widened to per-cell redraw on a 5×3 grid) auto-falls-back without exceeding the budget,
      including when the static estimate was wrong and the dynamic abort had to fire (test
      forces this path).
- [ ] REF-E (plugin) is reported `Sampled`; the system never reports `Exact` for it.
- [ ] The result reports per-subgraph strategy and aggregate provenance per D5.

---

# Phase 2 — Atomic mechanic layer + expressions + plugins

### G9 — Subgraph mechanism + bounded iteration in expressions
**Objective.** Prove the substrate is sufficient: any mechanic is composable from atoms +
expressions + subgraphs. No hardcoded C# molecule classes in the standard library.
**Deliverables.**
- **Named subgraph** as a first-class graph construct: a subgraph has a name, typed input and
  output ports, is stored as data (JSON) with a content hash (`subgraphHash`, D2), and is placed
  on the canvas like any primitive node. Subgraphs are the unit of reuse and the catalog entry
  format. The compiler inlines a subgraph reference to its constituent `Slot` program at compile
  time, resolving by content hash.
- **Bounded `fold / map / filter`** added to the level-(b) expression language:
  `fold(arr, init, (acc, x) => expr)`, `map(arr, x => expr)`, `filter(arr, x => pred)`.
  Bounded by the array's size at evaluation time (no infinite recursion); evaluates in rationals
  on the exact path; one level of nesting maximum (no nested fold) (D17). These atoms cover: line-scan
  (fold over payline set), cascade filter (filter over cell array), sticky-wild accumulate (fold
  over positions).
- **State-array read/write**: `Modify` nodes accept expressions that produce array values;
  expressions may index into state arrays (out-of-range per D1). This lets subgraphs accumulate
  and transform collections (symbol positions, collected values, multiplier stacks) without any
  C# class.
**Definition of Done.**
- [ ] A novel mechanic (not in any catalog) is built as a pure subgraph from Draw/Emit/State/
      Loop/Branch/Map + fold/map/filter expressions — **zero C# code**, zero interpreter/compiler
      changes required (proven by a test that authors and runs one).
- [ ] `fold`, `map`, `filter` produce correct values on hand cases; evaluation on the exact path
      is in rationals; the grammar prevents unbounded recursion (test: ill-formed nested fold
      rejected with a precise error).
- [ ] A subgraph round-trips through JSON serialisation without loss and with a stable
      `subgraphHash` (D23); the compiler resolves a subgraph reference to a runnable `Slot` program;
      a 10-level-deep subgraph nesting evaluates without stack overflow (within the D7 depth cap
      of 16).
- [ ] An expression-driven mechanic built with fold/map yields an **exact** RTP equal to a
      hand-computed fraction under the exact interpreter.

### G10 — Standard mechanic catalog (subgraphs, not C# molecules)
**Objective.** Ship the common mechanics as curated, reusable, **versioned** subgraphs — data,
not code.
**Deliverables.**
- **Catalog** of named subgraphs stored as JSON (no accompanying C# molecule class), each entry
  carrying `(name, semanticVersion, subgraphHash)`; configs pin entries by content hash (D2, D23), so
  catalog evolution never mutates existing games: **lines** (fold over payline set, compare each
  line against paytable, accumulate wins via `Emit`), **ways** (fold over columns, multiply
  per-column matching-symbol counts), **scatter** (filter cells by symbol, count matches, look up
  paytable), **cascade/tumble** (filter-remove winning cells + refill from above via state-array
  mutation in a capped Loop), **sticky-wild** (fold to accumulate wild positions into state
  across spins; map to overlay at stored positions on the next spin), **hold-and-win** (capped
  Loop with state-dependent stop checking collected positions).
- Each catalog subgraph is authored entirely in the graph schema — no C# class required.
- **Optional fast-path C# implementations** (Lines, Ways, Cluster) for performance, each paired
  with a **passing equivalence test** against the canonical subgraph, verified via Property-Based
  Testing (D25).
**Definition of Done.**
- [ ] Each catalog subgraph produces correct wins on ≥3 hand-computed cases (unit tests).
- [ ] A 6×5 game using the lines subgraph and a variable-height game using the ways subgraph both
      evaluate correctly — **no C# code change** required.
- [ ] Adding a **new** catalog subgraph requires **no change** to the interpreter, compiler, or
      any existing C# class (test: register and run a trivial new catalog entry).
- [ ] Editing a catalog entry produces a new `subgraphHash`; a config pinned to the old hash
      still resolves, runs, and yields its original metrics (test).
- [ ] For every fast-path that exists: the fast-path and canonical subgraph produce identical
      probability mass functions (D25) under **Property-Based Testing (CsCheck/FsCheck)** over at
      least 1,000 (recommended 10,000+) test cases, featuring **mandatory edge-case generators**
      (All Wild, Megaways min/max height, single-symbol, empty boards) and automatic shrinking (D25).

### G11 — Expression language (level b)
**Objective.** Pure, total, deterministic expressions that make ~95% of novel mechanics authorable
without code, while staying exact-friendly. (Bounded fold/map/filter delivered in G9; this goal
adds the full surface: parsing, type-checking, tooling, and higher-level aggregations.)
**Deliverables.** A complete DSL (AST + type-checker + evaluator): arithmetic, boolean,
comparison, conditional, **state-array aggregations** (`sum / product / count / min / max` with a
predicate), **bounded `fold / map / filter`** (from G9), field/index access over state. **No
unbounded loops or recursion.** Partial-operation semantics exactly per D1 (located deterministic
errors; identities for empty sum/product/count). Amount results evaluate in **exact rationals**
on the exact path and `double` on the sampled path; weight results may be rational;
integer-valued comparisons are exact on both paths (D1). A documented **cost model** (operation
count as a function of array sizes; fold/map/filter are O(n)) feeding the D16 expression budget.
Compiles to the same internal function type that plugins produce. Used for: `Draw` weights,
multiplier/payout values, trigger predicates, state-array transforms, payout adjustments.
Diagnostics carry stable error codes shared with the frontend (G21).
**Definition of Done.**
- [ ] Parses + type-checks an expression corpus; ill-typed expressions are rejected with precise,
      coded errors; the grammar makes unbounded loops/recursion unrepresentable.
- [ ] Pure/deterministic/terminating: identical inputs → identical output; no I/O; division by
      zero, out-of-range index, and empty min/max each produce the D1 located error on **both**
      paths (tests).
- [ ] State-array aggregations produce correct values on hand cases (e.g. product of all
      multiplier entries; sum of all money-symbol values; filter to non-empty positions), with
      empty-array identities per D1.
- [ ] A **fold-driven line-scan** (fold over payline set, compare against paytable) yields an
      **exact** RTP equal to a hand-computed fraction under the exact interpreter, and the sampled
      estimate converges to it within the D8 tolerance.
- [ ] An **expression-driven `Draw` weight** (e.g. selected by a state counter) evaluates
      correctly under both interpreters.
- [ ] An expression exceeding the D16 op budget is rejected at validation with a coded error (D19).

### G12 — Plugin escape hatch (level c)
**Objective.** "Anything is possible" for brand-new evaluators/transforms, safely and without
re-architecting.
**Deliverables.** A typed plugin contract = `IEvaluator : State → Wins` / `ITransform : State → State`
/ weight-source interfaces — these are the **only** uses of these interfaces in the codebase —
plus a **versioned plugin ABI** (contract version + binaryHash recorded per D2). Sandboxed host:
isolated `AssemblyLoadContext`, **no I/O**, time + memory caps, cancellation. Registration is an
**operator/admin action** + a **conformance harness** (purity, determinism, no-I/O, within-limits).
A game using a plugin is flagged sampled-regime.
**Definition of Done.**
- [ ] REF-E's sample plugin passes the conformance harness and produces correct results on a
      hand case.
- [ ] A misbehaving plugin (infinite loop, exception, attempted I/O) is contained and reported —
      it never crashes or hangs the host (test).
- [ ] REF-E reports provenance `Sampled`; the engine refuses to claim `Exact`.
- [ ] The standard catalog ships **zero** implementations of `IEvaluator` / `ITransform` —
      plugins are the only implementors (verified by a test that scans for non-plugin
      `IEvaluator`/`ITransform` implementations and asserts none exist outside the plugin host).
- [ ] A plugin compiled against a mismatched ABI version is rejected at registration with a
      coded error.

### G13 — Mechanic correctness harness (credibility gate)
**Objective.** Prove the *whole* mechanic layer is right, not just the substrate.
**Deliverables.** (a) **Exact-vs-sampled cross-check** over generated configs exercising
primitives + catalog subgraphs + **fold/map expression** leaves; (b) hand-computed full games —
oracle independence per the protocol — incl. cascade (Loop + filter-remove + refill subgraphs),
free-spins-with-retrigger (Loop + scatter-trigger subgraph; REF-B is the anchor),
Hold & Win (loop-until-no-new-lands subgraph), cluster+tumble+rising-multiplier (fold flood-fill +
cascade + state-multiplier); (c) determinism incl. parallel (D3); (d) serialization round-trip
with `configHash` identity; (e) plugin conformance; (f) **fast-path equivalence** for any
fast-path that exists; (g) **negative controls** per D8.
**Definition of Done.**
- [ ] Cross-check passes over ≥50 generated configs × the 20 pinned seeds under the **D8 rule**:
      every comparison within 4·stdErr except at most 2 of 1,000, none beyond 6·stdErr.
- [ ] **Negative control:** with the exact value artificially shifted by 1%, the cross-check
      fails (invariant 12).
- [ ] Every hand-computed game matches exactly (exact path, rational equality against the
      independent oracle) or within the D8 CI rule when a plugin forces sampled.
- [ ] Round-trip yields identical `configHash` and identical metrics; coverage ≥ 90% on the
      kernel + catalog subgraph layer.

---

# Phase 3 — Compiler, API, persistence, jobs

### G14 — Graph → program compiler + validation
**Objective.** Compile the visual graph (primitives + catalog subgraphs + expressions + plugin
refs) to a runnable program, with strict validation.
**Deliverables.** Compiler emitting `Slot<S,T>`; inlines subgraph references by content hash;
compiles expression-valued ports (including fold/map/filter) and resolves + sandboxes plugin
references. Validation: acyclic except through `Loop`; every `Loop` has a cap ≤ the D6 maximum;
a win cap is present (D19); edge type compatibility; **expression type-checking** (including
fold/map lambda bodies) and the D16 expression op budget; **plugin signature/conformance/ABI**
check; exactly one MetricsSink; unreachable/dead-node detection; circular subgraph dependency detection;
precise located errors with **stable error codes** (shared enum with the frontend).
**Definition of Done.**
- [ ] Valid graphs (incl. catalog subgraphs, fold/map expressions, and REF-E's plugin) compile to
      runnable programs; the REF-A…E corpus compiles and reproduces its D9 metrics end to end.
- [ ] Each invalid case — cycle without `Loop`, missing/over-max loop cap, missing win cap,
      type-mismatched edge, ill-typed or over-budget expression, non-conformant/missing/ABI-
      mismatched plugin, over-deep nesting, missing/duplicate sink, dead node, circular subgraph
      reference — is rejected with a precise coded error naming the offending node id (D20).

### G15 — Minimal API (vertical slices)
**Objective.** The HTTP surface.
**Deliverables.** Slices: config CRUD + versioning; `POST /validate`; `POST /evaluate/light`
(regime-aware fast metrics per the D7 policy); `POST /runs` with an explicit **run lifecycle**
(`Queued → Running → Completed | Failed | Cancelled`) and **Idempotency-Key** support; plugin
registry endpoints (admin-gated). **RFC-7807 problem-details from day one** for every error,
carrying the trace id and, where applicable, the compiler error code. OpenAPI.
**Definition of Done.**
- [ ] OpenAPI generates and is served; integration tests (Testcontainers) cover each slice
      (happy + error).
- [ ] `/validate` returns exactly the compiler's coded errors (D20).
- [ ] `/evaluate/light` implements the D7 policy — exact when cheap, else a small-N sampled
      estimate with CI, else `needsFullRun` — and meets p95 < 300 ms on the D10 harness over the
      fixture set.
- [ ] Replaying `POST /runs` with the same Idempotency-Key returns the same run, not a duplicate
      (test); every error response is problem-details with a trace id.

### G16 — Persistence + result cache
**Objective.** Durable configs and memoised results.
**Deliverables.** EF Core 10 + Postgres (`Projects`, `ConfigVersions`, `Runs`, `Users`,
`Plugins`); **`ConfigVersions` append-only/immutable** (a save is a new version; unique index on
`configHash`); optimistic concurrency on the project head; migrations; Redis result cache keyed
by canonical `configHash` (D2); cache hit/miss counters exported for G30.
**Definition of Done.**
- [ ] Migrations apply cleanly to an empty DB in CI (real Postgres container).
- [ ] Save → load → re-save preserves all versions; history queryable; attempting to mutate a
      stored version fails (immutability test); concurrent head updates resolve via optimistic
      concurrency (test).
- [ ] An identical config (same canonical hash, regardless of key order or rational
      representation) returns a cache hit with no recomputation (internal counter), and the
      cached metrics are byte-identical to the original.

### G17 — Heavy-run jobs + SignalR streaming
**Objective.** Long runs streamed live and cancellable.
**Deliverables.** Hangfire job (full-cycle exact and/or large Monte Carlo); SignalR hub streaming
batched `(seq, sampleCount, runningRtp, stdErr)` — **monotonic sequence numbers**, server retains
a replay buffer (≥ the last 1,000 batches or until run completion + 10 min) so a reconnecting
client resumes from its last seq with no gaps or duplicates; cancellation threaded into the
sampler; result persisted + cached.
**Definition of Done.**
- [ ] A 1,000,000-round REF-A run streams progress and persists a result. Convergence gates
      (pinned seed, statistically meaningful per D8): |running RTP − 3/4| ≤ 4σ/√n at
      n = 100,000 (= 4·(√15/4)/√100000 ≈ 1.23 percentage points) and ≤ 0.5 percentage points at
      n = 1,000,000.
- [ ] Cancel stops within 500 ms and persists partial stats with the correct n; a client that
      disconnects and reconnects mid-run receives the missed batches exactly once (seq-resume
      test).

---

# Phase 4 — Frontend: canvas, authoring, live metrics

*(Audit visual quality manually after each frontend goal — the `/goal` checker reads the
transcript only and cannot judge how the UI looks.)*

### G18 — Frontend scaffold
**Objective.** Shell and plumbing.
**Deliverables.** React 19 / TS-strict; tabs Build / Simulate / Results / Export; Zustand;
TanStack Query; generated typed API client; Zod; design baseline.
**Definition of Done.**
- [ ] `npm run build`, `tsc --noEmit`, `npm run lint` exit 0; four tabs route and render.
- [ ] API client is generated from the OpenAPI contract (regenerate script; no hand-written
      request types); axe/Lighthouse a11y ≥ 90 on the shell.

### G19 — Node canvas
**Objective.** The mechanic graph with primitive nodes and catalog subgraphs.
**Deliverables.** `@xyflow/react` canvas; palette of **primitive** nodes (Draw/Emit/State/Loop/
Branch/Map) and **catalog subgraph** nodes; edge connection with visual type-checking; explicit
`Loop` node UX surfacing its iteration cap; inline validation mirroring compiler error codes;
subgraph collapse/expand; **undo/redo (≥ 50 steps)** covering node, edge, and parameter changes.
**Definition of Done.**
- [ ] A user can build a base + scatter→free-spins + cascade graph visually using primitives and
      catalog subgraphs.
- [ ] Invalid connections are blocked or flagged inline with the compiler's coded message.
- [ ] Undo/redo restores graph state exactly across ≥ 50 mixed operations (test/E2E).
- [ ] Editing stays responsive (no main-thread block > 50 ms on node/edge changes, D7).

### G20 — Table data editors
**Objective.** Replace Excel for data with good table UX (invariant #7).
**Deliverables.** Symbol editor + paytable grid; reel-strip builder; state structure config;
ReelSet assembly; win-cap field.
**Definition of Done.**
- [ ] Symbols/paytable/strips edit fluidly with keyboard nav and bulk clipboard paste (TSV from
      a spreadsheet pastes into the grid correctly); edits validate against the Zod schema before
      submit.

### G21 — Expression editor (level b)
**Objective.** The authoring surface that makes the constructor flexible without code.
**Deliverables.** CodeMirror-6 typed expression inputs on expression-valued ports (multiplier,
weight, predicate, state-array transform, payout adjust); **autocomplete over state fields and
array operations (fold/map/filter syntax)**, live type-checking, inline errors keyed by the
**shared diagnostic codes** from G11/G14 — frontend and backend agree by code.
**Definition of Done.**
- [ ] A user can write `fold(paylines, 0, (acc, line) => acc + ...)` and a compound predicate,
      and see live validation; an ill-typed expression is flagged with the same **error code**
      (and equivalent message) the backend `/validate` returns for it (test asserts code
      equality).
- [ ] A saved expression round-trips and drives the live metric for that node.

### G22 — Custom-mechanic authoring + plugin management
**Objective.** Grow the catalog in-product (level a/b composition) and govern plugins (level c).
**Deliverables.** Compose + **name a reusable subgraph** as a custom catalog entry (versioned,
content-hashed per D2), reusable on the canvas. Plugin management UI: select among
**admin-registered** plugins, view contract/ABI version and **conformance status**, and surface
that a game using one is sampled-regime.
**Definition of Done.**
- [ ] A user composes a named custom mechanic from primitives + catalog subgraphs + expressions
      and reuses it in another graph (test/E2E); re-editing the entry creates a new version
      without mutating graphs pinned to the old hash (D23).
- [ ] Selecting a non-conformant plugin is blocked with the coded reason; a conformant one shows
      its contract and the sampled-regime flag.

### G23 — Live per-edge metrics
**Objective.** See what every mechanic does to the distribution, instantly.
**Deliverables.** Debounced `/evaluate/light` on edits (300 ms, D7) with **stale-request
abortion** — a superseded in-flight request is cancelled so out-of-order responses can never
paint stale numbers; per-edge / per-node distribution preview + RTP / hit-freq / variance badges;
**provenance** badge on every value per D5.
**Definition of Done.**
- [ ] Changing a symbol weight or an expression updates per-edge metrics within the D7 budget;
      a rapid edit burst never displays a metric from a superseded request (test simulates
      out-of-order completion).
- [ ] Provenance badge is always present and correct; a too-heavy config shows `needsFullRun`,
      not stale numbers.

### G24 — Simulate panel
**Objective.** Heavy runs, live, with the exact-vs-sampled story visible.
**Deliverables.** Run trigger; live uPlot RTP convergence (SignalR, seq-ordered) with
**client-side decimation** — batches arrive every ≤ 250 ms or 10,000 samples, the client keeps a
bounded buffer and renders min/max-per-pixel-bucket so a 1M-point run stays smooth; adaptive
sampled histogram (D14); progress + cancel; exact-vs-sampled comparison view.
**Definition of Done.**
- [ ] The convergence curve streams smoothly to ≥1M points without jank (no frame > 100 ms during
      streaming); cancel freezes the partial curve; the comparison on REF-A shows the sampled
      mean inside the exact value's 4·stdErr band (pinned seed).
- [ ] A reconnect mid-run resumes the curve with no visible gap or duplicate segment.

### G25 — Export / import + PAR sheet
**Objective.** Get the math out, share it, hand it off.
**Deliverables.** Versioned JSON export/import that **embeds resolved subgraph definitions and
plugin references** (D2 — exports are self-contained); copy/download; **shareable link = an
immutable server-side snapshot**, readable anonymously (read-only) and rate-limited;
PAR-sheet-style export (CSV + printable summary) containing at minimum: game/config identity
(`configHash`, schemaVersion), symbols + reel strips + paytable, RTP with per-feature (per-label)
breakdown, hit frequency, variance/std, max win + P(cap), loop caps, and the **provenance of
every figure** (D5).
**Definition of Done.**
- [ ] Export → import round-trips to an identical `configHash` — including on an instance whose
      catalog never contained the embedded subgraphs; a shareable link reopens the exact graph +
      data read-only without auth.
- [ ] The PAR export for REF-B shows the per-label breakdown (`base`, `freespins`) summing to
      total RTP, each figure tagged with provenance.

---

# Phase 5 — AI

### G26 — AI gateway + NL → graph
**Objective.** Describe a game in words, get a starting graph (including expression leaves).
**Deliverables.** Backend AI gateway (server-side key, rate-limited per G29). Structured output:
NL → graph JSON (catalog subgraph references + expression-valued ports) validated against the
schema (D22); one repair pass on validation failure; reject after. A pinned **evaluation corpus**
of 10 NL specs.
**Definition of Done.**
- [ ] ≥ 8 of the 8 valid corpus specs yield graphs that pass G14 compiler validation with at most
      one repair pass each, including any generated expressions; the 2 adversarial specs yield a
      clean, structured user-facing error (never a malformed graph).
- [ ] Malformed model output never throws — valid graph or clean user-facing error.
- [ ] The provider key is read only server-side — verified by an automated test that scans the
      built client bundle for the key and the gateway path for client-side exposure.

### G27 — Auto-tune / inverse design
**Objective.** Hit a target RTP within volatility/max-win constraints, using the exact engine as a
cheap objective.
**Deliverables.** Backend optimisation job (GA / coordinate search) over parameters explicitly
**annotated `tunable` with [min, max] bounds** — reel-strip weights, table values, and
**expression constants**; objective `|RTP − target| + λ·constraint penalties` (volatility band,
max-win cap probability); exact engine for fitness where feasible (else sampled with sufficient
n per D8); **seed-deterministic** (same seed → same search trajectory and same best-so-far
sequence); streamed best-so-far; "apply suggestion" mutates the graph.
**Definition of Done.**
- [ ] On REF-A with weights marked tunable over [0, 50], the optimiser converges to target
      RTP 90% within ±0.25 pp while respecting the volatility and max-win constraints inside a
      bounded budget (an exact integer solution exists, e.g. weights 6/0/14 → RTP = 9/10).
- [ ] Two runs with the same seed produce identical trajectories (test); progress streams; cancel
      keeps best-so-far; "apply" mutates the graph and the re-derived exact metrics match the
      reported solution.

### G28 — Explain + compliance lint
**Objective.** Tell the mathematician why, and catch mistakes.
**Deliverables.** Explain: AI reads the distribution + graph for variance drivers / RTP
concentration in plain language. Lint: rule-based checks, each with a failing fixture and a
clean fixture — **RTP outside the configurable band** (default 85–98%), **P(max-win cap) above
threshold**, **dead symbol** (on strips, absent from paytable), **unreachable pay** (in paytable,
absent from strips), **paytable monotonicity anomaly** (longer match pays less), **all-zero
weight Draw** — plus AI qualitative notes.
**Definition of Done.**
- [ ] Explain output only cites numbers passed in from the current distribution — enforced by an
      automated check that extracts every numeral from the output and asserts it appears in the
      supplied context (with rounding-format tolerance); no invented figures.
- [ ] Each lint rule fires on its crafted failing config, stays silent on its clean config, and
      links to the offending node/symbol id.

---

# Phase 6 — Production hardening & ship

### G29 — Security, limits, multi-tenancy & plugin governance
**Objective.** Safe for the open internet, including the plugin hatch.
**Deliverables.** Anonymous use works; OAuth + JWT gating saved/shared projects; project
ownership + sharing; rate limiting (defaults in the constants file, e.g. light-eval 10 rps/IP;
≤ 5 concurrent runs/user); input/budget caps enforced (max graph size, nodes, sim budget,
expression complexity, subgraph nesting depth, loop caps); **plugin governance** — registration is
admin-only, only trusted/approved plugins run, executed sandboxed; CORS allowlist + standard
security headers; **dependency audit in CI** (`dotnet list package --vulnerable`, `npm audit` —
fail on critical).
**Definition of Done.**
- [ ] Anonymous can build + simulate but not persist privately; an authed user owns/shares and
      non-owners cannot mutate (incl. via share links, which stay read-only).
- [ ] Oversized graphs, over-budget sims, over-complex expressions, over-deep nesting, and
      over-max loop caps are rejected with clear coded errors; only approved plugins execute;
      rate limits verified by test; CI fails on a planted critical-severity dependency.

### G30 — Observability & error handling
**Objective.** Operable in production.
**Deliverables.** Structured logging; OpenTelemetry traces + metrics (incl. cache hit ratio from
G16, run durations, regime decisions); `/health/live` + `/health/ready` (DB + Redis); RFC-7807
problem-details everywhere with the **trace id propagated into the frontend error UI**; frontend
error boundaries.
**Definition of Done.**
- [ ] A trace spans a full request incl. a heavy run; readiness reflects DB + Redis (test toggles
      a dependency and observes degraded → recovered).
- [ ] A forced backend error renders a graceful UI (no blank screen) **showing the trace id**
      that locates the corresponding backend trace.

### G31 — Docker, CI/CD & deploy
**Objective.** One pipeline from commit to live.
**Deliverables.** Multi-stage Dockerfiles; docker-compose (api + postgres + redis) with seed
data; GH Actions (build → unit → integration on real Postgres/Redis via Testcontainers → lint →
perf job (D10) → image); deploy to Railway (backend + Postgres + Redis) and Vercel/Railway
(frontend); documented rollback procedure (redeploy previous image).
**Definition of Done.**
- [ ] `docker compose up` brings the stack up with seed data (smoke test hits a health endpoint
      and runs a REF-A light eval).
- [ ] CI runs the full matrix incl. integration and fails on any red; a merge to `main` deploys
      and the URL serves the working app end to end; the rollback procedure is documented and
      exercised once.

### G32 — Docs, E2E & production acceptance
**Objective.** Hand-offable and proven, including the flexibility story.
**Deliverables.** README; architecture doc (incl. the v3 design decisions and Definitions);
**slot-math explainer**; **authoring guide** (levels a/b/c, writing fold/map expressions,
building a custom mechanic as a subgraph, plugin governance); API docs; runbook. Playwright
golden-path E2E. An auto-generated **`VERIFICATION.md`** (CI artifact + rendered page) listing
every D9 fixture with expected closed form, computed value, and provenance — the standing proof
that the math is right.
**Definition of Done.**
- [ ] The Playwright golden path (build a game → live metrics → run sim → auto-tune → export →
      **re-import to an identical `configHash`**) passes headless in CI.
- [ ] `VERIFICATION.md` regenerates in CI and shows every fixture matching its D9 closed form
      with correct provenance.
- [ ] A non-iGaming developer can follow the explainer; a designer can follow the authoring guide
      to build a custom mechanic as a pure subgraph with fold/map expressions — zero C# code.
- [ ] axe/Lighthouse a11y ≥ 90 on the Build, Simulate, and Results views (not just the shell).
- [ ] The Production acceptance checklist below is fully green.

---

## Production acceptance — the 100% bar

- [ ] Exact RTP matches closed form for every hand-computed game by **rational equality** —
      including REF-A = 3/4 (Var 15/16), REF-C against its independent enumerator, and REF-B
      inside its 15/17 enclosure — and `VERIFICATION.md` is green in CI.
- [ ] **Flexibility (no C# molecules):** a non-trivial novel mechanic is buildable as a pure
      **subgraph** from atoms + fold/map expressions (zero C# code, zero interpreter/compiler
      changes), and its exact RTP matches the hand-computed value. A **plugin** evaluator passes
      conformance, runs through the standard interpreter path, and is correctly flagged
      sampled-regime. The standard library ships **zero** `IEvaluator`/`ITransform` implementations.
- [ ] Exact-vs-sampled cross-check (incl. fold/map expression leaves) passes under the D8 rule
      (4·stdErr, ≤ 2/1,000 breaches, none beyond 6σ), and its **negative control fails on
      corrupted input** — as do the chi-squared harness's and every other statistical gate's.
- [ ] Running RTP on REF-A satisfies the 4σ/√n bound at 100k rounds and ≤ 0.5 pp at 1M rounds
      (pinned seeds); the alias sampler passes chi-squared at α = 0.01 on pinned seeds.
- [ ] Every displayed metric carries correct provenance per D5; per-label contributions sum
      exactly to total RTP on the exact path; adding a new catalog mechanic (subgraph) needs
      **no C# code** and no interpreter/compiler change.
- [ ] **Determinism holds in parallel**: identical `(seed, n)` stats at any thread count.
- [ ] Deep-recursion programs never stack-overflow; every loop is capped and cap hits are
      accounted; heavy runs are cancellable; identical configs (same canonical hash) hit the
      cache; export/import and share links round-trip to an identical `configHash` losslessly,
      independent of catalog state.
- [ ] Misbehaving plugins are sandboxed and contained; plugin registration is admin-only; auth,
      rate limits, input/budget/expression caps and plugin governance enforced; dependency audit
      green; secrets server-side only.
- [ ] Full stack deploys via CI; Playwright golden path (incl. re-import hash identity) green;
      observability live with trace ids surfaced to the UI.

## Deferred (later)

- **Exact fixpoint solve for recurrent loop states** — treating recurrent recurrence-states as a
  rational linear system (e.g. REF-B reduces to one equation, c = 3/4 + (3/20)c) and solving it
  exactly, removing the cap-deficit term entirely. v3.1 ships bounded unrolling under caps (D6),
  which REF-B proves is numerically sufficient.
- PixiJS reel **preview** renderer; custom payline **drawing** tool; in-browser WASM mirror of
  the kernel; collaboration/multiplayer; sound; per-feature higher-moment breakdowns (variance
  per label). (The engine's flexibility does not depend on these.)
