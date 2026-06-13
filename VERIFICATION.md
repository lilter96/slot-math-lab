# VERIFICATION — D9 reference fixtures

**Auto-generated** by `VerificationReportTests` (G32). Every figure below is computed
by the exact engine and checked against its D9 closed form on each CI run — the
standing proof that the math is right. Amounts are exact rationals (`n/d`, D1).

| Fixture | Quantity | Expected (D9) | Computed | Provenance | Match |
|---------|----------|---------------|----------|------------|-------|
| REF-A Coin | RTP | `3/4` | `3/4` | Exact | ✅ |
| REF-A Coin | Variance | `15/16` | `15/16` | Exact | ✅ |
| REF-A Coin | Hit frequency | `1/2` | `1/2` | Exact | ✅ |
| REF-A Coin | per-label base | `3/4` | `3/4` | Exact | ✅ |
| REF-C MiniCascade | RTP (= brute-force enumerator E(10)) | `24936787564766770588929/256000000000000000000000` | `24936787564766770588929/256000000000000000000000` | Exact | ✅ |
| REF-D Volcano | RTP (uncapped) | `1/2` | `1/2` | Exact | ✅ |
| REF-D Volcano | P(cap reached @ 1000) | `1/10000` | `1/10000` | Exact | ✅ |
| REF-B Retrigger | RTP (enclosure of 15/17, 15/17 − r < 1e-9) | `≤ 15/17, deficit < 1e-9` | `26042462221705797522505268191706781622370022298468575939353415914836960256144980764912687/29514790517935282585600000000000000000000000000000000000000000000000000000000000000000000` | ExactInterval | ✅ |

All computations use exact `BigInteger` rationals (invariant 1); REF-C is checked
against an independent brute-force enumerator that imports nothing from `SlotMath.Core`.
