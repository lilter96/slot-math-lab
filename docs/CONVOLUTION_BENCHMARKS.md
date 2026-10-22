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

`--unit-coefficients` substitutes ones at the regular positions, retaining
the two small off-lattice atoms. This checks small `BigInteger` arithmetic
separately from the 32/256/1,024-bit affine families.

For a matched before/after admission comparison, copy the final benchmark
output directory and replace **only** `SlotMath.Core.dll` and its PDB with a
preserved baseline build. Pass that copy's benchmark DLL using
`--baseline-benchmark /tmp/frozen-control/SlotMath.Benchmarks.dll`. The controller
requires identical benchmark DLL hashes, freezes both Core hashes and runs
current Auto versus baseline Auto in ABBA/BAAB order. Rebuilding either library
during measurement invalidates the run. This distinguishes admission changes
from a comparison against an always-forced algorithm.

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

## Completed baseline matrix

The 2026-10-22 baseline completed all 36 inputs, 528 child processes and 1,584
timed complete outputs, in addition to 528 warmups. Its 66 paired comparisons
have 12 samples per variant. Six oversized direct controls were explicitly
skipped; the corresponding forced-transform controls still checked full output
equality. Every retained report verified the 1 GiB managed-memory allowance.
The host was an i5-13600KF with 20 logical processors and .NET 10.0.13. Host
one-minute load rose from 1.00 to 2.42. Raw evidence remains under
`/tmp/convolution-capacity-baseline-20261022`, outside Git.

The baseline admission missed several useful transforms. These medians compare
baseline Auto with its separately forced transform; they are kernel timings,
not whole-graph improvements:

| Length per input | Coefficient bits | Stride | Baseline Auto ms | Forced NTT ms | Auto / NTT |
| --- | ---: | ---: | ---: | ---: | ---: |
| 512 | 1,024 | 1 | 197.177 | 33.728 | 5.85x |
| 4,096 | 32 | 4 | 71.167 | 5.087 | 13.99x |
| 4,096 | 256 | 4 | 134.455 | 31.654 | 4.25x |
| 4,096 | 1,024 | 4 | 855.317 | 174.387 | 4.90x |
| 65,536 | 32 | 16 | 1,220.552 | 86.725 | 14.07x |
| 65,536 | 1,024 | 16 | 12,798.331 | 2,287.916 | 5.59x |

These gapped arrays contain an additional atom at position one. Their spacing
cannot be removed by a common lattice step. The raw kernel accepts stride-16
inputs; production law builders retain their separate density guards, so those
rows do not imply that a graph will choose NTT.

Baseline Core SHA-256:
`190fd6b51e089872daeb839df1852f61b8daab6735cfc9327ec0d429684707a4`.
The baseline matrix benchmark SHA-256 is
`a5119e2cfb62ac4e27ba46d921c657f49d41670ea2bad03b41806186e86eba36`.

## Admission and budget contract

The admission estimate now includes both operand widths in a direct
`BigInteger` multiplication, in addition to the result-width addition. The
transform estimate, minimum nonzero support and twofold safety margin remain.
This is a generic arithmetic cost model, independent of a graph's identity.
It does not change charged work units or the convolution's coefficients.

Structured inference and exposure aggregation also pass their remaining work
allowance before inspection. The transform preflight accounts for inspection,
modular conversion, three transforms, pointwise products, root setup and CRT
charges before renting scratch buffers. If this plan cannot fit, it returns to
the existing direct method after its inspection cost. This preserves complete
results in the tested cases where the direct method fits but NTT does not;
it does not promise that every fallback fits an arbitrary remaining budget.
Cancellation and actual budget exhaustion still propagate.

Regression inputs pin the exact 6,995,772-unit NTT boundary for the 512-point
1,024-bit family and reject one unit less. Separate kernel, structured-law and
public exposure cases verify a usable direct fallback, including full-law
equality and a larger allowance that does admit NTT. After both required
cleanup profiles, all 37 affected Core cases and 40 database-free API cases
passed, as did Release build and solution-wide formatting verification. The
single population browser scenario also passed under Node.js 22 and is tagged
`@critical` for the PR workflow. No complete local suite was needed.

## Matched admission results

The completed comparison used identical benchmark DLLs with the frozen old
and new Core libraries. All 36 main inputs, 12 unit-coefficient controls and
eight rectangular holdouts completed: 448 processes and 1,344 timed complete
outputs, plus their separate warmups. Every main report was reopened to verify
the binary identities, sample count, output hash and 1 GiB managed-memory
allowance. Main-run host load changed from 2.85 to 1.76.

