# Conditional products authored with ordinary graph atoms

`ConditionalProductComposer` prepares independent finite variables subject to
an inclusive integer-sum range. It returns a reusable editable `CustomMechanic`
and its categorical assets. It introduces no runtime node, named expression
function, kernel dispatch or mechanic-pattern optimization. The ordinary
compiler, reference interpreter and exact evaluators execute the emitted Draw,
ModifyState and typed expressions.

This is R13's authoring constructor. Public designer forms, impact previews and
application into private projects remain R18; migration of current games remains
R17. An existing authored graph is never silently replaced by this constructor.

## Inputs and result

A `ConditionalProductDefinition` supplies ordered `inputs`, `minimumSum`,
`maximumSum`, a bounded `namespace` and `outputKey`. Each input has ordered
ordinary `DrawWeight` outcomes and a matching `contributions` integer array.
Inputs are independent before conditioning. Weights are nonnegative integers
with positive total per input; at least one joint tuple must satisfy the range.
Outcome data follows the existing scalar categorical contract, including
homogeneous data within an input. Different inputs may have different types.

For example, two fair Boolean choices conditioned on exactly one success use
contributions `[0, 1]` for each input and range `[1, 1]`. The resulting two
joint outcomes each have probability one half. Unequal weights, duplicate data
values and zero-weight positions remain distinct ordered outcome positions.

The default result at `outputKey` is a typed record:

- `sum`: the actual signed sum of the chosen contributions;
- `indices`: a typed tuple record with fields `"0"`, `"1"`, etc., containing
  the selected original zero-based position for each input.

With `includeOutcomeData: true`, `positions` adds a tuple of records containing
`index`, `outcomeId`, typed `data`, original numeric `value`, and `contribution`.
This detailed output is explicit because creating it has runtime cost. Data
retains exact rational/wide-number, Boolean and String types. The new Draws
select numeric indices with a zero payout channel; the original `value` is
available in detailed output. The enclosing editable game explicitly defines
its payout expression and settlement.

The fragment writes an internal normalized sum and one index field per input,
then publishes the result. `InternalStateKeys` lists those fields. Its body has
one entry, one exit and `2R + 2` nodes for R inputs. Attach its distribution
assets at the containing graph level and add the body as an ordinary Library
subgraph. Namespace/output-key collisions are rejected. Source inputs are
snapshotted; later mutation of caller arrays cannot edit an already returned
artifact. Returned JSON itself remains editable.

## Exact preparation

For input i, subtract the smallest contribution with positive weight, m_i.
Let d_i(a) be the resulting nonnegative increment for a positive-weight
outcome. Zero-weight positions remain in their original positions but cannot
expand the positive support. Shift the requested range by the sum of m_i.
This normalization also supports negative original contributions.

For normalized prefix s and bounds L..U, prepare exact BigInteger masses:

```
F(R, s) = 1 if L <= s <= U, otherwise 0
F(i, s) = sum_a w_i(a) * F(i + 1, s + d_i(a))
```

Terms outside 0..U contribute zero. Forward reachability and positive backward
mass determine which conditional rows to emit. At stage i, after prefix s,
the ordered row has weights `w_i(a) * F(i+1, s+d_i(a))`. Dividing the whole
row by its exact integer GCD retains its probabilities. The emitted family
selector rejects unavailable prefix states before its Draw consumes RNG.
Unreachable family slots share an ordinary valid asset and are inaccessible
through that selector; they are not fallback distributions for invalid inputs.

For a permitted tuple a, multiplying its conditional probabilities cancels
successive F terms and leaves `product_i w_i(a_i) / F(0,0)`. This proves the
complete conditional joint law, rather than only its expected payout.
Preparation uses O(R K A) charged transitions and O(R K) masses, with K = U+1
and at most A outcomes per input. Sampling uses R ordinary conditional Draws;
it does not repeatedly reject whole tuples until the event occurs. Each Draw
retains the existing alias sampler's own integer rejection behavior.

An empty product is deterministic when the range contains zero: mass one,
empty index tuple and no product Draws. Otherwise its condition has zero mass.
Zero mass, invalid data and resource exhaustion fail preparation; no artifact
or unconditional replacement is returned.

