# Structured exact evidence planning and storage diagnostics

The planner selects generic symbolic, moment and law operations from requested
evidence. It never recognizes a game, mechanic name, transform identifier or
authored graph pattern. Exact error/precision/settlement checks and the authored
loop horizon remain part of the calculation.

## Evidence contract

`StructuredExactOptions.Evidence` is a flags value. `Mean` is mandatory:

| Flag or combination | Requested output |
| --- | --- |
| `Mean` | Settled expected payout |
| `SecondMoment` | Optional exact second moment, enabling variance |
| `HitFrequency` | Optional exact probability of positive settled payout |
| `PayoutLaw` | Opportunistic complete payout-law attempt within existing budgets |
| `Moments` | `Mean | SecondMoment` |
| `Report` (default) | All four flags, preserving the prior report behavior |

`MomentOrder` remains 2..4. Orders three/four request their additional raw
moments and require `SecondMoment`. Unknown flags, an omitted mean or an
inconsistent order/flags combination are rejected. An optional quantity that
cannot be computed stays unavailable. A requested law is not guaranteed to
fit: existing pruned/interval provenance and complete-law risk gates still
apply. An unrequested law, hit frequency or second moment is not returned.

`SolverPlan.Auto` preserves the existing law-oriented path for `Report` and
other requests containing `PayoutLaw`. Without that flag it starts long-loop
summarization with moments, including bounded finite-state matrix powers where
applicable. It does not first spend the law-trial allowance or perform the
conditional-zero-variance matrix-law probe. Small loops retain the existing
unrolling threshold and generic symbolic factoring.

The final payout law is skipped when structural inference proves valid
nonnegative integral payouts and an inactive cap. This can avoid both the
loop's accumulated-state law and the final convolution. Internal laws needed
for error obligations, dependence, branch probabilities or precision checks
are still allowed. Avoiding a **final** law does not mean avoiding all finite
distributions.

When the cap cannot be proved inactive, the settled mean may require the law
of `min(payout,cap)`. The planner first attempts exact moment execution, then
can retry with lawful loop state and unrolling under the remaining work.
It does not substitute `min(E[X],cap)` or infer a clipped payout from moments.
A law used internally for settlement is discarded from the public result if
it was not requested. Requested moments derived from that law remain available.

Two explicit controls support reference comparisons: `LawFirst` restores the
law-oriented order and an optional final law attempt even for a moment request;
`MomentsFirst` starts with moments. These controls preserve exact fallback and
requested-output masking. They do not guarantee a particular backend, bypass
budgets or make a full law mandatory. Internal test loop-mode overrides remain
stronger controls. The default is `Auto`.

## Attempt evidence

`StructuredExactResult` records the requested evidence and plan. `Attempts`
lists whole-graph attempts with their loop mode, outcome/decline reason, work,
elapsed time and three distinct final-law facts:

* Was it requested for the output?
* Was it needed because a valid inactive payout cap could not be proved?
* Was the final payout-law stage attempted?

Work includes unsuccessful attempts and optional work. The attempt work sums
to the result's total. Existing loop notes retain bounded matrix-probe and
transfer-fallback details. These records describe the computation that ran;
they do not certify equivalence of an unrelated manually supplied state model.

## Storage units and ownership

`TrackStorage=true` enables `Storage`; otherwise it is null. Per-category
`Current`/`Peak` counters measure the documented logical owners and buffer
capacities. Peaks survive failed attempts and fallback. Whole-attempt owners
are released before the final snapshot. An ending current count of zero
therefore describes completed working ownership, not garbage collection or
the absence of a returned result.

| Category | Counted unit and lifetime |
| --- | --- |
| Term objects | Interned symbolic objects in the current graph attempt |
| Random-weight slots | Array entries, deduplicated by array identity within the attempt |
| Cached law atoms | Interned law atoms plus an interning candidate; aliases to one law are counted once |
| Law-builder capacity | Owned value, mass, hash-key and hash-table array cells; shared builder capacity stays cached |
| Law-order capacity | Actual rented index-array capacity while sorting a law |
| Loop kernel coefficients | Unique retained rational row entries and current scaled integer row entries; shared rows are deduplicated |
| Loop exit coefficients | Normalized mass/moment/zero entries during materialization, retained when deferred moment readers need them |
| Transfer coefficients | Simultaneously owned mass, first/second/higher, zero and bound slots across running, next and exit cells |
| Matrix coefficients | Denominator plus mass/reward/zero/bound slots in live transition powers, current row and building product |
| Convolution lattice capacity | Actual rented BigInteger input-array capacities |
| Convolution result slots | Reconstruction/result-array elements during the transform and conversion to a law |
| Transform residue capacity | Actual capacities of both rented modular buffers |

