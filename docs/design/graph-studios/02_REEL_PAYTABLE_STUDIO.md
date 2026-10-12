# Reel & Paytable Studio

Status: proposed feature and visual specification. Read `01_GRAPH_EDITING_CONTRACT.md` first.

## 1. The job of this page

A mathematician opens ANY current game/variant, understands how its symbols are drawn and paid, edits those inputs with domain-specific tools, and applies the change to the existing executable graph. They do not have to navigate 95 raw Draw IDs or understand flattened array offsets to make an ordinary mathematical edit. They can always inspect exactly which graph sources changed.

This is a working authoring surface, not an Overview page and not a decorative slot animation. The graph-backed workbench itself is the principal visual element.

## 2. Page composition and art direction

Use a materially new, scoped two-tone editor: retain compatible application navigation, but place the mathematical work on a crisp light editing surface. The suggested starting palette is shell `#16202D`, canvas `#F6F8FC`, surface `#FFFFFF`, ink `#182233`, selection/action `#245FE5`, divider `#D5DEEA`. These are proposed tokens, not facts about current code. Measure normal text contrast; adjust tokens where necessary. Error, warning and provenance get separate semantic tokens; do not use an Exact green badge as brand decoration.

The distinction must come from visible strip lanes, pay matrices, board selection, meaningful source grouping and immediate editing feedback—not merely from changing the background. Apply new tokens inside the two studios, not through broad selectors that unexpectedly restyle existing Results or Simulate pages. A dark alternative can reuse the same geometry later; implementing a global theme switch is not this task.

Typography: one intentional readable UI family, using existing assets/system fallbacks without adding unnecessary downloads. Body 14–15px, field labels 13px, section titles 18px, page title 26px, table values 13–14px with tabular numerals. Monospace is reserved for actual source/exact-value details, not every label. Do not manufacture density using 10px text. Normal controls 36–40px high; editable rows about 40px; symbol thumbnails around 28–36px where they aid recognition.

At 1440px:

```text
Project / variant                         Undo   Validate & apply   Run analysis
Reel & Paytable Studio                    Applied source / pending changes
Sources       Paytables       Paylines       Symbols

SOURCE EXPLORER       | ACTIVE EDITING SURFACE                      | INSPECTOR
Search sources        | Symbol lanes OR cell-distribution matrix   | Selected item
Paid game             | Selected window / cells / payout range     | Exact values
Free spins            |                                             | Used by...
Refill                | Main workspace occupies most of the page    | Source binding
Prizes / mystery      |                                             | Consequences

                      | Pending changes: source + dependent fields  | Apply / Discard
```

Use a ~220–240px source explorer. The optional ~280–320px inspector opens for selection; at narrower desktop sizes it becomes a drawer instead of crushing the grid. At 1280px keep the central editor usefully wide; table-local scrolling is acceptable, document-level clipping is not. The page header, source selection and primary apply action remain discoverable without scrolling through diagnostic evidence. Avoid a long vertically stacked page of equal bordered cards.

Actual game symbols may be shown through verified project asset mappings. A custom game without art uses legible labeled symbols, not Nine Lives images or a blank grid. No fake round state or fabricated RTP is needed to make an editor attractive.

## 3. Source explorer

Group by executable role/use, not by arbitrary file names: paid draw, free-spin draw, refill, mystery replacement, coin prizes, bonus outcomes. A physical reel set is shown as a set of ordered strips; independent cell sources are shown as distributions assigned to cells/stages. An imported custom source keeps its authored name plus a precise structural source link.

A source may be shared across uses. Show the use count and list. Distinguish editing its definition from making one use independent. Do not merge separate distributions just because their numerical rows happen to be equal. Support search, pinning and navigating from a source to its existing graph nodes without losing the draft.

## 4. Physical reel editor

Required for Ancient Dragon, Woodland Whisper, Le Militare and compatible imported graphs:

- One ordered lane per reel, with stop numbers, symbol images/names and a selectable visible-window overlay.
- Reorder with drag and with keyboard/move controls; replace, insert, delete and bulk paste stops. Preserve stable identity/weights of retained stops where the graph distinguishes them.
- Different strip lengths are normal. Show physical length, symbol counts and stop weighting. Do not show wrap padding as additional physical positions.
- Local selected-window inspection is configuration inspection, not a spin result. A real test round is a separate action through the generic play endpoint.
- Read-only count/frequency summaries distinguish symbol frequency, stop probability and window-event probability. Do not infer multi-row probabilities from simple frequency counts.
- Paid/free reel sets are independent unless their bindings explicitly share a definition. Le Militare refill must continue using the selected set and preserve held-cell behavior.
- Changing a Woodland bought-entry strip recomputes conditional stop groups/pattern weights. If the event becomes impossible, validation rejects the candidate and preserves the current graph.

A reorder is not cosmetic. Changing physical order can affect windows and gameplay. The apply summary must say so.

Create-source workflow: define symbols, strip count/window rows and ordered stops, then generate ordinary Draw/state/expression/library fragments using the existing authoring primitives. Creating a new source does not silently replace a game's active source; the user selects the consuming action/connection.

## 5. Independent-cell and weighted-outcome editor

Required for Nine Lives and X7 Club, and also for non-board prize/mystery draws:

