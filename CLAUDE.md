# Slot Math Lab — Product & Build Spec (PRD)

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
- If a DoD item cannot be met, **stop and report** the blocker — never silently descope.
- The **Non-negotiable invariants** below are always-on; violating one fails the goal even if its
  DoD otherwise passes.

---

## Architecture on one screen

- **Substrate.** A game is a **free monad over a small effect algebra**:
  `Draw(weights)` (weighted choice; weights may depend on state), `GetState / PutState / Modify`
  (arbitrary, user-defined game state), and `Loop` (fixpoint with a state-dependent stop
  condition). Branch is sugar over bind. The substrate is **generic over the user's state type
  `S`** — the engine hardcodes no "multiplier", "free spins", "board", or any other game concept.
- **Two interpreters, one program.** `Exact` is the natural transformation `Slot → Dist`: it
  folds every branch into a distribution with **exact rational** probabilities, memoized into a
  DAG and ε-pruned. `Sampled` is a stack-safe Monte Carlo interpreter with streaming statistics.
  A **regime/budget** layer picks exact, sampled, or hybrid per subgraph and tags every metric
  with **provenance**.
- **Mechanic layer = atoms + expressions + named subgraphs.**
  Mechanics are **composed**, never coded into the engine. The catalog is a set of **named
  subgraphs** stored as data (JSON) — built entirely from substrate atoms (`Draw`, `State`,
  `Loop`, `Branch`, `Map`) and bounded fold/map expressions. There are **no C# molecule classes**
  in the standard library. `IEvaluator` / `ITransform` exist as **plugin contracts only** (level
  c, the escape hatch) — not as a standard library interface to implement. Optional C# **fast-paths**
  (lines, ways, cluster) may exist for performance, paired with equivalence tests proving they
  match the canonical subgraph.
- **Game state is fully user-defined.** The engine has no `Board` type. A "board" (reel grid,
  tile field, etc.) is just a user-defined array in the state `S`. State shape is expressed
  through data tables and subgraph wiring.

## Authoring capability model — the a + b + c flexibility contract

Capability lives on the **leaves**, as a dial, not as three separate products:

- **Level (a) — subgraph config.** A **named subgraph** from the catalog, parameterised by
  finite **tables / constants** (paytable, payline set, reel strips, thresholds, stepped
  multiplier tables). The subgraph is **data** (JSON) — not a C# class. Fully declarative.
  Covers the majority of real games; the simple path stays simple.
- **Level (b) — typed expression.** A small, **pure, total, deterministic** expression on
  specific node ports: `Draw` weights, multiplier/payout values, trigger predicates, payout
  adjustments, state-array transforms. Grammar: arithmetic, boolean, comparison, conditional,
  **state-array aggregations** (`sum / product / count / min / max` with a predicate), **bounded
  `fold / map / filter`** over state arrays (one level; no nested fold; bounded by array size →
  stays exact-analysable), and field/index access over state. **No unbounded loops or recursion.**
  Amount-typed expressions evaluate in **exact rationals** on the exact path. Compiles to the
  same internal function type that plugins produce.
- **Level (c) — typed plugin escape hatch.** A compiled, **pure** plugin implementing
  `IEvaluator : State → Wins` / `ITransform : State → State` / a weight source — these are the
  **only** uses of these interfaces; the standard library ships none. **Sandboxed** (no I/O, time
  + memory caps, isolated load context), server-side, trusted, and **test-gated**. A game that
  uses a plugin is **flagged sampled-regime** (the engine never claims `Exact` for it). The rare
  path, reserved for brand-new spatial evaluators or exotic logic unrepresentable in expressions.
- **Custom mechanic** = a **named, reusable subgraph** composed from primitives + catalog +
  expressions (pure no-code). This is how the catalog grows without touching C# code.

## Non-negotiable invariants

1. **Exact path stays exact.** Probabilities and amounts on the exact path are rational
   (`BigInteger` numerator over a positive denominator); floats are display-only and never derive
   the RTP ratio. Level-(b) expressions on the exact path evaluate in rationals.