Loop kernel caches are cleared after entry runs and materialization; deferred
readers retain normalized exit data rather than the transition work caches.
Matrix powering releases replaced rows and unused power tables. Shared input
operators in squaring are counted once. Pool buffers and temporary builder
owners are released on ordinary failure, work exhaustion and cancellation.

These counters are **not bytes, total allocation, a full managed heap census,
or a process working-set peak**. Array elements have different types; category
peaks can occur at different times and must not be summed into a RAM figure.
Dictionary/node overhead, term support/value subobjects, arbitrary-precision
limbs and sharing, transition-discovery input snapshots, allocator copies,
exported distributions, GC timing and
untracked infrastructure are outside this inventory. Builder capacity records
owned arrays after resizing, excluding the allocator's internal copy overlap.
Returned coefficients can share immutable integer storage with old values.
Use allocation/GC/process observations separately; do not present them as an
isolated kernel live-byte bound. Profiling itself has bookkeeping overhead.

## Reproducible controls

```sh
dotnet build backend/SlotMath.Benchmarks/SlotMath.Benchmarks.csproj -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --numeric-sum 4096 --exact --moment-order 4 \
  --evidence 'Moments,HitFrequency' --solver-plan Auto --storage \
  --distribution-work 100000000 --repeats 6 --report /tmp/planned.json
```

Use `--solver-plan LawFirst` as the reference with the **same** evidence and
moment order. Both outputs omit the unrequested PMF and must agree exactly on
all requested moments/hit frequency; the reference still attempts the law
internally. Use `--evidence Report` to inspect the complete law as a separate
oracle. `--direct-convolution` retains the direct convolution control.
For long-loop graphs, `--evidence Moments --no-loop-matrices` provides the
transfer control. A sufficiently generous `--loop-law-work` lets the law-first
reference retain an accumulated-state law where affordable; inspect the actual
loop evaluation field rather than assuming a requested method succeeded.

Reports include requested/attempted solvers, coefficient/span diagnostics,
storage counters, exact moments and hit frequency, total managed allocation,
GC pause, kernel/wall/CPU time and source graph/Core assembly identities.
Run sequentially with frozen binaries, balanced orders and warm samples;
verify equal requested evidence and mathematical outputs before timing ratios.
Storage counters are an optional diagnostic, not an enabled-by-default cost.

The graph API preserves the compatible default Report request and now exposes
explicit evidence selection and opt-in diagnostics through the generated client
and Results. The API and Results integration section below describes its limits;
benchmark-only reference plan controls are not public request options.

## Controlled measurements

The 2026-10-22 experiment used Release, SDK 10.0.113/runtime 10.0.13,
`DOTNET_TieredCompilation=0` and a shared Linux host exposing 20 logical
processors. Its one-minute load changed from 2.00 to 1.90. Both DLLs stayed
unchanged before and after every process, and the backend source manifest was
rechecked afterward. No test, build or cleanup process ran during measurement.

Each of three fixtures ran three variants in all six balanced orders, each
in a new process with six evaluations. The first two samples per process were
discarded. Thus each variant/case has 24 warm samples from six processes;
all 324 evaluations in 54 processes matched independent expected moments
through order four and exact hit frequency. Every variant requested the same
`Moments | HitFrequency` evidence and returned no unrequested PMF. Law and
distribution allowances were both 100,000,000; other work allowances kept
their defaults. Input/configuration hashes agreed within each case.

* The numeric fixture sums two independent 4,096-outcome integers, each with
  weight `1+i%7`. The independent oracle sums each marginal's four raw moments
  directly, then uses the binomial identity for their independent sum.
