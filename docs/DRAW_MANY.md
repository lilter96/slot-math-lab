# Ordered categorical batches

`drawMany` composes ordinary scalar categorical draws and writes their typed
data as one state array. It preserves outcome order, zero-weight positions,
alias rejection and random-word consumption. It uses the graph's
[distribution assets](DISTRIBUTIONS.md); asset names carry no mechanic behavior.

To repeat one asset thirty times, use this node fragment inside a complete
graph with normal ports, edges and a payout sink:

```json
{
  "nodeType": "drawMany",
  "id": "sample",
  "outputKey": "values",
  "distributionId": "choices",
  "count": 30
}
```

For an explicit mixed schedule, list asset IDs in execution order:

```json
{
  "nodeType": "drawMany",
  "id": "sample",
  "outputKey": "values",
  "distributionIds": ["first", "second", "first", "second"]
}
```

Exactly one of `distributionId` and `distributionIds` is required. With one
asset the default fixed count is one; with a schedule it is the schedule
length. A fixed count can select a shorter prefix. Empty schedules and zero
draws are valid. All assets must provide the same scalar state-data type;
different members can have different outcome counts and weights.

Alternatively, reference an exact Number expression and declare a maximum:

```json
{
  "nodeType": "drawMany",
  "id": "sample",
  "outputKey": "values",
  "distributionId": "choices",
  "countExpressionId": "needed-values",
  "maxCount": 64
}
```

The expression is read once before the first random word. It must return an
integer between zero and the authored maximum, inclusive. `count` and
`countExpressionId` are mutually exclusive. Counts, maxima and schedule
lengths are bounded to 4,096; a schedule's maximum cannot exceed its length.
Type, fractional and range failures occur before randomness or output writes.
Initial overrides use the existing guarded plan fallback.

The data array contains the same values that each scalar Draw would write:
`stateValue` when present, otherwise the string `outcomeId`. Integer and
rational Numbers, Boolean and String remain exact. Array data is not scaled
as money. The value-channel returns the last scalar payout, in the ordinary
WinScale units. With zero draws it preserves the incoming value-channel.
It does not sum payouts implicitly; editable expressions can aggregate the
result array and send the intended total to the sink.

The output array is committed once after successful completion. Cancellation
checks during the batch and before commit prevent publication of a partial
array. An interrupted attempt can already have consumed random words, following
the engine's existing interrupted-round/audit contract. A previous output
value is retained when count validation or cancellation fails before commit.

The canonical program uses ordinary scalar Draw slots and an immutable chain
of sampled elements, then materializes one result array. The compiled sampler
fills a frame-owned buffer. Exported snapshots copy arrays; persistent and
custom-measurement execution uses the existing stricter buffer-ownership
boundaries. No public per-element temporary state is created.

Structured exact execution creates one RV per possible element and shares
prefix terms. A finite count selector chooses both the prefix array and that
prefix's last value-channel. Constant counts and up to 256 finite count values
are supported within normal work/time/storage budgets. Unsupported support
explicitly declines under the existing exact fallback contract. Constructing
the batch does not itself enumerate the product of all element outcomes.

A batch has one authored observation point. Measurements after it see the
completed array. Replacing a scalar sequence is a source transformation with
dependency and schema consequences: consumers of old per-element fields and
measurement IDs must be retained or explicitly reconciled. The compiler does
not recognize game names or authored mechanic patterns to perform a replacement.

Reusable definitions preserve asset IDs and namespace `countExpressionId`.
Original JSON remains editable; private bound row arrays are omitted from
serialization. Loop liveness includes batch counts, family selectors and
weight-vector expressions, so state used only by a stochastic operand is not
discarded by exact loop analysis.

The implementation state, targeted validation and performance evidence are
tracked in [the compiler matrix](design/MODERN_COMPILER_IMPLEMENTATION.md).

The local 30-draw control measured an observed 1.21× ratio for repeating one
table. Its mixed-schedule ratio was 1.04× with overlapping ranges, so a reliable
mixed-table gain is not established. The source fragment shrinks from 31 nodes
to one. Complete-round, full-game effects remain a separate migration benchmark.
