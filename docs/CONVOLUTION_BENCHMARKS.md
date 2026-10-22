# Exact convolution admission and capacity measurements

The benchmark calls the actual internal `ExactIntegerConvolution`
implementation. A friend assembly grants benchmark access without introducing
a public math API or changing production selection. No game, mechanic, named
transform or authored graph pattern selects a measured path.

This addresses a gap in [MATH_ENGINE_ROADMAP.md](MATH_ENGINE_ROADMAP.md): one fast
dense-law example does not validate admission across support sizes, densities
and coefficient sizes. Whole-graph speed ratios remain separate.

## Inputs and independent correctness

`--length N`, `--right-length M`, `--bits B` and `--stride S` define arrays with
linear coefficients `a+3i` and `c+5j` at positions `S*i` and `S*j`, where
`a=2^(B-1)+11` and `c=2^(B-1)+23`. With `S>1`, each array has an extra atom at
position 1. This breaks the regularly spaced lattice; sparse cases are not
merely dense arrays in different units.

The independent oracle expands the product using closed-form sums of `1`, `i`
and `i²` over the exact overlap. Extra atoms contribute shifted cross-products.
A bounded 67×94 rectangular control checks the formula against literal
`BigInteger` schoolbook multiplication in every process. Every measured output
is compared coefficient by coefficient with the oracle, checked against a proven
coefficient bound and total mass, and fingerprinted. Verification is untimed.

* `direct` prepares sparse indices and computes literal nonnegative products.
* `transform` forces the existing NTT/CRT implementation as a reference control.
* `auto` includes current admission, then executes the selected transform or
  direct fallback.

Timing includes inspection/admission, sparse-index preparation or modular
conversion, output construction and CRT. It excludes expression evaluation,
graph compilation and probability-law conversion. Auto uses this same direct
benchmark control after kernel rejection, rather than a caller-specific law
builder. BigInteger arithmetic
allocates. Process-lifetime peak RSS includes setup/oracle/validation and is
not an isolated kernel peak.

## Reproduction

Build once; keep sources and binaries frozen until the matrix completes:

```sh
dotnet build backend/SlotMathLab.slnx -c Release
dotnet backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --convolution-matrix --lengths 64,512,4096,65536 \
  --coefficient-bits 32,256,1024 --strides 1,4,16 \
  --blocks 2 --warmups 1 --repeats 3 \
  --report-directory /tmp/convolution-capacity-run
```

The default matrix covers 36 inputs. Each Auto/direct and Auto/forced comparison
uses two alternating ABBA/BAAB process blocks, separate warmup and 12 timed
samples per variant. Direct controls exceeding the declared 64M-pair allowance
are explicitly skipped. Large rows then establish capacity/equality with the
forced transform, not a measured ratio against direct multiplication. Runs are
sequential; existing evidence directories are never overwritten or reused.

Children use `DOTNET_TieredCompilation=0` and a declared 1 GiB managed heap limit.
This bounds the experiment, not production RAM or total RSS. A 180-second
process allowance stops the controller with retained incomplete evidence on
timeout. Core/benchmark binaries and backend source hashes are checked before
and after every process. Reports retain timing, CPU, allocation, GC, coefficient
bits, modulus counts, output fingerprints and process-lifetime peaks. The
summary records ordering, CPU model, runtime, limits and host load. Require
`completed: true` and expected coverage before using its conclusions.

Use `--right-length M` on the matrix command to fix the right-hand length
independently of the left-hand sizes; by default they match.

For a development smoke check, use length `64`, bits `32`, stride `4`, one block
and one repetition with a fresh directory. A single-case control is:

```sh
DOTNET_TieredCompilation=0 dotnet \
  backend/SlotMath.Benchmarks/bin/Release/net10.0/SlotMath.Benchmarks.dll \
  --convolution-mode transform --length 4096 --bits 1024 --stride 1 \
  --warmups 2 --repeats 6 --report /tmp/convolution-transform.json
```

Inspect paired quartile ranges before changing admission. Correct fallback can
still be slower than a forced transform; tuning needs a generic measured rule
and affected regressions. Kernel ratios do not establish application-wide gains.


## Preflight validation

On 2026-10-22 the Release solution build, all 12 targeted integer-convolution
regressions, scoped formatting verification and both mandatory JetBrains profiles
passed. The three edited C# sources were listed by both profiles; existing
Roslyn provider-loading warnings did not prevent their successful completion.
A fresh rectangular matrix (64×94, 32/1,024-bit coefficients, strides 1/4)
completed all 32 child processes and eight paired comparisons, with complete
coefficient equality and a verified 1 GiB available managed-memory limit.
Those one-sample smoke timings are not performance conclusions. No integration
or browser suite was needed for this benchmark-only change. Raw evidence is
retained under `/tmp/convolution-capacity-*`, outside Git.

The full capacity/admission matrix and any resulting production tuning are
separate acceptance steps; this preflight does not mark them complete.