2. **No hardcoded mechanic molecules.** Mechanics are subgraphs (data, not C# classes). `IEvaluator`
   and `ITransform` are plugin contracts (level c) only — the standard library ships zero
   implementations of these interfaces. Adding a catalog mechanic requires zero C# code and zero
   interpreter/compiler changes.
3. **Layered authoring holds.** Level (a) is declarative subgraph config; level (b) expressions
   are pure, total, and loop-free (except bounded fold/map over fixed-size arrays); level (c)
   plugins are pure, sandboxed and test-gated, and force the sampled regime.
4. **Generic state, no engine types.** The recurrence state (everything that affects the future
   distribution) is hashable and is the memoization key; the win accumulator is held separate and
   combined by convolution. State is fully user-defined — the engine has **no `Board` type**; a
   "board" is just a user-defined array in `S`.
5. **Every metric carries provenance**: `Exact`, `ExactWithinEpsilon { prunedMass, bound }`, or
   `Sampled { n, stdErr, ci95 }`. No silent mixing.
6. **Determinism.** A given seed yields an identical sample sequence and identical stats anywhere.
7. **Tables, not nodes, for data.** Symbols, paytable, reel strips, and state structure config
   are tables, never graph nodes.
8. **Loops only via explicit `Loop`/fixpoint nodes.** The graph is otherwise acyclic. Bounded
   fold/map/filter in expressions are not graph loops — they operate over a fixed-size array in
   a single evaluation step.
9. **No secrets client-side.** The AI key and plugin execution live only on the backend.
10. **The kernel is pure.** No I/O, no framework, no shared mutation. Everything else is the shell.
11. **Fast-paths are proven equivalent.** Any optional C# performance implementation of a catalog
    subgraph must be accompanied by a passing equivalence test against the canonical subgraph on
    ≥20 random inputs. The canonical subgraph is the source of truth; the fast-path is an
    optimization detail.

## Tech stack

**Backend (.NET 10)** — C# math kernel `SlotMath.Core` (substrate + interpreters + the level-(b)
expression compiler/evaluator including fold/map/filter, all pure); the **standard mechanic
catalog** (curated subgraph definitions as JSON data, no C# molecule classes); a sandboxed plugin
host (isolated `AssemblyLoadContext`, no I/O, time/memory limits) for level-(c) plugins only.
C# host `SlotMath.Api` (Minimal APIs + Vertical Slice). EF Core 10 + Npgsql + PostgreSQL; Redis
(result cache by config hash, job coordination); Hangfire (heavy runs); SignalR (streaming);
built-in OpenAPI. Tests: xUnit + property-based (FsCheck or CsCheck); OpenTelemetry.

**Frontend** — React 19 + TypeScript (strict), Vite; `@xyflow/react` (canvas, confirm latest
major at scaffold); Zustand + TanStack Query; **CodeMirror 6** for the typed expression editor
(lint + autocomplete over state fields + fold/map syntax); uPlot (streaming convergence) + visx
(histograms); Zod + `openapi-typescript` (generated types) + a thin typed client.

**Infra** — Docker (multi-stage), docker-compose (api + postgres + redis); GitHub Actions CI/CD;
deploy backend + Postgres + Redis on Railway, frontend on Vercel (or Railway).

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
(restore → build → format-check → lint).
**Definition of Done.**
- [ ] From a clean clone: backend `dotnet build` and `dotnet format --verify-no-changes` pass.
- [ ] `npm ci && npm run build && tsc --noEmit && npm run lint` pass.
- [ ] CI is green with no manual steps; README documents one-command local bootstrap.

### G2 — Canonical schema + contract generation
**Objective.** One source-of-truth schema, no backend/frontend drift, expressive enough for the
whole authoring model.
**Deliverables.** C# model covering: data tables (Symbol, Paytable, ReelStrip, ReelSet, state
structure config); **primitive nodes** (Draw, GetState/PutState/Modify, Loop, Branch, Map);
**subgraph references** (catalog subgraphs, parameterised by tables/constants); **expression-valued
ports** (the level-(b) expression AST stored as data, including fold/map/filter nodes);
**plugin references** (level-(c), by id + contract); typed edges. Polymorphic System.Text.Json;
emitted JSON Schema; TS types + Zod mirror.
**Definition of Done.**
- [ ] A corpus of ≥12 sample graphs — including expression-valued ports, a subgraph reference,
      and a plugin reference — round-trips losslessly in .NET.