| Length per input | Bits | Stride | Old Auto ms | New Auto ms | Old / new |
| --- | ---: | ---: | ---: | ---: | ---: |
| 512 | 1,024 | 1 | 191.811 | 34.021 | 5.64x |
| 4,096 | 32 | 4 | 71.371 | 5.140 | 13.88x |
| 4,096 | 256 | 4 | 132.471 | 31.676 | 4.18x |
| 4,096 | 1,024 | 4 | 801.110 | 172.658 | 4.64x |
| 65,536 | 32 | 16 | 1,222.665 | 86.660 | 14.11x |
| 65,536 | 256 | 16 | 2,161.249 | 502.409 | 4.30x |
| 65,536 | 1,024 | 16 | 12,633.848 | 2,279.012 | 5.54x |

These seven newly admitted main cases reduced total managed allocation per
evaluation as well: for example, 142.19 to 21.30 MiB for 512/1,024-bit/dense,
72.50 to 0.78 MiB for 4,096/32-bit/stride-4, and 9,114.39 to 275.74 MiB for
65,536/1,024-bit/stride-16. These are cumulative allocations, not live memory;
the last baseline repeatedly collects within its 1 GiB heap allowance.

Already-admitted large transforms kept approximately the same timing. Small
controls are noisy: main-case old/new medians ranged down to 0.64x at tens of
microseconds, despite both choosing direct; unit controls ranged from 0.76x to
2.05x. The 1,024-unit dense control's quartiles overlapped (old 0.324–0.357 ms,
new 0.327–0.503 ms), so its 0.76x median is not evidence of a universal loss
or gain. Do not discard these controls from the assessment.

Rectangular holdouts used lengths 128/1,536 against 997, 32/1,024-bit
coefficients and strides 1/4. The newly admitted dense 128×997/1,024-bit case
improved 1.88x. Remaining holdouts stayed at 0.93–1.00x and retained their
original algorithm. This tests shapes outside the square tuning matrix.

Reproduce the extra controls with the same matrix command and baseline copy,
substituting either `--lengths 64,128,256,512,1024,4096 --coefficient-bits 32
--strides 1,4 --unit-coefficients` or `--lengths 128,1536 --right-length 997
--coefficient-bits 32,1024 --strides 1,4`.

The matched benchmark SHA-256 is
`1bde1307c88439a98b3cac9d758ca0248ff52443cc0948530e5db520670c4b51`;
new Core SHA-256 is
`9633800ff89b007d6e3ceda67c634649bb4d1e27bcc5b6397346bb45b7588f2b`.
Reports remain under `/tmp/convolution-admission-{capacity,unit,rectangular}-final`.

## Complete structured-evaluation control

Three ordinary JSON graphs used the same benchmark DLL and old/new Core
libraries, two ABBA/BAAB blocks and four evaluations per process. The first
evaluation was discarded, giving 12 warm samples per variant and 96 verified
complete outputs overall. Complete-law fingerprints were checked against
literal independent schoolbook convolution, with exact mean, second moment
and hit frequency checked separately. Full structured evaluation includes expression
inference and probability-law conversion; graph compilation, HTTP and risk/UI
analysis are outside this timing.

| Graph | Old ms [P25, P75] | New ms [P25, P75] | Old / new |
| --- | ---: | ---: | ---: |
| 1,024 regular atoms plus an off-lattice atom, stride 4, 32-bit weights | 85.727 [84.617, 88.022] | 11.456 [11.350, 11.637] | 7.48x |
| Dense 1,024 atoms, existing small weights | 4.432 [4.313, 4.515] | 4.462 [4.414, 4.571] | 0.99x |
| 128 regular atoms plus an off-lattice atom, stride 4, 32-bit weights | 1.980 [1.888, 2.083] | 2.053 [2.024, 2.206] | 0.96x |

The first graph admits one transform, reducing direct pairs from 1,051,650 to
1,025. Allocation falls from 78,830,760 to 3,972,832 bytes (94.96%). Charged
work **increases** from 1,073,182 to 1,186,504; work units are a resource
contract, not elapsed time, which is why budget preflight matters. The two
negative controls have unchanged allocations and algorithm choices, with
overlapping timing quartiles. Host one-minute load fell from 2.99 to 2.69.

Reproduce a single process with `--numeric-sum 1024 --sum-stride 4
--sum-weight-bits 32 --exact --distribution-work 100000000 --repeats 4
--report /tmp/gapped-graph.json`. Omit the stride/weight flags for the dense
control; substitute 128 for the small direct control. Alternate old/new
processes before comparing warm samples. Reports and the independent oracle
remain under `/tmp/convolution-admission-graphs-final`.