* The 256-step fixture is `MarkovLoopTests.RandomModel(256)`: a fair bit becomes
  the next phase, and each reward is previous phase plus that bit. Every bit
  except the final one contributes twice to the total payout, giving the closed-form moments
  and hit frequency `1-2^-256` used by the oracle.
* The deterministic 100,000-step fixture is the maintained finite cycle with
  only its horizon changed. It pays exactly 200,000 and therefore has raw
  moment `200000^k` and hit frequency one.

Wall medians in milliseconds; brackets give the first and third quartiles.
Storage profiling was off except in the explicitly profiled numeric variant.

| Fixture | Auto | LawFirst reference | Transfer or profiling control |
| --- | ---: | ---: | ---: |
| Numeric 4,096+4,096 | 8.925 [8.801, 9.112] | 19.702 [19.573, 19.980] | Profiled Auto: 9.330 [9.209, 9.547] |
| Random loop, 256 steps | 0.521 [0.448, 0.602] | 23.866 [23.712, 24.018] | Transfer: 1.928 [1.828, 2.208] |
| Deterministic cycle, 100,000 steps | 0.379 [0.321, 0.430] | 0.328 [0.301, 0.400] | Transfer: 128.409 [127.143, 130.621] |

The numeric requested-evidence plan improved wall median **2.21x**, with
2.30% less managed allocation. Process-paired ratios ranged from 2.14x to
2.23x. Auto performs no final convolution; the reference uses one exact NTT.
The profiled Auto wall median was 4.53% higher than unprofiled Auto in this
fixture; instrumentation remains opt-in.

The random-loop Auto improved wall median **45.81x** over the completed
law-first reference and **3.70x** over transfer. Process-paired law-first/Auto
ratios ranged from 38.36x to 51.64x. Managed allocation fell from 34,925,656
to 289,736 bytes, or 99.17%. Auto uses a two-state moment chain, while the
law-first reference carries accumulated amounts and computes its law.

The deterministic fixture is a negative control: the existing law-first
conditional-zero-variance probe already produces a tiny complete law cheaply.
There is no convincing Auto improvement over it: quartiles overlap and paired
ratios range from 0.69x to 1.38x. Auto allocates 200,976 bytes versus 147,064
for the reference. Both use matrices. Against linear transfer, Auto's wall
median improves 339.21x for this synthetic long horizon; this is not a gain
over the already optimized default report path.

Kernel and CPU medians, in the same variant order as the table, were:

| Fixture | Auto kernel / CPU ms | LawFirst kernel / CPU ms | Control kernel / CPU ms |
| --- | ---: | ---: | ---: |
| Numeric | 8.772 / 8.996 | 19.528 / 19.781 | 9.186 / 9.403 |
| Random loop | 0.374 / 0.566 | 23.672 / 23.960 | 1.763 / 1.975 |
| Deterministic cycle | 0.257 / 0.421 | 0.218 / 0.380 | 128.099 / 129.058 |

Wall time includes the evaluator's thread boundary and all final ownership
cleanup; kernel time has narrower boundaries. CPU observation includes the
common reporting harness. These measurements compare actual entrypoints on
the same requested results, not a whole-application throughput claim.

Separate profiled runs checked identical figures and zero ending working
counts. They expose tradeoffs that allocation totals alone would conceal:

| Fixture / category peak | Auto | LawFirst | Transfer |
| --- | ---: | ---: | ---: |
| Numeric / cached law atoms | 20,485 | 12,288 | — |
| Numeric / modular residue capacity | 0 | 16,384 | — |
| Numeric / law-builder capacity | 20,544 | 40,960 | — |
| Random loop / kernel coefficient slots | 24 | 2,040 | 48 |
| Random loop / transfer coefficient slots | 16 | 1,022 | 32 |
| Random loop / matrix coefficient slots | 298 | 226 | 0 |
| Deterministic / transfer coefficient slots | 8 | 6 | 16 |
| Deterministic / matrix coefficient slots | 290 | 222 | 0 |

The numeric moment path retains more marginal moment laws despite avoiding
the joint PMF. Matrix powering retains multiple operators, whereas transfer
keeps a smaller rolling buffer. Neither a skipped PMF nor a faster solve proves
a lower peak in every storage category or a smaller physical heap.

Frozen identities for the observed run:

