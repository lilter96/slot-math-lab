# Math engine roadmap: exact laws, risk and scalable state models

Assessment date: 2026-10-21. Checkout: `task/markov-loop-solver`, based on
`6e6f21e`, including the subsequent kernel implementation in this branch. This document
tracks implementation; only items in the delivery ledger below have been
implemented, and none of these changes constitutes certification. Existing contracts remain in
[architecture.md](architecture.md),
[MATH_VERIFICATION_METRICS.md](MATH_VERIFICATION_METRICS.md) and
[VERIFICATION_EXPERIMENTS.md](VERIFICATION_EXPERIMENTS.md).

**What exists now**

| Capability | Evidence in the current checkout | Limit |
| --- | --- | --- |
| Exact mean, second moment and variance from compiled graphs | `StructuredExactEvaluator`, structured inference and loop reward kernels | Extra metrics may be unavailable within their budgets; moments alone cannot settle an active payout cap |
| Moments through order four, skewness and excess kurtosis | `ExactPayoutLaw`, structured inference and multivariate loop reward operators; `RunningMoments` in the sampled collector | Compiled loops can retain joint moments through total accumulator degree four without a full law. Higher orders are requested explicitly and can remain unavailable within their budgets. Arbitrary nonlinear settled payouts can need moments beyond this closure |
| Quantiles, survival thresholds and upper-tail means | `ExactPayoutLaw`; sampled collector `Quantile` and `UpperTail` | Exact rational population queries for complete laws; empirical estimates or histogram enclosures for sampled observations. Pruned compiled results cannot claim exact risk |
| Absorbing and stationary Markov references | `FiniteModelAnalysis.Markov`, `FiniteStateReferences` | Supplied models of at most 32 states; no automatic equivalence certificate tying a manual reference to a game graph |
| Automatic finite-horizon Markov reward matrices | `MarkovRewardMatrix`, `Structured/LoopMatrices` | Small reachable state spaces, exact rational arithmetic, work/bit budgets and transfer fallback; full distribution attempts can still dominate Auto |
| Independent-variable factoring and tensor operators | Structured inference dependency sets; `ExactTensorOperator` for supplied products/mixtures and CTMC event sums | Constructive stochastic/generator contracts and matrix-free exact action; full probability vectors remain. No automatic compiler factorization or tensor-rank truncation |
| Fast sampling and buffer reuse | Alias sampling, compiled state plans, bounded arenas, allocation profiler | Some paths approach zero amortized allocation; arbitrary exact `BigInteger` computation does not have a zero-allocation guarantee |
| Sessions and operator exposure | Sampled `ExecutionOptions`/`SessionTrajectory`; exact `ExactOperatorExposure` with independent or supplied Markov settlement models | Terminal loss and first-passage operator ruin are separate. API portfolio analysis explicitly assumes independent resets. Configured player-session stops and arbitrary persistent graphs are not automatically equivalent to a supplied operator model |
| NTT/CRT | `ExactIntegerConvolution`, structured `TransformConvolution` | Generic adaptive exact convolution on bounded numeric lattices; small, sparse, excessive-span or excessive-coefficient cases retain the original direct algorithm |
| Certified survival and mixing bounds | `FiniteChainStructure`, `ExactSurvivalCertificate`, `ExactMixingCertificate` | Exact supplied-kernel SCC/period analysis, block survival, common-column minorization and stationary residual bounds. No automatic graph binding or evaluator truncation |
| Numerical spectral diagnostics, Snell/HJB, SDE models | Not implemented as general engine capabilities | Eigenvalue diagnostics need justified assumptions; the supplied-kernel convergence helpers use independently verifiable norm bounds. Policy/time models need their own contracts |

**Mathematical contracts to settle before implementation**