- [ ] TS types regenerate from one command; build fails on drift.
- [ ] **Drift-guard test:** the corpus validates identically under the .NET validator and the Zod
      schema.
- [ ] Schema carries `schemaVersion` with a documented migration hook.

---

# Phase 1 — Universal substrate + interpreters

### G3 — Seeded PRNG + Walker–Vose alias sampler
**Objective.** Reproducible O(1) weighted sampling, statistically validated; rational weights kept.
**Deliverables.** Deterministic seedable PRNG; alias build + sample; chi-squared harness; weights
accepted as integers **or rationals** (exact path reads them directly).
**Definition of Done.**
- [ ] Fixed seed → identical sequence (test).
- [ ] Alias output does **not** reject the target weights by chi-squared at α = 0.01 over ≥100k
      samples across ≥10 profiles (incl. a non-integer rational-weight profile).
- [ ] Degenerate inputs handled (single outcome, zero weights, non-normalised weights).

### G4 — Free monad over {Draw, State, Loop}
**Objective.** The one representation both interpreters consume, generic over user state `S`.
**Deliverables.** Sealed node hierarchy: `Pure`, `Draw` (weights may be a function of state),
`GetState`/`PutState`/`Modify`, `Loop` (fixpoint with a state-dependent stop). LINQ query syntax
as do-notation; branch = bind + conditional. **Stack-safe** interpretation (trampoline / explicit
continuation stack).
**Definition of Done.**
- [ ] Monad laws hold (property tests) by comparing exact interpretations of both sides.
- [ ] A depth-10,000 synthetic program evaluates without `StackOverflow`.
- [ ] State threads correctly through bind and across `Loop` iterations (test).
- [ ] A `Loop` with a state-dependent stop terminates and a `Draw` whose weights are computed from
      state both behave correctly (tests).

### G5 — Exact interpreter → rational `Dist` (generic-state memoised, ε-pruned)
**Objective.** Exact distribution with controlled blow-up, over **arbitrary** user state.
**Deliverables.** `Exact : Slot<S,T> → Dist<T>` with rational probabilities. **Memoisation** keyed
on a canonical hash of the **recurrence state** (the win accumulator excluded), sub-distributions
combined by convolution → DAG. **ε-pruning** with accumulated `prunedMass` and a reported RTP
interval `[lo, hi]` bounded by `prunedMass × maxRemainingWin`. Stack-safe.
**Definition of Done.**
- [ ] A hand-computed tiny game's exact RTP equals the closed-form fraction exactly (rational
      equality, no float).
- [ ] With ε = 0 the result is fully exact and `prunedMass = 0`; as ε decreases, `[lo,hi]` narrows
      monotonically and always contains the ε = 0 value.
- [ ] Memoisation works for a **user-defined hashable state record** and reduces the evaluated
      branch count on a cascade game vs the naive tree (internal counter); result equals naive
      enumeration.

### G6 — Sampled interpreter → streaming stats
**Objective.** Monte Carlo for the explosive regime; deterministic, tail-aware, cancellable.
**Deliverables.** Trampolined sampler; Welford mean/variance; histogram; tail / max-win tracking;
running stdErr + CI95.
**Definition of Done.**
- [ ] Fixed seed + n → identical stats; streaming stats match a naive batch on the same samples.
- [ ] Observed max win never exceeds the cap; cap-hits counted.
- [ ] A cancellation token stops promptly and returns partial stats with the correct n.

### G7 — Metric reducers + provenance
**Objective.** The numbers mathematicians want, from either interpreter, always tagged.
**Deliverables.** RTP (exact rational + display float), hit frequency, volatility (variance/std/
index), max win + P(cap reached), per-feature / per-state RTP breakdown, win histogram. Provenance
on every value.
**Definition of Done.**
- [ ] Every metric computable from both the exact distribution and the sampled stats.
- [ ] Per-feature contributions sum to total RTP exactly on the exact path; exact-path volatility
      is from the full distribution.
- [ ] Every metric value carries a correct provenance tag.

