# Exact finite decision and stopping references

`ExactDecisionModel`, `ExactDecisionSolver` and `ExactDecisionPotential` analyze
an explicitly supplied discrete finite model. They do not infer action semantics
from a game, expose a cash-out button, or change the compiled graph evaluator.
Mechanics remain authored JSON compositions. Binding player choices to a graph
needs a separate contract for available actions, timing, observations, transition
and settlement identity; new runtime atoms still require explicit authorization.

The belief-state basis is described in [Tedrake's MIT notes](https://underactuated.csail.mit.edu/belief.html).
For the stopping-time assumptions, see [Sheffield's MIT lecture 35](https://ocw.mit.edu/courses/18-440-probability-and-random-variables-spring-2014/95835880b8b2c186229980f4622897df_MIT18_440S14_Lecture35.pdf).
The contracts and inequalities below specify the implemented finite reference.

## Money, time and admissible actions

The model snapshots a complete initial law, active states and their action laws.
All input probabilities are canonical nonnegative rationals with mass exactly
one. There is no implicit pruning, missing-mass termination or normalization of
an incomplete law. An outcome explicitly uses `To=-1` for termination.

All payouts and costs use one declared credit/currency unit. `EntryCost` is paid
once, **before** the initial observation. It is subtracted once from the result.
Each state declares an observation label, stop availability, a stop payout,
forced horizon payout and action menu. Action IDs are opaque nonnegative
integers, unique within a state. Matching is by ID, independent of authored list
order. A state needs either a stop option or an action. Costs and payouts are
nonnegative; expected net values can be negative when action wagers are mandatory.

An action's quoted `Wager` is paid at the current decision time. Its joint
`DecisionOutcome` law supplies destination, probability, additional payout and
an observed integer signal. Payout and next-state continuation occur one step
later. The rational per-step discount `beta` lies in `(0,1]`:

```text
V_0(s) = HorizonPayout(s)
A_h(s,a) = -Wager(s,a)
           + beta * sum_o p(o|s,a) * (Payout(o) + V_(h-1)(To(o)))
V_h(s) = max(StopPayout(s) if allowed, A_h(s,a) over available actions)
```

A terminal outcome has continuation zero. An active destination at the final
step receives its separately authored forced horizon payout. Transition,
stop and horizon payouts are **additional** amounts; the model must not encode
the same payment twice. A stop payout is received immediately. Horizon zero
forces settlement without offering any action or optional stop.

All future-relevant variables must be in state, including available funds,
prior choices or accumulated rewards when they change future menus/payoffs.
The solver does not independently enforce a bankroll or derive it from past
payments. Bankroll restrictions are represented by the authored states and
menus. A finite horizon is a rule and remains part of the calculation.

## Player information and belief policies

`AuthoredObservations` is the default. The player sees the initial state label
after paying entry cost. After an action, the player observes its termination
status, the next state label, the realized payout and the authored signal.
The player remembers past actions and observations. Realized payouts are part
of the information contract even when every authored signal is identical.

States sharing a label must expose identical action IDs, quoted wagers and
stop availability. Otherwise the menu itself would reveal omitted information;
the constructor rejects such a model. Stop and horizon payouts can differ in
hidden states: those settlements are revealed only when paid. Any visible
cash-out **quote before choosing** must therefore be included in the observation
label. Additional information, a larger action set or a different timing
protocol needs its own model.

For a belief `b` and a chosen action, outcomes are grouped by the actually
observed tuple `(next label, signal, payout)`. Joint weights preserve the
correlation of money, hidden state and transition. For a nonterminal group,

```text
weight[j] = sum_s b[s] * Pr(To=j, observed tuple | s,a)
groupMass = sum_j weight[j]
nextBelief[j] = weight[j]/groupMass
```

The solver applies the Bellman equation on these beliefs and the remaining
horizon. Belief states are merged only at exact equality of canonical sparse
probabilities and remaining steps. The initial value averages the best policies
over observed initial groups. `FullState` explicitly reveals the actual state
at every decision. Its value is an information upper bound on the authored
observation problem. A profitable full-state witness need not be playable by
a player who only has authored observations.

Reachable belief nodes and action branches are built forward, then reduced
backward by remaining steps. Both passes are iterative; cyclic physical states
cannot cause recursive stack growth because the horizon decreases. With one
continue action, zero incremental rewards/costs and ordinary stopping payoffs,
the recurrence is the finite-horizon Snell envelope. Randomizing an action
cannot improve the maximum of its conditional expected values in this declared
risk-neutral finite model.

The returned policy is an immutable DAG. Roots include their initial observation
and probability. Each node retains its exact belief, remaining horizon, value,
choice and quoted wager; action branches retain observed tuples, probabilities
and successors. A terminal successor is null. A horizon node is forced
settlement, and a stop node ends immediately. Ties prefer stopping, followed
by the smallest action ID. `Value` includes action wagers and discounting;
`ExpectedNet = Value-EntryCost`. This objective is expected net present value,
not an RTP ratio with policy-dependent turnover.

## All-action potential certificate

`ExactDecisionPotential.Verify(model,u)` checks every state, including states
unreachable from the supplied initial law:

```text
HorizonPayout(s) <= u(s)
StopPayout(s) <= u(s), wherever stopping is allowed
-Wager(s,a) + beta*E[Payout + u(next)] <= u(s), for every available action
```

Terminal outcomes use zero for `u(next)`. If all inequalities hold, induction
on the finite horizon bounds every full-information strategy by `u`, and thus
also every strategy restricted to authored observations. Equivalently,
accumulated discounted payments plus discounted continuing potential define
a supermartingale under the allowed actions; settlement cannot increase it
in expectation. Finiteness of state, payments and horizon supplies the bounded
stopping argument. No claim for arbitrary unbounded stopping times is made.

The valid initial net upper bound is `sum_s initial[s]*u(s)-EntryCost`.
`ProvesNoPositiveExpectedNet` requires both all inequalities and a nonpositive
initial bound. This proves an upper bound on expectation; it does not make
every strategy have the same numerical house edge or bound loss quantiles.

A failed candidate returns the exact violation count and first local witness,
while `InitialNetUpperBound` is null. A local drift violation, especially in an
unreachable state, is not automatically an exploitable initial strategy.
`CandidateInitialNetValue` remains only the candidate's computed initial value.
A positive `ExpectedNet` with a valid solver policy is the separate constructive
profit witness, under the result's declared information mode and horizon.

## Resource and ownership contracts

Input records can contain caller-owned lists. Construction copies every nested
state/action/outcome collection and the initial law into read-only snapshots.
Results and potential witnesses retain the immutable model, and expose only
read-only collections. Partial computations never escape after a limit failure.

Defaults and hard maxima are:

| Guard | Default | Hard maximum |
| --- | ---: | ---: |
| Active states | 256 | 4,096 |
| Actions per state | 32 | 1,024 |
| Authored outcome entries | 100,000 | 1,000,000 |
| Horizon | 128 | 4,096 |
| Evaluated belief nodes | 100,000 | 1,000,000 |
| Explored action-observation branches | 1,000,000 | 4,000,000 |
| Charged belief entries | 1,000,000 | 4,000,000 |
| Stored-number bits | 65,536 | 1,048,576 |
| Charged arithmetic work | 40,000,000 | Caller allowance |

Belief-entry charges count cumulative posterior-weight slots and normalized
belief atoms, including candidates that hit the memo cache. This conservative
guard is not peak live bytes. The default work guard, optional cooperative time
guard and cancellation share `ExactLinearBudget`; arithmetic charges grow with
integer sizes. These are representation limits, not production capacity claims.
Exact rational/BigInteger arithmetic and policy construction allocate.

Full-state computation has at most `(H+1)*S` belief nodes and roughly
`O(H*sum_(s,a) outcomeCount(s,a))` arithmetic operations plus bookkeeping.
Partially observed reachable beliefs can grow exponentially with horizon.
The potential test is linear in model outcomes, ignoring rational bit growth.
This adds strategy verification, not a measured speedup to the existing engine.

## Independent regression evidence

The oracle enumerates deterministic **observation-history** policies, including
different decisions after histories that the solver may merge into one belief.
It computes discounted cash ledgers by forward path enumeration, without the
solver's belief updates or backward Bellman recurrence. Returned policies are
replayed independently against the model, including their terminal branches.
Sixteen pinned two-state models exercise both information modes with a two-step
horizon, joint payouts/transitions, mandatory costs and discounting.

Hand-specified examples include:

* A fixed play returning 0.8 against entry cost one, while adaptive visible
  play returns 1.3. With the state hidden, the optimal net is zero; a
  full-information witness has net 0.3.
* Information gathering that is valuable only when its signal is observed;
  realized monetary payments also reveal state even when authored signals match.
* Repeated paid observations: the high path pays `0+2`, the low path pays
  `1/10+1/10+1`; equal weights minus entry cost one give exact net `3/5`.
* A potential proving every bounded-horizon policy has expected net at most
  `-3/50`, and a failed potential in an unreachable state that supplies no
  exploitable initial strategy.
* Bankroll restrictions authored as state menus, forced horizon settlement,
  termination without duplicate payout, stable action IDs despite reordered
  inputs, immutable snapshots and a 4,096-step iterative policy construction.

Invalid laws, information-leaking menus, ownership, state/node/branch/belief
limits, arithmetic growth and cancellation have explicit regressions. This
reference changes no database, browser or compiled-game runtime behavior.

All 13 selected regressions passed after both JetBrains cleanup profiles on
the five touched C# sources. Cleanup diffs were reviewed; changes were formatting,
equivalent type syntax and independent field ordering. Both profiles exited zero
and listed every file despite their existing Roslyn provider-loading warnings.
The Release solution build and `dotnet format --verify-no-changes` passed.
No full suite, database integration or browser tests ran. Generated logs,
including the corrected example's initial failure evidence, remain outside Git
under `/tmp/decision-reference-*.log`.
