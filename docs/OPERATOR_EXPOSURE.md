# Exact operator exposure over a finite horizon

`ExactOperatorExposure` analyzes a supplied finite settlement model, or an
explicitly independent sequence of resets to one complete compiled payout law.
It returns the full signed law of operator loss, not an approximation inferred
from RTP or four moments. No new runtime atom, named mechanic dispatch or
game-specific optimization is involved.

## Settlement and solvency contract

For settlement `t`, the operator receives `Wager_t >= 0` and owes the settled
`Payout_t >= 0`. Define

```
L_t = sum(Payout_k - Wager_k, k=1..t)
Reserve_t = InitialReserve - L_t
```

Positive loss consumes reserve; negative loss is operator profit. Ruin means
the first **post-settlement** reserve strictly below zero, including the last
round of the declared horizon. Equality at zero remains solvent. This is
different from player-session ruin, which in `SessionTrajectory` means being
unable to fund the next wager. The initial operator reserve must be nonnegative.

The terminal loss law includes **all** horizon obligations. It does not stop
settlement after insolvency. A path can cross the insolvency boundary and then
recover, so `P(L_H > InitialReserve)` is generally smaller than the probability
of ruin by `H`. Neither quantity models an automatic shutdown, rescue funding,
cash withdrawal or a player's action policy. Such rules must be present in the
authored state model before their results can be claimed.

For example, three independent fair loss increments of `-1` or `+1`, starting
at zero reserve, give terminal positive-loss probability `1/2` and ruin
probability `5/8`. First passage occurs at round 1 with mass `1/2` and round 3
with mass `1/8`. The conditional expected ruin round is `7/5`; surviving paths
are censored and are not entered as zero times.

## State-conditioned polynomial operators

`OperatorExposureModel` supplies a finite state count, initial state and joint
`ExposureTransition(From, To, Probability, Payout, Wager)` outcomes. Every row
must sum to one exactly, including unreachable rows. Zero-cost free or
absorbing settlements are allowed. A complete loss state must retain every
dependency needed by the next settlement: a shared jackpot, bonus status or
other coupling cannot be replaced with an independent marginal law.

Let `s` be the common denominator of the reachable net monetary increments and
`ell_e = s * (Payout_e - Wager_e)`, an integer that may be negative. The operator

```
K(z)[i,j] = sum(Probability_e * z^ell_e, edges i -> j)
Law(L_H) = alpha * K(z)^H * 1
```

is a matrix of Laurent polynomials. Conditioning on each intermediate state
shows why coefficient convolution and matrix multiplication compose the joint
law. Substochastic cell laws retain their probability denominator; they are
never renormalized to their own surviving mass. Only a common integer GCD is
removed. Multiplying unconditional scalar transforms would lose state/reward
correlation and is not used for Markov-dependent rounds.

Binary powers need logarithmically many operator compositions, but support and
integer size can still grow with the horizon. Small/sparse supports use direct
integer convolution. Suitable dense affine lattices use the existing exact
NTT/CRT implementation with a proven coefficient bound. Wide gaps do not
force a dense allocation. Duplicate edges with equal next state and net loss
are combined before constructing a cell, avoiding quadratic repeated copying
of a large initial law. Reachability removes unused rows from computation.

`ExactExposureLaw` owns an immutable snapshot. It translates signed support to
reuse the complete payout-law analyzer and restores the loss offset in raw
moments, CDF/survival, quantiles and ES. Variance and central moments are
translation invariant. A negative loss VaR/ES is valid operator profit; it is
not silently floored to zero. Quantiles use the left inverse CDF; ES selects
exactly the upper `1-p` mass, splitting a tied boundary atom.

## First-passage calculation and limits

Ruin uses a separate forward table over `(state, cumulative loss)` for paths
that have not yet crossed the boundary. Crossing mass is recorded once at its
first round and removed from that table. It still belongs to the unrestricted
terminal-law calculation. The exact first-passage masses plus surviving mass
must equal one. Their conditional mean excludes subjects surviving the horizon.

A per-step upper bound can prove no ruin: if every net increment is at most
`M >= 0` and `H*M <= InitialReserve`, every prefix remains solvent. A bound on
the terminal loss alone cannot establish this property.

Resource guards limit work, model states, support and integer size; cancellation
propagates. A required terminal-law failure throws `ExactExposureLimitException`,
without a partial or renormalized law. First passage has a separate optional
allowance within the remaining total work. If exhausted, its status becomes
`Unavailable`, its probability stays null, and the exact terminal law remains.
A computed first-passage prefix is never presented as a completed query.

Core defaults are 40 million total work units, up to 4 million optional ruin
units within that total, 100,000 input/support entries, 32 model states and
65,536-bit integer coordinates/coefficients/denominators. Configurable hard
maxima are 1,000,000 support entries, 128 states and 1,048,576 bits. The support
limit applies to the total coefficients of each retained matrix, not just each
individual cell. Several input/output operators and temporary dictionaries can
coexist; diagnostic atom counts are not a measured peak-byte or zero-allocation
claim. Polynomial bit guards apply before normalization as well, so they can
conservatively decline an intermediate that might later reduce. These bounds
describe stored polynomial values, not every arithmetic temporary. Raw fourth
moment numerators can additionally need probability-denominator bits plus about
four times the loss-coordinate bit length; their denominators also include the
fourth power of the currency scale.

## Graph-evaluation API

The optional `analysis` object for `POST /api/evaluate/graph` can contain:

```json
{
  "stake": "1",
  "tailProbabilities": ["19/20", "99/100"],
  "independentRounds": {
    "rounds": 100,
    "initialReserve": "1000"
  }
}
```

