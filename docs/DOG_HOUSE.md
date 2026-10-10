# The Dog House constructor model

The playable reference uses the same graph that you edit in Build. All stops, multiplier draws, payline payouts, bonus awards and Sticky Wild state changes are ordinary graph nodes and typed expressions. The API contains generic graph execution and evaluation endpoints, with no Dog House payout implementation.

The default constructor profile is calibrated to the requested **98% paid-round RTP**. Its exact rational expectation rounds to **98.000000000000%**; its difference from `49/50` is approximately `−6.529 × 10⁻¹⁷` in stake units. The graph checks the target within `10⁻¹⁰` percentage points. Calibration changes free-spin reel composition and multiplier probabilities while retaining the original payout table and game rules.

## Build and edit

1. Open [the assembled model](http://localhost:4173/build?project=dog-house). A fresh Build also loads the 98% model automatically. The canvas immediately shows the twelve-node round graph, including an explicit bonus-only completion point for episode metrics; the full project has 108 nodes across the round graph and three editable subgraphs.
2. Use **Find node** to focus a node. Select a library node and **Open subgraph** to inspect the base spin, free spin or reusable line calculation. Nested edits propagate when you select **Save & return**.
3. The inspector edits draw weights, library parameters, loop caps, output state keys and typed AST trees. **Data / AST** edits the actual reel strips, payout table and initial state. Reel tables include two wrapped cells after the circular strip.
4. Save the graph or reload the page to verify draft persistence. **Play** executes the current complete constructor graph; **Simulate** saves that graph to the API and creates a seeded run. Results and Export use measured metrics with their provenance.

The navigation above the canvas opens **Full game**, **Base reels** (43 nodes), **Paylines & payouts** (8 nodes), **Free spins & Sticky Wilds** (45 nodes), **Reels / paytable**, and **Exact RTP / calibration**. Library cards also have an **Open subgraph** button and support double-click. Switching graphs saves nested edits into the parent; the direct model link reopens the complete existing Dog House project with those edits. The viewport fits each opened graph, and zoom allows an overview of large subgraphs. Existing nonempty projects remain intact on ordinary Build navigation.

The root graph draws the base game, checks three paw symbols, adds the scatter award, reveals nine bonus cells, executes the awarded free spins and reads the total payout at a capped sink. The line subgraph finds the longest matching prefix, looks up its coin payout and adds only the Wild multipliers that participate in that prefix. The free-spin subgraph overlays remembered Wild positions and preserves their original values.

**Play** provides bet controls, demo credit, quick spin, sound toggle, bounded autoplay, stop, rules/paytable, line highlights, fullscreen, bonus progression, history and replay. Credit is settled once per paid round. A failed request is refunded; replay changes no credit. Settings and settled history persist in the browser. A round is reproduced by its graph hash, seed and round index. Trace recording does not change payouts.

## Math and assumptions

The board has five reels, three rows and twenty fixed paylines. There are no Wilds on the first or fifth reel, so a line's first regular symbol determines its target. Wilds on reels 2–4 use a fresh multiplier of 2 or 3 per reel; participating values add. In free spins, the positions and values lock until the bonus ends. Three paws pay five total stakes and trigger nine independent 1/2/3 awards, for 9–27 free spins. Free spins use separate strips and do not retrigger.

The profile uses uniform circular stops, equal base-game ×2/×3 probabilities and equal probabilities for each 1/2/3 bonus cell. The base strips are the public-demo reference strips. On each middle free-spin reel, regular symbols repeat three times and one Wild remains; free-spin strip lengths are 29, 73, 76, 85 and 27. The free-spin ×2/×3 integer weights are `303792868695691` and `696207131304309`, giving approximately 69.6207131304309% probability of ×3. These are editable constructor inputs. Provider probability weights are unavailable, so this is an independently specified 98% model.

The round cap is a conservative nonbinding bound: 28 possible spins × 20 lines × 750 maximum line coins × 9 maximum additive factor ÷ 20 stake coins + 5 scatter stakes = 189005 stakes.

The visual reference and rules come from the [provider page](https://www.pragmaticplay.com/en/games/the-dog-house-slot/), its public demo and [provider rules distributed by an operator](https://cdn-sp.kertn.net/assets/cms/App_Data/20e8c524-e223-48de-9382-7401c87cd065/PDFs/GameRules/Pragmatic/GamerulesRO/TheDogHouse.pdf). The atlas provenance and hashes are in [provenance.json](../frontend/public/dog-house/provenance.json). These assets remain provider reference material; the repository's code license does not relicense them.

## Exact expectation graph

**Play → Verify RTP → Run rational expectation graph** executes a second ordinary constructor graph. **Open expectation graph in constructor** lets you inspect and edit every formula. **Data / AST → Execute & inspect** shows its exact rational final state, including `expectedRtp`, `baseRtp`, `scatterRtp`, `bonusRtp`, `triggerProbability` and the bonus-count probabilities. **Return to game graph** restores the saved gameplay project.

`targetRtpPercent` is an editable project state field, initially 98. The proof calculates `targetRtp`, `targetDelta` and `targetMet` with rational AST expressions. A modified model is verified again against its actual expectation. Existing saved projects retain their parameters; load the 98% preset to replace an older default model.

The same proof executes the bonus recurrence at all-×2 and all-×3 multiplier settings. For the fixed strips and bonus-count distribution, expectation is affine in the mean additive Wild multiplier. Its calibration nodes solve

```text
P(×3) = (targetRtp − calibrationRtp2) / (calibrationRtp3 − calibrationRtp2)
weight3 = round(P(×3) × calibrationWeightTotal)
weight2 = calibrationWeightTotal − weight3
```

The default endpoints are approximately 87.58707147693227% and 102.54372454016596%. If an edited graph misses the target, **Apply calibrated Wild weights** writes the graph-calculated weights into all three free-spin multiplier Draw nodes and returns to Build. Run the proof again to verify the result. A target outside the attainable range requires editing reel probabilities. All calibration formulas and the three bounded bonus recurrences remain visible in the constructor; the frontend does no payout or expectation calculation. The independent Fraction oracle and Core tests verify the complete rational results and proposed weights.

This proof uses linearity over paylines, independent reel marginals and Sticky Wild recurrence. An unlocked cell survives through turn t with probability `(1 − pWild)^t`. Each target's matching probability, no-Wild probability and multiplier first moment give its longest-prefix expectation. Nine bounded convolutions produce the bonus-count distribution; twenty-seven bounded iterations weight the cumulative free-spin expectations by that distribution.

The proof covers expectation, not the full paid-round payout distribution. The constructor identifies this project as an expectation proof and suppresses gameplay RTP preview; its zero-valued payout sink exists only to satisfy the graph execution contract. Its result is the rational `expectedRtp` state field. Changes to supported strip data, payout values or multiplier/bonus weights flow into the proof. Nonuniform stops, altered payout/loop expressions, incompatible initial state or a binding cap cause the proof author to reject its assumptions explicitly.

| Contribution | Expected return |
| --- | ---: |
| Base paylines | 53.98724489795919% |
| Scatter award | 3.0612244897959183% |
| Free spins | 40.95153061224489% |
| Complete paid round | 98.000000000000% |

The exact trigger probability is `3/490`, or one bonus per 163.3333 paid rounds. Conditional bonus expectation rounds to 66.8875 total stakes.

## Verification results

[The recorded production UI report](verification/doghouse-ui-report.json) includes graph hashes, seed, actual strategy, sample counts, target verification and intervals. The authenticated HTTPS browser used seed 42, one hundred thousand samples and four workers.

| Requested calculation | Actual result |
| --- | --- |
| Rational expectation graph | 98.0000%; rational target check passed |
| Full-round Exact | Branch/time budget exceeded; no RTP reported |
| Full-round epsilon pruned at 0.1 | Exact interval 0–189005 total stakes; all probability mass omitted |
| Sampled | 97.0671%; approximate 95% CI 86.5311–107.6031%, containing 98% |
| Auto mode labelled Hybrid | Sampled fallback, identical samples and result |

The pruned interval is conservative and uninformative. A smaller epsilon can exceed the online budget. The current Hybrid implementation selects Exact or Sampled for the complete program; it does not claim a mixed estimator. Exact attempts have a ten-thousand-branch/three-second budget. Sampled requests stop after forty-five seconds and label incomplete runs; a partial run must not be mistaken for the requested sample count.

The explicit mode endpoint uses fixed 4096-round logical streams and merges them by stream index. The stream scheme is recorded as `splitmix64-chunks-4096-v1`; worker count does not affect completed results. Existing background runs keep their legacy 65536-round stream scheme. Play/replay derives a separate stream for each round index, so its index is not an index into the Monte Carlo sequence.

Independent verification checks the complete payout distribution of a reduced two-stop model against direct enumeration of 256 stop/multiplier combinations. A separate Fraction-based oracle verifies the full model's exact rational expectation, calibration endpoints and weights. A real bonus at seed 42, index 230 checks every frame's manual payout, nine-cell award sum, retained Wild positions and multiplier values, replay equality and trace-on/off equivalence. The full UI graph also produces bit-identical mean, variance, hit frequency and confidence intervals with one and four workers across multiple sample chunks. This was checked on one hundred thousand samples through the authenticated production UI.

The verified suite has 726 Core tests, 54 API tests and 21 browser E2E tests. The Dog House browser tests open a fresh Build with the complete model already loaded, navigate every math subgraph, preserve edits through a direct model link, edit nested AST/weights/data, observe changed payouts, execute all modes, inspect the proof graph, detect an off-target model, apply AST-calculated calibration, confirm 98%, replay a bonus, exercise controls/mobile dialogs, and complete Simulate → Results → Export. Production HTTPS authentication, seeded play, target calibration and all mode results were exercised against the built containers.

## Reproduce

```sh
node frontend/scripts/export-doghouse-fixtures.mjs --check
python3 scripts/verification/doghouse_reference.py --check
dotnet test backend/SlotMath.Core.Tests
ASPNETCORE_ENVIRONMENT=CI dotnet test backend/SlotMath.Api.Tests
npm --prefix frontend run build
npm --prefix frontend run lint
cd frontend && npm run test:e2e
```

API persistence tests require PostgreSQL via `ConnectionStrings__DefaultConnection`. Deployment and operator login instructions are in [PRODUCTION.md](PRODUCTION.md). Generated fixtures in `backend/SlotMath.Core.Tests/TestData/DogHouse` are checked against the frontend authoring code in CI.
