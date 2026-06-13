# PROGRESS — Slot Math Lab (PRD v3.1)

This ledger tracks conformance work against **CLAUDE.md v3.1**. Per the agent execution
protocol, on completing a goal append one line in the strict format:

```
| G<n> | <commit-hash-short> | <key numbers> |
```

## Normative targets (D9 reference fixtures)

These closed forms are part of the contract and are the standing oracle set:

| Fixture | RTP | Variance | Notes |
|---------|-----|----------|-------|
| REF-A "Coin"      | 3/4 (0.75)            | 15/16 (0.9375)     | hit freq 1/2; max win 3; P(cap)=0 |
| REF-B "Retrigger" | 15/17 (≈0.88235)      | —                  | per-label: base=3/4, freespins=9/68; ExactInterval, cap-deficit < 1e-9 |
| REF-C "MiniCascade" | 94/965 (≈0.09741)   | —                  | equals independent brute-force enumerator exactly; cascade cap 10, win cap 100 |
| REF-D "Volcano"   | 1/2 (0.5)             | 2499.75            | P(cap reached)=1/10000 exactly with win cap 1000 |
| REF-E "PluginPassthrough" | 3/4 (sampled) | —                  | ITransform plugin forces `Sampled` provenance |

Pinned seeds: `SEED_MAIN = 0xC0FFEE`; cross-check seed list `0x5EED0001…0x5EED0014` (20 seeds).

## v3.1 conformance ledger

| Goal | Commit | Key numbers |
|------|--------|-------------|
| v3.1-foundations | 7ea39e4 | Constants file (D3/D6/D7/D8/D9/D16/D18) backend + frontend mirror; PROGRESS.md initialized |
| G3 (PRNG, D3) | 379f259 | xoshiro256** + SplitMix64 stream splitting; alias chi-squared pinned to SEED_MAIN (salt=1) passes all 12 profiles at α=0.01; negative control rejects perturbed weights; 41 Random tests green |
| D5 (provenance) | 8bd1983 | Taxonomy {Exact, ExactInterval(lo,hi,prunedMass,boundSource), ExactWithMassLoss, Sampled(n,mean,stdErr,ci95,capHits,loopCapHits)} + aggregation rule + IsRegulatory; 747 Core tests green |
| D1 (Rational) | cab49d2 | Canonical Rational value type (reduced, den>0, 0/1); REF-A RTP 6/8=3/4 by rational equality; 760 Core tests green |
| G4/D13/D6 (Emit + caps) | c1fd1da | Emit(label,amount:Rational) substrate primitive + capped Loop (cap≤100000) in all 3 interpreters; trampoline per-label accumulation; sampled REF-A→0.75, REF-C→94/965, REF-D→0.5 (4·stdErr), REF-D cap P≈1/10000; loop force-exits at cap w/ LoopCapHit; 770 Core tests green |
| G5/G7 (exact Emit) | 3a8bffb | ExactEmitInterpreter: Slot<S,Unit>→Dist<Rational> total win + per-label E (D4). REF-A exact RTP=3/4, Var=15/16, hit=1/2 by rational equality, base=3/4, provenance Exact. REF-C exact == independent enumerator E(10), ≤94/965, |·−94/965|≤1e-12, memoisation collapses (CacheHits>0). 772 Core tests green. |
| G5/D6 (REF-B exact) | c4ac709 | Truncate primitive (D6 bounded unrolling: path mass → pruned on exact, ends round on sampled). REF-B exact via (remaining,budget) DAG: 15/17 − r = 6.1e-14 ≤ 1e-9, r ≤ 15/17, provenance ExactInterval; per-label base→3/4, freespins→9/68 summing EXACTLY to total. Fixed DistBuilder.Add losing pruned mass on empty sub-distributions. 773 Core tests green. |
| G2/D2 (canonical hash) | 02aa384 | CanonicalJson (ordinal-sorted keys, compact, float-free) + ConfigHash/SubgraphHash (SHA-256). Order-insensitive (key order → same hash), one-bit change flips hash; API CanonicalHash now delegates to Core (G16 order-insensitive cache keys). 778 Core tests green. |
| G14/D6/D7/D20 (validation) | (this commit) | Coded errors OVER_MAX_LOOP_CAP / CIRCULAR_SUBGRAPH_REFERENCE / SUBGRAPH_NESTING_TOO_DEEP (+ MISSING_WIN_CAP, PLUGIN_ABI_MISMATCH, EXPRESSION_BUDGET_EXCEEDED reserved). Loop-cap bounds [1,100000] validated; static pre-inline subgraph cycle + nesting-depth(≤16) check naming the node. 789 Core tests green. |
