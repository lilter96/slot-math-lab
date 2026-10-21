# Exact factorized finite-state operators

`ExactTensorOperator` is a matrix-free reference for a **supplied** factorization.
It composes immutable, nonnegative `ExactSparseMatrix` factors and applies them
to a full rational row vector. It neither infers a game mechanic nor recognizes
an authored graph pattern. No new runtime atom or expression function is added.

This prototype does not claim that an arbitrary coupled game has a small tensor
representation. A factorization must be authored or proved separately. The
constructors prove that the supplied representation defines the requested kind
of operator; equivalence to an unrelated compiled graph remains a separate gate.

## Discrete transitions and synchronizing events

For discrete time the contract is

```
P = sum_e w_e * (P_e,0 tensor ... tensor P_e,k-1)
w_e >= 0, sum_e w_e = 1
each local P_e,i >= 0 with every row summing to 1
```

The row sum of a Kronecker product is the product of its local row sums. A
convex mixture therefore remains stochastic. Each term can represent a global
event with conditionally independent component updates. Mixing such events can
create dependence between components: choosing identity or a simultaneous flip
of two bits with equal probability takes state `00` to `00`/`11`, not a product
of two fair independent bit laws. The implementation retains that dependence.

It does **not** assume that the input probability vector factors. State order
is mixed radix, with the last component changing fastest. Each axis application
visits fibers of the full tensor and applies its local sparse matrix. For
component dimensions `d_i` and `N = product(d_i)`, one term needs approximately
`sum_i (N/d_i)*nnz(P_e,i)` scalar products, plus vector scans. No `N*N` operator
is materialized. Identity factors are skipped as a generic algebraic rule.

## Continuous-time event generators

For CTMC/SAN events, local factors are nonnegative rate/guard/update matrices;
their row sums can be zero, one or larger than one. Define

```
E_e = A_e,0 tensor ... tensor A_e,k-1
Q = sum_e rate_e * (E_e - diag(E_e*1)), rate_e >= 0
```

Off-diagonal entries are nonnegative and every row sum is zero. Diagonal-only
self events cancel exactly. An event acting on one component with identity
factors elsewhere gives the corresponding local generator term in a Kronecker
sum. Explicit multi-component event terms also support guards and simultaneous
updates; a pure sum of independent component generators cannot express every
such coupling.

Every completed event entry must use the same inverse-time unit. The
constructors prove algebraic row/sign contracts, not physical dimensional
consistency. A simultaneous event has its own authored clock; multiplying
independent clock rates does not establish such an event.

The generator action is a signed derivative vector `v*Q`, not a probability
update. `Uniformized(events, lambda)` constructs `P = I + Q/lambda` under the
verified sufficient condition

```
lambda > 0
lambda >= sum_e rate_e * product_i max_row sum_column A_e,i[row,column]
```

This is a conservative bound, including possible self events. It need not be
the smallest valid rate. For any state the total event rate is at most this
bound, so the resulting diagonal is nonnegative and row sums are one.
`P^n` is a uniformized discrete-step law, not by itself the CTMC law at elapsed
time `t`; the latter would additionally need a Poisson mixture and a tail/error
contract. No continuous-time propagator or SDE approximation is claimed here.

## Ownership, memory and budgets

The operator copies term/factor arrays and retains immutable matrix snapshots.
Shared matrix objects are stored once. Local storage includes nonzero entries,
row sums and sparse-row metadata; `StoredNonzeroEntries` counts entries, not
bytes. There is no tensor-rank truncation or silent approximation.

The **full** probability/state vector still has `N` entries. Callers provide
separate source/destination vectors and one `ExactTensorWorkspace` with two
scratch vectors of `N` entries each. Buffers are reused; no internal buffer
escapes or becomes a result. Operators are immutable; concurrent applications
need different workspaces. Concurrent reuse of one workspace is rejected.

Source values must be canonical rationals, including `Rational.Zero` rather
than the invalid default struct value `0/0`. Destination and scratch are filled
with canonical zeros before use. Signed source vectors are allowed for general
linear algebra. A failed application may leave partial destination data, which
must be discarded; the workspace remains reusable. Source and destination
overlap is rejected.

