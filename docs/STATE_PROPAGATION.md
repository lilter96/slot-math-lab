# Conditional state constant propagation

The first R5 transformation propagates scalar constants through the resolved
continuation graph. It runs only when sampling expression simplification is
enabled. The authored JSON, canonical/exact program, node/edge identities,
state writes and observation points remain available unchanged.

The pass uses the R4 continuation functions and memory regions. Function
inputs and outputs are joined through a worklist. An unreachable environment
is bottom; a captured Number/Boolean/String is a constant; an absent field
fact is overdefined, not evidence that the field is absent at runtime. Joins
keep a constant only if every reaching state agrees. Conditional branches use
known Boolean conditions; unknown conditions visit both arms. Ordinary
fan-out preserves sequential state transfer.

This follows the constant/reachability reasoning described by
[LLVM SCCP](https://llvm.org/docs/Passes.html#sccp-sparse-conditional-constant-propagation),
with the engine's state, continuation and exception semantics retained.

## Transfer and scope rules

A successful pure scalar Modify result establishes the stored value of its
output field. The legacy `__modified__` path establishes its truncated integer,
not the pre-storage rational. Unknown writes invalidate all facts. Other
writes kill the affected fields. Draws remain in order and their state outputs
are conservatively unknown; no random choice is skipped because its payload
looks constant.

Scalar evaluation uses the shared exact evaluator with only the known facts.
An unknown executed read prevents a fact; an unknown read in a lazy untaken
branch does not. Errors do not become values or invented zeroes. Constant
capture excludes observer/RNG/writer/iterator effects and bounds numeric bit
sizes and string lengths. Arrays and collection iteration are not evaluated
by this first state-value domain; the existing guarded expression simplifier
continues to handle immutable collection facts.

Loop-carried writes are widened to unknown before visiting the body. A proven
zero-iteration loop can retain entry facts. No fixed trip count is inferred
from a cap when the body might reset the service counter. Loop conditions are
not rewritten with pre-initialization entry facts. Service counters are not
promoted to ordinary scalar facts because their storage representation is
part of the existing sampler behavior. Facts established by a
write inside a body can still reach later nodes in that continuation.

Expression references shared by nodes are specialized per use. Private IDs
are allocated without collisions with authored IDs. Iterator scopes retain
shadowing, and local names remain separate from recurrence fields. Global
measurement expressions are not rewritten from one node's facts.

## Integration, invalidation and limits

A runner whose input overrides an immutable initial fact receives a fresh
preparation without that fact. Facts established by dominating graph writes
remain usable. The original source configuration is retained for rebinding;
assumptions from an earlier prepared graph never become new source truth.

After propagation and existing expression simplification, changed executable
graphs receive fresh verified expression/memory IR. Worker replicas share one
prepared graph and analysis snapshot, avoiding repeated preparation of the
same source. Masks and override modes retain separate guarded preparations.

The work budget covers transfer steps, attempted scalar evaluation and
rewriting. Exhaustion discards all partial rewrites. Numeric and string fact
size limits keep retained values bounded. No graph is dispatched by a game,
mechanic name or authored layout.

This stage performs no CSE, LICM or dead-store elimination. Observable state
writes and cancellation checkpoints are retained. Those remaining R5 passes
must prove dominance, effect compatibility and observation/exception behavior
before deleting or relocating work. R6 buffer/fusion work remains separate.

## Regression controls

Controls cover dominating assignments, per-use shared expression IDs,
conditional and phi merges, sequential fan-out, loop widening and zero-entry,
legacy integer storage, immutable-fact overrides, scoped iterator bindings,
known errors, work-budget fallback, measurements before/after writes, RNG
transcripts including the following word and full seeded exported state in
all sampling tiers. A replica control checks shared preparation ownership.

## Verification and measured limits

The stage passed 313 selected Core cases and five API preview cases, including
23 new propagation controls and the existing 15-game seeded full-state corpus.
Both mandatory Rider profiles and scoped format verification covered all three
changed C# files; the Release solution build passed. Full integration, browser
and large generative suites were not run for this pass.

Two small controls used the same frozen Release binary on CPU 2, one worker,
ABBA simplification-off/on/on/off processes, 1,048,576 warmup rounds and three
1,048,576-round measurements per process. The first measurement was excluded
from performance summaries. Neither control has initial state facts, so the
new opportunities arise from graph writes. The CLI off switch also disables
preexisting expression simplification; this is not a global attribution test.
All measured payout statistics, histograms and loop/cap counters agreed.

| Control | Off, median million rounds/s | On, median million rounds/s | Observed ratio |
| --- | ---: | ---: | ---: |
| Dominating write followed by repeated arithmetic | 1.507 | 1.805 | 1.197 |
| Simple branch, first ABBA run | 1.614 | 1.523 | 0.943 |

A targeted branch diagnostic compared the original condition, a hand-authored
literal condition with simplification off, and propagation on, in mirrored
order. Medians were 1.611, 1.655 and 1.669 million rounds/s respectively.
The original-condition range was 1.437–1.772 million, and the propagated range
was 1.625–1.762 million. The initial branch regression did not reproduce; these
samples establish neither a reliable branch gain nor a reliable regression.
The arithmetic on-range was 1.576–1.917 million versus 1.461–1.545 million off.
These are controls with visible process variation, not a promised game speedup.

Two current paid-round game inputs also used CPU 2, one worker and ABBA
processes, with 32,768 warmup rounds and three 32,768-round measurements per
process, excluding the first measurement. Here both versions retain all
preexisting simplification: the baseline is the preceding memory-SSA stage.

| Current game | Before, thousand rounds/s range | SCCP, thousand rounds/s range | Median ratio | Before compile ms | SCCP compile ms |
| --- | --- | --- | ---: | --- | --- |
| Nine Lives | 111.4–120.9 | 111.3–121.0 | 1.002 | 481.9, 494.4 | 646.5, 661.6 |
| Le Militare spin | 37.2–38.3 | 37.3–39.7 | 1.024 | 494.9, 509.3 | 723.7, 748.5 |

Outcome statistics agreed in every game measurement. Median allocations were
unchanged: 46.68 B/round for Nine Lives and 277.25 B/round for Le Militare.
The throughput ranges overlap; this stage does not establish a substantial
current-game speedup. The additional preparation cost is material and must be
weighed against execution savings as later passes and migrations are admitted.
Two cold compile samples per version do not define a precise cost model.

Frozen Core SHA-256:
`3dad1579421e06682f42a5b5fd00fe8c3e90f22af58ab0d0c7e03a7149484f99`.