The raw second moment is `m2 = E[X^2]`; variance is `m2 - E[X]^2`. Skewness
and excess kurtosis use central third/fourth moments. They summarize shape but
do not identify a full tail distribution or uniquely determine VaR/ES. A finite
WinCap gives bounded settled payouts, not an asymptotically heavy-tailed law.
Zero variance leaves skewness and kurtosis undefined. See
[NIST's definitions](https://www.itl.nist.gov/div898/handbook/eda/section3/eda35b.htm).

For independent rounds with a fixed positive stake `b`, let `X = W/b` and let
`sigma` denote the standard deviation of `X`. Fixed-count normal planning gives
`halfWidth = z*sigma/sqrt(N)` and `N >= (z*sigma/delta)^2`. These are normal
approximations, not finite-sample guarantees for an unobserved rare tail.
For `sigma = 10`, `delta = 0.01` and `z = 1.96`, the planning count is 3,841,600
rounds. Here 0.01 means one percentage point of RTP, not one percent of the
target RTP. Variable stakes require payout/turnover ratio analysis; correlated
rounds require an appropriate long-run variance or independent session design.
Repeated monitoring needs a declared sequential procedure rather than stopping
when an ordinary fixed-count interval happens to pass.
[UK Gambling Commission RTP monitoring](https://www.gamblingcommission.gov.uk/licensees-and-businesses/guide/page/how-to-calculate-return-to-player-rtp),
[time-uniform confidence sequences](https://arxiv.org/abs/1810.08240).

The existing public `VolatilityIndex` field is explicitly `sigma/mean`, a
coefficient of variation. Preserve compatibility while introducing unambiguous
names for coefficient of variation, confidence coefficient and finite-count
half-width. Do not label the last of these a universally mandated GLI index.
The reviewed GLI-19 v3.0 contains no definition of a volatility index; its
sections 4.7.1 and 4.7.2 do address optimal play and RTP disclosure. Applicable
requirements depend on the jurisdiction and submission scope.
[GLI-19 v3.0](https://gaminglabs.com/wp-content/uploads/2024/06/GLI-19-Interactive-Gaming-Systems-v3.0.pdf),
[GLI standards and jurisdictional applicability](https://gaminglabs.com/gli-standards/).

For independent increments, scalar characteristic functions multiply. For
Markov-dependent rewards, use the joint transition/reward transform
`K_t(u)[i,j] = E[exp(i*u*R_t) * 1{S_(t+1)=j} | S_t=i]` and
`phi(u) = alpha * K_0(u) * ... * K_(n-1)(u) * 1`.
This formula follows by conditioning on intermediate states. Multiplying
unconditional scalar transforms would discard reward/state dependence.
For our discrete payouts the result is a PMF/CDF; an inverse floating-point FFT
does not create an exact continuous PDF. Linear convolution needs sufficient
padding and support bounds to prevent circular wraparound.

Kronecker sums describe independent asynchronous continuous-time components.
Independent simultaneous discrete-time updates use products; interacting SANs
need synchronizing event terms or another proven factorization. Componentwise
storage does not remove the full probability vector unless a separate low-rank
representation is justified. Truncating tensor rank introduces error and must
never retain an unqualified `Exact` label.
[SAN and Kronecker analysis](https://www.sciencedirect.com/science/article/pii/037722179400075N).

A spectral relaxation heuristic is not a general stopping certificate. Finite
reversible ergodic chains admit useful spectral bounds, with stationary-mass
factors and treatment of periodicity. Nonreversible chains need additional
analysis; absorbing chains require their transient matrix. Eigenvalues alone
can omit important finite-time prefactors. Keep the authored finite horizon.
[Aldous and Fill, relaxation and mixing](https://www.stat.berkeley.edu/~aldous/RWG/Book_Ralph/Ch4.S4.html).

Optional stopping requires its hypotheses. First establish the appropriate
player-wealth supermartingale for every allowed action and all information the
player can observe. Then check bounded horizon or another sufficient stopping
condition. A negative unconditional RTP does not prove that every conditional
cash-out strategy loses, and a supermartingale inequality alone does not prove
an identical numerical house edge under every policy.
[MIT optional stopping notes](https://ocw.mit.edu/courses/18-440-probability-and-random-variables-spring-2014/95835880b8b2c186229980f4622897df_MIT18_440S14_Lecture35.pdf).

**Baseline observations before the automatic matrix-law probe**

Two short Release benchmark jobs ran on the shared workstation using .NET
10.0.13. Their lifetimes briefly overlapped; these are exploratory observations,
not an isolated before/after experiment or a capacity claim.

* The existing two-phase JSON cycle, with only `maxIterations` changed from 65
  to 100,000, returns exactly 200,000 units and one distribution atom. Default
  Auto takes the full-law route: 100,018 terms and 800,048 work units. After the
  first cold evaluation, elapsed times were 130.42, 163.59, 122.86 and 170.98 ms
  (median 147.00 ms); managed allocation was about 338,561,600 bytes per
  evaluation, or 322.88 MiB. This exposes solver selection and term growth as a
  concrete optimization target. It does not measure the speedup of matrix mode.
* The generic scaled-cap sampling control, seed 42, one worker, 20,000 warmup
  and three 100,000-round repetitions measured 1.79–2.05 million rounds/s and
  about 0.219 B/round on the harness's current-thread counter, with no reported
  collections. The fixed run overhead is amortized; this is not literally zero
  allocation or evidence for a complex game's throughput.

The cycle input's raw SHA-256 is
`30c0caeaf5e2d76d03f76c58b0c1f3df80b3e05878f0795aded4ad8766c36cd6`.
The benchmark's Release Core binary SHA-256 is
`7139981629cc0d016548033c58bf2f350e5cbb08ae2710cd023bcbd1bbd4dac7`.
Local logs/reports are `/tmp/math-roadmap-{cycle,sampled}-baseline.{log,json}`;
generated output remains outside Git. Existing performance history in
[PERFORMANCE.md](PERFORMANCE.md) uses different graphs and conditions and must
not be multiplied into these observations.

The initial matrix implementation had 36 new regression cases and 19 selected
existing mathematical regressions passing. Those are correctness evidence,
not a measured throughput multiplier. Subsequent validation belongs to the
delivery ledger below and does not replace these historical observations.

**Implementation sequence and acceptance**

| Priority | Work package | Result and completion gate |
| --- | --- | --- |
| P0 | Measure and select the solver by requested evidence | Add benchmark diagnostics for selected/attempted solvers, convolution sizes, coefficient bit lengths, peak live storage, total allocations and time. Compare Auto, transfer, matrix and full-law modes on identical graphs. Avoid building optional full laws before requested moments when that changes no requested result. Reuse the existing conditional-zero-variance proof to retain a full law when moments determine it. Benchmark switches must not recognize named games or graph patterns |
| P1 | Unambiguous risk metrics and exact finite tails | Add explicit coefficient-of-variation and CI-planning semantics. Analyze exact compiled payout laws for CDF, survival probabilities, quantiles and upper-tail ES with fractional mass at discrete ties. Label sampled estimates and pruning enclosures separately; do not infer exact tail probabilities from four moments |
| P1 | Requested third/fourth moments through loops | Generalize the joint transition/reward moment operator to the requested multi-indices through order four, with binomial composition and per-exit conditional moments. Preserve dependence, finite caps and optional metric budgets. Unavailable moments remain unavailable; do not fabricate a law |
| P2 | Exact adaptive convolution | Add a generic integer-lattice fast path to `Inference.Convolve`: density/span/bit-budget checks, NTT under sufficient pairwise coprime primes, CRT reconstruction and existing rational denominators. Prove a coefficient bound before choosing the CRT modulus product. Retain direct multiplication for small/sparse supports and a fallback for excessive spans. No conversion of Exact masses to floating-point FFT |
| P2 | Operator exposure over a declared horizon | Define operator loss as settled payouts minus wagers, in declared currency/stake units. Aggregate independent rounds by exact convolution/powering and dependent rounds with state-conditioned polynomial operators. Include stakes, shared jackpots and modelled dependencies. Distinguish end-horizon VaR/ES from path-dependent bankroll ruin; reuse session evidence and validate small bankroll-state models independently |
| P3 | Generic factorized state-model prototype | First prove component/event dependence from typed expressions or a separately authored finite reference. Implement matrix-free Kronecker products/sums of products with explicit synchronizing terms. Compare to both sparse transitions and existing symbolic factoring. Admit only models whose factorization and memory advantage are established; do not claim that a coupled full-board game is thereby solved |
| P3 | Certified convergence and resource bounds | Start with exact reachability/SCC, periodicity and absorption checks. Add spectral diagnostics for justified models, but use verifiable residual/norm or block-survival bounds for truncation. For example, `Q^k*1 <= (1-delta)*1` gives survival at `m*k` at most `(1-delta)^m`. A numerical eigenvalue estimate alone cannot authorize ExactInterval pruning |
| P4 | Optimal stopping reference, then authored action semantics | Introduce an explicit finite observable state/action reference and backward Bellman/Snell analysis, including costs and terminal settlement. Exhaustively enumerate policies in small cases and produce a witness when a policy beats the asserted bound. Only bind it to compiled games after action availability, player information, timing and transitions are authored and checked |
| Conditional | Continuous-time models and SDE research | Require an actual product need for continuous state/time first. Assess OU, jump diffusion or other models against that contract. Document time-step, barrier-crossing and truncation errors; use exact transitions where available and independent analytic controls. A crash distribution can be authored directly and does not inherently require a financial SDE. HJB is relevant only after a controlled continuous-state model is specified |

Risk metrics and exact convolution are independent work streams after P0.
Higher moments are useful when full laws remain unaffordable. An exact law
already contains all finite moments, so do not recompute them through an
expensive state solver merely to demonstrate a technique.

No work package authorizes a new game-specific kernel branch. Mechanics remain
editable JSON compositions. A missing general action/time/continuous-state
atom needs its own contract and explicit authorization before implementation,
as required by AGENTS.md. SDEs and financial modelling are not assumed to be
requirements for the existing slot graph engine.

**Performance estimates and limits**

These figures are planning hypotheses or operation/storage comparisons, not
measured improvements to the application. P0 measurements decide whether each
fast path should be enabled.

| Change | Quantitative assessment | Decision criterion |
| --- | --- | --- |
| Solver selection on the measured cycle | The initial planning target was at least 80% less allocation while retaining its exact one-atom law. The controlled comparison now records 99.958% less managed allocation and a 211.58x kernel speedup for this deterministic workload; see the delivery ledger and PERFORMANCE.md | Include budgets where matrix mode wins and cases where a small full law remains cheaper; do not generalize this synthetic result to other games |
| NTT+CRT for large dense laws | Initial hypothesis: 2–20x faster local convolution on suitable supports around 4K–64K points and moderate coefficient sizes. Tiny/sparse laws can be slower. For two 10,000-point supports, direct convolution has 100 million coefficient products; a length-32,768 transform with three primes has about 2.21 million butterflies plus 98,304 pointwise modular products and CRT. At 32 primes the butterfly count is about 23.59 million. These are different-cost operations, not wall-time ratios | Benchmark 64–65,536-point supports, several densities and 32/256/1,024-bit coefficients; select by measured cost including CRT and conversion |
| End-to-end effect of convolution | Amdahl: `speedup = 1 / ((1-p) + p/s)`. A 20x local improvement gives only 1.90x total at p=50%, or 4.17x at p=80%. If convolution is absent, the benefit is zero | Record the actual fraction of total time spent in convolution before forecasting whole-engine gains |
| Third/fourth moments | Additional capability, normally additional work. For one reward channel, orders 0–2 need three coefficients and orders 0–4 need five; straightforward scalar composition uses 6 versus 15 moment products. Carrying every mixed moment through order four uses `C(A+4,4)` coefficients: 495 for eight channels versus 45 through order two | Request only needed moment closure; benchmark overhead and retain optional budgets. No claim that extra moments accelerate the kernel |
| Tensor operators | Six independent ten-state components give one million joint states. A hypothetical dense binary64 operator needs 8 TB, six local 10x10 matrices need 4.8 KB, and an unfactored probability vector still needs 8 MB. Exact rational storage is larger. The implemented 256-state supplied-factorization fixture measures 51.66x/37.73x over current structured entrypoints for correlated dense/sparse inputs, but is 3.65x slower for independent dense inputs; see [TENSOR_OPERATORS.md](TENSOR_OPERATORS.md) | This is a prototype entrypoint comparison, not automatic compiled-game acceleration. Prove factorization/equivalence and include input/proof setup, full-vector memory and the existing solver before admitting a production backend |
| Spectral diagnostics/certificates | They can avoid unnecessary approximate iteration or justify a bounded tail. They do not intrinsically speed exact finite matrix powers. Diagnostic setup can cost more than the original small solve | Enable only when certificate/setup cost is lower than the saved work; compare the certificate against exact survival probabilities |
| Allocation work | The tiny sampling control already amortizes to about 0.219 B/round and has no observed GC collections; another large speedup from eliminating that allocation is unsupported. The full-law cycle's hundreds of MiB are a stronger target. Arbitrary-precision values themselves can allocate | Profile both allocation CPU and GC/peak memory. Use bounded worker-owned scratch storage; respect ownership on exported arrays and never pool escaped mutable state |
| Snell/MDP and SDE/HJB | New verification or modelling capability, not a justified speedup over today's evaluator. Backward finite-horizon stopping on S states and A actions costs approximately O(H*A*transitions-per-action) | Require an authored use case, independent policy oracle and explicit error contract; keep outside the default solver until applicable |

**Regression and benchmark gates**

Use deterministic small references and run only the affected tests during each
work package. No blanket integration or browser run is needed for a core-only
mathematical helper. Add boundary tests at the layer owning the contract; API
and UI checks become relevant when their exposed behavior changes.

* Moment regressions: raw versus central moments, deterministic/zero-variance
  laws, asymmetric jackpots, correlation with next state, multiple reward
  channels, fractional precision, condition/cap exits, cancellation and unknown
  optional moments. Compare to explicit path enumeration or independently
  specified complete laws, rather than another copy of the same recurrence.
* Tail regressions: zero mass, tied quantiles, fractional ES boundary mass,
  same mean/variance with different tails, active caps, pruned support, unknown
  maximum and one-round versus session/operator-loss sign conventions.
* NTT regressions: direct `BigInteger` oracle, pinned generated inputs, offset
  and fractional lattices, zeros, gaps, point masses, multiple moduli, a result
  exceeding one prime, insufficient CRT modulus rejection, no cyclic aliasing,
  exact mass/mean/second-moment conservation and resource fallback. The sum of
  input masses bounds nonnegative output coefficients, but reconstruction must
  respect the chosen signed or unsigned convention.
* Factorized/spectral regressions: compare with explicit tiny operators;
  independent and synchronizing events, periodic/closed/reducible cases,
  nonnormal transient chains, and adversarial cases where a claimed
  factorization or spectral shortcut is invalid. Test certificate inequalities
  independently and retain provenance for approximate results.
* Policy regressions: enumerate all deterministic policies in tiny finite
  horizons; include a deliberately exploitable conditional state, hidden versus
  observable information and stopping/bankroll constraints. A general negative
  RTP claim must not hide that counterexample.
* Benchmark protocol: Release; pin graph/options and source/binary identities;
  warm JIT separately; run variants sequentially in alternating ABBA/BAAB order;
  report repetitions, median/spread, CPU, allocations, peak storage and GC;
  record background load. Keep logs in ignored output or CI artifacts.

For certification support, retain rule/model identity, requested metrics,
algorithm/version, proof assumptions, provenance/error bounds and replayable
independent evidence. These artifacts support a submission; they do not confer
GLI/BMM certification or guarantee compliance by themselves.

**Delivery ledger**

| Work | Current state | Remaining evidence or scope |
| --- | --- | --- |
| Automatic matrix-law probe | Implemented. A bounded Auto probe accepts matrix moments only if every accumulator is determined conditional on entry/exit and no loop probability was pruned. Non-degenerate laws keep the original full-law attempt | Selected regressions passed. The controlled 100,000-step deterministic example reduced warm kernel median from 153.56 to 0.726 ms and managed allocation by 99.958%. Broader solver planning and peak live-storage instrumentation remain pending |
| Exact transform convolution | Implemented in structured inference. Numeric offsets and rational lattice steps are exact; unsigned CRT requires a modulus product strictly exceeding the coefficient bound. Transform length is limited to 2^20 and reconstruction to 96 independently tested primes. Pooled scratch never escapes; cancellation/work failures propagate | Selected regressions passed. The complete 4,096+4,096-outcome workload improved kernel median 13.07x, with matching full-law hashes and 4.74% more managed allocation. Additional workload matrices remain before further tuning the cost model |
| Exact finite payout risk | Implemented as `ExactPayoutLaw`, `StructuredExactResult.AnalyzeRisk` and optional `/api/evaluate/graph` analysis: credit-unit moments through order four, CDF, inclusive survival, quantiles, tied-boundary ES, fixed-stake RTP/operator loss and explicit CV. Exact and Hybrid retain the actual settled law when requested | UI and generated client exposure remain pending. Sampled and pruned results cannot claim exact population risk. Fixed-count normal planning and a conservative Chebyshev bound are named separately; legacy `VolatilityIndex` remains compatible |
| Higher moments without a full law | Implemented: requested multivariate moment closure, matrix and transfer operators, conditional moments per exit, independent-sum moment sequences and optional metric budgets | Independent path enumeration and closed-form regressions cover multiple accumulators, state/reward correlation, exit-state payouts, skewed rewards, absorption, active caps and absent optional moments. Closure is bounded to 8,192 coefficients and degree four; default order stays two. Performance overhead must be measured, not described as a speedup |
| Session/operator exposure | Implemented exact independent aggregation and supplied Markov settlement models by polynomial matrix powers, with adaptive NTT/direct convolution. Separate optional first-passage ruin preserves complete terminal loss. The graph API exposes an explicitly independent horizon with fixed stake and optional initial reserve | Contract and independent path oracles are in [OPERATOR_EXPOSURE.md](OPERATOR_EXPOSURE.md). UI and broader capacity/performance acceptance remain. A supplied model does not establish arbitrary persistent-graph equivalence or automatically introduce stop policies; those need their own authored contracts |
| Factorized operators | Implemented supplied-factorization prototype: exact matrix-free Kronecker products/mixtures, CTMC synchronizing events with diagonal compensation and certified uniformization rate bound. Caller-owned scratch and immutable factors preserve ownership | Eight targeted regressions passed, including 40 pinned explicit-matrix oracles. All 432 evaluations in the controlled four-case comparison agreed on complete laws and metrics; measured wins and the independent-dense loss are recorded in [TENSOR_OPERATORS.md](TENSOR_OPERATORS.md). No automatic graph-factorization claim or rank compression; production solver admission still requires an equivalence proof and measured resource benefit |
| Convergence certificates | Implemented for supplied sparse kernels: iterative reachability/SCC/periodicity, exact block survival and reward tails, common-column mixing and candidate residual bounds, plus exact threshold planning | Contract, proofs, complexity and independent regressions in [CHAIN_CERTIFICATES.md](CHAIN_CERTIFICATES.md). Graph equivalence and explicit error-enclosure propagation remain separate integration gates; these helpers do not truncate the evaluator or use an eigenvalue heuristic as a stop rule |
| Player policy analysis | Pending | Explicit action/information contract, Bellman/Snell reference and exhaustive small-policy oracle |
| Continuous models | Conditional research | A product use case and separate atom contract/authorization if new runtime capabilities are required |

Reproduce the numeric-sum workload without maintaining thousands of JSON atoms:

```sh
dotnet build backend/SlotMath.Benchmarks/SlotMath.Benchmarks.csproj -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --numeric-sum 4096 --exact --distribution-work 100000000 --repeats 6 \
  --report /tmp/numeric-transform.json
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --numeric-sum 4096 --exact --distribution-work 100000000 --repeats 6 \
  --direct-convolution --report /tmp/numeric-direct.json
```

Compare reports' configuration identities and canonical distribution SHA-256,
exact mean, second moment and hit frequency before comparing timings. Reports
also include binary identity, options, transform/direct operation diagnostics
and loop solver notes. `--no-loop-matrices` provides the transfer/full-law
control for graph benchmarks. Run paired variants sequentially in alternating
order; a single cold timing is not a performance conclusion.

**Optional API population analysis**

`POST /api/evaluate/graph` accepts an optional `analysis` object:

```json
{
  "momentOrder": 4,
  "tailProbabilities": ["19/20", "99/100"],
  "payoutThresholds": ["100", "1000"],
  "stake": "1",
  "rtpHalfWidth": "1/100"
}
```

Inputs are bounded integer/rational strings, with at most 16 tail levels and
16 thresholds. `stake` is positive; it transforms the retained payout law into
RTP and operator loss and does not change the authored graph or its payouts.
`rtpHalfWidth` is an absolute RTP-ratio width: `1/100` means one percentage
point. Planning assumes independent identically distributed rounds, a fixed
stake, a fixed sample count and known population variance. `normal95Rounds`
uses the approximate coefficient 1.96; `chebyshev95Rounds` is the conservative
finite-sample count from `Var(X)/(N*delta^2) <= 1/20`. Neither is a sequential
monitoring or correlated-session guarantee. At least one round is returned,
including a zero-variance population.

The response has `ExactLaw`, `ExactMoments` or `Unavailable` status. Rational
values remain canonical strings; display values are finite numbers or null
when they overflow binary64. Shape statistics are undefined at zero variance,
and CV is undefined at zero mean. With no complete law, tails and thresholds
are absent, even if four exact moments exist. Pruned/shortened games and
sampled reports return `Unavailable` for this population interface. Optional
moments can be absent independently of a successful mean/variance calculation.
The existing report's per-unit RTP field is preserved; `rtpForStake` explicitly
uses the analysis stake.

The benchmark harness additionally accepts `--moment-order 2|3|4`,
`--higher-moment-work N` and `--loop-law-work N`, records these options and
the exact raw third/fourth moments, and supports disabling optional full-law
work for a moment-only experiment. This is not a promise that all authored
graphs can be summarized by a finite moment closure.

Validation of the higher-moment/API milestone: 100 selected Core regressions
and 15 database-free API/strategy cases passed, including 32 new cases in this
milestone. After resolving the Rider/Roslyn nested-loop formatting difference,
all 17 affected mathematical cases and all 15 API cases passed again. The
Release solution build and `dotnet format --verify-no-changes` passed. Both
JetBrains profiles ran on the 24 touched C# files and were repeated on every
subsequent source edit; their diffs were reviewed. The tool still emitted its
existing Roslyn fixer-provider loading warnings, but both profiles exited zero
and reported every scoped source file. Generated logs remain outside Git.
No full suite, database integration or browser tests ran for this milestone.

The benchmark CLI was checked on the deterministic 100,000-step cycle with
distribution work disabled: three evaluations returned no law and exactly
200,000, 40,000,000,000, 8,000,000,000,000,000 and
1,600,000,000,000,000,000,000 for raw moments one through four. This is a
CLI/correctness check, not a new timing comparison. API display regressions
also preserve representable roots such as `sqrt(10^-400)` and `sqrt(10^400)`
without first underflowing or overflowing the squared quantity in binary64.