Default limits are 1,048,576 joint states, 1,048,576 stored factor coefficients,
1,024 terms, 32 factors per term, 65,536-bit stored numbers and 40 million
charged work units. Configurable hard maxima are respectively 1,048,576 states,
16,777,216 coefficients, 65,536 terms, 64 factors and 1,048,576 bits. These are
representation guards, not measured production capacities. Sparse creation
also bounds authored input entries before combining duplicates. Runtime scans
are charged even for zero vectors; arithmetic charges grow with operand size.
Work units are resource estimates, not a conversion to CPU nanoseconds. Time
guards are cooperative and cancellation propagates. Intermediate arithmetic
can require larger temporary integers than the stored-number guard.

The vector/control path can reuse all its objects. A regression measures zero
managed allocation for repeated small permutation applications after warmup.
General `Rational`/`BigInteger` arithmetic still allocates when its integers
outgrow inline storage; no universal zero-allocation promise is made.

## Reproducible comparisons

The benchmark fixture authors the same finite transition in three forms:

* a product of local matrices with the matrix-free application;
* an explicitly expanded sparse matrix using the ordinary sparse application;
* a compiled JSON graph with initial Draw atoms, Branch atoms and per-row Draw
  atoms, evaluated by the existing structured symbolic inference engine.

There are independent and correlated initial-law cases, each with dense or
genuinely sparse local transitions. The correlated case uses one
joint draw and verifies exactly that the joint law differs from the product
of its marginals. Only the transition operator factors in that case. Both
cases return the complete encoded-state PMF, exact mean, second moment and hit
frequency. Every run checks the entire PMF against a separate scalar oracle,
not just an RTP checksum.

```sh
dotnet build backend/SlotMath.Benchmarks/SlotMath.Benchmarks.csproj -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --tensor-mode tensor --tensor-components 4 --tensor-input correlated \
  --repeats 6 --report /tmp/tensor-correlated.json
```

Repeat with `--tensor-mode sparse` and `--tensor-mode structured`, and with
`--tensor-input independent`. `--tensor-sparsity sparse` gives two local
outcomes per row and 16 global destinations per state in the four-component
fixture, rather than the dense fixture's 256. Reports pin graph/input/full-output hashes, Core
and benchmark binary hashes, runtime and CLI parameters; fixed resource limits
are specified in the implementation. Run variants sequentially in
balanced orders; discard cold samples and report spread as well as medians.

The operator paths receive a prebuilt probability vector and cache their
factorization proof. The structured path rebuilds its symbolic model/input law
and performs its usual graph checks. This is a repeated-evaluation comparison
between actual entrypoints, not an isolated replacement-function speedup.
Setup is outside timing. Allocations include common timing-harness overhead;
GC counters are captured before output verification. Retained managed deltas
include runtime caches and process peak working sets include setup; neither is
an isolated kernel peak-byte measurement. Work-counter units differ between
the symbolic and linear engines; they are not compared as CPU time. The full
vector, setup and validation costs matter when
deciding whether a factorized backend is useful. The existing symbolic factoring
path remains the production default; these ratios do not authorize automatic
tensor selection for arbitrary compiled games.

## Regression gate

Eight selected Core tests passed after both JetBrains cleanup profiles. Their
independent explicit-matrix oracles cover unequal component dimensions, signed
vectors, 40 pinned stochastic mixtures, correlated synchronizing events,
guarded CTMC events, self-event cancellation and uniformization. Ownership,
invalid factors, cancellation, bit/work limits, overlapping vectors and scratch
reuse after a failed application are checked separately. Identity construction
validates options and a minimum work allowance before allocating its entries.
The small permutation case checks zero managed allocation after warmup; this
does not extend that result to general arbitrary-precision arithmetic.

Only the affected Core tests ran. No database integration, browser tests or
complete test suite was needed for this standalone mathematical prototype.

## Controlled prototype measurements

On 2026-10-21, the four-component fixture had 256 joint states. All three
entrypoints ran sequentially in six balanced orders, with six evaluations per
process and the first two discarded from timing summaries. Thus each
mode/case has 24 warm samples from six processes; all 432 evaluations in the
72 processes checked the complete PMF and exact mean/second moment/hit
frequency. Graph, input and output identities agreed within each case.

