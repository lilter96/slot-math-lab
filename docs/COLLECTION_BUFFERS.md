# Collection ownership and buffer lowering

R6 separates logical immutable array values from their physical storage. The
arena still returns distinct rentals during a round. Its bounded retention
policy now covers both small and larger collection buffers. The compiler can additionally transfer storage from a dead,
private expression result to its only consumer. No game or mechanic identifiers
participate in this analysis.

## SSA lifetime and alias analysis

Expression IR records the owned source identity and all SSA results associated
with each Map/Filter occurrence. One AST object can occur in multiple lexical
contexts; every occurrence must satisfy a decision before lowering uses it.
This avoids borrowing a proof from a differently bound occurrence.

`CollectionBufferAnalysis` counts SSA operands and region results. A Let input
defines a lexical binding; its actual projections count as uses. Fresh Map and
Filter containers introduce allocation origins. Empty-path projections, Let
results and conditional results propagate those origins only through unique
uses. Every possible conditional source must be private. Multiple references,
state inputs, collection parameters, record-field projections and unknown
results conservatively prevent transfer. Nested elements remain borrowed even
when their outer container is private.

This follows the separation of whole-IR use/alias analysis from physical
lowering described by [MLIR Bufferization](https://mlir.llvm.org/docs/Bufferization/).
An inferred array shape is not evidence of ownership. Unknown operations,
state-writing expressions, observers and RNG effects decline these expression
plans. The work budget includes use collection, alias traversal and origin
propagation. Exhaustion discards all partial proofs.

Analysis is lazy and shared by the immutable expression IR. Native generation
uses the compiler-owned root when available. Standalone compiled expressions
build their own conservative IR. A private diagnostic switch can retain the
ordinary copying path for independent tests; no authored schema is changed.

## Private buffer transfer

A Map or Filter can use its unique source's typed storage for the result. A
producer still completes before its consumer starts. This preserves the order
of producer errors, source-bound checks and consumer errors. Filters compact
in original order, writing only at or behind the current cursor; any shorter
result retains the existing exact-length resize step.

A source containing symbols keeps the existing source normalization before
transfer. The normalized private container can be reused, while borrowed nested
arrays remain untouched. No state-backed input is reused based only on a field
name, a payout dependency or a shape. Escaping lexical aliases prevent transfer.
The run's exported state and measurement points retain their original values.

Trajectory reset and recursive state export retain their existing lifetime
rules. This optimization also applies when persistence disables the arena:
a fresh anonymous intermediate can transfer storage within that one expression.
It does not mutate a retained state array or an earlier exported snapshot.

## Bounded retention across rounds

The original fast buckets for lengths up to 64 remain. Sparse buckets additionally
retain exact lengths from 65 through 4,096, without allocating a dense table for
all lengths. Both categories share a maximum of 256 buffers and 16,384 element
positions, matching the original worst-case element capacity. Headers and bucket
metadata are separately bounded by the retained buffer count.

Every rental within a round is distinct. Exhausted capacity and lengths above
4,096 allocate without retention. Reset rewinds cursors, not live buffers inside
a round. Persistence still disables the arena. The physical source transfers
above reduce both the number of rentals and the buffers that need retention.

## Guarded constant-producer fusion

When the IR proves that a private Map producer returns the same bounded scalar
without state reads or observable effects, its materialized array can become a
virtual repeated source for a following Map. The producer source is evaluated
once. Its errors and bound checks precede the consumer's bound check and body.
Consumer indices retain their original order.

Raw collections and cancelable tokens retain an original producer preflight,
including item accesses, scoped restoration and cancellation checkpoints. Thus
an invalid raw getter or cancellation during production still wins over an
early consumer failure. A negative raw count retains the original allocation
failure rather than becoming a consumer-bound error.

For a concrete typed array and a token that cannot be canceled, source-item
reads are total and the scalar producer is proven total. The physical producer
loop can then be omitted. Its logical checkpoint count is advanced, so a later
cancelable phase sees the same next polling boundary. Cancelable tokens never
use that shortcut. The consumer produces directly into one output allocation.
No RNG event, observer or failing operation is speculated across a boundary.

This admitted fusion domain does not interleave arbitrary potentially throwing
Map bodies. Other private compositions can still transfer buffers while
retaining separate loops. Wider fusion needs its own effect, alias and cost
proof rather than an assumed commutation of failures.

## Regression controls

Independent controls check use counts and allocation origins, a single lexical
consumer versus escaping aliases, conditional private/borrowed sources, input
storage, Map/Filter/Map compaction and nested borrowed arrays. A direct compiled
control verifies an allocation reduction without an arena. Error controls pin
producer-before-consumer precedence, raw index callback counts and locations,
consumer bounds and malformed negative lengths. Cancellation controls pin both
producer cancellation precedence and the subsequent logical polling boundary.

Persistent-session controls span all three sampling tiers, two runners, 100
rounds and trajectory reset, retaining earlier exported hashes. Measurement
controls compare canonical execution before and after private buffer transfer.
Existing collection, snapshot, mask and seeded full-game regressions remain
part of final validation. Performance claims require paired complete-round
measurements; fewer allocations alone do not establish a throughput gain.

## Verification and measured limits

The final stage passed 409 selected Core cases and five API preview cases,
including 28 new ownership, fusion and retention controls and the existing
15-game seeded full-state corpus. All seven changed C# files passed both
mandatory Rider profiles, final scoped formatting and the Release build. The
two files changed for larger retention received both profiles again. Full
integration, browser and large generative suites were not run for this stage.

The first paired control isolated private buffer transfer before larger arena
retention. Three Maps over 256 elements fell from 12,423 to 4,183 B/round. After
shared bounded retention, the final pair measured:

| Complete-round control | Before B/round | After B/round | Allocation reduction |
| --- | ---: | ---: | ---: |
| Three Maps, 256 elements | 12,423.09 | 63.22 | 99.49% |
| Constant producer and consumer, 256 elements | 8,303.08 | 63.22 | 99.24% |
| Three Maps, 64 elements | 62.84 | 62.81 | Near parity; already pooled |

Both frozen Release binaries used CPU 2, one worker, ABBA process order, the
same seed and old simplification enabled. The 64-element controls used 65,536
rounds, the 256-element controls and two current paid-round games used 32,768;
all had 32,768 warmup rounds and three measured repeats, initially discarding
the first. Every matched measurement retained identical payout statistics,
histograms, cap counts and loop evidence. The borrowed-source negative control
retained 62.80 B/round; current Nine Lives and Le Militare retained 46.68 and
277.25 B/round respectively.

Initial default-runtime timing was unstable. The first final pair measured
0.850x/0.893x on the two reuse controls, 1.048x/1.040x on virtual-source
controls, 1.010x on the borrowed control and 1.009x/0.954x on the games. Those
results are retained rather than selected away. A longer same-binary on/off
control then rose from about 40,000 to 170,000–198,000 rounds/s across repeats
while preserving exactly 131,072 rounds and the same payout statistics. One
discarded sample therefore did not establish steady state.

An expression-only on/off ablation used the same frozen Core binary, 32,768
warmup calls and five 131,072-call repeats per process, in mirrored order.
Without an arena, the three-Map 256-element control measured 212.8 versus
270.8 thousand calls/s (1.273x), with disjoint ranges and allocation reduction
from 12,360 to 4,120 B/call. With the arena, the measured ratio was 1.046 and
the ranges overlapped. These are expression calls, not whole-game rates.

A separate diagnostic disabled CLR tier promotion for both binaries with
`DOTNET_TieredCompilation=0`, retaining the same engine sampling tier and
inputs. This is a measurement environment override, not a project/runtime
configuration change. [.NET compilation settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation)
describe the background optimized tier and this diagnostic switch.

| Diagnostic control | Before thousand rounds/s range | After thousand rounds/s range | Median ratio |
| --- | --- | --- | ---: |
| Three Maps, 64 elements | 392–543 | 489–523 | 1.119 |
| Three Maps, 256 elements | 108.5–126.3 | 136.3–139.0 | 1.254 |
| Current Nine Lives paid round | 289–334 | 268–332 | 1.004 |
| Current Le Militare paid round | 80.1–85.3 | 82.3–87.7 | 1.016 |

All diagnostic outcome statistics matched. The 256-element control establishes
a gain in that optimized-JIT environment; it does not establish an equivalent
gain for API requests with cancelable tokens or every production warmup phase.
Game ranges overlap, so no substantial current-game throughput gain is claimed.
The stable result is reduced allocation and correct ownership, with a measured
benefit on admitted collection work. Wider fusion and game migrations need
their own complete-round measurements after adequate warmup. Cold compile
samples alone exclude hot native generation and are not an end-to-end cost model.

Frozen final Core SHA-256:
`979409fcb22b789c115bfcb8ca79b306c697b17126451ec3d65ffa2e795fbf30`.


## Scan storage

R8 extends fresh allocation origins and unique-source transfer to Scan. A
private Map/Filter/Scan source can transfer its buffer to Scan, and a private
Scan can feed Map without another container. Homogeneous input kind is captured
before overwriting a Boolean source with numeric prefixes. Borrowed state,
record projections and escaping lexical aliases still block transfer.
[Scan and compaction](SCAN_COMPACTION.md) records independent controls and the
conservative ownership of compaction record arrays.
