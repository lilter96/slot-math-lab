# Slot Math Lab — Product & Build Spec (PRD) — v4.0

**This is a build contract for an agentic coding system.** It specifies **goals** and
**definitions of done** only — no implementation code. The agent writes all code, and each
goal's Definition of Done (DoD) is its acceptance gate.

A high-assurance, compiler-first infrastructure platform for iGaming slot mathematics. Designers
build slot math on an abstract graph; the system mathematically proves exact RTP, run-time behavior,
and compiles the model directly into zero-allocation, production-ready C# code.

**The Single Source of Truth (SSOT) Thesis.** The core problem of slot game development is
mathematical fragmentation. A single game historically exists as four distinct, drift-prone
implementations: *Excel models*, *written specifications*, *manual backend code*, and *QA test scripts*.
Slot Math Lab collapses this pipeline into one immutable **Graph Truth**:

$$\text{Graph Truth} \quad \Longrightarrow \quad \text{Everything Else Derived}$$

The mathematician designs the graph $\to$ the engine mathematically verifies exact RTP $\to$ the
compiler generates zero-allocation C# production code. Human translation error is reduced to zero.
There are **no C# molecule classes** in the standard library; all mechanics are data (JSON subgraphs).

**What changed in v4.**
1. **CLI-First Bootstrapping:** UI Canvas, cloud VM runtimes, and AI models are deferred to Year 2. Year 1 focuses strictly on a highly optimized command-line compiler/verification engine (`slotmath-cli`).
2. **Deterministic Replay (D24):** Core execution trace reproduction is elevated to a first-class citizen before any UI is built.
3. **Excel Migration Toolkit (EMT):** An onboard XLSX/CSV ingestion pipeline is introduced in Phase 1 as the primary commercial wedge to replace legacy spreadsheet workflows.
4. **Frictionless Flat Monetization:** Metered billing is removed for Year 1. Pricing is strictly flat-rate subscription.
5. **Private Knowledge Graph:** Slot configurations are treated as strict trade secrets. Multi-tenant storage is isolated and encrypted, with public data features made strictly opt-in.

---

## 0. Agent execution protocol

- Execute goals **in order within a phase**. Phases are sequential.
- Each goal's **DoD is a hard gate.** Do not start the next goal until every DoD item is
  objectively satisfied (tests green, compiles pass, tolerances met).
- **One commit per goal**, message `feat: G<N> <title>`. CI must be green at every commit on `main`.
- On completing a goal, append one line to `PROGRESS.md`: goal id, commit hash, and the key
  measured numbers (e.g. computed fixture RTPs, perf figures) in the strict format:
  `| G<n> | <commit-hash-short> | <key numbers> |`
- If a DoD item cannot be met, **stop and report** the blocker — never silently descope.
- The **Non-negotiable invariants** below are always-on; violating one fails the goal even if its
  DoD otherwise passes.
- All definitions (D1–D25) are normative. PBT tests must run with automatic shrinking (CsCheck/FsCheck) and contain custom edge-case generators.

---

## Non-negotiable invariants