## Bounds and failure contracts

Limits are 64 inputs, 256 outcomes per input and normalized U at most 255.
Preparation allows eight million charged transitions and 4,096-bit intermediate
masses. Emitted data is bounded by 1,024 assets and 262,144 positions, matching
the existing graph catalog limits. After GCD reduction, every emitted weight
must fit the existing nonnegative signed-64-bit categorical coefficient.
A larger exact coefficient is explicitly rejected, never rounded to double.
The compact source identity input is limited to 1,048,576 UTF-16 code units;
the editable body plus assets is limited to eight MiB of compact UTF-8 JSON.
These bound the admitted representation, not every CLR allocation or peak heap.

Inputs are signed 32-bit contributions, while requested bounds and output
sums are signed 64-bit integers. Extreme range arithmetic uses BigInteger
before applying the small normalized horizon, avoiding overflow. Literal/ID
lengths are bounded too. Cancellation checks run throughout preparation and
before publishing its completed result. Failures use `ConditionalProductException`
with stable `PRODUCT_*` categories.

A wide normalized sum can decline even when the interval itself is narrow.
The constructor is a bounded additive solver; it does not support arbitrary
cross-variable predicates, dependent priors, or unrestricted numeric horizons.

## Source and RNG identity

`RandomSchedule` is `ordered-conditional-categorical-v1`. Every input performs
one ordinary categorical Draw in input order, retaining all positions, including
zeros. Exact GCD normalization belongs to this new authoring contract.
The following RNG word is validated against independent scalar aliases for
that contract.

`SourceIdentity` includes the ordered original outcomes and typed data,
contributions, bounds, namespace, output key, output detail choice and schedule
version. It is an evaluated authoring identity, separate from a private user
or project ID. The actual saved editable graph remains the source for ordinary
configuration/evidence hashing; this helper's label cannot override that hash.

A flat joint Draw or an old pattern-plus-stops graph generally consumes random
words differently. Equal joint probabilities do not establish replay with the
same seed. Generate a new authored graph/version and retain older saved graphs
for their existing runs. Never claim seed parity between the old and new
representations.

## Verification and measurements

The final affected selection passed 74 Core cases without skips: 31 new
constructor controls plus 43 existing categorical asset/family cases. The
Release solution build and scoped formatting passed. Both mandatory Rider
profiles covered all three new C# files, repeated for the final formatting
changes. No API, frontend, full integration or browser suite ran for this
authoring-only backend addition. It includes
complete independent Cartesian joint-law oracles for five nonnegative ranges
and a signed range, using both classic and structured exact execution. Three
sampling tiers check 128 seeds each against scalar aliases derived from the
independent flat joint table, including following Draw, RNG audit, following
word, typed payloads, original values, immutable snapshots and canonical full
state. Controls also cover empty products, extreme bounds, zero-weight extreme
contributions, JSON round-trips, source identity and mutation, cancelled work,
invalid requests and numeric/asset/coefficient bounds.

A 20-input, eight-outcome, sum-80 case prepares 1,701 cells and 12,960
transitions, emitting 704 assets with 5,632 positions and a 42-node reusable
body. Its ordinary graph compiles and executes; sampled indices independently
sum to 80. Its Cartesian product has 8^20 = 1,152,921,504,606,846,976 tuples.
That comparison establishes preparation/representation scalability, not a
measured throughput ratio against enumerating that enormous product.

A preliminary six-input, four-outcome, sum-eight independent experiment found
546 permitted tuples. Its ordinary conditional-family representation used 128
positions versus the flat joint table's 546. Both classic and structured exact
evaluators agreed on every joint mass. The preliminary JSON omitted the final
public result contract. After cleanup, the public-constructor experiment
validated all 546 joint masses again with both exact evaluators for flat,
default DP and detailed DP output, before timing. The 20-input graph separately
passed 32 seeded constant-sum controls.