This explicitly assumes independent, identically distributed rounds, each
resetting to the same graph initial state. It cannot represent persistent or
shared-jackpot dependence. The stake changes external loss accounting, not the
authored payout amounts; change and recompile the game to change its payouts.
The public Core Markov interface supports supplied state-dependent settlements,
but no automatic equivalence to an arbitrary persistent compiled graph is claimed.

The response separates `meanLoss`, `lossVariance`, loss quantiles/ES,
`probabilityFinalLossExceedsReserve` and `probabilityRuinByHorizon`. The latter
has its own `ruinStatus`/`ruinReason`. `conditionalExpectedRuinRound` conditions
on ruin by this horizon. Omitting `initialReserve` leaves ruin `NotRequested`.
Zero rounds give a point mass at zero loss. Values remain rational strings with
finite display projections, and units match the one-round credit amounts.

The API permits horizons 0..1,000,000 and bounded rational reserve strings. Its
optional portfolio computation is limited to 8 million total work units,
2 million ruin units, 16,384 support entries, 16,384 bits and a cooperative
five-second time guard, checked at work boundaries and after law analysis.
Exceeding those limits returns nested `Unavailable` without discarding the
one-round population result. A pruned, sampled or missing per-round law cannot
supply this exact portfolio interface, even when four moments are available.
Unknown tail or ruin probabilities are never substituted with zero.

These are mathematical model results, not a certification of game rules,
real-world independence or operational capital adequacy.

## Verification evidence

The delivery gate ran 35 selected Core cases (17 new exposure cases plus the
complete payout-law and exact integer-convolution regressions) and all 19
database-free payout API/strategy cases. All passed. The new cases cover exact
binomial laws and signed moments, full path enumeration of fractional Markov
settlements, absorption with zero subsequent wagers, reward/state correlation,
recovery after insolvency, tied tails, equality at zero reserve, zero and
trillion-round deterministic horizons, partial first-passage budget exhaustion,
invalid/pruned laws, cancellation, immutable output ownership, sparse gaps and
complete-law equality of direct versus NTT aggregation. API cases independently
check the `1/2` terminal-loss versus `5/8` ruin example and preserve per-round
results when optional portfolio limits are exceeded.

Both mandatory JetBrains profiles ran on all seven changed C# files. The final
time-guard change repeated both profiles on its one file; all 17 affected Core
cases and four affected API cases then passed again. Cleanup diffs were
reviewed, the Release solution build passed, and solution-wide
`dotnet format --verify-no-changes` passed. JetBrains still logged the existing
Roslyn fixer-provider loading warnings, while both profiles exited zero and
reported every scoped file. Failure and verification logs remain local under
`/tmp/exposure-*`, outside Git. No full suite, database integration or browser
tests ran for this math/API milestone.

The original transform regression establishes exact law equality and a lower
charged work count for its selected dense fixture. The subsequent generic
convolution matrix and JSON-graph measurements are documented separately in
[CONVOLUTION_BENCHMARKS.md](CONVOLUTION_BENCHMARKS.md); their speed ratios are
not measurements of arbitrary portfolio models. The deterministic long-horizon
case verifies bounded composition work, not a throughput claim for stochastic
trillion-round laws.

## Public API capacity boundary probe

On 2026-10-22 a local Release API received 21 sequential real HTTP requests:
three repetitions for each case below. The graph pays zero or two credits
with equal probability, stake is one, and all round resets are explicitly
independent. Each response requests exact one-round moments/tails and an
independent horizon with reserve zero, except the last row. These are boundary
observations on a shared host, not an isolated throughput benchmark or a
general capacity promise.

| Rounds / reserve | Final loss law | First-passage ruin | HTTP wall median ms |
| --- | --- | --- | ---: |
| 3 / 0 | Exact | Exact | 1.731 |
| 64 / 0 | Exact | Exact | 3.746 |
| 256 / 0 | Exact | Exact | 35.723 |
| 1,024 / 0 | Exact | Unavailable: optional work exhausted | 192.292 |
| 4,096 / 0 | Unavailable: total work exhausted | Unavailable | 37.374 |
| 1,000,000 / 0 | Unavailable: total work exhausted | Unavailable | 43.639 |
| 1,024 / 1,024 | Exact | Exact zero by the prefix bound | 35.713 |

All 21 responses returned HTTP 200, preserved exact one-round population
results and retained one configuration fingerprint. Successful horizon
results independently matched mean zero, variance `H` and the binomial final
loss formula. For zero reserve, the exact ruin oracle is
`1 - C(H,floor(H/2))/2^H`, from the simple-walk reflection/counting identity.
Exact terminal exceedance for even `H` is `(1-C(H,H/2)/2^H)/2`, and one half
for odd `H`. Unavailable fields remained null with explicit reasons. The
largest observed HTTP duration was 217.375 ms; this is an observation for these
inputs, not a wall-time guarantee from cooperative guards. Bit growth and
support/state coupling change the boundary on other models.

Reproduce with the equal-mass 0/2 graph in
`frontend/e2e/exact-population.spec.ts`, named `Operator capacity probe`, using
`POST /api/evaluate/graph` with `mode: "Exact"`, stake `"1"`, moment order four,
tail probabilities `['19/20','99/100']` and the rounds/reserve above. The graph
configuration hash is
`505d6397c84b784b94496da1eb9637be5959bb00140bce2be169e6f567b3b0e3`.
Raw responses, input, independent oracle and timing log remain under
`/tmp/exposure-api-capacity-final`, outside Git. The Core binary is the measured
candidate identified in [CONVOLUTION_BENCHMARKS.md](CONVOLUTION_BENCHMARKS.md).