1. **Exact path stays exact.** Probabilities and amounts on the exact path are rational (`BigInteger` numerator over positive denominator in lowest terms, canonical zero = `0/1`, D1); floats are display-only.
2. **No hardcoded mechanic molecules.** Mechanics are subgraphs (data, not C# classes). `IEvaluator` and `ITransform` are plugin contracts (level c) only — the standard library ships zero implementations of these interfaces.
3. **Layered authoring holds.** Level (a) is declarative subgraph config; level (b) expressions are pure, deterministic, terminating, and loop-free (except bounded fold/map over fixed-size arrays).
4. **Generic state, no engine types.** The recurrence state is the memoization key; the win accumulator is held separate and combined by convolution. State is fully user-defined — the engine has **no `Board` type**.
5. **Every metric carries provenance**: `Exact`, `ExactInterval { lo, hi, prunedMass, boundSource }`, `ExactWithMassLoss { prunedMass }` (non-regulatory), or `Sampled { n, stdErr, ci95, capHits, loopCapHits }` (D5).
6. **Determinism.** A given seed yields an identical sample sequence and identical stats anywhere — including under parallel sampling and regardless of thread count or physical CPU topology (D3).
7. **Tables, not nodes, for data.** Symbols, paytable, reel strips, and state structure config are tables, never graph nodes.
8. **Loops only via explicit `Loop`/fixpoint nodes, and every `Loop` carries an iteration cap.** Every game has a top-level round win cap on the Metrics Sink.
9. **Private-by-default.** Client configurations and mathematical profiles are trade secrets; they are stored in encrypted, isolated Private Knowledge Graphs per client. Cross-studio sharing is strictly opt-in.
10. **The kernel is pure.** No I/O, no framework, no shared mutation.
11. **Fast-paths are proven equivalent.** C# performance implementations of catalog subgraphs must be verified via **Property-Based Testing (PBT)** over at least 1,000 (recommended 10,000+) cases with automated shrinking and dedicated edge-case generators (D25).
12. **Statistical gates are deterministic and falsifiable.** Pinned seeds; theoretical false-fail probability ≤ 10⁻³ per suite; every statistical harness ships a negative control that must fail on corrupted input (D8).

---

## Tech stack

**Backend (.NET 10)** — C# math kernel `SlotMath.Core` (substrate + exact/sampled interpreters + level-(b) expression evaluator including fold/map/filter, all pure); C# CLI tool `slotmath-cli`; standard catalog (JSON subgraph definitions, no C# classes). C# host `SlotMath.Api` (Minimal APIs + Testcontainers integration). EF Core 10 + Postgres; Redis; Hangfire; SignalR.

**Frontend (Year 2)** — React 19 + TS, `@xyflow/react`, Zustand, TanStack Query, CodeMirror 6, uPlot.

---

## Definitions (normative)

### D1 — Numbers, canonical rational form, expression partiality
- A **rational** is a `BigInteger` numerator over a `BigInteger` denominator in lowest terms; canonical zero is `0/1`.
- Partial operations (division by zero, out-of-range index, empty `min`/`max`) yield **deterministic, located evaluation errors** (containing `Code`, `NodeId`, `Message`, and `Location`) on both paths. `sum`, `product`, `count` over empty arrays return their identities (0, 1, 0).

### D2 — Canonical serialization & config hash
- **Canonical JSON**: UTF-8; object keys sorted by ordinal; no insignificant whitespace; rationals serialized as `"n/d"`.
- `configHash` = SHA-256 over canonical JSON of the fully resolved config (subgraphs inlined content-addressed by their own SHA-256 `subgraphHash`).

### D3 — PRNG & parallel determinism
- Pinned PRNG: **xoshiro256\*\*** seeded via SplitMix64.
- Deterministic multi-threading: work split into fixed chunks of `CHUNK = 65,536` rounds; chunk *i* runs on stream *i* seeded via `SplitMix64(masterSeed, i)`; chunk results merged in strict ascending chunk index order (guarantees bit-identical stats regardless of CPU core topology).

### D4 — Metric definitions
- **RTP** = E[total round net win] / bet (D13). **Hit frequency** = P(total round net win > 0). **Volatility** = variance/std of total net win.
- **Per-feature RTP** = expected value of each `Emit` label. Sum of per-label RTPs equals total RTP exactly on the exact path.

### D5 — Provenance taxonomy
- `Exact` — no pruning, no truncation mass, no plugins; exact rational.
- `ExactInterval { lo, hi, prunedMass, boundSource }` — ε-pruning/loop-truncation occurred with proven/declared win cap. `boundSource` declares the bound provenance (`ProvenMaxWin`, `DeclaredWinCap`, `UserCap`).
- `ExactWithMassLoss { prunedMass }` — exact calculation with mass loss where max win is unproven. Marked as *non-regulatory: cannot be used for certification without additional review*.
- `Sampled { n, mean, stdErr, ci95, capHits, loopCapHits }`.

### D6 — Loop policy, caps, and recurrence
- Every `Loop` has an iteration cap (default 1,000, max 100,000). Every game requires a declared win cap on the Metrics Sink.
- Recurrence modeling: recurrent loop iteration (e.g. retrigger) modeled as a **subcritical Galton-Watson branching process** with offspring distribution $Z \in \{0, 3\}$, $P(Z = 3) = 0.05$, offspring mean $m = 0.15$. The probability of exceeding loop cap 1,000 is mathematically bounded by $\le 10^{-15}$, ensuring exact-path safety under the cap.

### D7 — Latency baseline hardware
- Performance benchmarks (`/evaluate/light` p95 < 300 ms, REF-C exact < 2 s) are evaluated on standard **GitHub Actions Standard Runner (2 vCPU, 8 GB RAM, Linux)**.

### D8 — Statistical test policy
- Pinned seeds. Tolerances set so false-fail probability is ≤ 10⁻³ per suite. G13 cross-check: comparisons must satisfy $|sampledRTP - exactRTP| \le 4\sigma$ except at most 2 of 1,000, and 0 beyond $6\sigma$.
- **Negative controls**: every statistical harness includes a corrupted case (e.g. weights shifted by 1%) that must fail.

### D9 — Reference fixtures (normative)
- **REF-A "Coin"** — pay 3 with weight 1, pay 1 with weight 3, pay 0 with weight 4. RTP = 3/4, Var = 15/16.
- **REF-B "Retrigger"** — pay draw REF-A labeled `base` + trigger draw (weight 1/20). Trigger awards 3 free spins. Free spin = pay draw REF-A labeled `freespins` + retrigger draw (weight 1/20, awards 3 spins). Offspring mean $m = 0.15$. Exact capped value r: $15/17 - 10^{-9} < r \le 15/17$.
- **REF-C "MiniCascade"** — 3-cell row i.i.d. from {P: weight 2, pay 5; Q: weight 3, pay 2; R: weight 5, pay 0}; three-of-a-kind P or Q pays and triggers cascade (redraw). Cascade cap 10. RTP $v_{capped}$ is exact rational satisfying $|v_{capped} - 94/965| \le 10^{-12}$, and $P(\ge 10 \text{ cascades}) = (35/1000)^{10} \times 1000/965 \approx 2.85 \cdot 10^{-15}$. Win emitted through `cascade_win` label.

### D11 — State Hashing, Identity & Canonicalization
- **CanonicalStateIdentity**: structural equality. Ordering of keys, arrays, enums as names, `missing != null`, rationals as `"n/d"`.
- **RuntimeStateHash (Memoization)**: zero-allocation structural hash (xxHash64 / MurmurHash3) over flat key-value structs in the hot execution path.
- **ConfigHash**: canonical JSON hash over the compiled AST.
- **Invariant**: `RuntimeStateHash != ConfigHash` is a normative contract.

### D12 — Exact Interpreter Memoization & Program Point
- Memoization key in exact-path is:
  $$\text{MemoKey} = (\text{RecurrenceState}, \text{ProgramPoint})$$
  where `ProgramPoint` is the canonical ID of the execution continuation cursor in the free monad.

### D13 — Win & Payout Semantics
- `Emit(label, amount)` is the only payout generator. RTP is calculated over `Expected(NetRoundWin)`. Negative payouts allowed. Win cap applied at Metrics Sink.

### D14 — Adaptive Histogram Representation (Sampled Path)
- **Exact Path**: lossless PMF map stored exactly.
- **Sampled Path**: decade-based adaptive histogram: `0..100x` (1x steps), `100..1,000x` (10x steps), `1,000..10,000x` (100x steps), `≥10,000x` (tail bucket).

### D15 — State-Space Complexity Model
- Static complexity computed by compiler: `EstimatedUniqueStates` (State Space Expansion) and `ExpressionCost`.
- Loop worst-case: $\text{LoopWorstCaseCost} = \min(\text{LoopCap}, \text{StateBudget}) \times \text{BodyCost}$.

### D24 — Reproducibility & Deterministic Replay Contract
- Given a `ReplayTuple = (configHash, seed, runtimeVersion, initialPlayerState)`, the system must deterministically reproduce the identical metrics, identical sample sequences (draws), and identical state/emit traces on any OS and core topology.

### D25 — Distribution Equivalence Contract
- Fast-Path C# optimization is equivalent to its canonical subgraph if and only if they yield identical probability mass functions (PMF): $\forall x \in \text{Outcomes}, P_{\text{fast}}(x) = P_{\text{canonical}}(x)$.

---

## Phase overview & 12-Month Bootstrapping Plan

| Phase | Theme | Goals |
|------|-------|-------|
| 0 | Mathematical Kernel & IR Freeze (Sprints 1–3) | G1–G5 |
| 1 | High-Assurance Codegen & Excel Migration (Sprints 4–5) | G6–G10 |
| 2 | CLI Refinement, Testing & Verification | G11–G13 |
| 3 | Minimal API, Persistence & Jobs (SaaS Backend) | G14–G17 |
| 4 | Frontend Canvas, Visual Editor & Replay (UI Shell) | G18–G25 |
| 5 | Advanced AI, Compliance & Production Hardening | G26–G32 |

---

# Phase 0 — Mathematical Kernel & IR Freeze (Sprints 1–3)

### G1 — Monorepo, tooling, CI skeleton
**Objective.** Reproducible repo with both stacks building and a green CI.
**Deliverables.** Monorepo structure; .NET 10 solution (`SlotMath.Core`, `SlotMath.Core.Tests`); CI setup; `PROGRESS.md` initialized.
**Definition of Done.**
- [ ] From a clean clone: backend `dotnet build` and `dotnet format --verify-no-changes` pass.
- [ ] `PROGRESS.md` initialized.

### G2 — Graph IR Freeze (Sprint 1)
**Objective.** Freeze the intermediate representation (IR) as the absolute schema to prevent compile/codegen breakages downstream.
**Deliverables.** C# record-based model for `GraphConfig`, `Node`, `Edge`, `State`, `Emit`, `Draw`; JSON schema generation; canonical serialization and `configHash` / `subgraphHash` generation (D2, D11).
**Definition of Done.**
- [ ] A corpus of sample graphs (REF-A through REF-C) round-trips losslessly through JSON serialization preserving `configHash` bit-for-bit.
- [ ] Invariant `RuntimeStateHash != ConfigHash` enforced.

### G3 — Seeded PRNG & Walker–Vose Alias Sampler
**Objective.** Reproducible O(1) sampling from rational/integer weights.
**Deliverables.** **xoshiro256\*\*** with SplitMix64 seeding (D3); alias sampler converting rational weights to a common denominator; chi-squared validation harness on pinned seeds with a negative control.
**Definition of Done.**
- [ ] Seed -> identical sequence on ARM64 and x64.
- [ ] Pinned seed output passes chi-squared at α = 0.01; negative control with shifted weights **fails** (invariant 12).

### G4 — Free Monad & Deterministic Replay (Sprint 2)
**Objective.** Algebraic substrate with absolute execution trace reproduction.
**Deliverables.** Sealed node hierarchy (`Pure`, `Draw`, `Emit`, `GetState/PutState/Modify`, `Loop` with cap, `Branch`, `Map`); stack-safe trampoline interpreter; `ReplayTuple` execution trace logger (D24).
**Definition of Done.**
- [ ] Monad laws hold; `Emit` is additive, commute-safe, and cannot read/write state.
- [ ] Given a `ReplayTuple` (D24), the trace of draws, state modifications, and emits is 100% identical on ARM64 Mac and x64 Linux.

### G5 — Exact Interpreter & Canonical Oracles (Sprint 3)
**Objective.** Exact rational distribution over user state, validated against mathematical oracles.
**Deliverables.** `Exact` interpreter; memoization keyed on `MemoKey = (RecurrenceState, ProgramPoint)` (D12); ε-pruning with `ExactInterval` boundaries using declared win cap (D5).
**Definition of Done.**
- [ ] **REF-A**: exact RTP equals `3/4` by rational equality.
- [ ] **REF-C**: exact RTP matches independent brute-force enumerator exactly (rational equality), cascade win emitted through `cascade_win` label.
- [ ] **REF-B**: exact capped RTP $r$ satisfies $15/17 - 10^{-9} < r \le 15/17$ with `boundSource = DeclaredWinCap` (D5).

---

# Phase 1 — High-Assurance Codegen & Excel Migration (Sprints 4–5)

### G6 — Sampled Interpreter & Parallel Determinism
**Objective.** Multi-threaded Monte Carlo with guaranteed bit-identical results.
**Deliverables.** Sampled interpreter; adaptive histogram (D14); parallel execution chunking with independent streams and ordered merge (D3).
**Definition of Done.**
- [ ] **Parallel determinism:** Same `(seed, n)` produces bit-identical stats at 1 thread and 8 threads.
- [ ] REF-D volcanoes record deterministic win cap hits at $10,000$ spins (pinned seed).

### G7 — High-Assurance C# Codegen (Sprint 4)
**Objective.** Compile the abstract graph into high-performance, zero-allocation C# production code.
**Deliverables.** `GraphCompiler` compiling `GraphConfig` directly to a compiled `.cs` file; property-based verification harness (CsCheck).
**Definition of Done.**
- [ ] Compiles valid graphs to clean C# code; generated code has $O(1)$ allocations on the hot execution path.
- [ ] **Distribution Equivalence (D25) PBT:** exact PMF of compiled C# code is identical to exact interpreter execution on 1,000+ random states with automated shrinking.

### G8 — Subgraph Inliner
**Objective.** Inlines CustomMechanics as pure data-flow configurations.
**Deliverables.** `SubgraphInliner` performing macro expansion of `LibraryNode` before compilation (ID namespacing, port-name boundary rewiring, nesting cap verification).
**Definition of Done.**
- [ ] Subgraphs are expanded, inlined, and compiled successfully; circular subgraph references are detected and rejected with a precise compiler error (D7).
- [ ] A 10-level nested subgraph evaluates without stack overflow.

### G9 — Bounded Iteration in Expressions
**Objective.** Deliver map, filter, and fold operations inside the AST evaluator.
**Deliverables.** Bounded `fold`, `map`, `filter` execution; array-index and state-array read/write; fold nested-recursion prevention.
**Definition of Done.**
- [ ] Bounded fold/map/filter evaluate correctly in rationals; nested folds are rejected at compilation.

### G10 — Excel Migration Toolkit (EMT) (Sprint 5)
**Objective.** The primary B2B acquisition wedge — ingest XLSX models directly into executable code.
**Deliverables.** XLSX ingestion parser; auto-generator producing starting `GraphConfig` (ingesting paylines, paytables, reel matrices); differential exact RTP verification.
**Definition of Done.**
- [ ] **The Ultimate Milestone:** Ingests a real Excel slot model, compiles to C#, runs exact evaluation, and proves exact RTP matches the Excel original; runs 100M simulated spins on the compiled C# code and proves it converges to the same exact RTP with zero manual code written.

---

# Phase 2 — CLI Refinement, Testing & Verification

### G11 — Expression Language (Level b) DSL
**Objective.** Complete the mathematical DSL with parsing and type-checking.
**Deliverables.** Parser; type-checker; error diagnostic system with stable error codes (D20); expression cost model evaluator (D16).
**Definition of Done.**
- [ ] Parses and type-checks complex expressions; over-budget expressions (>10,000 ops) are rejected.

### G12 — Plugin Host Sandbox (Level c)
**Objective.** Secure, sandboxed environment for custom C# execution.
**Deliverables.** Sandboxed plugin host utilizing isolated `AssemblyLoadContext` with memory/time caps and no I/O.
**Definition of Done.**
- [ ] Trivial plugin (REF-E) executes successfully; misbehaving plugins (infinite loops/I/O attempts) are terminated safely.
- [ ] Invariant check: standard library contains zero `IEvaluator`/`ITransform` implementations.

### G13 — Complete Correctness & Verification Harness
**Objective.** Complete statistical verification of the entire math stack.
**Deliverables.** Cross-check runner comparing exact vs sampled on 1,000 generated configs under 4-sigma thresholds; `VERIFICATION.md` automated generator.
**Definition of Done.**
- [ ] Cross-check passes (at most 2/1,000 breaches exceed 4σ, 0 beyond 6σ); negative control (shifting exact RTP by 1%) **fails** the cross-check.
- [ ] `VERIFICATION.md` is generated in CI with all REF-A...E fixtures.

---

# Phase 3 — Compiler, API, persistence, jobs

### G14 — Compiler Validation & Schema Emitter
**Objective.** Strict static validation of the fully resolved inlined graph.
**Deliverables.** Comprehensive static validator (checking loops, win caps, edge types, compile-time complexity estimation D15).
**Definition of Done.**
- [ ] Rejects invalid graphs with precise located errors (D20) containing stable error codes.

### G15 — Minimal API (Vertical Slices)
**Objective.** High-performance HTTP endpoints.
**Deliverables.** Slices: config CRUD, `/validate`, `/evaluate/light` (implementing D7 GActions runner baselines), `/runs` with Idempotency-Key.
**Definition of Done.**
- [ ] `/evaluate/light` returns exact or sampled metrics under 300 ms on standard hardware.

### G16 — Persistence & Redis Cache
**Objective.** Appendix-only database storage and cache.
**Deliverables.** EF Core + Postgres schema; Redis results cache keyed by `configHash`.
**Definition of Done.**
- [ ] Save/load round-trips; identical `configHash` hits the cache with byte-identical output.

### G17 — Heavy Jobs & SignalR Progress
**Objective.** Background processing of heavy simulations.
**Deliverables.** Hangfire jobs; SignalR progression hub with monotonic sequence numbers.
**Definition of Done.**
- [ ] Long runs are processing, streaming progress, and can be cancelled within 500 ms.

---

# Phase 4 — Frontend Canvas, Visual Editor & Replay (UI Shell) (Year 2)

*(G18-G25 are deferred to Year 2. They will build the React 19 / Vite / xyflow canvas visual editor, CodeMirror 6 expression editor, uPlot convergence panels, and PAR-sheet exports, driving the compiled Graph Configs directly into the CLI-first backend compiler.)*

---

# Phase 5 — Advanced AI, Compliance & Production Hardening (Year 2)

*(G26-G32 are deferred to Year 2. They will deliver the AI structured NL->graph generator, AI parameter auto-tuner/inverse-designer, rule-based and AI slot compliance linters, OAuth security, OpenTelemetry observability, and multi-stage Docker deployment pipelines.)*