| Complete editable graph | Nodes | Assets | Positions | Compact UTF-8 bytes |
| --- | ---: | ---: | ---: | ---: |
| Flat six-input joint table plus division-decoded result | 4 | 1 | 546 | 54,557 |
| Same flat table with bit-decoded result | 4 | 1 | 546 | 52,697 |
| Six-input conditional DP, sum and indices | 16 | 32 | 128 | 41,324 |
| Six-input DP with detailed outcome data | 16 | 32 | 128 | 63,910 |
| Twenty-input DP, sum and indices | 44 | 704 | 5,632 | 1,059,393 |

The small default graph has 4.27 times fewer categorical positions, but its
complete JSON is only 24.3% smaller and it has more nodes. Detailed output
makes this small JSON larger than the flat representation. Node count,
categorical-data size and total authored JSON size are distinct measures;
none alone establishes faster sampling. The graph sizes above include scoring
and settlement, so they have two more nodes than the reusable bodies.

The use of prepared weighted masses and a separate sampling phase follows the
same broad separation discussed in [DPSampler, IJCAI 2022](https://ijcai.org/proceedings/2022/250).
That work solves a different Boolean-formula problem. This implementation is
limited to the bounded additive finite case described above; it is not a
port of a general constraint sampler.

## Complete-round admission controls

The same frozen Release binary ran on CPU 2 with one worker, 65,536 warmup
rounds and three measurements of 262,144 complete rounds per process. The
first measurement was discarded. The first mirrored series used flat/default/
detailed/large/large/detailed/default/flat order. A follow-up ABBA series
compared an improved bit-decoded flat baseline with default DP. The follow-up
addresses a concrete confound: division/floor decoding in the first flat
baseline can cost more than ordinary exact bit operations on its 12-bit code.
Both decoded variants and the DP graphs passed the complete exact joint-law
controls. Original graph hashes stayed unchanged during the follow-up.
`DOTNET_TieredCompilation=0` was a diagnostic setting; application defaults
are unchanged.

| First series, complete graph | Median rounds/s | Retained range | B/round |
| --- | ---: | ---: | ---: |
| Flat table, division decoder | 159,911 | 139,149–177,156 | 8,107.98 |
| Default DP | 200,316 | 198,480–207,899 | 7,613.13 |
| DP with detailed result | 46,935 | 46,203–47,366 | 36,189.13 |
| Twenty-input DP, sum-only score | 173,252 | 168,099–176,795 | 6,662.61 |

The apparent default/division-flat ratio was 1.253. The improved paired control
changes the admission decision:

| Follow-up pair | Median rounds/s | Retained range | B/round |
| --- | ---: | ---: | ---: |
| Flat table, bit decoder | 211,968 | 205,969–218,085 | 7,613.13 |
| Default DP | 183,400 | 173,663–197,032 | 7,613.13 |

The paired DP/flat ratio is 0.865: DP is about 13.5% slower in this small
control despite smaller data. Keep the compact flat representation when its
size is acceptable and throughput is the goal. DP is an explicit scalable
authoring alternative, not a universal automatic performance replacement.
It must not change an existing graph's RNG schedule silently.

Detailed output costs substantially more here, so it stays opt-in. These paths
allocate records and consume nested state; none is a zero-allocation claim.
The large graph's score reads only its sum, while the small scores encode all
six selected indices to distinguish the complete joint law. Their rates are
not direct scaling comparisons. A flat table for the large 8^20 product was
not built or timed, and no speedup against that unavailable baseline is claimed.
Cold whole-graph compile observations ranged from 325.6 to 430.0 ms for the
small variants and 595.8/661.8 ms for the large one; these are not isolated
constructor timings.

Finite sampled histograms need not match between different authored RNG
schedules. Equality is established by the complete exact joint-law oracles,
while each new schedule independently matches its scalar alias transcript.
All benchmark measurements completed their requested count without cap hits.
No current production-game throughput gain is claimed before R17 admission.

Frozen Core SHA-256:
`9057a62a54c00ac4dfe29683ac8b50e2daab895799ed7f4e2c9b8eea740e8ed7`.
Reports, executable benchmark inputs, failed formatting diagnostics and source/
binary manifests remain outside Git under `/tmp/modern-primitives-evidence/`.