### G8 — Regime detection, budget control & hybrid
**Objective.** Exact-vs-sampled as a managed spectrum, aware of expression/plugin leaves.
**Deliverables.** Branch-count estimator; a `Budget` (time/branch ceiling); a selector running
exact when ≤ budget, sampled otherwise, hybrid where the graph allows. A subgraph that contains a
**plugin** (level c) or a non-discretisable continuous expression is forced sampled. Output records
per-subgraph strategy and aggregate provenance.
**Definition of Done.**
- [ ] A base-only game computes exactly within budget; a deep-retrigger game auto-falls-back
      without exceeding budget.
- [ ] A subgraph using a plugin is reported `Sampled`; the system never reports `Exact` for it.
- [ ] The result reports per-subgraph strategy and aggregate provenance.

---

# Phase 2 — Atomic mechanic layer + expressions + plugins

### G9 — Subgraph mechanism + bounded iteration in expressions
**Objective.** Prove the substrate is sufficient: any mechanic is composable from atoms +
expressions + subgraphs. No hardcoded C# molecule classes in the standard library.
**Deliverables.**
- **Named subgraph** as a first-class graph construct: a subgraph has a name, typed input and
  output ports, is stored as data (JSON), and is placed on the canvas like any primitive node.
  Subgraphs are the unit of reuse and the catalog entry format. The compiler inlines a subgraph
  reference to its constituent `Slot` program at compile time.
- **Bounded `fold / map / filter`** added to the level-(b) expression language:
  `fold(arr, init, (acc, x) => expr)`, `map(arr, x => expr)`, `filter(arr, x => pred)`.
  Bounded by the array's size at evaluation time (no infinite recursion); evaluates in rationals
  on the exact path; one level of nesting maximum (no nested fold). These atoms cover: line-scan
  (fold over payline set), cascade filter (filter over cell array), sticky-wild accumulate (fold
  over positions).
- **State-array read/write**: `Modify` nodes accept expressions that produce array values;
  expressions may index into state arrays. This lets subgraphs accumulate and transform
  collections (symbol positions, collected values, multiplier stacks) without any C# class.
**Definition of Done.**
- [ ] A novel mechanic (not in any catalog) is built as a pure subgraph from Draw/State/Loop/
      Branch/Map + fold/map/filter expressions — **zero C# code**, zero interpreter/compiler
      changes required (proven by a test that authors and runs one).
- [ ] `fold`, `map`, `filter` produce correct values on hand cases; evaluation on the exact path
      is in rationals; the grammar prevents unbounded recursion (test: ill-formed nested fold
      rejected with a precise error).
- [ ] A subgraph round-trips through JSON serialisation without loss; the compiler resolves a
      subgraph reference to a runnable `Slot` program; a 10-level-deep subgraph nesting evaluates
      without stack overflow.
- [ ] An expression-driven mechanic built with fold/map yields an **exact** RTP equal to a
      hand-computed fraction under the exact interpreter.