```text
Backend source manifest SHA-256:
ccbc7c748284e83a967cef3493646c8bc0a186d991b5c45d01a3296bc88bc1a1
Core assembly SHA-256:
bcc365f95865951b9fc65e9958ddce29fe7829c1dff018d09446873844d015c3
Benchmark assembly SHA-256:
0d703bcbf44ef175135b76112a8a35652d371f28fe5045a431e6683adddf2d20
```

The source manifest hashes compact, key-sorted JSON mapping backend source,
project, props/targets and present SDK selection files to their SHA-256.
Reports, input JSON, oracle runner and source manifest remain under
`/tmp/evidence-planner-benchmarks`; generated output is not committed.

## Cost of requesting fourth-order moments

A separate 2026-10-22 comparison keeps the two-state 256-step random fixture,
Auto plan and `Moments,HitFrequency` evidence fixed, changing only
`--moment-order 2` versus `--moment-order 4`. Two ABBA/BAAB blocks run eight
processes with tiered compilation disabled and a 1 GiB managed heap allowance.
Each process evaluates six times and discards the first two, giving 16 warm
samples per variant. All 48 outputs match the independently derived requested
moments and hit probability; neither variant attempts an optional payout law.

| Requested order | Wall ms [P25, P75] | Allocated bytes | Charged work |
| --- | ---: | ---: | ---: |
| 2 | 0.533 [0.410, 0.606] | 184,304 | 1,014 |
| 4 | 0.620 [0.544, 0.710] | 289,736 | 1,963 |

Fourth-order evidence costs 57.2% more allocation and 93.6% more charged work
in this single-channel fixture. The wall median is 16.4% higher, but quartiles
overlap; it is not a general overhead prediction. Multichannel closures grow
combinatorially and retain their independent degree/coefficient/work guards.
Higher moments add evidence rather than accelerate the existing mean.

Reproduce with the same fixture described above and
`--exact --evidence Moments,HitFrequency --moment-order 2 --repeats 6 --report
/tmp/order2.json`, alternating with order four in separate processes. The
input SHA-256 is
`0d87fdebc2c3175981270915a660cbd7a0d18453cf89c54eecb7429767aa8421`;
configuration hash is
`395e067785580fdd3a6d33d264b8acba6f0e958f470867a8e6fb3237bb8027ad`.
Core/benchmark identities match the new libraries in
[CONVOLUTION_BENCHMARKS.md](CONVOLUTION_BENCHMARKS.md). Sources and binaries
were checked before and after each process. One-minute host load changed from
2.56 to 2.32. Raw reports, input, oracle runner and source manifest remain under
`/tmp/loop-moment-overhead-final`, outside Git.

## Regression gate

126 selected Core cases passed, including 13 new evidence/storage cases and
the affected symbolic equivalence, higher-moment, loop, matrix and NTT cases.
They cover active-cap law fallback, mean-only masking, matching requested
metrics under both plans, sequential exit-buffer release, alias deduplication,
power-table lifetime and transform cancellation. Existing independent classical
interpreter comparisons and generated loop models guard deferred moments after
working-cache eviction. This was a selected mathematical run, not the full
backend suite; no database or browser tests ran.

The Release solution build and `dotnet format --verify-no-changes` passed.
Both required JetBrains profiles processed the 14 scoped C# sources and were
repeated after the two later ownership/test edits. Their diffs were reviewed;
the existing Roslyn provider-loading warnings did not prevent successful
profiles or listing every scoped source. Generated test/cleanup logs remain
outside Git under `/tmp/evidence-planner-*.log`.


## API and Results integration

The retained Results population container has an explicit zero minimum width
so expanded risk/diagnostic tables scroll within their own region on phones.
The critical population regression checks the workspace's actual scroll width,
in addition to document width, at desktop and phone sizes. This catches grid
overflow that an outer viewport can otherwise hide. After the scoped Rider
profiles on all three affected frontend files, scoped ESLint, the Node.js 22
build and the single browser regression passed; phone screenshots of the form,
exposure and lower evidence sections were inspected. Earlier clipped screenshots
remain retained locally as failure evidence.

`POST /api/evaluate/graph` accepts an optional `evidence` string and
`diagnostics` boolean. Existing callers default to `Report` with diagnostics
excluded. The public presets deliberately differ from the low-level flags:

