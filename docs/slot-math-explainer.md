# Slot Math — What Every Number Means

This guide explains the math behind iGaming slot machines for developers and
designers who are new to the domain.

---

## Return to Player (RTP)

**RTP** is the long-run fraction of total wagers returned as wins.

```
RTP = E[win] / wager = sum(win_i × P(win_i))
```

An RTP of **96%** means: for every €100 wagered, the house expects €4 profit over
many spins. Individual players win or lose unpredictably — RTP is only a statistical
guarantee over millions of spins.

**Regulatory bands**: most jurisdictions require RTP ∈ [85%, 100%]. Slot Math Lab
flags any config outside [80%, 100%] with a lint warning.

---

## Hit Frequency

**Hit frequency** is the probability that a single spin produces a win > 0.

```
hitFreq = P(win > 0)
```

A hit frequency of **35%** means roughly 1 in 3 spins wins something. Low-hit-freq
games feel "dry" but pay bigger when they hit; high-hit-freq games feel "alive" but
pay small amounts often.

---

## Volatility (Variance / Standard Deviation Index)

**Variance** measures how spread out the win distribution is.

```
Var = E[win²] - E[win]²
```

**Standard deviation** = √Var.

**Volatility index** (VI) is often defined as Var / (RTP × wager)².

| Volatility | Feel |
|-----------|------|
| Low (VI < 5) | Frequent small wins; bankroll-friendly |
| Medium (5–15) | Balanced; common in video slots |
| High (> 15) | Rare but large wins; needs deep bankroll |

---

## Max Win

The **max win** is the largest possible payout. Regulators often require
P(win ≥ maxWin) > 0 and may cap max-win-to-wager ratios.

---

## Weighted draws

Every spin outcome is produced by a weighted draw. Weights are integers:

```
weights = [3, 2, 1]
→ P(0) = 3/6  P(1) = 2/6  P(2) = 1/6
```

Slot Math Lab stores weights as integers (exact path) and converts to fractions
internally — no floating-point rounding.

---

## Exact vs Sampled computation

The **exact interpreter** enumerates every possible outcome and computes a rational
probability for each. This is only feasible for games with bounded state spaces.

The **sampled interpreter** runs millions of Monte Carlo spins, accumulating
running mean/variance (Welford algorithm). It reports a **confidence interval**:

```
CI₉₅ = mean ± 1.96 × stdErr    where stdErr = stdDev / √n
```

Every metric displayed by Slot Math Lab carries a **provenance tag** telling you
which interpreter produced it.

---

## Provenance tags

| Tag | Meaning |
|-----|---------|
| `Exact` | Rational closed-form result; no approximation |
| `ε-pruned` | Exact within a bounded interval; prunedMass reported |
| `Sampled` | Monte Carlo with n spins; stdErr and CI₉₅ reported |

---

## The cascade mechanic

A **cascade** (tumble/avalanche) removes winning symbols and refills from above,
potentially triggering additional wins in the same spin. This is modelled as a
`Loop` node with a state-dependent stop condition:

```
state = { board: [...], winAccum: 0 }
Loop until no_new_wins(state):
  evaluate wins
  remove winning cells
  refill board
  accum wins
```

The exact interpreter memoises on the board state hash, keeping the computation
tractable for boards with bounded symbol sets.

---

## Hold & Win

**Hold & Win** (also called Collect & Respin) locks money symbols in place and
respins until no new money symbols land. This is a Loop with a landing-count
stop condition. Slot Math Lab models this with a `Loop` node and a
`hold-and-win` catalog subgraph.