Release used .NET 10.0.13 on a shared Linux host exposing 20 logical
processors. `DOTNET_TieredCompilation=0` removed tier-transition variance;
a separate JIT-disassembly check reported `FullOpts` for `Apply`. No source or
binary changed during the experiment, and both assembly hashes were checked
before and after every process. Host one-minute load rose from 8.83 to 9.15.
These are prototype entrypoint measurements under those conditions, not a
production capacity or general compiler speedup claim.

Wall medians in milliseconds; brackets are the first and third quartiles:

| Initial law / transitions | Tensor | Expanded sparse | Existing structured | Structured / tensor median |
| --- | ---: | ---: | ---: | ---: |
| Independent / dense | 4.082 [3.935, 4.259] | 47.827 [46.803, 49.576] | 1.119 [0.965, 1.416] | 0.27x |
| Correlated / dense | 4.226 [4.130, 4.447] | 49.427 [48.645, 50.565] | 218.323 [211.260, 222.446] | 51.66x |
| Independent / sparse | 0.828 [0.795, 0.870] | 1.619 [1.578, 1.695] | 1.026 [0.844, 1.327] | 1.24x |
| Correlated / sparse | 0.897 [0.875, 0.936] | 1.746 [1.633, 1.855] | 33.859 [32.605, 35.240] | 37.73x |

On independent dense inputs the existing symbolic factoring is **3.65x faster**
than this tensor prototype. For independent sparse inputs, process-paired
speed ratios ranged from 0.92x to 1.61x: the modest aggregate advantage is not
a robust universal winner. The correlated cases ranged from 46.54x to 55.25x
(dense) and 36.21x to 39.14x (sparse). Against the actually expanded sparse
matrix, tensor medians improved 11.70–11.72x in the dense cases and 1.95x in
the sparse cases. These comparisons retain the entrypoint/setup distinction
described above; they do not establish a default compiler selection rule.

Median managed bytes allocated per evaluation, including the common harness:

| Initial law / transitions | Tensor | Expanded sparse | Existing structured |
| --- | ---: | ---: | ---: |
| Independent / dense | 993,888 | 14,041,128 | 621,504 |
| Correlated / dense | 1,097,136 | 14,424,152 | 195,055,808 |
| Independent / sparse | 32,648 | 73,736 | 500,616 |
| Correlated / sparse | 70,584 | 164,952 | 37,181,120 |

CPU medians followed the wall-time ordering. Every warm sample reported zero
collections in all three GC generations. Dense factors store 64 entries
versus 65,536 in the expanded operator, and sparse factors store 32 versus
4,096. Applications reported 4,352 versus 65,536 products for dense
transitions and 2,304 versus 4,096 for sparse transitions. Stored coefficients
exclude vector/scratch and object metadata; these counts and the retained/peak
process observations are not an isolated live-byte bound.

The frozen identities were:

```text
Backend source manifest SHA-256:
90df4d40bf12d1cd8f077b858420818f1a125c3df821465110096282d67e0f24
Core assembly SHA-256:
33a14c1f58d94e459527d3d6d83454a09e617b0a09b267d02d71fac8cebeb5c6
Benchmark assembly SHA-256:
267c7df7939bcbfbe5e8d6a7dbcf02eaf4d7ca9ce35310f013c76ca24922e5fb
```

The source manifest hashes compact, key-sorted JSON mapping each backend
`.cs`, `.csproj`, `.props` and `.targets` path to its SHA-256. The six mode
orders were tensor/sparse/structured, sparse/structured/tensor,
structured/tensor/sparse, structured/sparse/tensor, sparse/tensor/structured,
and tensor/structured/sparse. Reports, manifest and runner logs are retained
locally under `/tmp/tensor-benchmarks-final`; generated output is not committed.
The Release solution build and `dotnet format --verify-no-changes` passed.
Both required JetBrains profiles exited zero and listed every scoped source;
their existing Roslyn provider-loading warnings did not prevent those passes.
