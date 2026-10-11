# Build with reusable subgraphs

In Build, expand **Ready subgraphs** in the palette. Open **Cluster pays** or
**Cascading clusters**, or inspect their subgraph directly. **Restore my model**
returns to the draft you had open, including its subgraph navigation and selection.
The retained draft survives navigation within the application, but is held in memory;
export a model before closing or reloading the browser.

The same panel also offers **Complete game models**: Nine Lives, Ancient Dragon,
Woodland Whisper, X7 Club and Le Militare. Open a model to inspect its paid round,
triggered features, tables and shared definitions, then simulate or export it.
These are the migrated games' full models; the cluster/cascade starters use
illustrative data.

The examples are complete editable GraphConfig models. Their integer weights and
payouts demonstrate composition; their RTP is not calibrated. Use **Data tables**
to inspect initial state and lookup tables, and the inspector to edit each typed
expression. No calculation lives in the TypeScript authoring helpers: they build
the JSON that the engine executes.

Inside a subgraph, choose **Reuse this subgraph**. This saves open edits back into
the full game and adds a Library node referring to the same definition. Connect
that node where another invocation belongs. The **Mechanics → Custom Mechanics**
panel lists these same definitions: **Open** edits one and **Use** adds a reference. It retains the definition's input and
output port contracts and its dependencies. Editing the shared definition changes
every instance. State keys are shared too: two instances do not create private
state automatically. For independent state, make a separate definition and change
its state bindings and data.

For your own mechanic, Shift-select its atoms, enter a name next to **Extract
selection**, and extract them. The canvas replaces the selection with a Library
reference and keeps its incoming and outgoing connections. The definition retains
its typed expressions and references to other subgraphs; data remains in the
project. Open its new navigation button to edit it or add another reference.
Include branch joins and loop bodies in the selection, and keep the round payout
sink in the full graph. Ambiguous boundary ports are reported before any change.
Extraction copies the complete transitive closure of named expression references, including
inline port defaults and loop exit classifications. Nested library references retain their
parameter bindings. Cycles or unresolved references are reported before modifying the model;
a parameter available only from an enclosing instance cannot be silently frozen into the
new shared definition. Expose it through ordinary state or include a local named definition
before extracting. Forwarding dynamic parameters between nested library instances is not
part of the current constant-string parameter contract.

## Cluster example

The board has six columns and five rows. Cells are numbered column by column:
`column * 5 + row`. `board` contains symbol IDs as strings. `W` is wild; `A` through
`E` pay in orthogonally connected groups of five or more. Wilds belong to the first
paying group that claims them. If a symbol pays, all cells of that symbol leave
the board, including those outside its paying group.

The root is **draw cells → cluster pays → payout**. Open its parts:

- `draw-cells` uses scalar Draw atoms, then assembles the board. Its immutable
  position list lets the existing compiler bind each output position once.
- `read-board` scans the board once into binary symbol planes. The sets of cells
  showing each symbol are recovered with intersections and differences.
- `cluster-candidates` compares adjacent symbol planes together. On a board
  without wilds it keeps a seed in every component of at least five cells and
  excludes smaller components. All 295 connected four-cell placements and all
  766 connected five-cell placements are checked against independent geometry.
- `symbol-clusters` preserves first-occurrence order when wilds can be claimed.
  A connected group of k cells requires at least k−1 edges; counting available
  edges rejects impossible payouts before traversal.
- `pay-group` composes frontier growth from Union, Intersect, Except and Shift,
  then counts the component and looks up its payout. It emits detailed group
  histories only when the play endpoint enables tracing.

Candidate ordering is enabled only for untraced boards without wilds and without
weighted cells. Detailed played rounds retain their original group ordering.
The general traversal also supports smaller payout thresholds and weighted cells.

## Cascade example

The same `cluster-pays` definition is reused by `cascade`. Each step invokes
`award-step`, then `tumble` removes the marked cells, lets survivors fall within
their columns, and draws replacements. New cells draw in column order, with the
first replacement landing lowest. The round ends when no group pays or after
thirty paying steps; the example caps the total at 5,000 credits.

For columns of up to eight rows, `tumble` exports `settledSources`: one source-row
entry for each removal pattern and output row. On a five-row column it has just
32 × 5 = 160 small integers. A nonnegative entry reads a survivor from the old
board; a negative entry identifies a fresh row. The same lookup moves carried
cell values. The removal pattern is computed once per column with Shift and
Intersect. Taller columns use the general Filter composition to keep tables small.
All of this is visible in JSON and uses existing expressions. The authoring helper
keeps `survivors` and carried survivor arrays by default, for downstream consumers.
Set `retainSurvivors: false` when those outputs are unused, as in this starter and
Nine Lives, to avoid their allocations.

Keep repeated work out of inner loops: calculate board-wide symbol sets once,
use immutable geometry tables, collect detailed histories only for played rounds,
and skip traversal only with a mathematical bound that cannot discard a payout.
Measure complete rounds with bonuses, the same seeds and worker streams, and
live statistics enabled. Compare the resulting payouts as well as throughput.
The game graphs and the examples remain editable JSON; the kernel does not
recognize their names or structures.