| API preset | Returned evidence |
| --- | --- |
| `Mean` | Settled unit-stake graph expectation; analysis, variance and hit frequency excluded |
| `Moments` | Mean, optional second/higher moments and hit frequency; payout law is not requested |
| `Report` | Existing report and optional complete-law risk analysis |

The endpoint preserves case-insensitive named modes and validates canonical
evidence preset names, rejecting numeric or combined enum values. Modes are
normalized before strategy dispatch. `Sampled` requires `Report`. An `analysis` request is incompatible with
`Mean`. With `Moments`, explicitly send `tailProbabilities: []`; nonempty tails,
payout thresholds and independent-round exposure require `Report`. These
combinations are rejected instead of silently dropping a requested risk metric.
Orders two through four and the analysis stake retain their existing contracts.

Exact/Pruned and automatic strategies try structured evaluation first for
non-report presets. If unsupported, ordinary enumeration (or automatic sampling)
remains the fallback. Such a fallback can still construct a complete internal
law. Returned `distribution: NotRequested` distinguishes an unrequested law
from `NotBuilt`; unrequested maximum payout and other metrics are masked even
when computed internally. `ExactMoments` never implies identified tail risk.

With `diagnostics: true`, successful structured results expose the Auto plan,
all attempt work/time and law obligations, convolution counters and named
logical storage categories. Other paths have no structured diagnostics. These
counts are not bytes, a GC heap estimate or an additive process peak. No public
request can force a reference solver plan or raise the kernel work budgets.

Results → Overview → Exact population analysis operates on the verified saved
graph, with a matching SHA-256 required before accepting a response. It shows
rational values beside finite display approximations, moments through order
four, tails/thresholds, fixed-sample planning and explicitly independent-reset
operator exposure. Initial reserve is optional; declaring it enables separate
terminal insolvency and first-passage ruin queries. Persistent saved runs are
explicitly distinguished from this reset population. Results tabs retain the calculation for the selected run. Changing inputs discards
the displayed result; cancellation, budget failure and server errors do not
retain an exact result. Export includes the pinned request and response.

The OpenAPI snapshot and TypeScript client are regenerated from the running API,
not manually edited. New API regressions independently check capped coin moments,
preset validation, metric masking and diagnostic ownership. The browser case
uses a 100-round saved fixture, then exact independent expectations for the
0/2 payout coin: mean and variance 1, fourth moment 8, excess kurtosis −2;
two rounds with reserve zero give final insolvency 1/4 and first-passage ruin
1/2. It checks edited/foreign evidence, unavailable/error states, cancellation,
export and desktop/phone layouts. This is a selected contract/browser scenario,
not the full integration or end-to-end suite.


Integration validation (2026-10-22, .NET SDK 10.0.113 / Node 22.23.4):
**40** selected API/strategy cases and
**17** existing frontend logic cases passed. The targeted real API/browser
scenario passed after both cleanup profiles, including retention across Results
tabs, cancellation, rejected fingerprints, server failure and unavailable risk.
The browser fixture uses isolated API/Postgres ports and only 100 sampled rounds;
no full test suite or unrelated database integration ran. Release solution build,
scoped `dotnet format --verify-no-changes`, frontend build, scoped ESLint and
`git diff --check` passed. The generated client matches the recorded OpenAPI
schema byte for byte. Desktop/phone forms, moments, tails, diagnostics, exposure,
all four Results tabs and error/unavailable states were visually reviewed.

Both mandatory JetBrains profiles ran on the eight touched C# sources and were
repeated on the three later mode-validation/test edits. A real isolated Rider
solution processed all five touched frontend sources with Reformat Code followed
by Full Cleanup; its log names each file and reports frontend Success for both
passes. Stale cached external files initially overwrote an edit; the tab-retention
regression caught that, and a fresh IDE session preserved it through both passes
before successful final validation. See [RIDER_FRONTEND_CLEANUP.md](RIDER_FRONTEND_CLEANUP.md).
Existing Roslyn provider-loading/compiler and frontend bundler warnings remain;
none prevented the required checks. Logs, failure traces, screenshots and temporary
cleanup solutions are retained locally, outside Git.