### G10 — Standard mechanic catalog (subgraphs, not C# molecules)
**Objective.** Ship the common mechanics as curated, reusable subgraphs — data, not code.
**Deliverables.**
- **Catalog** of named subgraphs stored as JSON (no accompanying C# molecule class): **lines**
  (fold over payline set, compare each line against paytable, accumulate wins), **ways** (fold
  over columns, multiply per-column matching-symbol counts), **scatter** (filter cells by symbol,
  count matches, look up paytable), **cascade/tumble** (filter-remove winning cells + refill from
  above via state-array mutation in a Loop), **sticky-wild** (fold to accumulate wild positions
  into state across spins; map to overlay at stored positions on the next spin), **hold-and-win**
  (Loop with state-dependent stop checking collected positions).
- Each catalog subgraph is authored entirely in the graph schema — no C# class required.
- **Optional fast-path C# implementations** (Lines, Ways, Cluster) for performance, each paired
  with a **passing equivalence test** against the canonical subgraph (invariant 11).
**Definition of Done.**
- [ ] Each catalog subgraph produces correct wins on ≥3 hand-computed cases (unit tests).
- [ ] A 6×5 game using the lines subgraph and a variable-height game using the ways subgraph both
      evaluate correctly — **no C# code change** required.
- [ ] Adding a **new** catalog subgraph requires **no change** to the interpreter, compiler, or
      any existing C# class (test: register and run a trivial new catalog entry).
- [ ] For every fast-path that exists: the fast-path and canonical subgraph produce identical
      results on ≥20 random state inputs (equivalence test, invariant 11).

### G11 — Expression language (level b)
**Objective.** Pure, total, deterministic expressions that make ~95% of novel mechanics authorable
without code, while staying exact-friendly. (Bounded fold/map/filter delivered in G9; this goal
adds the full surface: parsing, type-checking, tooling, and higher-level aggregations.)
**Deliverables.** A complete DSL (AST + type-checker + evaluator): arithmetic, boolean,
comparison, conditional, **state-array aggregations** (`sum / product / count / min / max` with a
predicate), **bounded `fold / map / filter`** (from G9), field/index access over state. **No
unbounded loops or recursion.** Amount results evaluate in **exact rationals** on the exact path
and `double` on the sampled path; weight results may be rational. Compiles to the same internal
function type that plugins produce. Used for: `Draw` weights, multiplier/payout values, trigger
predicates, state-array transforms, payout adjustments.
**Definition of Done.**
- [ ] Parses + type-checks an expression corpus; ill-typed expressions are rejected with precise
      errors; the grammar makes unbounded loops/recursion unrepresentable.
- [ ] Pure/total/deterministic: identical inputs → identical output; no I/O.
- [ ] State-array aggregations produce correct values on hand cases (e.g. product of all
      multiplier entries; sum of all money-symbol values; filter to non-empty positions).
- [ ] A **fold-driven line-scan** (fold over payline set, compare against paytable) yields an
      **exact** RTP equal to a hand-computed fraction under the exact interpreter, and the sampled
      estimate converges to it.
- [ ] An **expression-driven `Draw` weight** (e.g. selected by a state counter) evaluates
      correctly under both interpreters.

### G12 — Plugin escape hatch (level c)
**Objective.** "Anything is possible" for brand-new evaluators/transforms, safely and without
re-architecting.
**Deliverables.** A typed plugin contract = `IEvaluator : State → Wins` / `ITransform : State → State`
/ weight-source interfaces — these are the **only** uses of these interfaces in the codebase.
Sandboxed host: isolated `AssemblyLoadContext`, **no I/O**, time + memory caps, cancellation.
Registration/discovery + a **conformance harness** (purity, determinism, no-I/O, within-limits).
A game using a plugin is flagged sampled-regime.
**Definition of Done.**
- [ ] A sample custom-evaluator plugin passes the conformance harness and produces correct wins
      on a hand case.
- [ ] A misbehaving plugin (infinite loop, exception, attempted I/O) is contained and reported —
      it never crashes or hangs the host (test).
- [ ] A game using a plugin reports provenance `Sampled`; the engine refuses to claim `Exact`.
- [ ] The standard catalog ships **zero** implementations of `IEvaluator` / `ITransform` —
      plugins are the only implementors (verified by a test that scans for non-plugin
      `IEvaluator`/`ITransform` implementations and asserts none exist outside the plugin host).

### G13 — Mechanic correctness harness (credibility gate)
**Objective.** Prove the *whole* mechanic layer is right, not just the substrate.
**Deliverables.** (a) **Exact-vs-sampled cross-check** over generated configs exercising
primitives + catalog subgraphs + **fold/map expression** leaves; (b) hand-computed full games
incl. cascade (Loop + filter-remove + refill subgraphs), free-spins-with-retrigger (Loop +
scatter-trigger subgraph), Hold & Win (loop-until-no-new-lands subgraph), cluster+tumble+rising-
multiplier (fold flood-fill + cascade + state-multiplier); (c) determinism; (d) serialization
round-trip; (e) plugin conformance; (f) **fast-path equivalence** for any fast-path that exists.
**Definition of Done.**
- [ ] Cross-check passes over ≥50 generated configs × ≥20 seeds (sampled RTP within exact ±
      3·stdErr).
- [ ] Every hand-computed game matches exactly (exact path) or within CI (when a plugin forces
      sampled).
- [ ] Round-trip yields identical config hash and identical metrics; coverage ≥ 90% on the kernel
      + catalog subgraph layer.

---

# Phase 3 — Compiler, API, persistence, jobs

### G14 — Graph → program compiler + validation
**Objective.** Compile the visual graph (primitives + catalog subgraphs + expressions + plugin
refs) to a runnable program, with strict validation.
**Deliverables.** Compiler emitting `Slot<S,T>`; inlines subgraph references; compiles
expression-valued ports (including fold/map/filter) and resolves + sandboxes plugin references.
Validation: acyclic except through `Loop`; edge type compatibility; **expression type-checking**
(including fold/map lambda bodies); **plugin signature/conformance** check; exactly one
MetricsSink; unreachable/dead-node detection; precise located errors.
**Definition of Done.**
- [ ] Valid graphs (incl. catalog subgraphs, fold/map expressions, and a plugin) compile to a
      runnable program.
- [ ] Each invalid case — cycle without `Loop`, type-mismatched edge, ill-typed expression,
      non-conformant/missing plugin, missing/duplicate sink, dead node — is rejected with a
      precise error naming the offending node id.

### G15 — Minimal API (vertical slices)
**Objective.** The HTTP surface.
**Deliverables.** Slices: config CRUD + versioning; `POST /validate`; `POST /evaluate/light`
(regime-aware fast metrics); `POST /runs`; plugin registry endpoints. OpenAPI.
**Definition of Done.**
- [ ] OpenAPI generates and is served; integration tests cover each slice (happy + error).
- [ ] `/validate` returns the same errors as the compiler.
- [ ] `/evaluate/light` p95 < 300 ms for MVP-class configs; returns exact when cheap, else a
      small-N sampled estimate with CI, else `needsFullRun`.

### G16 — Persistence + result cache
**Objective.** Durable configs and memoised results.
**Deliverables.** EF Core 10 + Postgres (`Projects`, `ConfigVersions`, `Runs`, `Users`, `Plugins`);
migrations; Redis result cache keyed by canonical config hash.
**Definition of Done.**
- [ ] Migrations apply cleanly to an empty DB in CI (real Postgres container).
- [ ] Save → load → re-save preserves all versions; history queryable.
- [ ] An identical config (same canonical hash) returns a cache hit with no recomputation
      (internal counter).

### G17 — Heavy-run jobs + SignalR streaming
**Objective.** Long runs streamed live and cancellable.
**Deliverables.** Hangfire job (full-cycle exact and/or large Monte Carlo); SignalR hub streaming
batched `(sampleCount, runningRtp, stdErr)`; cancellation threaded into the sampler; result
persisted + cached.
**Definition of Done.**
- [ ] A 1,000,000-spin run streams progress and persists a result; running RTP within 0.5% of
      exact by 100,000 spins on the reference game.
- [ ] Cancel stops within 500 ms and persists partial stats; a reconnecting client re-attaches.

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
**Deliverables.** `@xyflow/react` canvas; palette of **primitive** nodes (Draw/State/Loop/Branch/
Map) and **catalog subgraph** nodes; edge connection with visual type-checking; explicit `Loop`
node UX; inline validation; subgraph collapse/expand.
**Definition of Done.**
- [ ] A user can build a base + scatter→free-spins + cascade graph visually using primitives and
      catalog subgraphs.
- [ ] Invalid connections are blocked or flagged inline with the compiler message.
- [ ] Editing stays responsive (no main-thread block > 50 ms on node/edge changes).

### G20 — Table data editors
**Objective.** Replace Excel for data with good table UX (invariant #7).
**Deliverables.** Symbol editor + paytable grid; reel-strip builder; state structure config;
ReelSet assembly.
**Definition of Done.**
- [ ] Symbols/paytable/strips edit fluidly with keyboard nav and bulk clipboard paste; edits
      validate against the Zod schema before submit.

### G21 — Expression editor (level b)
**Objective.** The authoring surface that makes the constructor flexible without code.
**Deliverables.** CodeMirror-6 typed expression inputs on expression-valued ports (multiplier,
weight, predicate, state-array transform, payout adjust); **autocomplete over state fields and
array operations (fold/map/filter syntax)**, live type-checking, inline errors mirroring the
backend type-checker.
**Definition of Done.**
- [ ] A user can write `fold(paylines, 0, (acc, line) => acc + ...)` and a compound predicate,
      and see live validation; an ill-typed expression is flagged with the same message the
      backend gives.
- [ ] A saved expression round-trips and drives the live metric for that node.

### G22 — Custom-mechanic authoring + plugin management
**Objective.** Grow the catalog in-product (level a/b composition) and govern plugins (level c).
**Deliverables.** Compose + **name a reusable subgraph** as a custom catalog entry, reusable on
the canvas. Plugin management UI: register/select a plugin, view its contract and **conformance
status**, and surface that a game using it is sampled-regime.
**Definition of Done.**
- [ ] A user composes a named custom mechanic from primitives + catalog subgraphs + expressions
      and reuses it in another graph (test/E2E).
- [ ] Selecting a non-conformant plugin is blocked with the reason; a conformant one shows its
      contract and the sampled-regime flag.

### G23 — Live per-edge metrics
**Objective.** See what every mechanic does to the distribution, instantly.
**Deliverables.** Debounced `/evaluate/light` on edits; per-edge / per-node distribution preview +
RTP / hit-freq / variance badges; **provenance** badge on every value.
**Definition of Done.**
- [ ] Changing a symbol weight or an expression updates per-edge metrics within the latency budget.
- [ ] Provenance badge is always present and correct; a too-heavy config shows `needsFullRun`, not
      stale numbers.

### G24 — Simulate panel
**Objective.** Heavy runs, live, with the exact-vs-sampled story visible.
**Deliverables.** Run trigger; live uPlot RTP convergence (SignalR); visx hit histogram; progress +
cancel; exact-vs-sampled comparison view.
**Definition of Done.**
- [ ] The convergence curve streams smoothly to ≥1M points without jank; cancel freezes the
      partial curve; the comparison shows the sampled mean inside the exact CI band on the
      reference game.

### G25 — Export / import + PAR sheet
**Objective.** Get the math out, share it, hand it off.
**Deliverables.** Versioned JSON export/import; copy/download; shareable link; PAR-sheet-style
export (CSV + printable summary with full metric breakdown and provenance).
**Definition of Done.**
- [ ] Export → import round-trips to an identical config hash; a shareable link reopens the exact
      graph + data; the PAR export contains the full breakdown with provenance noted.

---

# Phase 5 — AI

### G26 — AI gateway + NL → graph
**Objective.** Describe a game in words, get a starting graph (including expression leaves).
**Deliverables.** Backend AI gateway (server-side key). Structured output: NL → graph JSON
(catalog subgraph references + expression-valued ports) validated against the schema; one repair
pass on validation failure; reject after.
**Definition of Done.**
- [ ] A valid NL spec yields a graph that passes the G14 compiler validation, including any
      generated expressions.
- [ ] Malformed model output never throws — valid graph or clean user-facing error.
- [ ] The provider key is read only server-side (no key reaches the client bundle; verified).

### G27 — Auto-tune / inverse design
**Objective.** Hit a target RTP within volatility/max-win constraints, using the exact engine as a
cheap objective.
**Deliverables.** Backend optimisation job (GA / coordinate search) over tunable parameters —
reel-strip weights, tables, and **expression constants** — with bounds; objective
`|RTP − target| + penalties`; exact engine for fitness where feasible (else sampled with sufficient
n); streamed best-so-far; "apply suggestion" mutates the graph.
**Definition of Done.**
- [ ] On the reference game it converges to within the target RTP band while respecting volatility
      and max-win constraints inside a bounded budget.
- [ ] Progress streams; cancel keeps best-so-far; "apply" mutates the graph and re-derived metrics
      match the target.

### G28 — Explain + compliance lint
**Objective.** Tell the mathematician why, and catch mistakes.
**Deliverables.** Explain: AI reads the distribution + graph for variance drivers / RTP
concentration in plain language. Lint: rule-based checks (RTP out of band, max-win-cap
probability, dead/unreachable symbol, paytable anomaly) + AI qualitative notes.
**Definition of Done.**
- [ ] Explain output only cites numbers passed in from the current distribution (no invented
      figures).
- [ ] Each lint rule fires on a crafted failing config and stays silent on a clean one, and links
      to the offending node/symbol.

---

# Phase 6 — Production hardening & ship

### G29 — Security, limits, multi-tenancy & plugin governance
**Objective.** Safe for the open internet, including the plugin hatch.
**Deliverables.** Anonymous use works; OAuth + JWT gating saved/shared projects; project ownership +
sharing; rate limiting; input/budget caps (max graph size, nodes, sim budget, expression
complexity, subgraph nesting depth); **plugin governance** — only trusted/approved plugins run,
executed sandboxed.
**Definition of Done.**
- [ ] Anonymous can build + simulate but not persist privately; an authed user owns/shares and
      non-owners cannot mutate.
- [ ] Oversized graphs, over-budget sims, over-complex expressions, and over-deep subgraph nesting
      are rejected with clear errors; only approved plugins execute; rate limits verified.

### G30 — Observability & error handling
**Objective.** Operable in production.
**Deliverables.** Structured logging; OpenTelemetry traces + metrics; health/readiness; RFC-7807
problem-details; frontend error boundaries.
**Definition of Done.**
- [ ] A trace spans a full request incl. a heavy run; health endpoints reflect DB + Redis (test
      toggles a dependency); a forced backend error renders a graceful UI (no blank screen).

### G31 — Docker, CI/CD & deploy
**Objective.** One pipeline from commit to live.
**Deliverables.** Multi-stage Dockerfiles; docker-compose (api + postgres + redis); GH Actions
(build → unit → integration on real Postgres/Redis → lint → image); deploy to Railway (backend +
Postgres + Redis) and Vercel/Railway (frontend).
**Definition of Done.**
- [ ] `docker compose up` brings the stack up with seed data (smoke test hits a health endpoint).
- [ ] CI runs the full matrix incl. integration and fails on any red; a merge to `main` deploys
      and the URL serves the working app end to end.

### G32 — Docs, E2E & production acceptance
**Objective.** Hand-offable and proven, including the flexibility story.
**Deliverables.** README; architecture doc; **slot-math explainer**; **authoring guide** (levels
a/b/c, writing fold/map expressions, building a custom mechanic as a subgraph, registering a
plugin); API docs; runbook. Playwright golden-path E2E.
**Definition of Done.**
- [ ] The Playwright golden path (build a game → live metrics → run sim → auto-tune → export)
      passes headless in CI.
- [ ] A non-iGaming developer can follow the explainer; a designer can follow the authoring guide
      to build a custom mechanic as a pure subgraph with fold/map expressions — zero C# code.
- [ ] The Production acceptance checklist below is fully green.

---

## Production acceptance — the 100% bar

- [ ] Exact RTP matches closed form for every hand-computed game (rational equality).
- [ ] **Flexibility (no C# molecules):** a non-trivial novel mechanic is buildable as a pure
      **subgraph** from atoms + fold/map expressions (zero C# code, zero interpreter/compiler
      changes), and its exact RTP matches the hand-computed value. A **plugin** evaluator passes
      conformance, runs through the standard interpreter path, and is correctly flagged
      sampled-regime. The standard library ships **zero** `IEvaluator`/`ITransform` implementations.
- [ ] Exact-vs-sampled cross-check (incl. fold/map expression leaves) passes; sampled within
      exact ± 3·stdErr.
- [ ] Running RTP converges within 0.5% of exact by 100k spins on the reference game; alias
      passes chi-squared at α = 0.01.
- [ ] Every displayed metric carries correct provenance; adding a new catalog mechanic (subgraph)
      needs **no C# code** and no interpreter/compiler change.
- [ ] Deep-recursion programs never stack-overflow; heavy runs cancellable; identical configs hit
      the cache; export/import round-trips losslessly.
- [ ] Misbehaving plugins are sandboxed and contained; auth, rate limits, input/budget/expression
      caps and plugin governance enforced; secrets server-side only.
- [ ] Full stack deploys via CI; Playwright golden path green; observability live.

## Deferred (later)

PixiJS reel **preview** renderer; custom payline **drawing** tool; in-browser WASM mirror of the
kernel; collaboration/multiplayer; sound. (The engine's flexibility does not depend on these.)
