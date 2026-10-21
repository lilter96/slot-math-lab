# Exact finite-chain convergence certificates

These helpers analyze a **supplied**, immutable `ExactSparseMatrix`. They do
not infer a game's state model, prove equivalence to a compiled graph, alter
an authored horizon, or enable pruning in the existing evaluator. No new
runtime atom, expression function or mechanic dispatch is introduced.

Standard definitions of communication classes, absorption and total variation
are described in [Grinstead and Snell, chapter 11](https://math.dartmouth.edu/~prob/prob/prob.pdf)
and [Levin and Peres, chapters 1 and 4](https://pages.uoregon.edu/dlevin/MARKOV/markovmixing.pdf).
The contracts and explicit inequalities below specify what this implementation
proves for its supplied rational kernels.

## Reachability and recurrence

`FiniteChainStructure.Analyze(Q, starts)` accepts a nonempty, in-range initial
support, with at most `Q.States` entries. Duplicate entries are harmless within
that limit. It follows strictly positive edges and uses iterative Kosaraju
passes to find reachable strongly connected components. Reverse adjacency is
stored in CSR form. No native-stack recursion or dense reachability matrix is
used. `ComponentOf` is -1 for unreachable states; component IDs and member order
are traversal identifiers, not stable model IDs.

All row sums must be at most one. Missing mass goes to an implicit cemetery
state, from which it cannot return. A reported class is `IsClosed` only when
all its rows have unit mass and all positive edges stay inside it. A bottom
component with a leaking row is therefore not a closed recurrent class.
`AbsorptionCertain` is true exactly when no such closed class is reachable
from the supplied support. This does not report the numerical absorption
probability when it is below one.

Inside each component a spanning traversal assigns integer depths. The GCD
of `depth[i] + 1 - depth[j]` over its internal positive edges gives its period.
An acyclic singleton has period zero; a singleton with a self loop has period
one. A leaking cycle may be periodic and still absorb almost surely.

Reachability is support-specific. The numerical certificates below are uniform
over **every** state in their supplied matrix. An unreachable immortal class
can prevent a global survival contraction even when `AbsorptionCertain` is
true for a smaller initial support. A caller that restricts a kernel must
preserve its positive transitions, missing-mass semantics and state mapping.

## Killed-chain survival and reward tails

For a substochastic kernel `Q`, let `T` be the number of transient visits,
including the initial visit. For any initial probability row vector `alpha`,

```text
Pr(T > n) = alpha * Q^n * 1
h = Q^k * 1
rho = max_i h[i]
```

`ExactSurvivalCertificate.Create(Q, k)` computes and retains the entire exact
vector `h` by sparse column actions. Since `Q` is nonnegative, `h <= rho*1`
implies `Q^(m*k)*1 <= rho^m*1` by induction. For `n = m*k + r`, `0 <= r < k`,
substochasticity also gives `Q^r*1 <= 1`, hence

```text
Pr(T > n) <= rho^floor(n/k)
E[T] <= k/(1-rho), if rho < 1
```

`UpperSurvival(n)` returns this bound, with the convention `rho^0 = 1`,
including when `rho = 0`. `Contracts` distinguishes a strict contraction from
the valid but uninformative bound one. A failure to contract at the selected
block length does not establish nontermination; a longer block can work.
`ExpectedStepsUpperBound` returns null when the certificate cannot prove a
finite mean.

If an independently justified bound `R >= 0` limits the absolute reward of
**each transient visit**, then the sum of absolute rewards remaining after
`n` transitions has expectation at most

```text
R * k/(1-rho) * rho^floor(n/k)
```

`FutureAbsoluteRewardUpperBound` returns this conservative quantity, or null
without a strict contraction. Zero reward has bound zero even without one.
It is a bound on an unconditioned expected sum, not a payout quantile,
tail probability or conditional-on-survival reward. It uses the beginning of
the containing block and can therefore be loose. Separately, a terminal payoff
bounded in absolute value by `B` contributes at most `B*Pr(T>n)` of unfinished
expected payoff. The caller must prove the external reward/payoff bounds;
these helpers have no access to the authored payout expressions.

These estimates do not authorize replacing a finite capped game by an
unbounded chain. Binding them to an `ExactInterval` result needs model identity,
reward semantics, preservation of caps and horizon, and explicit propagation
of the resulting error enclosure. That integration remains separate.

## Stationary mixing and residuals

`ExactMixingCertificate.Create(P, k)` requires a stochastic kernel and computes

```text
v[j] = min_i P^k[i,j]
delta = sum_j v[j]
rho = 1-delta
```

It streams each row of `P^k` through reusable vectors instead of materializing
the powered matrix. If `delta > 0`, every row shares at least the subprobability
`v`. Subtracting this common part leaves `rho` times a stochastic kernel.
Stochastic kernels contract total variation, so

```text
TV(alpha*P^k, beta*P^k) <= rho * TV(alpha, beta)
TV(alpha*P^n, pi) <= rho^floor(n/k)
```

The contraction on the finite probability simplex gives a unique fixed law
for `P^k`. Its image under `P` is another such fixed law; uniqueness makes it
stationary for `P` as well. Reversibility and irreducibility need not be
assumed: a reducible chain with one accessible absorbing class can satisfy
this certificate. `delta = 1` means exact agreement after the certified block.

When `delta = 0`, the common-column criterion is inconclusive. A mixing chain
may have no common column after one step and acquire one after two steps.
This criterion is sufficient, not the exact pairwise Dobrushin coefficient.
Periodic permutations and multiple closed classes cannot receive a strict
contraction certificate. `UpperDistance` then returns only the trivial bound
one and makes no uniqueness claim.

`VerifyCandidate(x)` requires a canonical, nonnegative probability vector with
mass exactly one. It snapshots the candidate and calculates

```text
residual = TV(x*P^k, x) = sum_i abs((x*P^k)[i]-x[i])/2
TV(x, pi) <= min(1, residual/delta), if delta > 0
```

The latter follows by the triangle inequality: the distance is at most
`residual + rho*distance`. Without contraction the distance bound is null,
even if the residual is zero. A stationary vector alone does not prove
uniqueness or convergence from other starts. No floating-point eigensolver,
logarithm or tolerance comparison enters these certificates.

## Threshold planning and resource contracts

Both certificate types offer `FindSteps(tolerance, maxBlocks)`. It uses exact
doubling and binary search to find the smallest **block multiple** whose
reported uniform bound meets the tolerance. This need not be the chain's true
mixing/absorption threshold. Tolerance is in `(0,1]`; the result is zero for
tolerance one. Block lengths and search caps are independently limited to
1,000,000. Null means no qualifying bound within the cap, including an
uninformative factor one. Resource exhaustion throws `ExactLinearLimitException`
and cancellation propagates; neither is reported as a mathematical failure.
Exact powers can grow large, so a near-unit factor may exhaust the bit budget
before a threshold is found.

The helpers share `ExactLinearOptions` work, coefficient, state, stored-number,
cooperative time and cancellation guards. Matrix metadata and entries are
validated against the requested limits before vector allocation. Constructors
are private; returned arrays have read-only wrappers and no mutable input
array becomes proof evidence. A failed computation returns no certificate.
Arbitrary-precision arithmetic and certificate construction allocate.

Ignoring rational bit growth, with `N` states, `M` nonzeros and block length
`k`, the costs are:

| Operation | Work | Additional storage |
| --- | --- | --- |
| Reachability, SCC and periods | O(N+M) | O(N+M) |
| Survival block certificate | O(k*(N+M)) | O(N) |
| Common-column mixing certificate | O(k*N*(N+M)) | O(N) |
| Candidate residual | O(k*(N+M)) | O(N) |
| One bound at a fixed horizon | O(log(n/k)) rational products | Bounded scalar temporaries, subject to bit growth |
| Threshold search through `L` blocks | O(log^2(L)) rational products | Bounded scalar temporaries, subject to bit growth |

This adds verification and iteration planning. It does not accelerate exact
finite matrix powers by itself. Setup can outweigh a small original solve;
no whole-engine speed multiplier is claimed. The two-phase example with
`Q=[[0,1],[1/2,0]]` has `k=2`, `rho=1/2` and certifies survival below `10^-6`
after 40 transitions. A strictly upper-triangular five-state shift has only
zero eigenvalues but can retain all mass for four transitions; a five-step
survival block proves the actual finite extinction. A nonnormal two-state
Jordan example retains the prefactor `(1+n/2)*2^-n`. These concrete regressions
explain why an eigenvalue magnitude alone is not the implemented stop rule.

## Regression evidence

The selected suite uses direct path enumeration for 20 pinned tiny models;
24 additional pinned graphs compare SCC membership to Boolean transitive
closure and periods to independent simple-cycle enumeration. Hand-specified
examples cover delayed extinction, leaking periodic chains, nonnormal tails,
nonreversible stationary laws, zero residual without uniqueness, multiple
closed classes, two-step minorization and a 10,000-state iterative traversal.
Invalid models/candidates, ownership, cancellation and bit/work/state/storage
limits have explicit regressions. The API and graph evaluator are unchanged;
no database or browser dependency is needed for these checks.

All 12 selected tests passed after the required JetBrains **Reformat Code**
and **Full Cleanup** passes on the three new C# sources. Cleanup diffs were
reviewed; both profiles exited zero and listed every scoped file despite the
tool's existing Roslyn provider-loading warnings. The Release solution build,
`dotnet format --verify-no-changes` and diff whitespace checks passed. No full
suite, database integration or browser test ran. Generated evidence remains
outside Git in `/tmp/chain-certificates-*.log`.
