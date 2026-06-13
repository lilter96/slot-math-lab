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
| v3.1-foundations | (pending) | Constants file (D3/D6/D7/D8/D9/D16/D18) backend + frontend mirror; PROGRESS.md initialized |