- A matrix of symbol/outcome rows and linked source columns, with an optional board-cell assignment view.
- Paid/free/refill sources are separate selections. Clearly identify where a common distribution is reused for many draws.
- Show weight, total weight and the exact ratio `weight / total`, plus a rounded percentage as display. This is the local draw probability, NOT RTP.
- Keep outcomeId, payload/value and stateWriteKey distinct. Many current symbol Draws have payload `value: 0`; do not turn them into zero-pay symbols or overwrite outcomeId.
- Support existing zero weights, adding/removing supported outcomes, multi-cell edits and explicit linked/unlinked editing. The candidate must retain a valid positive total and all downstream contracts.
- Dynamic expressions remain expressions. Show a state-scoped preview only when the backend can evaluate it; label it with the chosen state. Do not convert a dynamic source into a static table by sampling it.
- No cell editor pretending to be a reel-strip editor. Source type is visible before editing.

## 6. Payout matrix

Required tabs depend on the model: Line, Cluster, Scatter, Prizes/Multipliers. Each has the right axis semantics and payout units.

- Freeze the symbol column and count/size headings; support rectangular selection, keyboard navigation and clipboard copy/paste.
- Show real symbol thumbnails/names, not just numeric IDs. IDs remain available in source details.
- For cluster games, columns mean group size. For line games, matching reels. For coin-prize lists, rows are outcomes with prize and weight. Do not fake one universal table where these meanings become ambiguous.
- Show credits / total-bet multiples / round-price multiples explicitly. Changing display units must not change the underlying value.
- Distinguish authored values, derived values and unsupported/custom expressions. A derived all-wild row is labeled and recalculated according to its verified rule; an explicitly authored wild row is not replaced by maxima.
- Required edits include changing a payout, adding/removing a paying size or symbol where the source contract permits it, and changing prize values/weights. Dependent eligibility, minimum size, pruning, normalized precision tables and bounds are part of the transaction.
- Where an edited custom graph breaks a template contract, preserve it and offer a precise local source-edit path. This fallback is NOT an excuse to leave the five unmodified bundled families unsupported.

Do not claim every numeric change must increase/decrease full-game RTP monotonically. Show real evaluation only after execution and keep target/observed/exact distinctions.

## 7. Payline editor

For Ancient Dragon, Woodland Whisper and X7 Club:

Select a line in a compact list and draw its path over the actual board geometry. The table below shows the selected row per column. Changing a path updates the metadata and executable Library parameters together. Validate one position per column and bounds. Add/remove/duplicate lines only with an explicit stake policy: X7's current graph asserts one credit per line, so a changed count cannot silently keep an incompatible total bet. Preserve valid custom stake contracts rather than imposing that policy on every game.

Switching to a cluster game shows that paylines are not used; do not invent them and do not make the whole Studio unavailable.

## 8. Symbols and imports

Renaming a displayed symbol or changing its asset does not reorder IDs. Adding/removing a mathematical symbol updates all required source/paytable references or reports blockers before mutation. Do not offer deletion of a still-referenced symbol as a blind JSON splice.

Provide clipboard TSV, CSV and genuine `.xlsx` data import/export for the sources/tables delivered here. Inspect available dependencies and choose a maintained parser only when needed; do not install a wholesale UI framework. Import has worksheet/column selection, explicit symbol matching, units, an exact-value preview, row-level errors and one atomic apply. Never execute spreadsheet formulas/macros or silently evaluate external links. Formula cells require an explicit supported value policy; values whose exactness cannot be established are rejected or require textual input, not rounded. CSV numeric formatting must not depend on the user's decimal locale. Export includes enough source/unit labels to reimport deliberately, not secret credentials or unrelated project data.

JSON graph import/export remains the full-fidelity execution interchange. Spreadsheet editing is an input view, not a competing model format.

## 9. Interaction states and recovery

Implement pristine, dirty-valid, dirty-invalid, validating, applied, conflict, source-changed, execution-error, source-unavailable, and cancelled states. The displayed apply state belongs to the current project and revision. Pending validation cannot overwrite a graph edited elsewhere. Closing a drawer does not silently discard a table edit. A failed bulk import leaves the current model intact.

Show provenance in a collapsed source inspector by default, rather than pushing AST paths and warnings above the main table. Important validation errors remain adjacent to cells and in a summary. Every primary action actually works; a disabled action gives a reason. Avoid decorative control stubs.

## 10. Mandatory user acceptance journeys

1. Ancient Dragon: edit and reorder a reel, alter a line payout/path, apply, inspect corresponding graph sources, run and export/reimport. Unchanged round trips preserve behavior.
2. Woodland Whisper bought feature: edit a scatter-relevant strip, rebuild conditioning, verify it still represents the conditional ordinary distribution and impossible-event rejection.
3. X7 Club: edit cell/coin prize weights and a line payout. No physical strip is fabricated. The Hold & Spin consumer still receives correct coins and stake units.
4. Nine Lives: edit paid and refill distributions independently; change cluster payouts including a supported minimum-size boundary. Collection and free-spin rules remain intact.
5. Le Militare: edit paid/free strips separately, a cluster payout and a multiplier distribution. Test selected mode/entry identities and carried/weighted-cell semantics.
6. Custom graph: rename nodes/definitions, add an unrelated AST field, round-trip through the Studio, then make an intentional graph edit and verify binding reconciliation.

Pass these through the actual compiler/run path. A mock response may test an error state but cannot prove that applying the editor changes the mathematics.
