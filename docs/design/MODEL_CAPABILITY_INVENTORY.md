# Bundled model capability inventory

Captured 2026-10-12 against f4cce7f. Every factory in gameModelLibrary/starterLibrary plus the separate reference Nine Lives factory was invoked and its complete authored JSON inspected. [Raw structural inventory](qa/model-schema-inventory.json) records all top-level fields, state arrays/types/lengths and expression scopes referencing those arrays. TypeScript was transpiled to CommonJS for this read-only local inspection. These are authored-source counts, not compiled instruction counts, timings, sampled RTP or certified game facts. Examples remain uncalibrated.

| Template | Columns × rows | Root nodes/edges | Definitions | All authored nodes | Guarded weighted editor sources |
|---|---|---|---|---|---|
| Nine Lives reference | 6×5 | 4/3 | 40 | 382 | 95 |
| Nine Lives | 6×5 | 5/5 | 21 | 225 | 91 |
| Ancient Dragon | 5×3 | 6/6 | 7 | 72 | 6 |
| Woodland Whisper | 5×3 | 5/5 | 10 | 89 | 7 |
| X7 Club | 5×3 | 5/5 | 10 | 148 | 61 |
| Le Militare | 6×5 | 6/6 | 38 | 254 | 18 |
| Cluster pays | 6×5 | 3/2 | 9 | 96 | 30 |
| Cascading clusters | 6×5 | 3/2 | 14 | 119 | 31 |

## Source families and safe editing

Runtime qualification from the AI source audit: a Draw `weightExpression` referencing an array-valued state field passes structural validation but the current light evaluator returns `EVAL_TYPE_ERROR: Numeric weight expression required.` Declared dynamic mode is not proof of executable vector-weight support. Preserve source and show the actual error; do not claim compiler acceptance certifies every evaluation. See [AI source evidence](qa/AI_MODEL_SOURCE_REVIEW.md). No new kernel atom was introduced.

- Nine Lives reference: `src/games/nine-lives/graph.ts`, separate authored row-major reference model, 40 reusable definitions. Finite draws, initial state, typed AST, flattened executable cluster payout inputs and matching paytable metadata. Existing cluster payout/free-spin form adapters guard their exact executable mappings.
- Nine Lives migrated: `src/games/nine-lives-tgslots/graph.ts`, composed shared atomic authoring helpers. It is a separate 6×5 model, not the reference adapter's universal schema. Do not assume identical payout/form mappings because IDs or names resemble each other.
- Ancient Dragon and Woodland Whisper: their game factories compose `reelState` and `spinReels` from `src/games/shared/graph/mechanics/reels.ts`. **Physical circular strips are already present** in `initialState.reel0` through `reel4`. Stored lengths include the first `rows−1` symbols repeated as a suffix so window reads do not wrap. Stop draws encode allowed indices independently of that suffix.
- Le Militare: factory composes paid/free reel sets under `paidStrip0…5` and `freeStrip0…5`, again with a four-symbol wrap suffix for its five-row window. `windowReels6x5` is a cell-to-reel lookup, not a physical strip. Shared stop subgraphs are also used by cascades/refill; changing a strip must retain those dependencies.
- X7 Club: source uses finite composed draws and editable state/AST/payline metadata. The current authored inventory has no physical strip family; do not infer one from its 5×3 board or 'reel' labels.
- Cluster pays / Cascading clusters starters: independent cell draws, cluster payout lookup state, typed expressions and optional cascade/refill. Their 6×5 geometry does not imply physical reels.

## Physical strips: ordinary and coupled conditional mappings

The prior detector only checks a top-level `reelStrips` array. None of these eight models has that field; this **does not prove absence of physical strips**. The actual shared source above disproves that inference. 87c95d8 now delivers a guarded editor for all three ordinary template families, including separate Le Militare paid/free sets. It recognizes actual state/AST/stop domains and updates their repeated tails together. See qa/REEL_EDITOR_REVIEW.md. c4d4c9e additionally delivers the coupled bought-variant mapping. See qa/CONDITIONED_REEL_REVIEW.md for exact shape/weight/metadata guards and source-rule preservation.

| Model | Stored strip lengths | Additional dependencies |
|---|---|---|
| Ancient Dragon | 45, 45, 51, 44, 44 | Three-row window; raw periods 43, 43, 49, 42, 42. Stop draws and wrap suffix must update together. Mystery-symbol weights are a distinct weighted source. |
| Woodland Whisper | 45, 44, 51, 44, 44 | Three-row window; raw periods 43, 42, 49, 42, 42. Ordinary template uses ordinary stops. The separate bought variant materializes showPatterns, weighted pattern selection and conditional stop subsets; raw strip-only mutation would invalidate its conditioning. |
| Le Militare paid | 142, 150, 142, 150, 142, 150 | Five-row window; raw periods 138/146. Stop subgraphs shared with refill/cascade. |
| Le Militare free | 177, 181, 177, 181, 177, 181 | Five-row window; raw periods 173/177. Keep paid and free sets distinct and identify each referenced stop domain. |

Required behavior remains column/index/symbol presentation, source-derived counts/probabilities, keyboard add/remove/reorder, invalid empty/unknown-symbol warnings, staged CSV preview, validated transaction/Undo and faithful round trips. A detector must inspect actual state/AST/stop mappings, not just names or board geometry. Preserve unrelated/unknown authored fields and fail closed on an unproven mapping. Do not add game-specific execution or new atoms to implement the editor.

## Other data contracts

All eight models include symbols/paytable metadata and boardConfig; Ancient Dragon, Woodland Whisper and X7 Club also include paylineSets. Metadata is not automatically an executable payout source. State arrays are commonly strings that encode exact integers or symbolic IDs; retain their original types. Boolean/nested/null state remains read-only in the generic adapter. Finite draw weights may encode stop indices rather than direct payouts, so editing a weight must never be described as universal symbol frequency or physical strip editing.

The existing supported table/feature adapters have scoped tests. This inventory is source evidence and an implementation input. Ordinary reel mapping has separate scoped verification; coupled conditional mapping now has separate scoped evidence. Universal arbitrary form conversion is not claimed.
