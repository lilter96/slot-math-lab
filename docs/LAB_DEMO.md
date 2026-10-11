# Nine Lives / Lab Demo

The release demo uses the owner's Nine Lives configuration and artwork from
TGSlots, revision `651df1e0e39414ce44e45dc910d4ec2fb8c4cef8`.
The unmodified source configuration is `frontend/src/games/nine-lives/model.json`
(SHA-256 `315b8589749359b42ecc2b91c56c72dbeb61703f1fd3e495a6987c55c34dcce4`).
This is a migration of that game, including bonus buy. The earlier 5×3 payline
proposal was superseded; no weights or payouts have been calibrated.

## Rules

- Six columns, five rows; four-neighbor clusters of at least five symbols pay.
  Standard symbols: BONE, FISH, YARN, CANDLE, CAT. WILD substitutes. COIN and
  SCATTER have no cluster payout; all-Wild components cannot pay alone.
- Symbols are swept in column-major first-occurrence order. All components of
  one symbol are evaluated before the next symbol. A Wild is claimed only by
  a paying component and is unavailable to subsequent symbol sweeps.
- Winning positions and every other cell of a paying symbol type disappear; unrelated Wilds stay; survivors fall in stable order. Vacancies are
  drawn column by column, from the lowest vacant row upwards. Cascade multiplier
  starts at 1 and rises after each winning step, capped at 25. A chain has at
  most 30 winning steps; the last step still refills and evaluates the final board.
- Four or more scatters on the **initial** paid-spin board award nine free spins
  after base cascades resolve, provided the round has not reached its cap.
  Scatters do not pay separately. There is no retrigger.
- A free spin draws its whole board and thirty independent chip prizes.
  Initial coins collect once at the carried multiplier, then become Wilds
  for that spin. They are not sticky across spins. Multiplier carries between
  free spins; bonus begins at 1.
- Prizes are 1, 2, 5, 10, 50 and 250 original stakes, with weights
  600, 250, 100, 35, 12 and 3. Purchase costs 240 original stakes and enters
  the same nine-spin bonus. Original stake is 20 credits × bet level.
- The round cap is 9,999 original stakes, including the triggering base win.
  Collection and each cascade award are clamped sequentially. The current
  cascade chain finishes resolving after the cap; further free spins are skipped.

## Graph and tables

`backend/SlotMath.Core.Tests/TestData/LabDemo/lab-demo.json` is the exported
paid-round GraphConfig; `lab-demo-buy.json` is the bonus-buy entry.
Symbols and normalized payouts are stored in GraphConfig tables. This game
uses independently weighted cells, so reel strips and paylines are empty.
Draw outcome tables retain the source integer weights, excluding zero-weight
outcomes. The raw-credit lookup used by expressions is in `initialState.clusterPaytable`.
It is generated from the same source paytable; any future importer must update
the table and lookup consistently.

All mechanics are editable JSON subgraphs composed from Draw, ModifyState,
GetState, Branch, Loop and Library atoms. The six explicitly approved finite-set
expressions are Union, Intersect, Except, Shift, Count and First. They operate
on ordinary exact integers representing positions 0–63; their independent
contract is documented in [BITSET_PROPOSAL.md](BITSET_PROPOSAL.md).

The untraced graph builds three symbol bit planes with two 15-cell Folds.
The initial scatter count is captured from those planes once, before any
paying cascade; traced rounds retain their explicit board count.
Frontier expansion, geometry, component discovery and exclusive Wild claims are
authored in JSON. On boards without Wilds, a board-wide seed calculation excludes
components smaller than five, including four-cell stars and isolated squares.
Each component begins with two authored frontier expansions, then a bounded Loop
performs at most twenty-eight further steps. Detailed traces use the original
ordered queue traversal. A 32-entry table describes stable survivor rows;
gravity stages six columns and reconstructs the board with one Map. The kernel
contains no game or mechanic dispatch, authored-pattern recognition or named
array-update capability.

The graph computes awards in raw credits. `winCredits` divides by 20 to express
complete-round payout in original stakes. For bonus buy, RTP must additionally
divide by the entry wager of 240; the main payout mean alone is not purchase RTP.
The existing cost/payout measurement contract provides that normalization.

## Evidence and reproducibility

The source target is 96%; it is **not an exact proof** for this graph. Full RTP
is Sampled with uncertainty. No separate expectation graph has been supplied
for the complete cascade bonus, and no Exact provenance is claimed for it.
The reduced two-outcome component fixture has independently derived Exact
expectation 1/40; this proves that fixture only.

`scripts/verification/demo_reference.py` reads JSON tables and independently
implements the rules using Python Fraction. It neither imports product code
nor interprets graph expressions. Seeded replay follows the published
SplitMix64/xoshiro256** and FIFO alias-table contract. Floating thresholds
belong to the sampler; payouts and stake conversion use Fraction.

CI regenerates and checks fixtures, then compares complete rounds and all
cascade traces against the independent oracle. Cases include shared Wild
claiming, nonpaying components, all-Wild boards and a modified-paytable negative
control. The corpus includes 862 component boards and 22 complete rounds, with untraced
payouts and final boards also checked. The compiled plan is compared to the
canonical interpreter. Generic iteration compilation preserves language errors,
shadowed bindings, lazy reads and cancellation. Mask-state range proofs require
every initial value and every writer to preserve the invariant; caller overrides
use the general plan. The approved finite-set capability is the only new graph
capability in this optimization.

Play records `(configHash, seed, index)` and entry mode. Hash mismatch rejects
replay. Presentation frames are derived from graph traces; replay does not
charge or credit the virtual session. Real-money settlement is outside this demo.

Regenerate data with:

```sh
node frontend/scripts/export-lab-demo-fixtures.mjs
python3 scripts/verification/demo_reference.py
```

Performance evidence is recorded in [PERFORMANCE.md](PERFORMANCE.md).
Reports belonging to the retired demo were removed, rather than attributed
to this configuration. Ten million paid rounds produced a Sampled mean of 96.370975%, with a 95% normal-approximation interval of 95.138468%–97.603482%. This is compatible with the authored target and does not establish exact RTP. Local one-worker diagnostics exceed the original 39,800 rounds/s floor. The
active acceptance target is now 2,000,000 complete paid rounds/s on eight workers;
the current Release build measured 2,061,865 / 2,059,413 / 2,065,394 rounds/s
with live statistics over three 10M-round repeats (median 2,061,865). See the
linked measurements and their configuration hashes.
